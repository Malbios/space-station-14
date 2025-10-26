namespace Content.BoleroSpike.Client

open System
open System.Net
open System.Net.Http
open System.Text
open System.Text.Json
open Bolero
open Bolero.Html
open Elmish
open Microsoft.AspNetCore.Components

[<CLIMutable>]
type ServerStatus =
    { Name: string option
      Map: string option
      Players: int option
      SoftMaxPlayers: int option
      PanicBunker: bool option
      RunLevel: int option
      RoundId: int option
      RoundStartTime: DateTimeOffset option }

module private ServerStatusParsing =
    let private tryGetProperty (json: JsonElement) (name: string) =
        match json.TryGetProperty(name) with
        | true, value -> Some value
        | _ -> None

    let private tryGetString element =
        if element.ValueKind = JsonValueKind.String then
            element.GetString() |> Option.ofObj
        else
            None

    let private tryGetInt element =
        if element.ValueKind = JsonValueKind.Number then
            match element.TryGetInt32() with
            | true, value -> Some value
            | _ -> None
        else
            None

    let private tryGetBool element =
        if element.ValueKind = JsonValueKind.True then
            Some true
        elif element.ValueKind = JsonValueKind.False then
            Some false
        else
            None

    let private tryGetInstant (element: JsonElement) =
        if element.ValueKind = JsonValueKind.String then
            match element.GetString() |> Option.ofObj with
            | Some value ->
                match DateTimeOffset.TryParse(value) with
                | true, timestamp -> Some timestamp
                | _ -> None
            | None -> None
        else
            None

    let parse (rawJson: string) : ServerStatus =
        use document = JsonDocument.Parse(rawJson)
        let root = document.RootElement

        let getString name =
            tryGetProperty root name |> Option.bind tryGetString

        let getInt name =
            tryGetProperty root name |> Option.bind tryGetInt

        let getBool name =
            tryGetProperty root name |> Option.bind tryGetBool

        let getInstant name =
            tryGetProperty root name |> Option.bind tryGetInstant

        { Name = getString "name"
          Map = getString "map"
          Players = getInt "players"
          SoftMaxPlayers = getInt "soft_max_players"
          PanicBunker = getBool "panic_bunker"
          RunLevel = getInt "run_level"
          RoundId = getInt "round_id"
          RoundStartTime = getInstant "round_start_time" }

