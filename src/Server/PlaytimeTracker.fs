namespace Mediatheca.Server

open System
open System.Data
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Shared

module PlaytimeTracker =

    // SQLite table initialization

    let initialize (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS steam_playtime_snapshot (
                steam_app_id  INTEGER PRIMARY KEY,
                game_slug     TEXT NOT NULL,
                total_minutes INTEGER NOT NULL,
                updated_at    TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS game_play_session (
                id             INTEGER PRIMARY KEY AUTOINCREMENT,
                game_slug      TEXT NOT NULL,
                steam_app_id   INTEGER NOT NULL,
                date           TEXT NOT NULL,
                minutes_played INTEGER NOT NULL,
                created_at     TEXT NOT NULL,
                UNIQUE(game_slug, date)
            );

            CREATE INDEX IF NOT EXISTS idx_play_session_slug ON game_play_session(game_slug);
            CREATE INDEX IF NOT EXISTS idx_play_session_date ON game_play_session(date);
        """
        |> Db.exec

    // Snapshot CRUD

    let getLastSnapshot (conn: SqliteConnection) (steamAppId: int) : (int * string) option =
        conn
        |> Db.newCommand "SELECT total_minutes, updated_at FROM steam_playtime_snapshot WHERE steam_app_id = @app_id"
        |> Db.setParams [ "app_id", SqlType.Int32 steamAppId ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            rd.ReadInt32 "total_minutes", rd.ReadString "updated_at")

    let saveSnapshot (conn: SqliteConnection) (steamAppId: int) (slug: string) (totalMinutes: int) : unit =
        conn
        |> Db.newCommand """
            INSERT INTO steam_playtime_snapshot (steam_app_id, game_slug, total_minutes, updated_at)
            VALUES (@app_id, @slug, @minutes, @now)
            ON CONFLICT(steam_app_id) DO UPDATE SET
                game_slug = @slug,
                total_minutes = @minutes,
                updated_at = @now
        """
        |> Db.setParams [
            "app_id", SqlType.Int32 steamAppId
            "slug", SqlType.String slug
            "minutes", SqlType.Int32 totalMinutes
            "now", SqlType.String (DateTime.UtcNow.ToString("o"))
        ]
        |> Db.exec

    // Play session CRUD

    let recordPlaySession (conn: SqliteConnection) (slug: string) (steamAppId: int) (date: string) (minutesPlayed: int) : unit =
        conn
        |> Db.newCommand """
            INSERT OR IGNORE INTO game_play_session (game_slug, steam_app_id, date, minutes_played, created_at)
            VALUES (@slug, @app_id, @date, @minutes, @now)
        """
        |> Db.setParams [
            "slug", SqlType.String slug
            "app_id", SqlType.Int32 steamAppId
            "date", SqlType.String date
            "minutes", SqlType.Int32 minutesPlayed
            "now", SqlType.String (DateTime.UtcNow.ToString("o"))
        ]
        |> Db.exec

    let getPlaySessionsForGame (conn: SqliteConnection) (slug: string) : PlaySessionDto list =
        conn
        |> Db.newCommand "SELECT game_slug, date, minutes_played FROM game_play_session WHERE game_slug = @slug ORDER BY date DESC"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.query (fun (rd: IDataReader) ->
            { PlaySessionDto.GameSlug = rd.ReadString "game_slug"
              Date = rd.ReadString "date"
              MinutesPlayed = rd.ReadInt32 "minutes_played" })

    let getPlaytimeSummary (conn: SqliteConnection) (fromDate: string) (toDate: string) : PlaytimeSummaryItem list =
        conn
        |> Db.newCommand """
            SELECT ps.game_slug,
                   COALESCE(gd.name, ps.game_slug) as game_name,
                   gd.cover_ref,
                   SUM(ps.minutes_played) as total_minutes,
                   COUNT(*) as session_count
            FROM game_play_session ps
            LEFT JOIN game_detail gd ON gd.slug = ps.game_slug
            WHERE ps.date >= @from_date AND ps.date <= @to_date
            GROUP BY ps.game_slug
            ORDER BY total_minutes DESC
        """
        |> Db.setParams [
            "from_date", SqlType.String fromDate
            "to_date", SqlType.String toDate
        ]
        |> Db.query (fun (rd: IDataReader) ->
            { PlaytimeSummaryItem.GameSlug = rd.ReadString "game_slug"
              GameName = rd.ReadString "game_name"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              TotalMinutes = rd.ReadInt32 "total_minutes"
              SessionCount = rd.ReadInt32 "session_count" })

    // Dashboard play sessions (cross-game, last N days)

    let getDashboardPlaySessions (conn: SqliteConnection) (days: int) : DashboardPlaySession list =
        let fromDate = DateTime.UtcNow.AddDays(float -days).ToString("yyyy-MM-dd")
        conn
        |> Db.newCommand """
            SELECT ps.game_slug,
                   COALESCE(gd.name, ps.game_slug) as game_name,
                   gd.cover_ref,
                   ps.date,
                   ps.minutes_played
            FROM game_play_session ps
            LEFT JOIN game_detail gd ON gd.slug = ps.game_slug
            WHERE ps.date >= @from_date
            ORDER BY ps.date
        """
        |> Db.setParams [ "from_date", SqlType.String fromDate ]
        |> Db.query (fun (rd: IDataReader) ->
            { DashboardPlaySession.GameSlug = rd.ReadString "game_slug"
              GameName = rd.ReadString "game_name"
              CoverRef =
                if rd.IsDBNull(rd.GetOrdinal("cover_ref")) then None
                else Some (rd.ReadString "cover_ref")
              Date = rd.ReadString "date"
              MinutesPlayed = rd.ReadInt32 "minutes_played" })

    // Sync status

    let getSyncStatus (conn: SqliteConnection) : PlaytimeSyncStatus =
        let lastSync = SettingsStore.getSetting conn "playtime_last_sync"
        let syncHour =
            SettingsStore.getSetting conn "playtime_sync_hour"
            |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
            |> Option.defaultValue 4
        let steamKey = SettingsStore.getSetting conn "steam_api_key"
        let steamId = SettingsStore.getSetting conn "steam_id"
        let isEnabled =
            steamKey.IsSome && steamId.IsSome
            && not (String.IsNullOrWhiteSpace steamKey.Value)
            && not (String.IsNullOrWhiteSpace steamId.Value)
        let nextSync =
            if isEnabled then
                let now = DateTime.UtcNow
                let todaySync = DateTime(now.Year, now.Month, now.Day, syncHour, 0, 0, DateTimeKind.Utc)
                let next = if now > todaySync then todaySync.AddDays(1.0) else todaySync
                Some (next.ToString("o"))
            else None
        { LastSyncTime = lastSync
          NextSyncTime = nextSync
          IsEnabled = isEnabled
          SyncHourUtc = syncHour }

    // Execute game command — local helper (same pattern as Api.executeCommand, needed because Api.fs is compiled later)

    let private executeGameCommand
        (conn: SqliteConnection)
        (slug: string)
        (command: Games.GameCommand)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<unit, string> =

        let streamId = Games.streamId slug
        let storedEvents = EventStore.readStream conn streamId
        let events = storedEvents |> List.choose Games.Serialization.fromStoredEvent
        let state = Games.reconstitute events
        let currentPosition = EventStore.getStreamPosition conn streamId

        match Games.decide state command with
        | Error e -> Error e
        | Ok newEvents ->
            if List.isEmpty newEvents then
                Ok ()
            else
                let eventDataList = newEvents |> List.map Games.Serialization.toEventData
                match EventStore.appendToStream conn streamId currentPosition eventDataList with
                | EventStore.ConcurrencyConflict _ ->
                    Error "Concurrency conflict"
                | EventStore.Success _ ->
                    for handler in projectionHandlers do
                        Projection.runProjection conn handler
                    Ok ()

    // Main sync logic

    let runSync
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<Result<PlaytimeSyncResult, string>> =
        async {
            try
                let steamConfig = getSteamConfig()
                if String.IsNullOrWhiteSpace(steamConfig.ApiKey) || String.IsNullOrWhiteSpace(steamConfig.SteamId) then
                    return Error "Steam API key and Steam ID must be configured"
                else
                    let! recentGames = Steam.getRecentlyPlayedGames httpClient steamConfig
                    let mutable sessionsRecorded = 0
                    let mutable snapshotsUpdated = 0
                    let today = DateTime.UtcNow.ToString("yyyy-MM-dd")

                    for steamGame in recentGames do
                        match GameProjection.findBySteamAppId conn steamGame.AppId with
                        | None -> () // Game not in library, skip
                        | Some slug ->
                            let currentPlaytime = steamGame.PlaytimeMinutes
                            match getLastSnapshot conn steamGame.AppId with
                            | None ->
                                // First run — record baseline snapshot only, no phantom session
                                saveSnapshot conn steamGame.AppId slug currentPlaytime
                                snapshotsUpdated <- snapshotsUpdated + 1
                            | Some (lastTotal, lastUpdatedAt) ->
                                let delta = currentPlaytime - lastTotal
                                if delta > 0 then
                                    // Determine session date
                                    let sessionDate =
                                        let lastDate =
                                            try DateTime.Parse(lastUpdatedAt).ToString("yyyy-MM-dd")
                                            with _ -> today
                                        if lastDate = today then
                                            today
                                        elif lastDate < today then
                                            // Missed days — use rtime_last_played from Steam if available
                                            match Steam.unixTimestampToDateString steamGame.RtimeLastPlayed with
                                            | Some d -> d
                                            | None -> today
                                        else
                                            today

                                    recordPlaySession conn slug steamGame.AppId sessionDate delta
                                    sessionsRecorded <- sessionsRecorded + 1

                                    // Update game entity's TotalPlayTimeMinutes via event store
                                    executeGameCommand conn slug (Games.Set_play_time currentPlaytime) projectionHandlers |> ignore

                                // Always update snapshot
                                saveSnapshot conn steamGame.AppId slug currentPlaytime
                                snapshotsUpdated <- snapshotsUpdated + 1

                    // Record last sync time
                    SettingsStore.setSetting conn "playtime_last_sync" (DateTime.UtcNow.ToString("o"))

                    return Ok {
                        SessionsRecorded = sessionsRecorded
                        SnapshotsUpdated = snapshotsUpdated
                    }
            with ex ->
                return Error (sprintf "Playtime sync failed: %s" ex.Message)
        }

    // Background timer

    let startBackgroundTimer
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Timer =

        let syncHour =
            SettingsStore.getSetting conn "playtime_sync_hour"
            |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
            |> Option.defaultValue 4

        let runSyncSafe () =
            async {
                try
                    eprintfn "[PlaytimeTracker] Starting daily playtime sync..."
                    match! runSync conn httpClient getSteamConfig projectionHandlers with
                    | Ok result ->
                        eprintfn "[PlaytimeTracker] Sync complete: %d sessions recorded, %d snapshots updated" result.SessionsRecorded result.SnapshotsUpdated
                    | Error err ->
                        eprintfn "[PlaytimeTracker] Sync skipped: %s" err
                with ex ->
                    eprintfn "[PlaytimeTracker] Sync error: %s" ex.Message
            }

        let callback _ =
            let now = DateTime.UtcNow
            // Only run if it's within the sync hour (allows for timer drift)
            // On first run (startup), always run
            runSyncSafe () |> Async.StartImmediate

        let timerCallback = new TimerCallback(callback)

        // Calculate initial delay: run immediately on startup (1 second delay)
        // Then schedule to repeat daily
        let now = DateTime.UtcNow
        let todaySync = DateTime(now.Year, now.Month, now.Day, syncHour, 0, 0, DateTimeKind.Utc)
        let nextSync = if now > todaySync then todaySync.AddDays(1.0) else todaySync
        let untilNext = nextSync - now

        // First fire after 5 seconds (startup catch-up), then daily at sync hour
        let initialDelay = TimeSpan.FromSeconds(5.0)
        let dailyInterval = TimeSpan.FromHours(24.0)

        eprintfn "[PlaytimeTracker] Background timer started. Next scheduled sync at %s UTC (in %.1f hours)" (nextSync.ToString("HH:mm")) untilNext.TotalHours

        new Timer(timerCallback, null, initialDelay, dailyInterval)
