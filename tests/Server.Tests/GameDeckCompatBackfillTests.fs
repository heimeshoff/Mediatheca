module Mediatheca.Tests.GameDeckCompatBackfillTests

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

/// games-b8xnw (ADR-0043/ADR-0045): the resumable throttled Deck-compat
/// backfill job — reuses `GameFacetBackfill`'s shape (games-a7dqx
/// `depends_on`), walking its OWN `deck_compat_fetched_at` cursor against
/// the store app-page HTML scrape that replaces the dead
/// `ajaxgetdeckappcompatibilityreport` endpoint (`Steam.fs`'s module doc
/// comment).

type private StubHttpMessageHandler(responseFor: string -> string) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        let html = responseFor (request.RequestUri.ToString())
        let response = new HttpResponseMessage(HttpStatusCode.OK)
        response.Content <- new StringContent(html)
        Task.FromResult<HttpResponseMessage>(response)

let private hardwareCompatHtml (appId: int) (resolvedCategory: int) : string =
    sprintf
        """<html><body><div data-hardwarecompatibility="{&quot;appid&quot;:%d,&quot;resolved_category&quot;:%d}"></div></body></html>"""
        appId resolvedCategory

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    // games-kfpqp: PlaySessionProjection is needed for `game_play_session`,
    // which `GameProjection.getBySlug`'s own SQL joins against — a table this
    // file's earlier tests never needed since none of them called
    // `GameProjection.getBySlug`.
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn
    conn

