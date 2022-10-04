module FsTweetWeb.Main

open Suave
open Suave.Successful
open System.IO
open System.Reflection
open Suave.DotLiquid

let currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)

let initDotLiquid () =
    let templatesDir = Path.Combine(currentPath, "views")
    setTemplatesDir templatesDir

[<EntryPoint>]
let main argv =
    initDotLiquid ()
    setCSharpNamingConvention ()
    startWebServer defaultConfig (OK "Hello World!")
    0
