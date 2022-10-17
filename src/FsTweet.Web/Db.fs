module Database

open FSharp.Data.Sql
open Chessie.ErrorHandling
open Chessie

[<Literal>]
let private connString =
  "Server=172.19.0.3;Port=5432;Database=FsTweet;User Id=root;Password=root;"

[<Literal>]
let private dbVendor =
  Common.DatabaseProviderTypes.POSTGRESQL

type Db =
  SqlDataProvider<ConnectionString=connString, DatabaseVendor=dbVendor, UseOptionTypes=Common.NullableColumnType.OPTION>

type DataContext = Db.dataContext

type GetDataContext = unit -> DataContext

let dataContext (connString: string) : GetDataContext =
  fun _ -> Db.GetDataContext connString

let submitUpdates (ctx: DataContext) =
  ctx.SubmitUpdatesAsync()
  |> Async.AwaitTask
  |> AR.catch

// let toAsyncResult queryable = queryable |> AR.catch
