module User

open Chessie.ErrorHandling
open Chessie
open BCrypt.Net

type Username =
  private
  | Username of string

  static member TryCreate(username: string) =
    match username with
    | null
    | "" -> fail "Username should not be empty"
    | x when x.Length > 12 ->
      fail "Username should not be more than 12 characters"
    | x ->
      x.Trim().ToLowerInvariant()
      |> Username
      |> ok

  static member TryCreateAsync username =
    Username.TryCreate username
    |> mapFailure (System.Exception)
    |> Async.singleton
    |> AR

  member this.Value =
    let (Username username) = this
    username

type UserId = UserId of int

type EmailAddress =
  private
  | EmailAddress of string

  static member TryCreate(emailAddress: string) =
    try
      new System.Net.Mail.MailAddress(emailAddress)
      |> ignore

      emailAddress.Trim().ToLowerInvariant()
      |> EmailAddress
      |> ok
    with _ ->
      fail "Invalid Email Address"

  member this.Value =
    let (EmailAddress emailAddress) = this
    emailAddress

type Password =
  private
  | Password of string

  static member TryCreate(password: string) =
    match password with
    | null
    | "" -> fail "Password should not be empty"
    | x when x.Length < 4 || x.Length > 8 ->
      fail "Password should contain only 4-8 characters"
    | x -> Password x |> ok

  member this.Value =
    let (Password password) = this
    password

type PasswordHash =
  private
  | PasswordHash of string

  member this.Value =
    let (PasswordHash passwordHash) = this
    passwordHash

  static member Create(password: Password) =
    BCrypt.HashPassword(password.Value)
    |> PasswordHash

  static member TryCreate passwordHash =
    try
      BCrypt.InterrogateHash passwordHash
      |> ignore

      PasswordHash passwordHash |> ok
    with _ ->
      fail "Invalid Password Hash"

  static member VerifyPassword
    (password: Password)
    (passwordHash: PasswordHash)
    =
    BCrypt.Verify(password.Value, passwordHash.Value)

type UserEmailAddress =
  | Verified of EmailAddress
  | NotVerified of EmailAddress

  member this.Value =
    match this with
    | Verified e
    | NotVerified e -> e.Value

type User =
  { UserId: UserId
    Username: Username
    PasswordHash: PasswordHash
    EmailAddress: UserEmailAddress }

type FindUser = Username -> AsyncResult<User option, System.Exception>


module Persistence =
  open Database
  open FSharp.Data.Sql
  open System

  let mapUserEntityToUser (user: DataContext.``public.UsersEntity``) =
    let userResult =
      trial {
        let! username = Username.TryCreate user.Username
        let! passwordHash = PasswordHash.TryCreate user.PasswordHash
        let! email = EmailAddress.TryCreate user.Email

        let userEmailAddress =
          match user.IsEmailVerified with
          | true -> Verified email
          | _ -> NotVerified email

        return
          { UserId = UserId user.Id
            Username = username
            PasswordHash = passwordHash
            EmailAddress = userEmailAddress }
      }

    userResult |> mapFailure Exception

  let mapUserEntity (user: DataContext.``public.UsersEntity``) =
    mapUserEntityToUser user
    |> Async.singleton
    |> AR

  let mapUserEntities
    (users: DataContext.``public.UsersEntity`` seq)
    =
    users
    |> Seq.map mapUserEntityToUser
    |> collect
    |> mapFailure (fun errs ->
      new AggregateException(errs) :> Exception
    )
    |> Async.singleton
    |> AR

  let findUser (getDataCtx: GetDataContext) (username: Username) =
    asyncTrial {
      let ctx = getDataCtx ()

      let! userToFind =
        query {
          for u in ctx.Public.Users do
            where (u.Username = username.Value)
        }
        |> Seq.tryHeadAsync
        |> Async.AwaitTask
        |> AR.catch

      match userToFind with
      | None -> return None
      | Some user ->
        let! user = mapUserEntity user
        return Some user
    }
