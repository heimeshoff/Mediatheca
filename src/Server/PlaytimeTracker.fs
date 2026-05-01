namespace Mediatheca.Server

open System
open System.Data
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open Donald
open System.Text.RegularExpressions
open Mediatheca.Shared

module PlaytimeTracker =

    let private stripHtmlTags (html: string) =
        if String.IsNullOrEmpty(html) then ""
        else Regex.Replace(html, "<[^>]+>", "")

    let private generateUniqueSlug (conn: SqliteConnection) (streamIdFn: string -> string) (baseSlug: string) : string =
        let mutable slug = baseSlug
        let mutable suffix = 2
        while EventStore.getStreamPosition conn (streamIdFn slug) >= 0L do
            slug <- sprintf "%s-%d" baseSlug suffix
            suffix <- suffix + 1
        slug

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

    // Sentinel for manual sessions: steam_app_id = 0 means "not from Steam".
    // Avoids a schema migration; only the DTO mapping (Source field) depends on this convention.
    [<Literal>]
    let private ManualSteamAppId = 0

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

    let hasAnyPlaySessions (conn: SqliteConnection) (slug: string) : bool =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_play_session WHERE game_slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
        |> Option.map (fun c -> c > 0)
        |> Option.defaultValue false

    let private toPlaySessionDto (rd: IDataReader) : PlaySessionDto =
        let appId = rd.ReadInt32 "steam_app_id"
        let source = if appId = ManualSteamAppId then Manual else SteamSync
        { Id = rd.ReadInt64 "id"
          GameSlug = rd.ReadString "game_slug"
          Date = rd.ReadString "date"
          MinutesPlayed = rd.ReadInt32 "minutes_played"
          Source = source }

    let getPlaySessionsForGame (conn: SqliteConnection) (slug: string) : PlaySessionDto list =
        conn
        |> Db.newCommand "SELECT id, game_slug, date, minutes_played, steam_app_id FROM game_play_session WHERE game_slug = @slug ORDER BY date DESC"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.query toPlaySessionDto

    let getPlaySessionById (conn: SqliteConnection) (sessionId: int64) : PlaySessionDto option =
        conn
        |> Db.newCommand "SELECT id, game_slug, date, minutes_played, steam_app_id FROM game_play_session WHERE id = @id"
        |> Db.setParams [ "id", SqlType.Int64 sessionId ]
        |> Db.querySingle toPlaySessionDto

    // Validation helpers for manual sessions

    let private parseSessionDate (date: string) : Result<DateTime, string> =
        let parsed, dt =
            DateTime.TryParseExact(
                date,
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None)
        if not parsed then Error "Date must be in yyyy-MM-dd format"
        else
            let today = DateTime.Now.Date
            if dt.Date > today then Error "Date cannot be in the future"
            else Ok dt

    let private validateMinutes (minutes: int) : Result<unit, string> =
        if minutes <= 0 then Error "Minutes must be greater than 0"
        elif minutes > 24 * 60 then Error "A single session cannot exceed 24 hours (1440 minutes)"
        else Ok ()

    let private getTotalMinutesForGame (conn: SqliteConnection) (slug: string) : int =
        conn
        |> Db.newCommand "SELECT COALESCE(SUM(minutes_played), 0) as total FROM game_play_session WHERE game_slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "total")
        |> Option.defaultValue 0

    /// Add OR merge: if (slug, date) already exists, sum minutes into the existing row.
    /// Returns (id, totalMinutesForThatDate).
    let upsertManualPlaySession
        (conn: SqliteConnection)
        (slug: string)
        (date: string)
        (minutesPlayed: int)
        : (int64 * int) =
        // Check if a row already exists for (slug, date)
        let existing =
            conn
            |> Db.newCommand "SELECT id, minutes_played FROM game_play_session WHERE game_slug = @slug AND date = @date"
            |> Db.setParams [
                "slug", SqlType.String slug
                "date", SqlType.String date
            ]
            |> Db.querySingle (fun (rd: IDataReader) ->
                rd.ReadInt64 "id", rd.ReadInt32 "minutes_played")
        match existing with
        | Some (id, current) ->
            let newTotal = current + minutesPlayed
            conn
            |> Db.newCommand "UPDATE game_play_session SET minutes_played = @minutes WHERE id = @id"
            |> Db.setParams [
                "minutes", SqlType.Int32 newTotal
                "id", SqlType.Int64 id
            ]
            |> Db.exec
            id, newTotal
        | None ->
            conn
            |> Db.newCommand """
                INSERT INTO game_play_session (game_slug, steam_app_id, date, minutes_played, created_at)
                VALUES (@slug, @app_id, @date, @minutes, @now)
            """
            |> Db.setParams [
                "slug", SqlType.String slug
                "app_id", SqlType.Int32 ManualSteamAppId
                "date", SqlType.String date
                "minutes", SqlType.Int32 minutesPlayed
                "now", SqlType.String (DateTime.UtcNow.ToString("o"))
            ]
            |> Db.exec
            let newId =
                conn
                |> Db.newCommand "SELECT last_insert_rowid() as id"
                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt64 "id")
                |> Option.defaultValue 0L
            newId, minutesPlayed

    /// Edit an existing session by id. If newDate collides with another existing session
    /// for the same game, MERGE (sum minutes into the other row, delete this one).
    /// Returns the resulting session id.
    let updatePlaySession
        (conn: SqliteConnection)
        (sessionId: int64)
        (newDate: string)
        (newMinutes: int)
        : Result<int64, string> =
        match getPlaySessionById conn sessionId with
        | None -> Error "Play session not found"
        | Some existing ->
            // Look for a different row with the same (slug, newDate)
            let collision =
                conn
                |> Db.newCommand """
                    SELECT id, minutes_played FROM game_play_session
                    WHERE game_slug = @slug AND date = @date AND id <> @id
                """
                |> Db.setParams [
                    "slug", SqlType.String existing.GameSlug
                    "date", SqlType.String newDate
                    "id", SqlType.Int64 sessionId
                ]
                |> Db.querySingle (fun (rd: IDataReader) ->
                    rd.ReadInt64 "id", rd.ReadInt32 "minutes_played")
            match collision with
            | Some (otherId, otherMinutes) ->
                // Merge: add new minutes into the other row, delete this one
                let merged = otherMinutes + newMinutes
                conn
                |> Db.newCommand "UPDATE game_play_session SET minutes_played = @minutes WHERE id = @id"
                |> Db.setParams [
                    "minutes", SqlType.Int32 merged
                    "id", SqlType.Int64 otherId
                ]
                |> Db.exec
                conn
                |> Db.newCommand "DELETE FROM game_play_session WHERE id = @id"
                |> Db.setParams [ "id", SqlType.Int64 sessionId ]
                |> Db.exec
                Ok otherId
            | None ->
                conn
                |> Db.newCommand "UPDATE game_play_session SET date = @date, minutes_played = @minutes WHERE id = @id"
                |> Db.setParams [
                    "date", SqlType.String newDate
                    "minutes", SqlType.Int32 newMinutes
                    "id", SqlType.Int64 sessionId
                ]
                |> Db.exec
                Ok sessionId

    /// Delete by id. No-op if the id doesn't exist (returns Ok ()).
    let deletePlaySession
        (conn: SqliteConnection)
        (sessionId: int64)
        : Result<unit, string> =
        conn
        |> Db.newCommand "DELETE FROM game_play_session WHERE id = @id"
        |> Db.setParams [ "id", SqlType.Int64 sessionId ]
        |> Db.exec
        Ok ()

    /// Recompute SUM(minutes_played) for the game and emit Games.Set_play_time.
    let recomputeAndPublishTotal
        (conn: SqliteConnection)
        (slug: string)
        (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
        : unit =
        let total = getTotalMinutesForGame conn slug
        executeGameCommand slug (Games.Set_play_time total) |> ignore

    /// If the game's status is anything other than InFocus, emit Change_status InFocus.
    /// Returns true if the helper actually emitted the event.
    /// Task 048: any new play activity (Steam sync OR manual session) bumps the game back into focus,
    /// regardless of prior status (Backlog, OnHold, Completed, Abandoned, Dismissed all get pulled in).
    let promoteToInFocusIfNeeded
        (conn: SqliteConnection)
        (slug: string)
        (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
        : bool =
        match GameProjection.getGameStatus conn slug with
        | Some InFocus -> false
        | Some _ | None ->
            match executeGameCommand slug (Games.Change_status InFocus) with
            | Ok () -> true
            | Error _ -> false

    // Public-facing manual session API: validate inputs, mutate, recompute total.

    let addManualPlaySessionApi
        (conn: SqliteConnection)
        (slug: string)
        (date: string)
        (minutesPlayed: int)
        (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
        : Result<PlaySessionDto, string> =
        match parseSessionDate date with
        | Error e -> Error e
        | Ok _ ->
            match validateMinutes minutesPlayed with
            | Error e -> Error e
            | Ok () ->
                match GameProjection.getBySlug conn slug with
                | None -> Error "Game not found"
                | Some _ ->
                    let id, _ = upsertManualPlaySession conn slug date minutesPlayed
                    recomputeAndPublishTotal conn slug executeGameCommand
                    // Task 048: a recorded session also bumps the game into focus.
                    promoteToInFocusIfNeeded conn slug executeGameCommand |> ignore
                    match getPlaySessionById conn id with
                    | Some dto -> Ok dto
                    | None -> Error "Failed to retrieve session after insert"

    let updatePlaySessionApi
        (conn: SqliteConnection)
        (sessionId: int64)
        (newDate: string)
        (newMinutes: int)
        (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
        : Result<PlaySessionDto, string> =
        match parseSessionDate newDate with
        | Error e -> Error e
        | Ok _ ->
            match validateMinutes newMinutes with
            | Error e -> Error e
            | Ok () ->
                match getPlaySessionById conn sessionId with
                | None -> Error "Play session not found"
                | Some existing ->
                    match updatePlaySession conn sessionId newDate newMinutes with
                    | Error e -> Error e
                    | Ok resultId ->
                        recomputeAndPublishTotal conn existing.GameSlug executeGameCommand
                        // Task 048: a recorded session also bumps the game into focus.
                        promoteToInFocusIfNeeded conn existing.GameSlug executeGameCommand |> ignore
                        match getPlaySessionById conn resultId with
                        | Some dto -> Ok dto
                        | None -> Error "Failed to retrieve session after update"

    let deletePlaySessionApi
        (conn: SqliteConnection)
        (sessionId: int64)
        (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
        : Result<unit, string> =
        let slugOpt =
            getPlaySessionById conn sessionId
            |> Option.map (fun s -> s.GameSlug)
        match deletePlaySession conn sessionId with
        | Error e -> Error e
        | Ok () ->
            match slugOpt with
            | Some slug -> recomputeAndPublishTotal conn slug executeGameCommand
            | None -> ()
            Ok ()

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
        let fromDate = DateTime.Now.AddDays(float -days).ToString("yyyy-MM-dd")
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
                let now = DateTime.Now
                let todaySync = DateTime(now.Year, now.Month, now.Day, syncHour, 0, 0, DateTimeKind.Local)
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

    let private createGameFromSteam
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (steamGame: SteamOwnedGame)
        : Async<Result<string, string>> =
        async {
            try
                let rawgConfig = getRawgConfig()
                let! rawgResults =
                    if not (String.IsNullOrWhiteSpace(rawgConfig.ApiKey)) then
                        Rawg.searchGames httpClient rawgConfig steamGame.Name None
                    else
                        async { return [] }

                let rawgMatch = rawgResults |> List.tryHead

                let genres, rawgId, rawgRating, year =
                    match rawgMatch with
                    | Some r ->
                        let rawgYear = r.Year |> Option.defaultValue 0
                        r.Genres, Some r.RawgId, r.Rating, rawgYear
                    | None ->
                        [], None, None, 0

                let! storeDetails = Steam.getSteamStoreDetails httpClient steamGame.AppId
                let steamDescription, steamShortDescription, steamWebsiteUrl, steamCategories =
                    match storeDetails with
                    | Ok details ->
                        let desc =
                            if details.AboutTheGame <> "" then stripHtmlTags details.AboutTheGame
                            elif details.DetailedDescription <> "" then stripHtmlTags details.DetailedDescription
                            else ""
                        desc, details.ShortDescription, details.WebsiteUrl, details.Categories
                    | Error _ -> "", "", None, []

                let description =
                    if steamDescription <> "" then steamDescription
                    else ""

                let baseSlug = Slug.gameSlug steamGame.Name (if year > 0 then year else 2000)
                let slug = generateUniqueSlug conn Games.streamId baseSlug
                let! coverRef = Steam.downloadSteamCover httpClient steamGame.AppId slug imageBasePath
                let! backdropRef = Steam.downloadSteamBackdrop httpClient steamGame.AppId slug imageBasePath

                let gameData: Games.GameAddedData = {
                    Name = steamGame.Name
                    Year = if year > 0 then year else 0
                    Genres = genres
                    Description = description
                    ShortDescription = steamShortDescription
                    WebsiteUrl = steamWebsiteUrl
                    CoverRef = coverRef
                    BackdropRef = backdropRef
                    RawgId = rawgId
                    RawgRating = rawgRating
                }

                let result = executeGameCommand conn slug (Games.Add_game gameData) projectionHandlers
                match result with
                | Ok () ->
                    executeGameCommand conn slug (Games.Set_steam_app_id steamGame.AppId) projectionHandlers |> ignore
                    if steamGame.PlaytimeMinutes > 0 then
                        executeGameCommand conn slug (Games.Set_play_time steamGame.PlaytimeMinutes) projectionHandlers |> ignore
                    for category in steamCategories do
                        executeGameCommand conn slug (Games.Add_play_mode category) projectionHandlers |> ignore
                    executeGameCommand conn slug (Games.Set_steam_last_played (Steam.unixTimestampToDateString steamGame.RtimeLastPlayed)) projectionHandlers |> ignore
                    executeGameCommand conn slug Games.Mark_as_owned projectionHandlers |> ignore
                    return Ok slug
                | Error e ->
                    return Error (sprintf "Failed to create '%s': %s" steamGame.Name e)
            with ex ->
                return Error (sprintf "Error creating '%s': %s" steamGame.Name ex.Message)
        }

    let runSync
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (effectiveDate: string option)
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
                    let mutable gamesCreated = 0
                    let mutable gamesPromotedToFocus = 0
                    let today = defaultArg effectiveDate (DateTime.Now.ToString("yyyy-MM-dd"))

                    // Task 048: any new play activity flips the game's status to InFocus
                    // (skipped when already InFocus to avoid redundant events).
                    let runCmdAll s c = executeGameCommand conn s c projectionHandlers
                    let promote slug =
                        if promoteToInFocusIfNeeded conn slug runCmdAll then
                            gamesPromotedToFocus <- gamesPromotedToFocus + 1

                    for steamGame in recentGames do
                        let! slugResult = async {
                            match GameProjection.findBySteamAppId conn steamGame.AppId with
                            | Some slug -> return Some (slug, false)
                            | None ->
                                // Try to match by name
                                match GameProjection.findByName conn steamGame.Name with
                                | (slug, _) :: _ ->
                                    // Found by name — link steam_app_id
                                    executeGameCommand conn slug (Games.Set_steam_app_id steamGame.AppId) projectionHandlers |> ignore
                                    return Some (slug, false)
                                | [] ->
                                    // Not in library — create new game
                                    let! result = createGameFromSteam conn httpClient getRawgConfig imageBasePath projectionHandlers steamGame
                                    match result with
                                    | Ok slug ->
                                        eprintfn "[PlaytimeTracker] Created new game: %s (%s)" steamGame.Name slug
                                        gamesCreated <- gamesCreated + 1
                                        return Some (slug, true)
                                    | Error err ->
                                        eprintfn "[PlaytimeTracker] %s" err
                                        return None
                        }

                        match slugResult with
                        | None -> ()
                        | Some (slug, wasJustCreated) ->
                            let currentPlaytime = steamGame.PlaytimeMinutes
                            match getLastSnapshot conn steamGame.AppId with
                            | None ->
                                // First time seeing this game in the tracker
                                if currentPlaytime > 0 then
                                    // Game with existing playtime but no snapshot — record an initial play session
                                    let sessionDate =
                                        match Steam.unixTimestampToDateString steamGame.RtimeLastPlayed with
                                        | Some d -> d
                                        | None -> today
                                    recordPlaySession conn slug steamGame.AppId sessionDate currentPlaytime
                                    sessionsRecorded <- sessionsRecorded + 1
                                    promote slug
                                // Save baseline snapshot
                                saveSnapshot conn steamGame.AppId slug currentPlaytime
                                snapshotsUpdated <- snapshotsUpdated + 1
                            | Some (lastTotal, lastUpdatedAt) ->
                                // Reconciliation: if snapshot exists but no play sessions, backfill initial session
                                if currentPlaytime > 0 && not (hasAnyPlaySessions conn slug) then
                                    let sessionDate =
                                        match Steam.unixTimestampToDateString steamGame.RtimeLastPlayed with
                                        | Some d -> d
                                        | None -> today
                                    recordPlaySession conn slug steamGame.AppId sessionDate currentPlaytime
                                    sessionsRecorded <- sessionsRecorded + 1
                                    promote slug
                                    eprintfn "[PlaytimeTracker] Reconciled missing play session for %s (%d min)" slug currentPlaytime

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

                                    // Update game entity's TotalPlayTimeMinutes via event store.
                                    // Recompute from SUM(minutes_played) so manual sessions are preserved
                                    // alongside Steam-reported totals.
                                    recomputeAndPublishTotal conn slug runCmdAll
                                    promote slug

                                // Always update snapshot
                                saveSnapshot conn steamGame.AppId slug currentPlaytime
                                snapshotsUpdated <- snapshotsUpdated + 1

                    // Record last sync time
                    SettingsStore.setSetting conn "playtime_last_sync" (DateTime.UtcNow.ToString("o"))

                    return Ok {
                        SessionsRecorded = sessionsRecorded
                        SnapshotsUpdated = snapshotsUpdated
                        GamesCreated = gamesCreated
                        GamesPromotedToFocus = gamesPromotedToFocus
                    }
            with ex ->
                return Error (sprintf "Playtime sync failed: %s" ex.Message)
        }

    // Note: background scheduling is now handled by the generic
    // ScheduledJobs module registered from Program.fs.