module private StatusVisualization =
    let private htmlEncode (value: string) = WebUtility.HtmlEncode(value)

    let private describePlayers players softMax =
        match players, softMax with
        | Some count, Some cap -> sprintf "%d / %d players" count cap
        | Some count, None -> sprintf "%d players" count
        | None, Some cap -> sprintf "? / %d players" cap
        | None, None -> "Player count unavailable"

    let private describeRunLevel runLevel =
        match runLevel with
        | Some 0 -> "Initializing"
        | Some 1 -> "Lobby"
        | Some 2 -> "Pre-round"
        | Some 3 -> "In round"
        | Some level -> sprintf "Run level %d" level
        | None -> "Run level unknown"

    let private describeBunker bunker =
        match bunker with
        | Some true -> "Panic bunker enabled"
        | Some false -> "Panic bunker disabled"
        | None -> "Panic bunker status unknown"

    let toDataUri (status: ServerStatus) (retrievedAt: DateTimeOffset) =
        let name = status.Name |> Option.defaultValue "Unknown SS14 server"
        let mapName = status.Map |> Option.defaultValue "Unknown map"
        let players = describePlayers status.Players status.SoftMaxPlayers
        let bunker = describeBunker status.PanicBunker
        let runLevel = describeRunLevel status.RunLevel
        let roundId = status.RoundId |> Option.map string |> Option.defaultValue "?"
        let roundStart =
            status.RoundStartTime
            |> Option.map (fun timestamp -> timestamp.ToLocalTime().ToString("g"))
            |> Option.defaultValue "Unavailable"

        let retrieved = retrievedAt.ToLocalTime().ToString("T")

        let svg =
            $"""
<svg xmlns="http://www.w3.org/2000/svg" width="360" height="200" viewBox="0 0 360 200">
  <defs>
    <linearGradient id="status-bg" x1="0" x2="1" y1="0" y2="1">
      <stop offset="0%" stop-color="#1b2735" />
      <stop offset="100%" stop-color="#090a0f" />
    </linearGradient>
  </defs>
  <rect width="360" height="200" fill="url(#status-bg)" rx="16" />
  <text x="24" y="48" fill="#ffffff" font-family="'Segoe UI', sans-serif" font-size="22" font-weight="600">{htmlEncode name}</text>
  <text x="24" y="78" fill="#b3c5ff" font-family="'Segoe UI', sans-serif" font-size="16">Map: {htmlEncode mapName}</text>
  <text x="24" y="106" fill="#ffe082" font-family="'Segoe UI', sans-serif" font-size="18" font-weight="500">{htmlEncode players}</text>
  <text x="24" y="134" fill="#c8d0ff" font-family="'Segoe UI', sans-serif" font-size="14">Round #{htmlEncode roundId} · {htmlEncode runLevel}</text>
  <text x="24" y="156" fill="#c8ffc8" font-family="'Segoe UI', sans-serif" font-size="14">{htmlEncode bunker}</text>
  <text x="24" y="178" fill="#8aa0ff" font-family="'Segoe UI', sans-serif" font-size="12">Round start: {htmlEncode roundStart} · Retrieved: {htmlEncode retrieved}</text>
</svg>
"""

        let bytes = Encoding.UTF8.GetBytes(svg)
        "data:image/svg+xml;base64," + Convert.ToBase64String(bytes)

[<CLIMutable>]
type StatusPayload =
    { Status: ServerStatus
      RawJson: string
      RetrievedAt: DateTimeOffset }

type Model =
    { StatusMessage: string
      ServerBaseAddress: string
      Status: ServerStatus option
      RawJson: string option
      RetrievedAt: DateTimeOffset option
      ImageDataUri: string option }

[<RequireQualifiedAccess>]
type Message =
    | Fetch
    | StatusLoaded of StatusPayload
    | LoadFailed of string

module private Commands =
    let fetchStatus (http: HttpClient) =
        async {
            let! rawJson = http.GetStringAsync("status") |> Async.AwaitTask
            let status = ServerStatusParsing.parse rawJson
            let retrievedAt = DateTimeOffset.UtcNow
            return { Status = status; RawJson = rawJson; RetrievedAt = retrievedAt }
        }

    let load http =
        Cmd.OfAsync.either
            (fun () -> fetchStatus http)
            ()
            Message.StatusLoaded
            (fun ex -> Message.LoadFailed ex.Message)

