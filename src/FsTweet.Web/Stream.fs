[<RequireQualifiedAccess>]
module GetStream

open Stream

type Config =
  { ApiSecret: string
    ApiKey: string
    AppId: string }

type Client =
  { Config: Config
    StreamClient: StreamClient }

let newClient config =
  { StreamClient = new StreamClient(config.ApiKey, config.ApiSecret)
    Config = config }

let userFeedAndToken getStreamClient userId =
  getStreamClient.StreamClient.Feed("user", userId.ToString()),
  getStreamClient.StreamClient.CreateUserToken(userId.ToString())

let timeLineFeed getStreamClient (userId: int) =
  getStreamClient.StreamClient.Feed("timeline", userId.ToString())
