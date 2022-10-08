module FsTweetWeb.Main

open Suave
open System.IO
open System.Reflection
open Suave.DotLiquid
open Suave.Filters
open Suave.Operators
open Suave.Files

let currentPath =
  Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

let initDotLiquid () =
  let templatesDir = Path.Combine(currentPath, "views")

  setTemplatesDir templatesDir

let serveAssets =
  let faviconPath =
    Path.Combine(currentPath, "assets", "images", "favicon.ico")

  choose
    [ pathRegex "/assets/*" >=> browseHome
      path "/favicon.ico" >=> file faviconPath ]

[<EntryPoint>]
let main argv =
  initDotLiquid ()
  setCSharpNamingConvention ()

  let app =
    choose
      [ serveAssets
        path "/" >=> page "guest/home.liquid" ""
        UserSignup.Suave.webPart () ]

  startWebServer defaultConfig app
  0
