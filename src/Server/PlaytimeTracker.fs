namespace Mediatheca.Server

open System
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open System.Text.RegularExpressions
open Mediatheca.Shared

module PlaytimeTracker =

    let private stripHtmlTags (html: string) =
        if String.IsNullOrEmpty(html) then ""
        else Regex.Replace(html, "<[^>]+>", "")

    /// administration-tj8n2: the job connection (`runSync`'s `conn` param) is
    /// dedicated to scheduled-job use but is shared by BOTH jobs (Steam sync,
    /// Series refresh) and can be touched by two ThreadPool threads at once
    /// (catch-up collision, or same-hour daily fire). `Microsoft.Data.Sqlite.
    /// SqliteConnection` is not thread-safe for concurrent command creation/
    /// disposal, so every synchronous DB-touching section reached from
    /// `runSync` acquires `jobLock` for just that section — never across an
    /// awaited HTTP call — so the two jobs' network I/O still overlaps and
    /// only their brief DB moments serialize. Not reentrant: never call this
    /// from code that might already be holding `jobLock` on the same thread.
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    let private generateUniqueSlug (conn: SqliteConnection) (streamIdFn: string -> string) (baseSlug: string) : string =
        let mutable slug = baseSlug
        let mutable suffix = 2
        while EventStore.getStreamPosition conn (streamIdFn slug) >= 0L do
            slug <- sprintf "%s-%d" baseSlug suffix
            suffix <- suffix + 1
        slug

    let private getSyncHour (conn: SqliteConnection) : int =
        SettingsStore.getSetting conn "playtime_sync_hour"
        |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
        |> Option.defaultValue 4

    // Day boundary is (syncHour + 30 minutes) local — so the scheduled 04:00 sync
    // (and any late-firing within 30 min of it) attributes to yesterday's bucket,
    // and a late-night session that ends at 00:30 falls into the previous gaming day.
    let private gamingDayGraceMinutes = 30.0

    let private toGamingDay (syncHour: int) (dt: DateTime) : string =
        dt.AddHours(float -syncHour).AddMinutes(-gamingDayGraceMinutes).ToString("yyyy-MM-dd")

    let private unixTimestampToGamingDay (syncHour: int) (timestamp: int) : string option =
        if timestamp = 0 then None
        else
            let dt = DateTimeOffset.UnixEpoch.AddSeconds(float timestamp).LocalDateTime
            Some (toGamingDay syncHour dt)

    /// The setting `games-h4mrd`'s one-time history migration writes on
    /// success. Its presence (or the absence of any legacy
    /// `Game_play_time_set` events at all — a fresh install) is what
    /// un-gates `runSync` below.
    [<Literal>]
    let private migrationCompletedSettingKey = "play_session_migration_completed"

    /// Pure gate condition for the Steam sync (games-p6vkz): on a legacy
    /// store, every game reconstitutes with `SteamObservedMinutes = 0`
    /// (Game_play_time_set is a mandatory no-op in `Games.evolve`), so an
    /// ungated sync running in the deploy-to-migration window would treat
    /// every game as "first sight" and append `Prior_play_time_recorded`
    /// lumps to streams `games-h4mrd`'s migration hasn't reached yet — its
    /// per-stream idempotency refusal then permanently skips exactly those
    /// streams, leaving their real history unreconstructed. The gate
    /// self-retires: a fresh install has no legacy events and is never
    /// gated; an existing install un-gates the moment the migration
    /// completes. No setting to remove later, no UI.
    let syncGateOpen (hasLegacyPlayTimeEvents: bool) (migrationCompleted: bool) : bool =
        not hasLegacyPlayTimeEvents || migrationCompleted

    // Execute game command — local helper (same pattern as Api.executeCommand, needed because Api.fs is compiled later)

    /// Returns the events actually appended (empty if `decide` was a no-op),
    /// so callers that need to know *what* happened (session count, whether a
    /// promotion fired) don't have to re-read the stream.
    let private executeGameCommandWithEvents
        (conn: SqliteConnection)
        (slug: string)
        (command: Games.GameCommand)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<Games.GameEvent list, string> =

        let streamId = Games.streamId slug
        let storedEvents = EventStore.readStream conn streamId
        let events = storedEvents |> List.choose Games.Serialization.fromStoredEvent
        let state = Games.reconstitute events
        let currentPosition = EventStore.getStreamPosition conn streamId

        match Games.decide state command with
        | Error e -> Error e
        | Ok newEvents ->
            if List.isEmpty newEvents then
                Ok []
            else
                let eventDataList = newEvents |> List.map Games.Serialization.toEventData
                match EventStore.appendToStream conn streamId currentPosition eventDataList with
                | EventStore.ConcurrencyConflict _ ->
                    Error "Concurrency conflict"
                | EventStore.Success _ ->
                    for handler in projectionHandlers do
                        Projection.runProjection conn handler
                    Ok newEvents

    let private executeGameCommand
        (conn: SqliteConnection)
        (slug: string)
        (command: Games.GameCommand)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<unit, string> =
        executeGameCommandWithEvents conn slug command projectionHandlers |> Result.map ignore

    // Manual session API: validate inputs (date format/range, 1440-minute
    // ceiling), then dispatch through `Games.decide` and read the result back
    // from `PlaySessionProjection`. The ceiling and future-date check stay
    // here, deliberately NOT aggregate invariants (games-p6vkz): the aggregate
    // must accept Steam lumps far above 1440 minutes.

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
                    match executeGameCommand slug (Games.Record_play_session (date, minutesPlayed)) with
                    | Error e -> Error e
                    | Ok () ->
                        match PlaySessionProjection.getBySlugAndDay conn slug date with
                        | Some dto -> Ok dto
                        | None -> Error "Failed to retrieve session after insert"

    /// Session identity is the natural key `(GameSlug, Date)` — no synthetic
    /// id. A combined date+minutes edit is decomposed into the two aggregate
    /// primitives, in this order: correct the minutes first (while the
    /// session is still keyed at its original day), then move it — so the
    /// target minutes land on the target day, merging on collision.
    let updatePlaySessionApi
        (conn: SqliteConnection)
        (edit: PlaySessionEdit)
        (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
        : Result<PlaySessionDto, string> =
        match parseSessionDate edit.NewDate with
        | Error e -> Error e
        | Ok _ ->
            match validateMinutes edit.NewMinutes with
            | Error e -> Error e
            | Ok () ->
                match PlaySessionProjection.getBySlugAndDay conn edit.GameSlug edit.Date with
                | None -> Error "Play session not found"
                | Some existing ->
                    let correctResult =
                        if edit.NewMinutes <> existing.MinutesPlayed then
                            executeGameCommand edit.GameSlug (Games.Correct_play_session_minutes (edit.Date, edit.NewMinutes))
                        else Ok ()
                    match correctResult with
                    | Error e -> Error e
                    | Ok () ->
                        let moveResult =
                            if edit.NewDate <> edit.Date then
                                executeGameCommand edit.GameSlug (Games.Move_play_session (edit.Date, edit.NewDate))
                            else Ok ()
                        match moveResult with
                        | Error e -> Error e
                        | Ok () ->
                            match PlaySessionProjection.getBySlugAndDay conn edit.GameSlug edit.NewDate with
                            | Some dto -> Ok dto
                            | None -> Error "Failed to retrieve session after update"

    /// No-op (Ok) if the session doesn't exist — mirrors the old id-keyed
    /// `deletePlaySessionApi`'s idempotent-delete behaviour.
    let deletePlaySessionApi
        (conn: SqliteConnection)
        (slug: string)
        (day: string)
        (executeGameCommand: string -> Games.GameCommand -> Result<unit, string>)
        : Result<unit, string> =
        match PlaySessionProjection.getBySlugAndDay conn slug day with
        | None -> Ok ()
        | Some _ -> executeGameCommand slug (Games.Remove_play_session day)

    let getPlaySessionsForGame (conn: SqliteConnection) (slug: string) : PlaySessionDto list =
        PlaySessionProjection.getForGame conn slug

    let getPlaytimeSummary (conn: SqliteConnection) (fromDate: string) (toDate: string) : PlaytimeSummaryItem list =
        PlaySessionProjection.getPlaytimeSummary conn fromDate toDate

    let getDashboardPlaySessions (conn: SqliteConnection) (days: int) : DashboardPlaySession list =
        PlaySessionProjection.getDashboardPlaySessions conn days

    // Sync status

    let getSyncStatus (conn: SqliteConnection) : PlaytimeSyncStatus =
        let lastSync = SettingsStore.getSetting conn "playtime_last_sync"
        let syncHour = getSyncHour conn
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

    // Main sync logic

    let private createGameFromSteam
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (syncHour: int)
        (today: string)
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
                let slug = withLock jobLock (fun () -> generateUniqueSlug conn Games.streamId baseSlug)
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

                let commitResult =
                    withLock jobLock (fun () ->
                        let result = executeGameCommand conn slug (Games.Add_game gameData) projectionHandlers
                        match result with
                        | Ok () ->
                            executeGameCommand conn slug (Games.Set_steam_app_id steamGame.AppId) projectionHandlers |> ignore
                            if steamGame.PlaytimeMinutes > 0 then
                                let gamingDay =
                                    unixTimestampToGamingDay syncHour steamGame.RtimeLastPlayed
                                    |> Option.defaultValue today
                                executeGameCommand conn slug (Games.Record_steam_observed_total (steamGame.PlaytimeMinutes, gamingDay)) projectionHandlers |> ignore
                            for category in steamCategories do
                                executeGameCommand conn slug (Games.Add_play_mode category) projectionHandlers |> ignore
                            executeGameCommand conn slug (Games.Set_steam_last_played (Steam.unixTimestampToDateString steamGame.RtimeLastPlayed)) projectionHandlers |> ignore
                            executeGameCommand conn slug Games.Mark_as_owned projectionHandlers |> ignore
                            Ok slug
                        | Error e ->
                            Error (sprintf "Failed to create '%s': %s" steamGame.Name e))
                return commitResult
            with ex ->
                return Error (sprintf "Error creating '%s': %s" steamGame.Name ex.Message)
        }

    let runSync
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
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
                    // games-p6vkz: the migration gate — checked once, up
                    // front, exactly like the config-presence check above.
                    // See `syncGateOpen`'s doc comment for the race this closes.
                    let hasLegacyPlayTimeEvents, migrationCompleted =
                        withLock jobLock (fun () ->
                            (EventStore.getSampleEventForType conn "Game_play_time_set").IsSome,
                            (SettingsStore.getSetting conn migrationCompletedSettingKey).IsSome)
                    if not (syncGateOpen hasLegacyPlayTimeEvents migrationCompleted) then
                        let reason =
                            sprintf "Sync skipped: legacy Game_play_time_set events present and '%s' not yet set (play-session history migration has not completed)" migrationCompletedSettingKey
                        eprintfn "[PlaytimeTracker] %s" reason
                        return Error reason
                    else
                    let! recentGames = Steam.getRecentlyPlayedGames httpClient steamConfig
                    let mutable sessionsRecorded = 0
                    let mutable gamesObserved = 0
                    let mutable gamesCreated = 0
                    let mutable gamesPromotedToFocus = 0
                    // Gaming-day boundary is (syncHour + 30 min) — so the daily 04:00 sync
                    // (and late-night sessions ending after midnight) attribute to yesterday.
                    let syncHour = withLock jobLock (fun () -> getSyncHour conn)
                    let today = defaultArg effectiveDate (toGamingDay syncHour DateTime.Now)

                    for steamGame in recentGames do
                        let! slugResult = async {
                            match withLock jobLock (fun () -> GameProjection.findBySteamAppId conn steamGame.AppId) with
                            | Some slug -> return Some (slug, false)
                            | None ->
                                // Try to match by name
                                match withLock jobLock (fun () -> GameProjection.findByName conn steamGame.Name) with
                                | (slug, _) :: _ ->
                                    // Found by name — link steam_app_id
                                    withLock jobLock (fun () ->
                                        executeGameCommand conn slug (Games.Set_steam_app_id steamGame.AppId) projectionHandlers |> ignore)
                                    return Some (slug, false)
                                | [] ->
                                    // Not in library — create new game. createGameFromSteam
                                    // does its own DB locking internally; not wrapped here
                                    // since it awaits HTTP calls (RAWG/Steam images) first.
                                    let! result = createGameFromSteam conn jobLock httpClient getRawgConfig imageBasePath projectionHandlers syncHour today steamGame
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
                            // wasJustCreated games already had their observed
                            // total recorded inside createGameFromSteam.
                            if not wasJustCreated then
                                // Entirely synchronous — no awaited HTTP inside this branch —
                                // so one lock acquisition covers the whole per-game DB section.
                                withLock jobLock (fun () ->
                                    let gamingDay =
                                        match unixTimestampToGamingDay syncHour steamGame.RtimeLastPlayed with
                                        | Some d -> d
                                        | None -> today
                                    match executeGameCommandWithEvents conn slug (Games.Record_steam_observed_total (steamGame.PlaytimeMinutes, gamingDay)) projectionHandlers with
                                    | Error err ->
                                        eprintfn "[PlaytimeTracker] Failed to record observed playtime for %s: %s" slug err
                                    | Ok events ->
                                        gamesObserved <- gamesObserved + 1
                                        let sessionCount =
                                            events
                                            |> List.filter (function Games.Play_session_recorded _ -> true | _ -> false)
                                            |> List.length
                                        sessionsRecorded <- sessionsRecorded + sessionCount
                                        if events |> List.exists (function Games.Game_status_changed InFocus -> true | _ -> false) then
                                            gamesPromotedToFocus <- gamesPromotedToFocus + 1)

                    // Record last sync time
                    withLock jobLock (fun () ->
                        SettingsStore.setSetting conn "playtime_last_sync" (DateTime.UtcNow.ToString("o")))

                    return Ok {
                        SessionsRecorded = sessionsRecorded
                        // Repurposed (games-p6vkz, no snapshot table anymore):
                        // count of games for which a Steam-observed-total was
                        // recorded this run, Steam-sync cursor retired.
                        SnapshotsUpdated = gamesObserved
                        GamesCreated = gamesCreated
                        GamesPromotedToFocus = gamesPromotedToFocus
                    }
            with ex ->
                return Error (sprintf "Playtime sync failed: %s" ex.Message)
        }

    // Note: background scheduling is now handled by the generic
    // ScheduledJobs module registered from Program.fs.
