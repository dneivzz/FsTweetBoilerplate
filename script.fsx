#if FAKE
#r "paket:
nuget Suave"
#endif
#load "./.fake/script.fsx/intellisense.fsx"

open Suave.Utils
open System

Crypto.generateKey Crypto.KeyLength
|> Convert.ToBase64String
|> printfn "%s"