module private View =
    let statusBadge message =
        p [ attr.``class`` "status" ] [ text message ]

    let renderDetails (status: ServerStatus) (retrievedAt: DateTimeOffset option) (rawJson: string option) =
        div [ attr.``class`` "status-card" ] [
            h2 [] [ text (status.Name |> Option.defaultValue "Unknown SS14 server") ]
            dl [] [
                match status.Map with
                | Some value ->
                    yield dt [] [ text "Map" ]
                    yield dd [] [ text value ]
                | None -> ()
                match status.Players, status.SoftMaxPlayers with
                | Some players, Some softMax ->
                    yield dt [] [ text "Players" ]
                    yield dd [] [ text (sprintf "%d / %d" players softMax) ]
                | Some players, None ->
                    yield dt [] [ text "Players" ]
                    yield dd [] [ text (string players) ]
                | None, Some softMax ->
                    yield dt [] [ text "Players" ]
                    yield dd [] [ text (sprintf "? / %d" softMax) ]
                | None, None -> ()
                match status.RoundId with
                | Some roundId ->
                    yield dt [] [ text "Round" ]
                    yield dd [] [ text (string roundId) ]
                | None -> ()
                match status.RunLevel with
                | Some runLevel ->
                    yield dt [] [ text "Run level" ]
                    yield dd [] [ text (string runLevel) ]
                | None -> ()
                match status.PanicBunker with
                | Some value ->
                    yield dt [] [ text "Panic bunker" ]
                    yield dd [] [ text (if value then "Enabled" else "Disabled") ]
                | None -> ()
                match status.RoundStartTime with
                | Some timestamp ->
                    yield dt [] [ text "Round start" ]
                    yield dd [] [ text (timestamp.ToLocalTime().ToString("F")) ]
                | None -> ()
                match retrievedAt with
                | Some timestamp ->
                    yield dt [] [ text "Last updated" ]
                    yield dd [] [ text (timestamp.ToLocalTime().ToString("F")) ]
                | None -> ()
            ]
            match rawJson with
            | Some json when not (String.IsNullOrWhiteSpace json) ->
                details [] [
                    summary [] [ text "Raw status JSON" ]
                    pre [] [ text json ]
                ]
            | _ -> empty
        ]

    let renderImage imageData =
        img [
            attr.``class`` "status-preview"
            attr.src imageData
            attr.alt "Generated summary card for the connected SS14 server"
        ]

    let root model dispatch =
        let connectedTo =
            if String.IsNullOrWhiteSpace model.ServerBaseAddress then
                "an SS14 server"
            else
                $"the SS14 server at {model.ServerBaseAddress}"

        main' [ attr.``class`` "container" ] [
            h1 [] [ text "Bolero WASM SS14 Spike" ]
            p [] [ text "This WebAssembly client fetches live status information from a Space Station 14 server and visualizes it." ]
            p [ attr.``class`` "server-target" ] [ text ($"Targeting {connectedTo}.") ]
            button [ on.click (fun _ -> dispatch Message.Fetch) ] [ text "Refresh status" ]
            statusBadge model.StatusMessage
            match model.ImageDataUri with
            | Some image -> renderImage image
            | None -> empty
            match model.Status with
            | Some status -> renderDetails status model.RetrievedAt model.RawJson
            | None -> empty
        ]

open Commands
open View

type Main() =
    inherit ProgramComponent<Model, Message>()

    [<Inject>]
    member val HttpClient = Unchecked.defaultof<HttpClient> with get, set

    override this.Program =
        let baseAddress =
            match this.HttpClient.BaseAddress with
            | null -> ""
            | uri -> uri.ToString()

        let init () =
            { StatusMessage = "Waiting to contact the SS14 server..."
              ServerBaseAddress = baseAddress
              Status = None
              RawJson = None
              RetrievedAt = None
              ImageDataUri = None }, Cmd.ofMsg Message.Fetch

        let update msg model =
            match msg with
            | Message.Fetch ->
                let message =
                    if String.IsNullOrWhiteSpace model.ServerBaseAddress then
                        "Contacting SS14 server status endpoint..."
                    else
                        $"Contacting SS14 server at {model.ServerBaseAddress}status..."
                { model with
                    StatusMessage = message
                    RawJson = None
                    RetrievedAt = None
                    ImageDataUri = None }
                , load this.HttpClient
            | Message.StatusLoaded payload ->
                let image = StatusVisualization.toDataUri payload.Status payload.RetrievedAt
                let statusMessage =
                    payload.Status.Name
                    |> Option.map (fun name -> $"Connected to {name}.")
                    |> Option.defaultValue "Received server status."

                { model with
                    StatusMessage = statusMessage
                    Status = Some payload.Status
                    RawJson = Some payload.RawJson
                    RetrievedAt = Some payload.RetrievedAt
                    ImageDataUri = Some image }, Cmd.none
            | Message.LoadFailed error ->
                { model with
                    StatusMessage = $"Failed to contact server: {error}"
                    Status = None
                    RawJson = None
                    RetrievedAt = None
                    ImageDataUri = None }, Cmd.none

        let view model dispatch = root model dispatch

        Program.mkProgram init update view