let private sampleGameData: Games.GameAddedData = {
    Name = "Hades"
    Year = 2020
    Genres = [ "Roguelike" ]
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

/// Creates a Steam-linked game's `game_detail` row via real events —
/// deliberately WITHOUT seeding `game_metadata_cache`.
/// `MetadataCache.seedFromProjections` is a once-per-database operation
/// (gated by a settings marker — see its own doc comment): a test that seeds
/// several games must create every one's `game_detail` row FIRST and call
/// `seedFromProjections` exactly once afterwards, or every game after the
/// first silently gets no cache row at all (the marker is already set by the
/// first call). `seedGameWithSteamAppId`/`seedGameWithRecordedVerdict` below
/// are the single-game convenience wrappers; `seedManyGames` is the
/// multi-game shape.
let private addGameDetail (conn: SqliteConnection) (slug: string) (steamAppId: int) =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library sampleGameData) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    EventStore.appendToStream conn (Games.streamId slug) 0L
        [ Games.Serialization.toEventData (Games.Game_steam_app_id_set steamAppId) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private seedGameWithSteamAppId (conn: SqliteConnection) (slug: string) (steamAppId: int) =
    addGameDetail conn slug steamAppId
    MetadataCache.seedFromProjections conn

let private stampRecordedVerdict (conn: SqliteConnection) (slug: string) (verdict: string) (fetchedAt: DateTime) =
    conn
    |> Db.newCommand
        "UPDATE game_metadata_cache SET deck_compat = @deck_compat, deck_compat_fetched_at = @deck_compat_fetched_at WHERE game_slug = @slug"
    |> Db.setParams [
        "slug", SqlType.String slug
        "deck_compat", SqlType.String verdict
        "deck_compat_fetched_at", SqlType.String (fetchedAt.ToString("o"))
    ]
    |> Db.exec

/// games-kfpqp (ADR-0087): seeds a single game whose Deck-compat verdict is
/// already recorded, with a specific `deck_compat_fetched_at` stamp — the
/// shape the re-check cohort's cursor walks. Writes the two columns directly
/// by SQL (rather than via `MetadataCache.upsertGameDeckCompat`) so the test
/// can pick an arbitrary `fetchedAt` in the past, not just "now". Single-game
/// only — see `seedManyGames` for seeding several games in one connection.
let private seedGameWithRecordedVerdict (conn: SqliteConnection) (slug: string) (steamAppId: int) (verdict: string) (fetchedAt: DateTime) =
    seedGameWithSteamAppId conn slug steamAppId
    stampRecordedVerdict conn slug verdict fetchedAt

/// games-kfpqp (ADR-0087): seeds MANY games in one connection — every
/// `game_detail` row is created first, `MetadataCache.seedFromProjections` is
/// called exactly once, and only THEN is each game whose spec carries a
/// `Some (verdict, fetchedAt)` stamped with its recorded verdict (a `None`
/// spec leaves the game in the never-fetched cohort). See `addGameDetail`'s
/// doc comment for why the ordering here is load-bearing.
let private seedManyGames (conn: SqliteConnection) (games: (string * int * (string * DateTime) option) list) =
    for (slug, steamAppId, _) in games do
        addGameDetail conn slug steamAppId
    MetadataCache.seedFromProjections conn
    for (slug, _, recorded) in games do
        match recorded with
        | Some (verdict, fetchedAt) -> stampRecordedVerdict conn slug verdict fetchedAt
        | None -> ()

let private verdictOf (s: string) : DeckCompatibility =
    match s with
    | "Verified" -> Verified
    | "Playable" -> Playable
    | "Unsupported" -> Unsupported
    | _ -> Unknown

/// games-kfpqp: `Steam.getDeckCompatibility` converts EVERY HTTP-layer
/// exception into a `Result.Error` inside its own `try`/`with` (see
/// `Steam.fs`'s module) — a stub `HttpMessageHandler` that throws can only
/// ever exercise `GameDeckCompatBackfill.runBackfill`'s `Error` branch, never
/// its outer exception branch. To genuinely reach that outer `try`/`with`
/// (the "an unhandled exception" case ADR-0084 describes — a dropped
/// connection, momentary DB contention), the exception has to originate on
/// the DB-write side instead: a `BEFORE UPDATE` trigger that aborts only the
/// specific write `upsertGameDeckCompat`'s success path issues (identified by
/// its unconditional `deck_compat_failed_attempts = NULL`), leaving the
/// SELECT-based cursor query and `recordDeckCompatFailedAttempt`'s own
/// (non-NULL-setting) UPDATE untouched.
let private simulateWriteExceptionOnSuccess (conn: SqliteConnection) (slug: string) =
    conn
    |> Db.newCommand
        (sprintf
            """
            CREATE TRIGGER simulate_write_failure
            BEFORE UPDATE ON game_metadata_cache
            WHEN NEW.game_slug = '%s' AND NEW.deck_compat_failed_attempts IS NULL
            BEGIN
                SELECT RAISE(ABORT, 'simulated DB failure');
            END
            """
            slug)
    |> Db.exec

[<Tests>]
let tests =
    testList "GameDeckCompatBackfill (games-b8xnw)" [

        testCase "Fetches Deck-compat for a never-fetched game and stamps deck_compat_fetched_at, dropping it from the next run's cursor" <| fun _ ->
            let conn = createConnection ()
            let now = DateTime.UtcNow
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let candidatesBefore = MetadataCache.findGamesNeedingDeckCompatBackfill conn now
            Expect.equal candidatesBefore [ ("hades-2020", 1145360, None) ] "sanity: the seeded, never-fetched game is the one candidate"

            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> hardwareCompatHtml 1145360 3))
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient now |> Async.RunSynchronously

            Expect.equal result.Processed 1 "One candidate processed"
            Expect.equal result.Succeeded 1 "One candidate succeeded"
            Expect.equal result.Failed 0 "No failed fetches"
            Expect.equal result.Errors 0 "No errors"

            let row =
                conn
                |> Db.newCommand
                    "SELECT deck_compat, deck_compat_fetched_at, deck_compat_failed_attempts, deck_compat_last_attempt_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadString "deck_compat",
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_fetched_at")),
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_failed_attempts")),
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_last_attempt_at")))
            Expect.equal row (Some ("Verified", false, true, true))
                "Verified verdict written, deck_compat_fetched_at now stamped (not NULL), no failure bookkeeping"

            // Resumability: the WHERE deck_compat_fetched_at IS NULL clause
            // IS the cursor — a successfully-processed row drops out on its
            // own.
            let candidatesAfter = MetadataCache.findGamesNeedingDeckCompatBackfill conn now
            Expect.isEmpty candidatesAfter "The processed row no longer appears in the next run's cursor"

        testCase "A game with no Steam app id is never a candidate" <| fun _ ->
            let conn = createConnection ()
            EventStore.appendToStream conn (Games.streamId "no-steam-game") -1L
                [ Games.Serialization.toEventData (Games.Game_added_to_library { sampleGameData with Name = "No Steam Game" }) ] |> ignore
            Projection.runProjection conn GameProjection.handler
            MetadataCache.seedFromProjections conn

            let candidates = MetadataCache.findGamesNeedingDeckCompatBackfill conn DateTime.UtcNow
            Expect.isEmpty candidates "No steam_app_id — never a fetchable candidate"

        testCase "A fetch failure (e.g. missing attribute) increments deck_compat_failed_attempts and stamps deck_compat_last_attempt_at, leaving deck_compat/deck_compat_fetched_at untouched" <| fun _ ->
            let conn = createConnection ()
            let now = DateTime.UtcNow
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> "<html><body>no attribute</body></html>"))
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient now |> Async.RunSynchronously

            Expect.equal result.Processed 1 "One candidate attempted"
            Expect.equal result.Succeeded 0 "Steam returned nothing usable — not a success"
            Expect.equal result.Failed 1 "The Error result is reported as a failed fetch, not silently dropped"
            Expect.equal result.Errors 0 "Not an exception"

            let row =
                conn
                |> Db.newCommand
                    "SELECT deck_compat_failed_attempts, deck_compat_last_attempt_at, deck_compat, deck_compat_fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadInt32 "deck_compat_failed_attempts",
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_last_attempt_at")),
                    rd.IsDBNull(rd.GetOrdinal("deck_compat")),
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_fetched_at")))
            Expect.equal row (Some (1, false, true, true))
                "One failed attempt recorded, last-attempt stamped, deck_compat/deck_compat_fetched_at still untouched"

        testCase "A failed game is absent from the backfill cursor until its backoff window elapses, then present again" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> "<html><body>no attribute</body></html>"))
            GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient t0 |> Async.RunSynchronously |> ignore

            // One failed attempt -> backoff = min(2^1, 30) = 2 days.
            let candidatesJustAfter = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            Expect.isEmpty candidatesJustAfter "In backoff immediately after the failed attempt"

            let candidatesBeforeWindow = MetadataCache.findGamesNeedingDeckCompatBackfill conn (t0.AddDays(1.9))
            Expect.isEmpty candidatesBeforeWindow "Still in backoff just under 2 days later"

            let candidatesAfterWindow = MetadataCache.findGamesNeedingDeckCompatBackfill conn (t0.AddDays(2.1))
            Expect.equal candidatesAfterWindow [ ("hades-2020", 1145360, None) ] "Eligible again once the 2-day backoff has elapsed"

        testCase "A never-attempted, never-fetched game is always eligible regardless of the clock" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let candidates = MetadataCache.findGamesNeedingDeckCompatBackfill conn (DateTime.UtcNow.AddYears(10))
            Expect.equal candidates [ ("hades-2020", 1145360, None) ] "No prior attempt recorded -- always eligible"

        testCase "The backoff caps at 30 days flat, even for a large attempt count" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithSteamAppId conn "hades-2020" 1145360
            // Directly seed a high attempt count (2^6 = 64, above the cap) --
            // reaching this via six real failed runs would just be six
            // repetitions of the same assertion this test already makes for
            // one attempt.
            conn
            |> Db.newCommand
                "UPDATE game_metadata_cache SET deck_compat_failed_attempts = 6, deck_compat_last_attempt_at = @last_attempt_at WHERE game_slug = @slug"
            |> Db.setParams [ "slug", SqlType.String "hades-2020"; "last_attempt_at", SqlType.String (t0.ToString("o")) ]
            |> Db.exec

            let candidatesBeforeCap = MetadataCache.findGamesNeedingDeckCompatBackfill conn (t0.AddDays(29.0))
            Expect.isEmpty candidatesBeforeCap "Still in backoff just under the 30-day cap"

            let candidatesAfterCap = MetadataCache.findGamesNeedingDeckCompatBackfill conn (t0.AddDays(31.0))
            Expect.equal candidatesAfterCap [ ("hades-2020", 1145360, None) ] "Eligible again once the 30-day cap has elapsed, not 2^6 = 64 days"

        testCase "A successful fetch after earlier failures writes the verdict and resets both failure columns" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let failingHttpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> "<html><body>no attribute</body></html>"))
            GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) failingHttpClient t0 |> Async.RunSynchronously |> ignore

            let attemptsAfterFailure =
                conn
                |> Db.newCommand "SELECT deck_compat_failed_attempts FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "deck_compat_failed_attempts")
            Expect.equal attemptsAfterFailure (Some 1) "sanity: one failed attempt recorded"

            // Past the 2-day backoff so the game is a candidate again.
            let laterNow = t0.AddDays(3.0)
            let succeedingHttpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> hardwareCompatHtml 1145360 3))
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) succeedingHttpClient laterNow |> Async.RunSynchronously
            Expect.equal result.Succeeded 1 "The retried fetch succeeds"

            let row =
                conn
                |> Db.newCommand
                    "SELECT deck_compat, deck_compat_fetched_at, deck_compat_failed_attempts, deck_compat_last_attempt_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadString "deck_compat",
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_fetched_at")),
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_failed_attempts")),
                    rd.IsDBNull(rd.GetOrdinal("deck_compat_last_attempt_at")))
            Expect.equal row (Some ("Verified", false, true, true))
                "Verdict written and deck_compat_fetched_at stamped; both failure columns reset by the success"

        testCase "The Deck-compat backfill never touches the play-facets fetched_at cursor — the two backfills' cursors stay independent" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let facetsFetchedAtBefore =
                conn
                |> Db.newCommand "SELECT fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd -> rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> hardwareCompatHtml 1145360 3))
            GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient DateTime.UtcNow |> Async.RunSynchronously |> ignore
            let facetsFetchedAtAfter =
                conn
                |> Db.newCommand "SELECT fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd -> rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            Expect.equal facetsFetchedAtAfter facetsFetchedAtBefore "play-facets fetched_at is untouched — still NULL, as it was before this run"

        testCase "The backfill never writes game_detail — only game_metadata_cache" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> hardwareCompatHtml 1145360 2))
            GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient DateTime.UtcNow |> Async.RunSynchronously |> ignore

            let overrideStillNull =
                conn
                |> Db.newCommand "SELECT facet_override_solo FROM game_detail WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd -> rd.IsDBNull(rd.GetOrdinal("facet_override_solo")))
            Expect.equal overrideStillNull (Some true) "game_detail is untouched by the Deck-compat backfill"

        // ── games-kfpqp (ADR-0087): age-based re-check of recorded verdicts ──

        testCase "A recorded verdict older than its verdict's age limit is due for re-check; one younger than the limit is not (all four verdicts)" <| fun _ ->
            let ageLimitDaysByVerdict =
                [ "Unknown", 30.0
                  "Playable", 90.0
                  "Unsupported", 90.0
                  "Verified", 180.0 ]
            for (verdict, ageLimitDays) in ageLimitDaysByVerdict do
                let t0 = DateTime.UtcNow

                let dueConn = createConnection ()
                let dueSlug = sprintf "due-%s" verdict
                seedGameWithRecordedVerdict dueConn dueSlug 1000 verdict (t0.AddDays(-ageLimitDays).AddMinutes(-1.0))
                let dueCandidates = MetadataCache.findGamesNeedingDeckCompatBackfill dueConn t0
                Expect.equal dueCandidates [ (dueSlug, 1000, Some (verdictOf verdict)) ]
                    (sprintf "%s verdict just past its %.0f-day age limit is due for re-check" verdict ageLimitDays)

                let notDueConn = createConnection ()
                let notDueSlug = sprintf "not-due-%s" verdict
                seedGameWithRecordedVerdict notDueConn notDueSlug 1000 verdict (t0.AddDays(-ageLimitDays).AddMinutes(1.0))
                let notDueCandidates = MetadataCache.findGamesNeedingDeckCompatBackfill notDueConn t0
                Expect.isEmpty notDueCandidates
                    (sprintf "%s verdict just under its %.0f-day age limit is not yet due" verdict ageLimitDays)

        testCase "A NULL or unrecognised stored deck_compat re-checks as Unknown (30-day limit)" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithSteamAppId conn "hades-2020" 1145360
            // Stamp deck_compat_fetched_at directly, leaving deck_compat itself
            // NULL -- an otherwise-impossible-in-practice row shape this test
            // still wants covered defensively.
            conn
            |> Db.newCommand "UPDATE game_metadata_cache SET deck_compat_fetched_at = @fetched_at WHERE game_slug = @slug"
            |> Db.setParams [ "slug", SqlType.String "hades-2020"; "fetched_at", SqlType.String (t0.AddDays(-31.0).ToString("o")) ]
            |> Db.exec

            let candidates = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            Expect.equal candidates [ ("hades-2020", 1145360, Some Unknown) ]
                "A NULL stored verdict decodes to Unknown and is due after 30 days, exactly like a genuine Unknown verdict"

        testCase "With more than 25 due re-checks, one run returns exactly the 25 with the oldest deck_compat_fetched_at" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            // 30 Unknown-verdict games, all already past the 30-day limit, each
            // with a distinct deck_compat_fetched_at so "oldest first" is
            // unambiguous.
            let games =
                [ for i in 1 .. 30 ->
                    (sprintf "game-%02d" i, 1000 + i, Some ("Unknown", t0.AddDays(-40.0).AddMinutes(float i))) ]
            seedManyGames conn games

            let candidates = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            Expect.equal (List.length candidates) 25 "Exactly 25 re-checks returned, capped"
            let actualSlugs = candidates |> List.map (fun (slug, _, _) -> slug)
            let expectedSlugs = [ for i in 1 .. 25 -> sprintf "game-%02d" i ]
            Expect.equal actualSlugs expectedSlugs "The 25 oldest deck_compat_fetched_at rows are returned, oldest first"

        testCase "Due re-checks still inside their failed-attempt backoff do not consume cap slots" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            // 25 due re-checks (backoff-game-*) plus 5 more (eligible-game-*),
            // all Unknown and past the 30-day age limit.
            let backoffGames =
                [ for i in 1 .. 25 -> (sprintf "backoff-game-%02d" i, 2000 + i, Some ("Unknown", t0.AddDays(-40.0))) ]
            let eligibleGames =
                [ for i in 1 .. 5 -> (sprintf "eligible-game-%02d" i, 3000 + i, Some ("Unknown", t0.AddDays(-40.0).AddMinutes(float i))) ]
            seedManyGames conn (backoffGames @ eligibleGames)
            // The 25 backoff-game-* rows are ALSO inside their 2-day
            // failed-attempt backoff (one prior failed attempt, stamped at t0)
            // -- these must never occupy a cap slot.
            for i in 1 .. 25 do
                conn
                |> Db.newCommand
                    "UPDATE game_metadata_cache SET deck_compat_failed_attempts = 1, deck_compat_last_attempt_at = @last_attempt_at WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String (sprintf "backoff-game-%02d" i); "last_attempt_at", SqlType.String (t0.ToString("o")) ]
                |> Db.exec

            let candidates = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            let actualSlugs = candidates |> List.map (fun (slug, _, _) -> slug) |> List.sort
            let expectedSlugs = [ for i in 1 .. 5 -> sprintf "eligible-game-%02d" i ]
            Expect.equal actualSlugs expectedSlugs
                "Only the 5 eligible re-checks are returned; the 25 backoff-blocked ones never occupy a cap slot"

        testCase "Never-fetched games are returned in full regardless of the re-check cap" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            // 30 due re-checks (over the 25 cap) ...
            let recheckGames =
                [ for i in 1 .. 30 -> (sprintf "recheck-game-%02d" i, 4000 + i, Some ("Unknown", t0.AddDays(-40.0).AddMinutes(float i))) ]
            // ... plus 3 never-fetched games, which have no cap at all.
            let neverFetchedGames =
                [ for i in 1 .. 3 -> (sprintf "never-fetched-game-%02d" i, 5000 + i, None) ]
            seedManyGames conn (recheckGames @ neverFetchedGames)

            let candidates = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            let neverFetchedSlugs =
                candidates
                |> List.choose (fun (slug, _, verdict) -> if Option.isNone verdict then Some slug else None)
                |> List.sort
            let recheckCount = candidates |> List.filter (fun (_, _, verdict) -> Option.isSome verdict) |> List.length
            Expect.equal neverFetchedSlugs
                [ "never-fetched-game-01"; "never-fetched-game-02"; "never-fetched-game-03" ]
                "All three never-fetched games are returned"
            Expect.equal recheckCount 25 "The re-check cohort is still capped at 25 even with 3 more never-fetched candidates also present"

        testCase "A re-check that returns a different verdict overwrites deck_compat and re-stamps deck_compat_fetched_at with the injected now; the game is no longer due on a second run with the same clock" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithRecordedVerdict conn "hades-2020" 1145360 "Unknown" (t0.AddDays(-31.0))

            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> hardwareCompatHtml 1145360 3)) // category 3 -> Verified
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient t0 |> Async.RunSynchronously

            Expect.equal result.Processed 1 "One re-check candidate"
            Expect.equal result.Rechecks 1 "Counted as a re-check, not a first fetch"
            Expect.equal result.Succeeded 1 "The re-fetch succeeded"
            Expect.equal result.VerdictsChanged 1 "Unknown -> Verified is a changed verdict"

            let row =
                conn
                |> Db.newCommand "SELECT deck_compat, deck_compat_fetched_at FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd -> rd.ReadString "deck_compat", rd.ReadString "deck_compat_fetched_at")
            Expect.equal row (Some ("Verified", t0.ToString("o")))
                "deck_compat overwritten to Verified and deck_compat_fetched_at re-stamped with the job's injected now"

            // A list/detail read shows the new verdict straight through.
            let detail = GameProjection.getBySlug conn "hades-2020"
            Expect.equal (detail |> Option.map (fun d -> d.DeckCompat)) (Some Verified) "getBySlug reads the freshly re-checked verdict"

            // No longer due on a second run with the exact same clock.
            let candidatesAfter = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            Expect.isEmpty candidatesAfter "Freshly re-stamped game is not due again immediately with the same clock"

        testCase "A re-check that returns the SAME verdict re-stamps deck_compat_fetched_at but does not count as a changed verdict" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithRecordedVerdict conn "hades-2020" 1145360 "Verified" (t0.AddDays(-181.0))

            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> hardwareCompatHtml 1145360 3)) // category 3 -> Verified, unchanged
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient t0 |> Async.RunSynchronously

            Expect.equal result.Rechecks 1 "Counted as a re-check"
            Expect.equal result.Succeeded 1 "The re-fetch succeeded"
            Expect.equal result.VerdictsChanged 0 "Verified -> Verified is not a changed verdict"

        testCase "A re-check whose fetch returns Error leaves the recorded verdict and its stamp untouched, records the failed attempt, and is absent from the cursor until the backoff elapses" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithRecordedVerdict conn "hades-2020" 1145360 "Playable" (t0.AddDays(-91.0))

            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> "<html><body>no attribute</body></html>"))
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient t0 |> Async.RunSynchronously

            Expect.equal result.Rechecks 1 "Counted as a re-check attempt"
            Expect.equal result.Failed 1 "Reported as a failed fetch"
            Expect.equal result.Succeeded 0 "Not a success"
            Expect.equal result.VerdictsChanged 0 "No verdict change on a failure"

            let row =
                conn
                |> Db.newCommand
                    "SELECT deck_compat, deck_compat_fetched_at, deck_compat_failed_attempts FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd -> rd.ReadString "deck_compat", rd.ReadString "deck_compat_fetched_at", rd.ReadInt32 "deck_compat_failed_attempts")
            Expect.equal row (Some ("Playable", (t0.AddDays(-91.0)).ToString("o"), 1))
                "The recorded verdict and its stamp are untouched by the failure; one failed attempt recorded"

            // Absent from the cursor immediately after (backoff = min(2^1,30)=2 days) ...
            let candidatesImmediately = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            Expect.isEmpty candidatesImmediately "Still in backoff immediately after the failed re-check"

            // ... present again once the backoff window elapses.
            let candidatesAfterBackoff = MetadataCache.findGamesNeedingDeckCompatBackfill conn (t0.AddDays(2.1))
            Expect.equal candidatesAfterBackoff [ ("hades-2020", 1145360, Some Playable) ]
                "Eligible again once the 2-day backoff has elapsed"

        testCase "A re-check whose write throws (the exception branch) leaves the recorded verdict and its stamp untouched and records the failed attempt exactly like the Error branch" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedGameWithRecordedVerdict conn "hades-2020" 1145360 "Verified" (t0.AddDays(-181.0))
            // `Steam.getDeckCompatibility` converts every HTTP-layer exception
            // into a `Result.Error` itself, so the only way to genuinely reach
            // `runBackfill`'s own outer exception branch is a DB-write failure —
            // simulated here by a trigger that aborts only the
            // `upsertGameDeckCompat` success-path write (see
            // `simulateWriteExceptionOnSuccess`'s doc comment).
            simulateWriteExceptionOnSuccess conn "hades-2020"

            let httpClient = new HttpClient(new StubHttpMessageHandler(fun _ -> hardwareCompatHtml 1145360 3))
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient t0 |> Async.RunSynchronously

            Expect.equal result.Rechecks 1 "Counted as a re-check attempt"
            Expect.equal result.Errors 1 "The aborted write is reported as an exception, not a failed fetch or a success"
            Expect.equal result.Succeeded 0 "Not a success — the write never completed"
            Expect.equal result.VerdictsChanged 0 "No verdict change recorded when the write itself failed"

            let row =
                conn
                |> Db.newCommand
                    "SELECT deck_compat, deck_compat_fetched_at, deck_compat_failed_attempts FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "hades-2020" ]
                |> Db.querySingle (fun rd -> rd.ReadString "deck_compat", rd.ReadString "deck_compat_fetched_at", rd.ReadInt32 "deck_compat_failed_attempts")
            Expect.equal row (Some ("Verified", (t0.AddDays(-181.0)).ToString("o"), 1))
                "The recorded verdict/stamp survive the aborted write untouched; the exception branch still records a failed attempt"

            let candidatesImmediately = MetadataCache.findGamesNeedingDeckCompatBackfill conn t0
            Expect.isEmpty candidatesImmediately "Still in backoff immediately after the exception-branch failed attempt"

        testCase "BackfillResult separates first fetches from re-checks in one mixed run" <| fun _ ->
            let conn = createConnection ()
            let t0 = DateTime.UtcNow
            seedManyGames conn [
                ("never-fetched-game", 1145360, None)
                ("recheck-game", 1145361, Some ("Unknown", t0.AddDays(-31.0)))
            ]

            let httpClient = new HttpClient(new StubHttpMessageHandler(fun req ->
                if (req: string).Contains("1145360") then hardwareCompatHtml 1145360 3
                else hardwareCompatHtml 1145361 2))
            let result = GameDeckCompatBackfill.runBackfill conn (new SemaphoreSlim(1, 1)) httpClient t0 |> Async.RunSynchronously

            Expect.equal result.Processed 2 "Both the never-fetched game and the due re-check were processed"
            Expect.equal result.Rechecks 1 "Exactly one of the two candidates came from the re-check cohort"
            Expect.equal (result.Processed - result.Rechecks) 1 "The other candidate is the never-fetched cohort's own count"
            Expect.equal result.Succeeded 2 "Both fetches succeeded"
            Expect.equal result.VerdictsChanged 1 "Unknown -> Playable is a changed verdict for the re-check; the never-fetched game has nothing to compare against"
    ]
