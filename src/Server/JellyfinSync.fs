namespace Mediatheca.Server

open System
open System.Net.Http
open Microsoft.Data.Sqlite
open Thoth.Json.Net
open Mediatheca.Shared

module JellyfinSync =

    // In-memory sync state (single-user app — simple mutable state is sufficient)
    let mutable private syncInProgress = false
    let mutable private lastSyncTime: DateTime option = None
    let mutable private lastSyncResult: Result<JellyfinImportResult, string> option = None
    let private syncLock = obj ()

    let private cooldownMinutes = 5.0

    // Persisted-result key. The last sync RESULT (counts + error list, or the
    // failure message) is persisted alongside the timestamp so that a silent
    // breakage is no longer invisible across restarts (integration-001).
    let private lastResultKey = "jellyfin_last_sync_result"

    // Result is persisted as JSON: {"ok": <result>} or {"error": "<msg>"}.
    let private encodeResult (result: Result<JellyfinImportResult, string>) : string =
        match result with
        | Ok r ->
            Encode.object [
                "ok", Encode.object [
                    "moviesAdded", Encode.int r.MoviesAdded
                    "episodesAdded", Encode.int r.EpisodesAdded
                    "moviesAutoAdded", Encode.int r.MoviesAutoAdded
                    "seriesAutoAdded", Encode.int r.SeriesAutoAdded
                    "itemsSkipped", Encode.int r.ItemsSkipped
                    "errors", Encode.list (r.Errors |> List.map Encode.string)
                ]
            ]
            |> Encode.toString 0
        | Error e ->
            Encode.object [ "error", Encode.string e ] |> Encode.toString 0

    let private decodeImportResult: Decoder<JellyfinImportResult> =
        Decode.object (fun get -> {
            MoviesAdded = get.Optional.Field "moviesAdded" Decode.int |> Option.defaultValue 0
            EpisodesAdded = get.Optional.Field "episodesAdded" Decode.int |> Option.defaultValue 0
            MoviesAutoAdded = get.Optional.Field "moviesAutoAdded" Decode.int |> Option.defaultValue 0
            SeriesAutoAdded = get.Optional.Field "seriesAutoAdded" Decode.int |> Option.defaultValue 0
            ItemsSkipped = get.Optional.Field "itemsSkipped" Decode.int |> Option.defaultValue 0
            Errors = get.Optional.Field "errors" (Decode.list Decode.string) |> Option.defaultValue []
        })

    let private decodeResult: Decoder<Result<JellyfinImportResult, string>> =
        Decode.oneOf [
            Decode.field "error" Decode.string |> Decode.map Error
            Decode.field "ok" decodeImportResult |> Decode.map Ok
        ]

    /// Initialize last sync time + last result from persisted settings (call at startup)
    let initialize (conn: SqliteConnection) : unit =
        match SettingsStore.getSetting conn "jellyfin_last_sync" with
        | Some iso ->
            match DateTimeOffset.TryParse(iso) with
            | true, dto -> lastSyncTime <- Some dto.UtcDateTime
            | _ -> ()
        | None -> ()
        match SettingsStore.getSetting conn lastResultKey with
        | Some json ->
            match Decode.fromString decodeResult json with
            | Ok result -> lastSyncResult <- Some result
            | Error _ -> ()
        | None -> ()

    /// Get current sync status
    let getSyncStatus () : JellyfinSyncStatus =
        lock syncLock (fun () ->
            if syncInProgress then
                SyncInProgress
            else
                match lastSyncResult with
                | Some (Ok result) ->
                    let timeStr = lastSyncTime |> Option.map (fun dt -> dt.ToString("o"))
                    SyncCompleted (result, timeStr |> Option.defaultValue "")
                | Some (Error err) ->
                    let timeStr = lastSyncTime |> Option.map (fun dt -> dt.ToString("o"))
                    SyncFailed (err, timeStr)
                | None ->
                    let timeStr = lastSyncTime |> Option.map (fun dt -> dt.ToString("o"))
                    SyncIdle timeStr
        )

    /// Trigger a background sync. Returns immediately.
    ///
    /// administration-mz6kp (ADR-0033): `triggerSync` spawns the actual import
    /// via `Async.Start` — a genuinely detached computation that keeps running
    /// after this function (and the request that called it) has already
    /// returned. It cannot borrow a request-scoped `use conn = factory()`
    /// connection from its caller (that connection would already be disposed
    /// by the time the background work runs — a `factory()` result must never
    /// escape a disposing scope). So `triggerSync` takes the `factory` itself
    /// and opens its own connection *inside* the spawned background async,
    /// scoped to exactly that async's lifetime; `runImport` is threaded that
    /// same connection so the import and the sync-result persistence share it.
    let triggerSync
        (factory: unit -> SqliteConnection)
        (httpClient: HttpClient)
        (getJellyfinConfig: unit -> Jellyfin.JellyfinConfig)
        (runImport: SqliteConnection -> Async<Result<JellyfinImportResult, string>>)
        : Async<JellyfinSyncTriggerResult> =
        async {
            return
                lock syncLock (fun () ->
                    // Check if Jellyfin is configured
                    let config = getJellyfinConfig ()
                    if String.IsNullOrWhiteSpace(config.AccessToken) || String.IsNullOrWhiteSpace(config.UserId) then
                        SyncNotConfigured
                    // Check if already in progress
                    elif syncInProgress then
                        SyncAlreadyInProgress
                    // Check cooldown
                    else
                        match lastSyncTime with
                        | Some lastTime when (DateTime.UtcNow - lastTime).TotalMinutes < cooldownMinutes ->
                            SyncCooldownActive (lastTime.ToString("o"))
                        | _ ->
                            syncInProgress <- true
                            // Spawn background sync
                            async {
                                use conn = factory ()
                                try
                                    eprintfn "[JellyfinSync] Starting background sync..."
                                    let! result = runImport conn
                                    lock syncLock (fun () ->
                                        syncInProgress <- false
                                        lastSyncTime <- Some DateTime.UtcNow
                                        lastSyncResult <- Some result
                                        // Persist last sync time AND the result (counts +
                                        // error list / failure message) so a breakage is
                                        // visible across restarts, not just in memory.
                                        SettingsStore.setSetting conn "jellyfin_last_sync" (DateTime.UtcNow.ToString("o"))
                                        SettingsStore.setSetting conn lastResultKey (encodeResult result)
                                    )
                                    match result with
                                    | Ok r ->
                                        eprintfn "[JellyfinSync] Sync complete: %d movies, %d episodes added, %d movies auto-added, %d series auto-added"
                                            r.MoviesAdded r.EpisodesAdded r.MoviesAutoAdded r.SeriesAutoAdded
                                    | Error err ->
                                        eprintfn "[JellyfinSync] Sync failed: %s" err
                                with ex ->
                                    lock syncLock (fun () ->
                                        syncInProgress <- false
                                        lastSyncResult <- Some (Error ex.Message)
                                        SettingsStore.setSetting conn lastResultKey (encodeResult (Error ex.Message))
                                    )
                                    eprintfn "[JellyfinSync] Sync error: %s" ex.Message
                            }
                            |> Async.Start
                            SyncStarted
                )
        }
