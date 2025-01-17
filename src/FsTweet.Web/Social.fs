namespace Social

module Domain =
  open System
  open Chessie.ErrorHandling
  open User

  type CreateFollowing =
    User -> UserId -> AsyncResult<unit, Exception>

  type Subscribe = User -> UserId -> AsyncResult<unit, Exception>
  type FollowUser = User -> UserId -> AsyncResult<unit, Exception>
  type IsFollowing = User -> UserId -> AsyncResult<bool, Exception>
  type FindFollowers = UserId -> AsyncResult<User list, Exception>

  type FindFollowingUsers =
    UserId -> AsyncResult<User list, Exception>

  let followUser
    (subscribe: Subscribe)
    (createFollowing: CreateFollowing)
    user
    userId
    =
    asyncTrial {
      do! subscribe user userId
      do! createFollowing user userId
    }

module Persistence =
  open Database
  open User
  open Chessie.ErrorHandling
  open FSharp.Data.Sql
  open Chessie
  open System.Linq
  open User.Persistence

  let createFollowing
    (getDataCtx: GetDataContext)
    (user: User)
    (UserId userId)
    =
    let ctx = getDataCtx ()
    let social = ctx.Public.Social.Create()
    let (UserId followerUserId) = user.UserId
    social.FollowerUserId <- followerUserId
    social.FollowingUserId <- userId
    submitUpdates ctx

  let isFollowing
    (getDataCtx: GetDataContext)
    (user: User)
    (UserId userId)
    =
    asyncTrial {
      let ctx = getDataCtx ()
      let (UserId followerUserId) = user.UserId

      let! connection =
        query {
          for s in ctx.Public.Social do
            where (
              s.FollowerUserId = followerUserId
              && s.FollowingUserId = userId
            )
        }
        |> Seq.tryHeadAsync
        |> Async.AwaitTask
        |> AR.catch

      return connection.IsSome
    }


  let findFollowers (getDataCtx: GetDataContext) (UserId userId) =
    asyncTrial {
      let ctx = getDataCtx ()

      let selectFollowersQuery =
        query {
          for s in ctx.Public.Social do
            where (s.FollowingUserId = userId)
            select s.FollowerUserId
        }

      let! followers =
        query {
          for u in ctx.Public.Users do
            where (selectFollowersQuery.Contains(u.Id))
            select u
        }
        |> Seq.executeQueryAsync
        |> Async.AwaitTask
        |> AR.catch

      return! mapUserEntities followers
    }

  let findFollowingUsers
    (getDataCtx: GetDataContext)
    (UserId userId)
    =
    asyncTrial {
      let ctx = getDataCtx ()

      let selectFollowingUsersQuery =
        query {
          for s in ctx.Public.Social do
            where (s.FollowerUserId = userId)
            select s.FollowingUserId
        }

      let! followingUsers =
        query {
          for u in ctx.Public.Users do
            where (selectFollowingUsersQuery.Contains(u.Id))
            select u
        }
        |> Seq.executeQueryAsync
        |> Async.AwaitTask
        |> AR.catch

      return! mapUserEntities followingUsers
    }

module GetStream =
  open User
  open Chessie

  let subscribe
    (getStreamClient: GetStream.Client)
    (user: User)
    (UserId userId)
    =
    let (UserId followerUserId) = user.UserId

    let timeLineFeed =
      GetStream.timeLineFeed getStreamClient followerUserId

    let (userFeed, _) =
      GetStream.userFeedAndToken getStreamClient userId

    timeLineFeed.FollowFeedAsync(userFeed)
    |> Async.AwaitTask
    |> Async.map ignore
    |> AR.catch

module Suave =
  open Chiron
  open Suave
  open Domain
  open User
  open Chessie
  open Suave.Filters
  open Persistence
  open Suave.Operators
  open Auth.Suave

  type FollowUserRequest =
    | FollowUserRequest of int

    static member FromJson(_: FollowUserRequest) =
      json {
        let! userId = Json.read "userId"
        return FollowUserRequest userId
      }

  type UserDto =
    { Username: string }

    static member ToJson(u: UserDto) =
      json { do! Json.write "username" u.Username }

  type UserDtoList =
    | UserDtoList of (UserDto list)

    static member ToJson(UserDtoList userDtos) =
      let usersJson =
        userDtos
        |> List.map (Json.serializeWith UserDto.ToJson)

      json { do! Json.write "users" usersJson }

  let mapUsersToUserDtoList (users: User list) =
    users
    |> List.map (fun user -> { Username = user.Username.Value })
    |> UserDtoList

  let onFollowUserSuccess () = Successful.NO_CONTENT

  let onFollowUserFailure (ex: System.Exception) =
    printfn "%A" ex
    JSON.internalError

  let handleFollowUser (followUser: FollowUser) (user: User) ctx =
    async {
      match JSON.deserialize ctx.request with
      | Success (FollowUserRequest userId) ->
        let! webpart =
          followUser user (UserId userId)
          |> AR.either onFollowUserSuccess onFollowUserFailure

        return! webpart ctx
      | Failure _ ->
        return! JSON.badRequest "invalid user follow request" ctx
    }

  let onFindUsersFailure (ex: System.Exception) =
    printfn "%A" ex
    JSON.internalError

  let onFindUsersSuccess (users: User list) =
    mapUsersToUserDtoList users
    |> Json.serialize
    |> JSON.ok

  let fetchFollowers (findFollowers: FindFollowers) userId ctx =
    async {
      let! webpart =
        findFollowers (UserId userId)
        |> AR.either onFindUsersSuccess onFindUsersFailure

      return! webpart ctx
    }

  let fetchFollowingUsers
    (findFollowingUsers: FindFollowingUsers)
    userId
    ctx
    =
    async {
      let! webpart =
        findFollowingUsers (UserId userId)
        |> AR.either onFindUsersSuccess onFindUsersFailure

      return! webpart ctx
    }

  let webpart getDataCtx getStreamClient =
    let createFollowing =
      createFollowing getDataCtx

    let subscribe =
      GetStream.subscribe getStreamClient

    let followUser =
      followUser subscribe createFollowing

    let handleFollowUser =
      handleFollowUser followUser

    let findFollowers = findFollowers getDataCtx

    let findFollowingUsers =
      findFollowingUsers getDataCtx

    choose
      [ GET
        >=> pathScan "/%d/followers" (fetchFollowers findFollowers)
        GET
        >=> pathScan
              "/%d/following"
              (fetchFollowingUsers findFollowingUsers)
        POST
        >=> path "/follow"
        >=> requiresAuth2 handleFollowUser ]
