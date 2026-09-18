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

let private seedGameWithSteamAppId (conn: SqliteConnection) (slug: string) (steamAppId: int) =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library sampleGameData) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    EventStore.appendToStream conn (Games.streamId slug) 0L
        [ Games.Serialization.toEventData (Games.Game_steam_app_id_set steamAppId) ] |> ignore
    Projection.runProjection conn GameProjection.handler
    MetadataCache.seedFromProjections conn

[<Tests>]
let tests =
    testList "GameDeckCompatBackfill (games-b8xnw)" [

        testCase "Fetches Deck-compat for a never-fetched game and stamps deck_compat_fetched_at, dropping it from the next run's cursor" <| fun _ ->
            let conn = createConnection ()
            let now = DateTime.UtcNow
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let candidatesBefore = MetadataCache.findGamesNeedingDeckCompatBackfill conn now
            Expect.equal candidatesBefore [ ("hades-2020", 1145360) ] "sanity: the seeded, never-fetched game is the one candidate"

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
            Expect.equal candidatesAfterWindow [ ("hades-2020", 1145360) ] "Eligible again once the 2-day backoff has elapsed"

        testCase "A never-attempted, never-fetched game is always eligible regardless of the clock" <| fun _ ->
            let conn = createConnection ()
            seedGameWithSteamAppId conn "hades-2020" 1145360
            let candidates = MetadataCache.findGamesNeedingDeckCompatBackfill conn (DateTime.UtcNow.AddYears(10))
            Expect.equal candidates [ ("hades-2020", 1145360) ] "No prior attempt recorded -- always eligible"

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
            Expect.equal candidatesAfterCap [ ("hades-2020", 1145360) ] "Eligible again once the 30-day cap has elapsed, not 2^6 = 64 days"

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
    ]
