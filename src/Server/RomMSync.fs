namespace Mediatheca.Server

open System
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open Mediatheca.Shared

/// integration-jkbm1 (ADR-0088): the RomM sync -- pages through RomM's play
/// sessions, keeps only the closed ones on a selected platform, matches or
/// creates the corresponding Game, links it via `Set_romm_rom_id`, and
/// imports the sessions through `Record_romm_play_session`
/// (games-rmxg2's session-id cursor). Compiled after `PlaytimeTracker.fs`
/// (`Server.fsproj`) so it can reuse `PlaytimeTracker.getSyncHour`/
/// `toGamingDay` -- the SAME `playtime_sync_hour` boundary Steam and manual
/// sessions use, never a separate `romm_sync_hour` boundary (the task's own
/// acceptance criterion). Mirrors `PlaytimeTracker.fs`/`AudibleSync.fs`'s
/// shape: compiled before `Api.fs`, so it carries its own local
/// command-execution helper rather than reaching into `Api.fs`'s private
/// one.
module RomMSync =

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
                | EventStore.ConcurrencyConflict _ -> Error "Concurrency conflict"
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

    /// Lowercase, punctuation/symbols -> spaces, collapsed whitespace --
    /// deliberately simpler than `Steam.normalizeName` (no edition-suffix
    /// stripping): RomM names are already close to canonical, unlike
    /// Steam's storefront listing titles.
    let normalizeName (name: string) : string =
        if String.IsNullOrWhiteSpace name then ""
        else
            let mutable n = name.ToLowerInvariant()
            n <- Text.RegularExpressions.Regex.Replace(n, @"[\p{P}\p{S}]", " ")
            n <- Text.RegularExpressions.Regex.Replace(n, @"\s+", " ")
            n.Trim()

    /// Step 2 of the task's three-step "find or create" flow: matches an
    /// existing library game by normalized name, plus release year when
    /// BOTH the rom and the candidate have one (a candidate with no known
    /// year, `year <= 0`, is not excluded by a year mismatch -- there is
    /// nothing to mismatch against). Returns every matching slug; the
    /// caller treats more than one match as ambiguous and skips the rom
    /// entirely rather than guessing (builder decision, 2026-09-25).
    let matchByNormalizedNameAndYear (candidates: (string * string * int) list) (romName: string) (romYear: int option) : string list =
        let normalizedRomName = normalizeName romName
        candidates
        |> List.filter (fun (_slug, name, year) ->
            normalizeName name = normalizedRomName
            && (match romYear, year with
                | Some ry, gy when gy > 0 -> ry = gy
                | _ -> true))
        |> List.map (fun (slug, _, _) -> slug)

    let private createGameFromRomM
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (config: RomM.RomMConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (rom: RomM.RomMRomDetail)
        : Async<Result<string, string>> =
        async {
            try
                let year = rom.ReleaseYear |> Option.defaultValue 0
                let baseSlug = Slug.gameSlug rom.Name (if year > 0 then year else 2000)
                let slug = withLock jobLock (fun () -> generateUniqueSlug conn Games.streamId baseSlug)
                let! coverRef =
                    match rom.CoverUrl with
                    | Some url -> RomM.downloadCover httpClient config url slug imageBasePath
                    | None -> async { return None }
                let summary = rom.Summary |> Option.defaultValue ""
                let gameData: Games.GameAddedData = {
                    Name = rom.Name
                    Year = year
                    Genres = rom.Genres
                    Description = summary
                    ShortDescription = ""
                    WebsiteUrl = None
                    CoverRef = coverRef
                    BackdropRef = None
                    RawgId = None
                    RawgRating = None
                }
                let commitResult =
                    withLock jobLock (fun () ->
                        match executeGameCommand conn slug (Games.Add_game gameData) projectionHandlers with
                        | Ok () ->
                            // The identity-card path Steam creation uses
                            // (games-v4nqe/ADR-0045) -- description lands in
                            // the cache tier, never on the event.
                            MetadataCache.upsertGameIdentityCard conn slug {
                                Description = summary
                                ShortDescription = ""
                                WebsiteUrl = None
                            }
                            Ok slug
                        | Error e -> Error (sprintf "Failed to create '%s': %s" rom.Name e))
                return commitResult
            with ex ->
                return Error (sprintf "Error creating '%s': %s" rom.Name ex.Message)
        }

    /// The outcome of the task's three-step "find or create" flow for one
    /// unlinked rom, distinguishing the three branches so the caller's
    /// summary counters (`GamesCreated` vs `GamesLinked`) stay accurate
    /// without re-deriving "was this a create or a match" from side
    /// effects.
    type private ResolveOutcome =
        | AlreadyLinked of slug: string
        | Matched of slug: string
        | Created of slug: string
        | Ambiguous
        | Failed of string

    /// Resolves the slug to import `rom`'s sessions into, per the task's
    /// three-step flow. Matching/creation failures degrade to `Failed`,
    /// fault-isolated per rom, mirroring `PlaytimeTracker.runSync`'s
    /// per-game degrade-and-continue shape.
    let private resolveSlug
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (config: RomM.RomMConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (rom: RomM.RomMRomDetail)
        : Async<ResolveOutcome> =
        async {
            match withLock jobLock (fun () -> GameProjection.findByRommRomId conn rom.Id) with
            | Some slug ->
                // Already linked -- no matching/creation, just import.
                return AlreadyLinked slug
            | None ->
                let candidates = withLock jobLock (fun () -> GameProjection.allForNameMatching conn)
                match matchByNormalizedNameAndYear candidates rom.Name rom.ReleaseYear with
                | [ slug ] ->
                    // If attaching the rom id fails (e.g. a concurrency
                    // conflict, or the matched game is no longer Active),
                    // the rom must NOT be treated as linked -- no sessions
                    // are imported and it isn't counted in `GamesLinked`,
                    // exactly like a creation failure below. It is retried
                    // on the next run, same as an ambiguous match.
                    match withLock jobLock (fun () -> executeGameCommand conn slug (Games.Set_romm_rom_id rom.Id) projectionHandlers) with
                    | Ok () -> return Matched slug
                    | Error e -> return Failed (sprintf "Failed to link '%s' to rom %d: %s" rom.Name rom.Id e)
                | [] ->
                    let! created = createGameFromRomM conn jobLock httpClient config imageBasePath projectionHandlers rom
                    match created with
                    | Ok slug ->
                        match withLock jobLock (fun () -> executeGameCommand conn slug (Games.Set_romm_rom_id rom.Id) projectionHandlers) with
                        | Ok () -> return Created slug
                        | Error e -> return Failed (sprintf "Created '%s' but failed to link rom %d: %s" rom.Name rom.Id e)
                    | Error e ->
                        return Failed e
                | _ :: _ :: _ ->
                    // Ambiguous -- more than one library game matches by
                    // name (+ year). Skip entirely: nothing is linked or
                    // created, its sessions aren't imported this run
                    // (builder decision, 2026-09-25).
                    return Ambiguous
        }

    let private importSessionsForRom
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (syncHour: int)
        (slug: string)
        (closedSessions: RomM.RomMPlaySession list)
        (projectionHandlers: Projection.ProjectionHandler list)
        : int * int =
        // Group this rom's closed sessions onto their gaming day (server-local,
        // via the SAME `playtime_sync_hour` boundary Steam/manual sessions use
        // -- never a separate `romm_sync_hour`), then emit at most ONE
        // `Record_romm_play_session` per (game, gaming day) this run.
        let byDay =
            closedSessions
            |> List.map (fun s ->
                let minutes = RomM.minutesFromDurationMs (s.DurationMs |> Option.defaultValue 0L)
                let localStart = s.StartTime.ToLocalTime()
                let gamingDay = PlaytimeTracker.toGamingDay syncHour localStart
                gamingDay, (string s.Id, minutes))
            |> List.groupBy fst
            |> List.map (fun (day, entries) -> day, entries |> List.map snd)

        let mutable sessionsRecorded = 0
        let mutable promotedToFocus = 0
        for (day, sessions) in byDay do
            withLock jobLock (fun () ->
                match executeGameCommandWithEvents conn slug (Games.Record_romm_play_session (day, sessions)) projectionHandlers with
                | Error err ->
                    eprintfn "[RomMSync] Failed to record sessions for %s on %s: %s" slug day err
                | Ok events ->
                    let recorded =
                        events |> List.filter (function Games.Play_session_recorded _ -> true | _ -> false) |> List.length
                    sessionsRecorded <- sessionsRecorded + recorded
                    if events |> List.exists (function Games.Game_status_changed InFocus -> true | _ -> false) then
                        promotedToFocus <- promotedToFocus + 1)
        sessionsRecorded, promotedToFocus

    let runSync
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (getRomMConfig: unit -> RomM.RomMConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<Result<RomMSyncResult, string>> =
        async {
            try
                let config = getRomMConfig ()
                if String.IsNullOrWhiteSpace(config.BaseUrl) || String.IsNullOrWhiteSpace(config.ApiToken) then
                    return Error "RomM is not configured -- set a base URL and API token in Settings"
                else
                    let! sessionsResult = RomM.getAllPlaySessions httpClient config
                    match sessionsResult with
                    | Error RomM.TokenRejected ->
                        withLock jobLock (fun () -> SettingsStore.setSetting conn "romm_last_error" RomM.tokenRejectedMessage)
                        return Error RomM.tokenRejectedMessage
                    | Error (RomM.RomMOtherFailure m) ->
                        return Error (sprintf "RomM sync failed: %s" m)
                    | Ok allSessions ->
                        // A successful fetch (any status, even zero sessions)
                        // is unambiguous proof the token is valid -- unlike
                        // Steam's "recently played" endpoint, an empty RomM
                        // response here carries no privacy-setting ambiguity.
                        withLock jobLock (fun () -> SettingsStore.deleteSetting conn "romm_last_error")

                        let syncHour = withLock jobLock (fun () -> PlaytimeTracker.getSyncHour conn)
                        let selectedPlatforms = config.SelectedPlatformIds |> Set.ofList

                        let closedByRom =
                            allSessions
                            |> List.filter (fun s -> s.EndTime.IsSome)
                            |> List.groupBy (fun s -> s.RomId)

                        let mutable sessionsRecorded = 0
                        let mutable gamesCreated = 0
                        let mutable gamesLinked = 0
                        let mutable gamesPromotedToFocus = 0
                        let mutable ambiguous : string list = []
                        let mutable tokenRejectedMidRun = false

                        for (romId, sessions) in closedByRom do
                            if not tokenRejectedMidRun then
                                let! romResult = RomM.getRomDetail httpClient config romId
                                match romResult with
                                | Error RomM.TokenRejected ->
                                    withLock jobLock (fun () -> SettingsStore.setSetting conn "romm_last_error" RomM.tokenRejectedMessage)
                                    tokenRejectedMidRun <- true
                                | Error (RomM.RomMOtherFailure m) ->
                                    eprintfn "[RomMSync] Skipping rom %d: %s" romId m
                                | Ok rom when not (selectedPlatforms |> Set.contains rom.PlatformId) ->
                                    () // Not on a selected platform -- never touched.
                                | Ok rom ->
                                    let! slugOutcome = resolveSlug conn jobLock httpClient config imageBasePath projectionHandlers rom
                                    let importFor slug =
                                        // integration-q748k (ADR-0088 concept extended): record this
                                        // rom's platform slug for every rom that ends up linked this
                                        // run (already-linked, matched, or newly created) -- ambiguous/
                                        // failed roms below never call `importFor` at all, so nothing
                                        // is recorded for a rom that has no Game to join through.
                                        withLock jobLock (fun () -> GameProjection.upsertRommRomPlatform conn rom.Id rom.PlatformSlug)
                                        let recorded, promoted =
                                            importSessionsForRom conn jobLock syncHour slug sessions projectionHandlers
                                        sessionsRecorded <- sessionsRecorded + recorded
                                        gamesPromotedToFocus <- gamesPromotedToFocus + promoted
                                    match slugOutcome with
                                    | Ambiguous ->
                                        ambiguous <- ambiguous @ [ rom.Name ]
                                    | Failed e ->
                                        eprintfn "[RomMSync] %s" e
                                    | AlreadyLinked slug ->
                                        importFor slug
                                    | Matched slug ->
                                        gamesLinked <- gamesLinked + 1
                                        importFor slug
                                    | Created slug ->
                                        gamesCreated <- gamesCreated + 1
                                        importFor slug

                        if tokenRejectedMidRun then
                            return Error RomM.tokenRejectedMessage
                        else
                            withLock jobLock (fun () ->
                                SettingsStore.setSetting conn "romm_last_sync" (DateTime.UtcNow.ToString("o")))
                            return Ok {
                                SessionsRecorded = sessionsRecorded
                                GamesCreated = gamesCreated
                                GamesLinked = gamesLinked
                                GamesPromotedToFocus = gamesPromotedToFocus
                                Ambiguous = ambiguous
                            }
            with ex ->
                return Error (sprintf "RomM sync failed: %s" ex.Message)
        }
