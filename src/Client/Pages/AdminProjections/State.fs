module Mediatheca.Client.Pages.AdminProjections.State

open Elmish
open Fable.Core
open Fable.Core.JsInterop
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminProjections.Types

[<Emit("fetch($0)")>]
let private jsFetch (url: string) : JS.Promise<obj> = jsNative

[<Emit("new TextDecoder().decode($0)")>]
let private decodeBytes (value: obj) : string = jsNative

let init () : Model * Cmd<Msg> =
    { Stats = []
      IsLoading = true
      RebuildProgress = Map.empty
      RebuildingNames = Set.empty
      RebuildMessages = Map.empty
      IsRebuildingAll = false
      PendingRebuildAllQueue = [] },
    Cmd.ofMsg Load

/// Consumes the SSE stream from `/api/stream/rebuild-projection/{name}`
/// (Administration.projectionRebuildStreamHandler), dispatching a Msg per
/// server-sent event. Same reader/buffer/`data: ` framing as
/// Settings.State's Steam Family import consumer — deliberately not
/// factored into a shared helper since the two pages' progress payloads and
/// resulting Msg shapes differ.
let private runRebuildStream (projectionName: string) : Cmd<Msg> =
    Cmd.ofEffect (fun dispatch ->
        async {
            try
                let url = sprintf "/api/stream/rebuild-projection/%s" projectionName
                let! response = jsFetch url |> Async.AwaitPromise
                let reader: obj = response?body?getReader()
                let mutable buffer = ""
                let mutable reading = true
                while reading do
                    let! chunk = (reader?read() : JS.Promise<obj>) |> Async.AwaitPromise
                    let isDone: bool = chunk?``done``
                    if isDone then
                        reading <- false
                    else
                        let value: obj = chunk?value
                        let text = decodeBytes value
                        buffer <- buffer + text
                        let mutable idx = buffer.IndexOf("\n\n")
                        while idx >= 0 do
                            let message = buffer.[0..idx-1]
                            buffer <- buffer.[idx+2..]
                            let dataLine =
                                if message.StartsWith("data: ") then message.[6..]
                                else message
                            if dataLine <> "" then
                                let parsed: obj = JS.JSON.parse dataLine
                                let eventType: string = parsed?``type``
                                match eventType with
                                | "progress" ->
                                    let progress: RebuildProgress = {
                                        Position = parsed?position |> int64
                                        Head = parsed?head |> int64
                                        EventsProcessed = parsed?eventsProcessed |> int64
                                    }
                                    dispatch (Rebuild_progress (projectionName, progress))
                                | "rejected" ->
                                    dispatch (Rebuild_rejected (projectionName, parsed?message |> string))
                                | "complete" ->
                                    dispatch (Rebuild_completed projectionName)
                                | "error" ->
                                    dispatch (Rebuild_failed (projectionName, parsed?message |> string))
                                | _ -> ()
                            idx <- buffer.IndexOf("\n\n")
            with ex ->
                dispatch (Rebuild_failed (projectionName, ex.Message))
        } |> Async.StartImmediate
    )

let update (api: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load ->
        { model with IsLoading = true },
        Cmd.OfAsync.perform api.getProjectionStats () Stats_loaded

    | Stats_loaded stats ->
        { model with Stats = stats; IsLoading = false }, Cmd.none

    | Rebuild_clicked name ->
        { model with
            RebuildingNames = Set.add name model.RebuildingNames
            RebuildProgress = Map.remove name model.RebuildProgress
            RebuildMessages = Map.remove name model.RebuildMessages },
        runRebuildStream name

    | Rebuild_all_clicked ->
        match model.Stats |> List.map (fun s -> s.Name) with
        | [] -> model, Cmd.none
        | queue ->
            { model with IsRebuildingAll = true; PendingRebuildAllQueue = queue },
            Cmd.ofMsg Start_next_queued_rebuild

    | Start_next_queued_rebuild ->
        match model.PendingRebuildAllQueue with
        | next :: rest ->
            { model with PendingRebuildAllQueue = rest }, Cmd.ofMsg (Rebuild_clicked next)
        | [] ->
            { model with IsRebuildingAll = false }, Cmd.ofMsg Load

    | Rebuild_progress (name, progress) ->
        { model with RebuildProgress = Map.add name progress model.RebuildProgress }, Cmd.none

    | Rebuild_rejected (name, message) ->
        let model =
            { model with
                RebuildingNames = Set.remove name model.RebuildingNames
                RebuildProgress = Map.remove name model.RebuildProgress
                RebuildMessages = Map.add name message model.RebuildMessages }
        model, (if model.IsRebuildingAll then Cmd.ofMsg Start_next_queued_rebuild else Cmd.none)

    | Rebuild_completed name ->
        let model =
            { model with
                RebuildingNames = Set.remove name model.RebuildingNames
                RebuildProgress = Map.remove name model.RebuildProgress
                RebuildMessages = Map.remove name model.RebuildMessages }
        model,
        Cmd.batch [
            Cmd.ofMsg Load
            if model.IsRebuildingAll then Cmd.ofMsg Start_next_queued_rebuild
        ]

    | Rebuild_failed (name, message) ->
        let model =
            { model with
                RebuildingNames = Set.remove name model.RebuildingNames
                RebuildProgress = Map.remove name model.RebuildProgress
                RebuildMessages = Map.add name message model.RebuildMessages }
        model, (if model.IsRebuildingAll then Cmd.ofMsg Start_next_queued_rebuild else Cmd.none)
