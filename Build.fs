﻿open System
open Fake.Core
open Fake.IO
open Npgsql

let redirect createProcess =
  createProcess
  |> CreateProcess.redirectOutputIfNotRedirected
  |> CreateProcess.withOutputEvents
       Console.WriteLine
       Console.WriteLine

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

let localDbConnString =
  @"Server=172.19.0.3;Port=5432;Database=FsTweet;User Id=root;Password=root;" // let dbConnection = //   ConnectionString(connString, DatabaseProvider.PostgreSQL)

let connString =
  Environment.environVarOrDefault
    "FSTWEET_DB_CONN_STRING"
    localDbConnString

Environment.setEnvironVar "FSTWEET_DB_CONN_STRING" connString

let migrationAssembly =
  Path.combine buildPath "FsTweet.Db.Migrations.dll"

let projectsToBuild = [ "FsTweet.Web" ]

Target.create
  "Clean"
  (fun _ -> Shell.cleanDir (Path.getFullName "deploy"))

Target.create "InstallClient" (fun _ -> run npm "install" ".")

let dbFilePath = "./src/FsTweet.Web/Db.fs"

Target.create
  "VerifyLocalDbConnString"
  (fun _ ->
    let dbFileContent =
      System.IO.File.ReadAllText dbFilePath

    if not (dbFileContent.Contains(localDbConnString)) then
      failwith "local db connection string mismatch"
  )

let swapDbFileContent (oldValue: string) (newValue: string) =
  let dbFileContent =
    System.IO.File.ReadAllText dbFilePath

  let newDbFileContent =
    dbFileContent.Replace(oldValue, newValue)

  System.IO.File.WriteAllText(dbFilePath, newDbFileContent)

Target.create
  "ReplaceLocalDbConnStringForBuild"
  (fun _ -> swapDbFileContent localDbConnString connString)


Target.create
  "Build"
  (fun _ ->
    projectsToBuild
    |> List.map (fun p ->
      dotnet $"""build -o {buildPath} {Path.combine srcPath p}""" ""
    )
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

Target.create
  "RevertLocalDbConnStringChange"
  (fun _ -> swapDbFileContent connString localDbConnString)

Target.create
  "Run"
  (fun _ ->
    [ createProcess
        (Path.combine buildPath "FsTweet.Web")
        ""
        (Path.getFullName "./build") ]
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
    let srcDir =
      Path.combine srcPath "FsTweet.Web/views"

    [ copyToBuildDir srcDir ]
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

Target.create
  "Assets"
  (fun _ ->
    let srcDir =
      Path.combine srcPath "FsTweet.Web/assets"

    [ copyToBuildDir srcDir ]
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

Target.create
  "BuildMigrations"
  (fun _ ->
    [ "FsTweet.Db.Migrations" ]
    |> List.map (fun p ->
      dotnet $"""build -o {buildPath} {Path.combine srcPath p}""" ""
    )
    |> Seq.toArray
    |> Array.map redirect
    |> Array.Parallel.map Proc.run
    |> ignore
  )

Target.create
  "RunMigrations"
  (fun _ ->
    dotnet
      $"""dotnet-fm migrate -p postgres -c "{connString}" -a {migrationAssembly} --allowDirtyAssemblies"""
      ""
    |> redirect
    |> Proc.run
    |> ignore
  )


open Fake.Core.TargetOperators

let dependencies =
  [ "Clean"
    ==> "BuildMigrations"
    ==> "RunMigrations"
    // ==> "VerifyLocalDbConnString"
    // ==> "ReplaceLocalDbConnStringForBuild"
    ==> "Build"
    // ==> "RevertLocalDbConnStringChange"
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
