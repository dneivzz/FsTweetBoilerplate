module Email

open Chessie.ErrorHandling
open System
open PostmarkDotNet

type Email =
  { To: string
    TemplateId: int64
    PlaceHolders: Map<string, string> }

type SendEmail = Email -> AsyncResult<unit, Exception>

let mapPostmarkResponse response =
  match response with
  | Choice1Of2 (postmarkRes: PostmarkResponse) ->
    match postmarkRes.Status with
    | PostmarkStatus.Success -> ok ()
    | _ ->
      let ex = new Exception(postmarkRes.Message)
      fail ex
  | Choice2Of2 ex -> fail ex

let sendEmailViaPostmark
  senderEmailAddress
  (client: PostmarkClient)
  email
  =
  let msg =
    new TemplatedPostmarkMessage(
      From = senderEmailAddress,
      To = email.To,
      TemplateId = email.TemplateId,
      TemplateModel = email.PlaceHolders
    )

  client.SendMessageAsync(msg)
  |> Async.AwaitTask
  |> Async.Catch
  |> Async.map mapPostmarkResponse
  |> AR

let initSendEmail senderEmailAddress serverToken =
  let client = new PostmarkClient(serverToken)
  sendEmailViaPostmark senderEmailAddress client

let consoleSendEmail email = asyncTrial { printfn "%A" email }
