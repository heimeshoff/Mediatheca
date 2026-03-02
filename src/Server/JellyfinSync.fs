namespace Mediatheca.Server

open System
open System.Net.Http
open Microsoft.Data.Sqlite
open Mediatheca.Shared

module JellyfinSync =

    // In-memory sync state (single-user app — simple mutable state is sufficient)
    let mutable private syncInProgress = false
    let mutable private lastSyncTime: DateTime option = None
    let mutable private lastSyncResult: Result<JellyfinImportResult, string> option = None
    let private syncLock = obj ()

    let private cooldownMinutes = 5.0

    /// Initialize last sync time from persisted setting (call at startup)
    let initialize (conn: SqliteConnection) : unit =
        match SettingsStore.getSetting conn "jellyfin_last_sync" with
        | Some iso ->
            match DateTime.TryParse(iso) with
            | true, dt -> lastSyncTime <- Some dt
            | _ -> ()
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
    let triggerSync
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getJellyfinConfig: unit -> Jellyfin.JellyfinConfig)
        (runImport: unit -> Async<Result<JellyfinImportResult, string>>)
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
                                try
                                    eprintfn "[JellyfinSync] Starting background sync..."
                                    let! result = runImport ()
                                    lock syncLock (fun () ->
                                        syncInProgress <- false
                                        lastSyncTime <- Some DateTime.UtcNow
                                        lastSyncResult <- Some result
                                        // Persist last sync time
                                        SettingsStore.setSetting conn "jellyfin_last_sync" (DateTime.UtcNow.ToString("o"))
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
                                    )
                                    eprintfn "[JellyfinSync] Sync error: %s" ex.Message
                            }
                            |> Async.Start
                            SyncStarted
                )
        }
