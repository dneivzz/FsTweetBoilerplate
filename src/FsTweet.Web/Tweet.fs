namespace Tweet

open Chessie.ErrorHandling
open User
open System

type Post =
  private
  | Post of string

  static member TryCreate(post: string) =
    match post with
    | null
    | "" -> fail "Tweet should not be empty"
    | x when x.Length > 140 ->
      fail "Tweet should not be more than 140 characters"
    | x -> Post x |> ok

  member this.Value =
    let (Post post) = this
    post

type TweetId = TweetId of Guid
type CreateTweet = UserId -> Post -> AsyncResult<TweetId, Exception>

type Tweet =
  { UserId: UserId
    Username: Username
    Id: TweetId
    Post: Post }

module Persistence =

  open User
  open Database
  open System

  let createTweet
    (getDataCtx: GetDataContext)
    (UserId userId)
    (post: Post)
    =
    asyncTrial {
      let ctx = getDataCtx ()
      let newTweet = ctx.Public.Tweets.Create()
      let newTweetId = Guid.NewGuid()

      newTweet.UserId <- userId
      newTweet.Id <- newTweetId
      newTweet.Post <- post.Value
      newTweet.TweetedAt <- DateTime.UtcNow

      do! submitUpdates ctx
      return TweetId newTweetId
    }
