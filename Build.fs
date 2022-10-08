open Fake.Core
open Fake.IO
open System

let redirect createProcess =
  createProcess
  |> CreateProcess.redirectOutputIfNotRedirected
  |> CreateProcess.withOutputEvents Console.WriteLine Console.WriteLine

let createProcess exe arg dir =
  CreateProcess.fromRawCommandLine exe arg
  |> CreateProcess.withWorkingDirectory dir
  |> CreateProcess.ensureExitCode

let dotnet = createProcess "dotnet"

let npm =
  let npmPath =
    match ProcessUtils.tryFindFileOnPath "npm" with
    | Some path -> path
    | None -> failwith "npm was not found in path."

  createProcess npmPath

let run proc arg dir = proc arg dir |> Proc.run |> ignore

let execContext =
  Context.FakeExecutionContext.Create false "build.fsx" []

Context.setExecutionContext (Context.RuntimeContext.Fake execContext)

let buildPath = Path.getFullName "build"
let srcPath = Path.getFullName "src"

Target.create "Clean" (fun _ -> Shell.cleanDir (Path.getFullName "deploy"))

Target.create "InstallClient" (fun _ -> run npm "install" ".")

let projectsToBuild = [ "FsTweet.Web"; "FsTweet.Db.Migrations" ]

Target.create
  "Build"
  (fun _ ->
    projectsToBuild
    |> List.map (fun p -> dotnet $"""build -o {buildPath} {Path.combine srcPath p}""" "")
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

Target.create
  "Run"
  (fun _ ->
    [ createProcess (Path.combine buildPath "FsTweet.Web") "" (Path.getFullName "./build") ]
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

let copyToBuildDir srcDir =
  let targetDir = buildPath

  createProcess "cp" $"""-R {srcDir} {targetDir}""" ""

Target.create
  "Views"
  (fun _ ->
    let srcDir = Path.combine srcPath "FsTweet.Web/views"

    [ copyToBuildDir srcDir ]
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

Target.create
  "Assets"
  (fun _ ->
    let srcDir = Path.combine srcPath "FsTweet.Web/assets"

    [ copyToBuildDir srcDir ]
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

open Fake.Core.TargetOperators

let dependencies =
  [ "Clean"
    ==> "Build"
    ==> "Views"
    ==> "Assets"
    ==> "Run" ]

[<EntryPoint>]
let main args =
  try
    match args with
    | [| target |] -> Target.runOrDefault target
    | _ -> Target.runOrDefault "Run"

    0
  with e ->
    printfn "%A" e
    1
