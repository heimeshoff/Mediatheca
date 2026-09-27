module Mediatheca.Tests.RomMSyncTests

/// integration-jkbm1 (ADR-0088): fixture-driven coverage of `RomMSync.
/// runSync` -- the orchestration layer that pages RomM's play sessions,
/// keeps only closed ones on a selected platform, runs the three-step
/// find-or-create flow, and imports sessions via `Games.
/// Record_romm_play_session`. No live RomM instance -- every HTTP call
/// goes through a stub `HttpMessageHandler` routed by path, mirroring
/// `AudibleLibrarySyncTests.fs`'s shape for `AudibleSync.runProgressSync`.

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

type private RoutedStubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, Text.Encoding.UTF8, "application/json")
    resp

let private notFound () = new HttpResponseMessage(HttpStatusCode.NotFound)

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers = [ GameProjection.handler; PlaySessionProjection.handler ]
let private noImagesDir = "test-fixtures-do-not-exist/images"

let private config: RomM.RomMConfig =
    { BaseUrl = "https://romm.test"; ApiToken = "rmm_test_token"; SelectedPlatformIds = [ 1 ] }

/// `rom_user` is deliberately included in every rom-detail fixture below
/// (the live wire shape always carries it) -- proving decoding tolerates
/// it while `RomM.RomMRomDetail` never models it at all (the acceptance
/// criterion's enforcement mechanism, pinned directly in `RomMTests.fs`).
let private romDetailJson (id: int) (name: string) (platformId: int) (platformSlug: string) (releaseYear: int option) (coverUrl: string option) : string =
    let firstReleaseDate =
        // `metadatum.first_release_date` is Unix MILLISECONDS, not seconds
        // (iteration 2 -- confirmed against a live recording, see
        // `RomMTests.fs`'s `romDetailFixture` doc comment).
        releaseYear
        |> Option.map (fun y -> DateTimeOffset(DateTime(y, 6, 1, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeMilliseconds() |> string)
        |> Option.defaultValue "null"
    let cover = coverUrl |> Option.map (sprintf "\"%s\"") |> Option.defaultValue "null"
    sprintf
        """{"id":%d,"name":"%s","summary":"Summary of %s","platform_id":%d,"platform_slug":"%s","url_cover":%s,"metadatum":{"genres":["Platformer"],"companies":["Nintendo"],"first_release_date":%s},"rom_user":{"status":"finished","completion":100,"rating":5,"backlogged":false,"now_playing":true}}"""
        id name name platformId platformSlug cover firstReleaseDate

let private isoUtc (dt: DateTime) = dt.ToUniversalTime().ToString("o")

let private sessionJson (id: int) (romId: int) (startUtc: DateTime) (durationMs: int64 option) (closed: bool) : string =
    let endTime =
        if closed then sprintf "\"%s\"" (isoUtc (startUtc.AddMinutes(10.0))) else "null"
    let duration = durationMs |> Option.map string |> Option.defaultValue "null"
    sprintf """{"id":%d,"rom_id":%d,"start_time":"%s","end_time":%s,"duration_ms":%s}""" id romId (isoUtc startUtc) endTime duration

/// Routes `GET /api/play-sessions` to `sessionsJson` and `GET
/// /api/roms/{id}` to whatever `romDetails` has for that id (404
/// otherwise) -- everything else 404s too.
let private router (sessionsJson: string) (romDetails: Map<int, string>) : HttpMessageHandler =
    new RoutedStubHandler(fun request ->
        let path = request.RequestUri.AbsolutePath
        if path = "/api/play-sessions" then
            jsonResponse sessionsJson
        elif path.StartsWith("/api/roms/") then
            match Int32.TryParse(path.Substring("/api/roms/".Length)) with
            | true, id ->
                match romDetails |> Map.tryFind id with
                | Some json -> jsonResponse json
                | None -> notFound ()
            | _ -> notFound ()
        else notFound ())

let private seedGame (conn: SqliteConnection) (slug: string) (name: string) (year: int) =
    let data: Games.GameAddedData =
        { Name = name; Year = year; Genres = []; Description = ""; ShortDescription = ""
          WebsiteUrl = None; CoverRef = None; BackdropRef = None; RawgId = None; RawgRating = None }
    let streamId = Games.streamId slug
    EventStore.appendToStream conn streamId -1L [ Games.Serialization.toEventData (Games.Game_added_to_library data) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    Projection.runProjection conn PlaySessionProjection.handler

let private runSync (conn: SqliteConnection) (http: HttpClient) : Result<RomMSyncResult, string> =
    RomMSync.runSync conn (new SemaphoreSlim(1, 1)) http (fun () -> config) noImagesDir allProjectionHandlers
    |> Async.RunSynchronously

[<Tests>]
let rommSyncTests =
    testList "RomMSync.runSync (integration-jkbm1, ADR-0088)" [

        testCase "A rom with no closed session (only an open one) is never touched" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let openSession = sessionJson 1 20 (DateTime.UtcNow.AddHours(-1.0)) (Some 600000L) false
            let sessions = sprintf "[%s]" openSession
            let http = new HttpClient(router sessions Map.empty)
            match runSync db.Connection http with
            | Ok result ->
                Expect.equal result.GamesCreated 0 "No game created for a rom with no closed session"
                Expect.equal result.SessionsRecorded 0 "No sessions recorded"
            | Error e -> failtestf "Expected sync to succeed, got %s" e
            Expect.isNone (GameProjection.findByRommRomId db.Connection 20) "Never linked"
            Expect.isEmpty (GameProjection.getAll db.Connection) "No game exists in the library at all"

        testCase "A rom on an unselected platform is never touched, even with a closed session" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let closedSession = sessionJson 1 30 (DateTime.UtcNow.AddHours(-1.0)) (Some 600000L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 30, romDetailJson 30 "Off Platform Game" 99 "unknown-platform" (Some 2020) None ]
            let http = new HttpClient(router sessions roms)
            match runSync db.Connection http with
            | Ok result -> Expect.equal result.GamesCreated 0 "No game created -- platform 99 isn't selected"
            | Error e -> failtestf "Expected sync to succeed, got %s" e
            Expect.isEmpty (GameProjection.getAll db.Connection) "No game exists in the library"

        testCase "No existing match creates a Game via the identity-card path and links the rom id" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let closedSession = sessionJson 1 10 (DateTime.UtcNow.AddHours(-2.0)) (Some 1800000L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 10, romDetailJson 10 "Super Mario Odyssey" 1 "snes" (Some 2017) None ]
            let http = new HttpClient(router sessions roms)
            match runSync db.Connection http with
            | Ok result ->
                Expect.equal result.GamesCreated 1 "One game created"
                Expect.equal result.SessionsRecorded 1 "One session recorded"
                Expect.equal result.GamesPromotedToFocus 1 "The new game's first session promotes it to InFocus"
            | Error e -> failtestf "Expected sync to succeed, got %s" e
            match GameProjection.findByRommRomId db.Connection 10 with
            | Some slug ->
                match GameProjection.getBySlug db.Connection slug with
                | Some game ->
                    Expect.equal game.Name "Super Mario Odyssey" "Created game's name comes from the rom detail"
                    Expect.equal game.Status InFocus "Promoted to InFocus by its first session"
                | None -> failtest "Expected the created game to be readable from the projection"
            | None -> failtest "Expected the rom id to be linked to the newly created game"

        testCase "Exactly one normalized-name(+year) match attaches the rom id without creating a duplicate" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "chrono-trigger-1995" "Chrono Trigger" 1995
            let closedSession = sessionJson 1 40 (DateTime.UtcNow.AddHours(-2.0)) (Some 1200000L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 40, romDetailJson 40 "Chrono Trigger" 1 "snes" (Some 1995) None ]
            let http = new HttpClient(router sessions roms)
            match runSync db.Connection http with
            | Ok result ->
                Expect.equal result.GamesCreated 0 "No new game -- matched the existing one"
                Expect.equal result.GamesLinked 1 "The existing game was linked"
            | Error e -> failtestf "Expected sync to succeed, got %s" e
            Expect.equal (GameProjection.findByRommRomId db.Connection 40) (Some "chrono-trigger-1995") "Rom id attached to the existing game"
            Expect.equal (List.length (GameProjection.getAll db.Connection)) 1 "Still exactly one game -- no duplicate created"

        testCase "More than one matching library game is ambiguous -- skipped, named in the summary, nothing linked or created" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "duplicate-name-1999-a" "Duplicate Name" 1999
            seedGame db.Connection "duplicate-name-1999-b" "Duplicate Name" 1999
            let closedSession = sessionJson 1 50 (DateTime.UtcNow.AddHours(-2.0)) (Some 1200000L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 50, romDetailJson 50 "Duplicate Name" 1 "snes" (Some 1999) None ]
            let http = new HttpClient(router sessions roms)
            match runSync db.Connection http with
            | Ok result ->
                Expect.equal result.Ambiguous [ "Duplicate Name" ] "Named in the ambiguous list"
                Expect.equal result.SessionsRecorded 0 "Sessions are not imported for an ambiguous rom"
                Expect.equal result.GamesCreated 0 "No duplicate created"
                Expect.equal result.GamesLinked 0 "Nothing linked"
            | Error e -> failtestf "Expected sync to succeed, got %s" e
            Expect.isNone (GameProjection.findByRommRomId db.Connection 50) "Rom id stays unlinked -- retried on a later run once linked by hand"

        testCase "duration_ms below a minute still rounds up to 1 minute and consumes the session id" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let closedSession = sessionJson 1 60 (DateTime.UtcNow.AddHours(-3.0)) (Some 500L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 60, romDetailJson 60 "Short Session Game" 1 "snes" (Some 2021) None ]
            let http = new HttpClient(router sessions roms)
            match runSync db.Connection http with
            | Ok result -> Expect.equal result.SessionsRecorded 1 "The short session is still recorded"
            | Error e -> failtestf "Expected sync to succeed, got %s" e
            let slug = GameProjection.findByRommRomId db.Connection 60 |> Option.get
            let sessionsForGame = PlaySessionProjection.getForGame db.Connection slug
            Expect.equal (List.length sessionsForGame) 1 "One diary day recorded"
            Expect.equal sessionsForGame.[0].MinutesPlayed 1 "Rounded up to the 1-minute minimum"
            Expect.equal sessionsForGame.[0].Source RomM "Recorded with source RomM"

        testCase "A session's gaming day is pinned to PlaytimeTracker's syncHour+30min boundary, converted UTC to local" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            // Default sync hour (playtime_sync_hour unset -> 4, PlaytimeTracker.getSyncHour).
            let syncHour = 4
            let today = DateTime.Now.Date
            let boundaryLocal = DateTime(today.Year, today.Month, today.Day, syncHour, 30, 0, DateTimeKind.Local)
            let justBefore = boundaryLocal.AddMinutes(-1.0)
            let justAfter = boundaryLocal.AddMinutes(1.0)
            let expectedDayBefore = PlaytimeTracker.toGamingDay syncHour justBefore
            let expectedDayAfter = PlaytimeTracker.toGamingDay syncHour justAfter
            Expect.notEqual expectedDayBefore expectedDayAfter "Sanity: the two probe times land on different gaming days"

            let sBefore = sessionJson 1 70 justBefore (Some 600000L) true
            let sAfter = sessionJson 2 70 justAfter (Some 600000L) true
            let sessions = sprintf "[%s,%s]" sBefore sAfter
            let roms = Map.ofList [ 70, romDetailJson 70 "Boundary Game" 1 "snes" (Some 2022) None ]
            let http = new HttpClient(router sessions roms)
            match runSync db.Connection http with
            | Ok result -> Expect.equal result.SessionsRecorded 2 "Two separate gaming days -> two Play_session_recorded events"
            | Error e -> failtestf "Expected sync to succeed, got %s" e
            let slug = GameProjection.findByRommRomId db.Connection 70 |> Option.get
            Expect.isSome (PlaySessionProjection.getBySlugAndDay db.Connection slug expectedDayBefore) "The just-before session landed on the earlier gaming day"
            Expect.isSome (PlaySessionProjection.getBySlugAndDay db.Connection slug expectedDayAfter) "The just-after session landed on the later gaming day"

        testCase "Re-running against unchanged fixture data appends zero new events (idempotent)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let closedSession = sessionJson 1 80 (DateTime.UtcNow.AddHours(-2.0)) (Some 1500000L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 80, romDetailJson 80 "Idempotency Game" 1 "snes" (Some 2019) None ]
            let http1 = new HttpClient(router sessions roms)
            match runSync db.Connection http1 with
            | Ok result -> Expect.equal result.SessionsRecorded 1 "First run records the session"
            | Error e -> failtestf "Expected first sync to succeed, got %s" e
            let slug = GameProjection.findByRommRomId db.Connection 80 |> Option.get
            let positionAfterFirst = EventStore.getStreamPosition db.Connection (Games.streamId slug)

            let http2 = new HttpClient(router sessions roms)
            match runSync db.Connection http2 with
            | Ok result -> Expect.equal result.SessionsRecorded 0 "Second, identical run records nothing new"
            | Error e -> failtestf "Expected second sync to succeed, got %s" e
            let positionAfterSecond = EventStore.getStreamPosition db.Connection (Games.streamId slug)
            Expect.equal positionAfterSecond positionAfterFirst "The game stream's position is unchanged after a second identical sync"

        testCase "integration-q748k: a sync run upserts the linked rom's platform slug; a second identical run leaves it unchanged and still appends zero events" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let closedSession = sessionJson 1 100 (DateTime.UtcNow.AddHours(-2.0)) (Some 1500000L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 100, romDetailJson 100 "Platform Slug Game" 1 "snes" (Some 2019) None ]
            let http1 = new HttpClient(router sessions roms)
            match runSync db.Connection http1 with
            | Ok result -> Expect.equal result.GamesCreated 1 "First run creates the game"
            | Error e -> failtestf "Expected first sync to succeed, got %s" e
            let slug = GameProjection.findByRommRomId db.Connection 100 |> Option.get
            Expect.equal (GameProjection.getRommRomPlatformSlug db.Connection 100) (Some "snes") "Platform slug recorded for the newly-linked rom"
            let positionAfterFirst = EventStore.getStreamPosition db.Connection (Games.streamId slug)

            let http2 = new HttpClient(router sessions roms)
            match runSync db.Connection http2 with
            | Ok result -> Expect.equal result.SessionsRecorded 0 "Second, identical run records no new sessions"
            | Error e -> failtestf "Expected second sync to succeed, got %s" e
            Expect.equal (GameProjection.getRommRomPlatformSlug db.Connection 100) (Some "snes") "Platform slug is unchanged after the second, identical run"
            let positionAfterSecond = EventStore.getStreamPosition db.Connection (Games.streamId slug)
            Expect.equal positionAfterSecond positionAfterFirst "No new events appended on the second run either"

        testCase "A matched game whose Set_romm_rom_id command fails is not linked, not counted, and its sessions are not imported" <| fun _ ->
            // Iteration 2 (verifier note): `resolveSlug` used to discard the
            // `Set_romm_rom_id` command's Result with `|> ignore` -- a failed
            // link (e.g. the matched game's stream is no longer Active) still
            // recorded the rom as `Matched` and imported its sessions. Forces
            // that failure the surest way available in a fixture test: seed a
            // normal Active game, then append `Game_removed_from_library`
            // DIRECTLY to its event stream without running the projection, so
            // `game_detail` still lists it as a name-matching candidate while
            // `Games.decide` itself now refuses `Set_romm_rom_id` with "Game
            // has been removed" -- the exact `executeGameCommand` failure path
            // `resolveSlug` must degrade on instead of silently succeeding.
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "ghost-game-2020" "Ghost Game" 2020
            let streamId = Games.streamId "ghost-game-2020"
            let posBeforeRemoval = EventStore.getStreamPosition db.Connection streamId
            EventStore.appendToStream db.Connection streamId posBeforeRemoval
                [ Games.Serialization.toEventData Games.Game_removed_from_library ]
            |> ignore

            let closedSession = sessionJson 1 90 (DateTime.UtcNow.AddHours(-2.0)) (Some 1200000L) true
            let sessions = sprintf "[%s]" closedSession
            let roms = Map.ofList [ 90, romDetailJson 90 "Ghost Game" 1 "snes" (Some 2020) None ]
            let http = new HttpClient(router sessions roms)
            match runSync db.Connection http with
            | Ok result ->
                Expect.equal result.GamesLinked 0 "Linking failed -- not counted as linked"
                Expect.equal result.GamesCreated 0 "Not created either -- it matched by name first"
                Expect.equal result.SessionsRecorded 0 "Sessions are not imported when linking fails"
                Expect.isEmpty result.Ambiguous "Not an ambiguous match -- exactly one name candidate, but attaching the rom id failed"
            | Error e -> failtestf "Expected the sync overall to succeed (this rom's failure is isolated), got %s" e
            Expect.isNone (GameProjection.findByRommRomId db.Connection 90) "Rom id never attached -- retried on a later run"

        testCase "A 401 from the play-sessions call becomes a persisted romm_last_error, cleared by the next successful sync" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let http401 = new HttpClient(new RoutedStubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.Unauthorized)))
            match runSync db.Connection http401 with
            | Error msg -> Expect.stringStarts msg "RomM token rejected: " "The typed rejection message"
            | Ok r -> failtestf "Expected the sync to fail on a rejected token, got Ok %A" r
            match SettingsStore.getSetting db.Connection "romm_last_error" with
            | Some msg -> Expect.stringStarts msg "RomM token rejected: " "Persisted with the fixed prefix"
            | None -> failtest "Expected romm_last_error to be persisted"

            let httpOk = new HttpClient(router "[]" Map.empty)
            match runSync db.Connection httpOk with
            | Ok _ -> ()
            | Error e -> failtestf "Expected the follow-up sync to succeed, got %s" e
            Expect.isNone (SettingsStore.getSetting db.Connection "romm_last_error") "A successful sync clears the standing notice"
    ]
