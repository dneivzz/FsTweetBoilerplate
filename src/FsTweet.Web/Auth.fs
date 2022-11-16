namespace Auth

module Domain =
  open User
  open Chessie.ErrorHandling
  open Chessie

  type LoginRequest =
    { Username: Username
      Password: Password }

    static member TryCreate(username, password) =
      trial {
        let! username = Username.TryCreate username
        let! password = Password.TryCreate password

        return
          { Username = username
            Password = password }
      }

  type LoginError =
    | UsernameNotFound
    | EmailNotVerified
    | PasswordMisMatch
    | Error of System.Exception

  type Login =
    FindUser -> LoginRequest -> AsyncResult<User, LoginError>

  let login (findUser: FindUser) (req: LoginRequest) =
    asyncTrial {
      let! userToFind =
        findUser req.Username
        |> AR.mapFailure Error

      match userToFind with
      | None -> return! UsernameNotFound |> AR.fail
      | Some user ->
        match user.EmailAddress with
        | NotVerified _ -> return! EmailNotVerified |> AR.fail
        | Verified _ ->
          let isMatchingPassword =
            PasswordHash.VerifyPassword req.Password user.PasswordHash

          match isMatchingPassword with
          | false -> return! PasswordMisMatch |> AR.fail
          | true -> return user
    }

module Suave =
  open Suave
  open Suave.DotLiquid
  open Suave.Filters
  open Suave.Operators
  open Suave.Form
  open Suave.Authentication
  open Suave.Cookie
  open Domain
  open Chessie.ErrorHandling
  open Chessie
  open User
  open Suave.State.CookieStateStore


  type LoginViewModel =
    { Username: string
      Password: string
      Error: string option }

  let emptyLoginViewModel =
    { Username = ""
      Password = ""
      Error = None }

  let loginTemplatePath = "user/login.liquid"

  let renderLoginPage (viewModel: LoginViewModel) hasUserLoggedIn =
    match hasUserLoggedIn with
    | Some _ -> Redirection.FOUND "/wall"
    | _ -> page loginTemplatePath viewModel

  let onLoginFailure viewModel loginError =
    match loginError with
    | PasswordMisMatch ->
      let vm =
        { viewModel with Error = Some "password didn't match" }

      renderLoginPage vm None
    | EmailNotVerified ->
      let vm =
        { viewModel with Error = Some "email not verified" }

      renderLoginPage vm None
    | UsernameNotFound ->
      let vm =
        { viewModel with Error = Some "invalid username" }

      renderLoginPage vm None
    | Error ex ->
      printfn "%A" ex

      let vm =
        { viewModel with Error = Some "something went wrong" }

      renderLoginPage vm None

  let setState key value ctx =
    match HttpContext.state ctx with
    | Some state -> state.set key value
    | _ -> never

  let userSessionKey = "fsTweetUser"

  let createUserSession (user: User) =
    statefulForSession
    >=> context (setState userSessionKey user)

  let onLoginSuccess (user: User) =
    authenticated CookieLife.Session false
    >=> createUserSession user
    >=> Redirection.FOUND "/wall"

  let handleUserLogin findUser ctx =
    async {
      match bindEmptyForm ctx.request with
      | Choice1Of2 (vm: LoginViewModel) ->
        let result =
          LoginRequest.TryCreate(vm.Username, vm.Password)

        match result with
        | Success req ->
          let! webpart =
            login findUser req
            |> AR.either onLoginSuccess (onLoginFailure vm)

          return! webpart ctx
        | Failure err ->
          let viewModel = { vm with Error = Some err }
          return! renderLoginPage viewModel None ctx
      | Choice2Of2 err ->
        let viewModel =
          { emptyLoginViewModel with Error = Some err }

        return! renderLoginPage viewModel None ctx
    }

  let redirectToLoginPage =
    Redirection.FOUND "/login"

  let retrieveUser ctx : User option =
    match HttpContext.state ctx with
    | Some state -> state.get userSessionKey
    | _ -> None

  let initUserSession fFailure fSuccess ctx =
    match retrieveUser ctx with
    | Some user -> fSuccess user
    | _ -> fFailure

  let userSession fFailure fSuccess =
    statefulForSession
    >=> context (initUserSession fFailure fSuccess)

  let onAuthenticate fSuccess fFailure =
    authenticate
      CookieLife.Session
      false
      (fun _ -> Choice2Of2 fFailure)
      (fun _ -> Choice2Of2 fFailure)
      (userSession fFailure fSuccess)

  let requiresAuth fSuccess =
    onAuthenticate fSuccess redirectToLoginPage

  let requiresAuth2 fSuccess =
    onAuthenticate fSuccess JSON.unauthorized

  let optionalUserSession fSuccess =
    statefulForSession
    >=> context (fun ctx -> fSuccess (retrieveUser ctx))

  let mayRequireAuth fSuccess =
    authenticate
      CookieLife.Session
      false
      (fun _ -> Choice2Of2(fSuccess None))
      (fun _ -> Choice2Of2(fSuccess None))
      (optionalUserSession fSuccess)

  let webpart getDataCtx =
    let findUser =
      Persistence.findUser getDataCtx

    choose
      [ path "/login"
        >=> choose
              [ GET
                >=> mayRequireAuth (
                  renderLoginPage emptyLoginViewModel
                )
                POST >=> handleUserLogin findUser ]
        path "/logout"
        >=> deauthenticate
        >=> redirectToLoginPage ]
