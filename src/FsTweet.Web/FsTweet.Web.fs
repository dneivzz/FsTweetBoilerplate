module FsTweetWeb.Main

open Suave
open System
open Database
open System.IO
open System.Reflection
open Suave.DotLiquid
open Suave.Filters
open Suave.Operators
open Suave.Files
open Email
open System.Net

let currentPath =
  Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

let initDotLiquid () =
  let templatesDir =
    Path.Combine(currentPath, "views")

  setTemplatesDir templatesDir

let serveAssets =
  let faviconPath =
    Path.Combine(currentPath, "assets", "images", "favicon.ico")

  choose
    [ pathRegex "/assets/*" >=> browseHome
      path "/favicon.ico" >=> file faviconPath ]

[<EntryPoint>]
let main argv =
  let fsTweetConnString =
    Environment.GetEnvironmentVariable "FSTWEET_DB_CONN_STRING"

  let getDataCtx =
    dataContext fsTweetConnString

  let serverToken =
    Environment.GetEnvironmentVariable "FSTWEET_POSTMARK_SERVER_TOKEN"

  let senderEmailAddress =
    Environment.GetEnvironmentVariable "FSTWEET_SENDER_EMAIL_ADDRESS"

  let env =
    Environment.GetEnvironmentVariable "FSTWEET_ENVIRONMENT"

  let sendEmail =
    match env with
    | "dev" -> consoleSendEmail
    | _ -> initSendEmail senderEmailAddress serverToken

  let serverKey =
    Environment.GetEnvironmentVariable "FSTWEET_SERVER_KEY"
    |> ServerKey.fromBase64

  let streamConfig: GetStream.Config =
    { ApiKey = Environment.GetEnvironmentVariable "FSTWEET_STREAM_KEY"
      ApiSecret =
        Environment.GetEnvironmentVariable "FSTWEET_STREAM_SECRET"
      AppId =
        Environment.GetEnvironmentVariable "FSTWEET_STREAM_APP_ID" }

  let getStreamClient =
    GetStream.newClient streamConfig

  let ipZero = IPAddress.Parse("0.0.0.0")

  let port =
    Environment.GetEnvironmentVariable "PORT"

  let httpBinding =
    HttpBinding.create HTTP ipZero (uint16 port)

  let serverConfig =
    { defaultConfig with
        serverKey = serverKey
        bindings = [ httpBinding ] }

  initDotLiquid ()
  setCSharpNamingConvention ()

  let app =
    choose
      [ serveAssets
        path "/" >=> page "guest/home.liquid" ""
        UserSignup.Suave.webPart getDataCtx sendEmail
        Auth.Suave.webpart getDataCtx
        Wall.Suave.webpart getDataCtx getStreamClient
        Social.Suave.webpart getDataCtx getStreamClient
        UserProfile.Suave.webpart getDataCtx getStreamClient ]

  startWebServer serverConfig app
  0
