module Mediatheca.Tests.SeriesProjectionReadsTests

// series-q8jwc: `SeriesProjection`'s query functions compose their DTOs from
// the metadata cache tier (`MetadataCache.fs`, ADR-0045/0046) at read time —
// `series_metadata_cache` for TmdbRating/Overview/EpisodeRuntime, and the
// `series_next_up`/`series_episode_counts` views for the next-up tuple and
// season/episode counts that used to be materialized columns on
// `series_list`. The join happens inside `SeriesProjection`'s own query
// functions, never at the API layer — every Shared DTO keeps its existing
// shape (proven separately by `git diff --stat src/Shared/Shared.fs
// src/Client/` showing zero changed files, checked outside Expecto).

open System.Data
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server

let private newConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    // Mirrors Composition.buildApp's real startup order: MetadataCache.initialize
    // before the projection handler's own Init.
    MetadataCache.initialize conn
    SeriesProjection.handler.Init conn
    // getBySlug/getDashboardSeriesNextUp reach across into CastStore's
    // series_cast table, JellyfinStore's jellyfin_episode table, and
    // ContentBlockProjection's content_blocks table.
    CastStore.initialize conn
    JellyfinStore.initialize conn
    ContentBlockProjection.handler.Init conn
    conn

let private mkEpisode (num: int) : Series.EpisodeImportData = {
    EpisodeNumber = num
    Name = $"Episode {num}"
    Overview = ""
    Runtime = None
    AirDate = None
    StillRef = None
    TmdbRating = None
}

let private mkSeason (num: int) (episodeCount: int) : Series.SeasonImportData = {
    SeasonNumber = num
    Name = $"Season {num}"
    Overview = ""
    PosterRef = None
    AirDate = None
    Episodes = [ for i in 1 .. episodeCount -> mkEpisode i ]
}

/// Seeds an Active series via the real event + projection path (identity
/// card fields), then seeds the season/episode cache tier imperatively — the
/// command-time shape `Api.addSeriesToLibraryImpl` uses post-series-r2xhv.
/// Passing `seasons = []` and never calling `upsertSeasonEpisodeCache`
/// simulates a "cold" entry with nothing materialized in the cache yet.
let private seedSeries
    (conn: SqliteConnection)
    (slug: string)
    (tmdbId: int)
    (posterRef: string option)
    (seasons: Series.SeasonImportData list)
    : unit =
    let seriesData: Series.SeriesAddedData = {
        Name = $"Show {slug}"
        Year = 2010
        Overview = "STALE_OVERVIEW_FROM_ADD_TIME"
        Genres = [ "Drama" ]
        Status = "Returning"
        PosterRef = posterRef
        BackdropRef = None
        TmdbId = tmdbId
        TmdbRating = Some 1.1
        EpisodeRuntime = Some 5
        Seasons = seasons
    }
    let streamId = Series.streamId slug
    EventStore.appendToStream conn streamId -1L [ Series.Serialization.toEventData (Series.Series_added_to_library seriesData) ]
    |> ignore
    Projection.runProjection conn SeriesProjection.handler
    if not (List.isEmpty seasons) then
        SeriesRefresh.upsertSeasonEpisodeCache conn slug seasons

let private seedMetadataCache
    (conn: SqliteConnection)
    (slug: string)
    (overview: string)
    (tmdbRating: float)
    (episodeRuntime: int)
    : unit =
    conn
    |> Db.newCommand
        "INSERT INTO series_metadata_cache (series_slug, overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at) VALUES (@slug, @overview, NULL, @rating, @runtime, '2026-08-01')"
    |> Db.setParams [
        "slug", SqlType.String slug
        "overview", SqlType.String overview
        "rating", SqlType.Double tmdbRating
        "runtime", SqlType.Int32 episodeRuntime
    ]
    |> Db.exec

let private markWatched (conn: SqliteConnection) (slug: string) (rewatchId: string) (season: int) (episode: int) (date: string) : unit =
    let streamId = Series.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    let data: Series.EpisodeWatchedData = { RewatchId = rewatchId; SeasonNumber = season; EpisodeNumber = episode; Date = date }
    EventStore.appendToStream conn streamId position [ Series.Serialization.toEventData (Series.Episode_watched data) ]
    |> ignore
    Projection.runProjection conn SeriesProjection.handler

let private createRewatchSession (conn: SqliteConnection) (slug: string) (rewatchId: string) : unit =
    let streamId = Series.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    let data: Series.RewatchSessionCreatedData = { RewatchId = rewatchId; Name = Some rewatchId; FriendSlugs = [] }
    EventStore.appendToStream conn streamId position [ Series.Serialization.toEventData (Series.Rewatch_session_created data) ]
    |> ignore
    Projection.runProjection conn SeriesProjection.handler

/// series-ww1rb: guarantees a series surfaces in `getDashboardSeriesNextUp`'s
/// results regardless of `watched_date` freshness — the dashboard WHERE
/// clause's other visibility path (`>= date('now', '-7 days')`) is fragile
/// against the fixed dates these tests use, so tests that need a
/// fully-watched (no-NextUp) series to appear mark it in-focus instead.
let private markInFocus (conn: SqliteConnection) (slug: string) : unit =
    let streamId = Series.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Series.Serialization.toEventData Series.Series_in_focus_set ]
    |> ignore
    Projection.runProjection conn SeriesProjection.handler

[<Tests>]
let getBySlugTests =
    testList "SeriesProjection.getBySlug composes reads from the metadata cache" [

        testCase "a populated cache wins over series_detail's own stale columns" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "the-wire" 100 (Some "posters/the-wire.jpg") [ mkSeason 1 2 ]
            createRewatchSession conn "the-wire" "default-session-unused"
            markWatched conn "the-wire" "default" 1 1 "2024-05-01"
            // series_detail's own overview/tmdb_rating/episode_runtime columns
            // hold "STALE_OVERVIEW_FROM_ADD_TIME" / 1.1 / 5 (set at add time,
            // never refreshed since — see seedSeries). The cache holds
            // different, fresher values. If getBySlug still read the
            // projection's own columns, this test would see the stale values.
            seedMetadataCache conn "the-wire" "Fresh cache overview" 8.5 42

            let result = SeriesProjection.getBySlug conn "the-wire" None
            match result with
            | None -> failtest "expected the-wire to be found"
            | Some detail ->
                Expect.equal detail.Name "Show the-wire" "identity card: Name comes from the projection"
                Expect.equal detail.Year 2010 "identity card: Year comes from the projection"
                Expect.equal detail.PosterRef (Some "posters/the-wire.jpg") "identity card: PosterRef comes from the projection"
                Expect.equal detail.Overview "Fresh cache overview" "Overview must come from series_metadata_cache, not the stale series_detail column"
                Expect.equal detail.TmdbRating (Some 8.5) "TmdbRating must come from series_metadata_cache, not the stale series_detail column"
                Expect.equal detail.EpisodeRuntime (Some 42) "EpisodeRuntime must come from series_metadata_cache, not the stale series_detail column"
                Expect.equal (List.length detail.Seasons) 1 "one season was seeded into the cache"
                let season = detail.Seasons.[0]
                Expect.equal (List.length season.Episodes) 2 "two episodes were seeded into the cache"
                let ep1 = season.Episodes |> List.find (fun e -> e.EpisodeNumber = 1)
                let ep2 = season.Episodes |> List.find (fun e -> e.EpisodeNumber = 2)
                Expect.isTrue ep1.IsWatched "episode 1 was marked watched under the default rewatch session"
                Expect.equal ep1.WatchedDate (Some "2024-05-01") "episode 1's watched date comes through the series_episode_progress join"
                Expect.isFalse ep2.IsWatched "episode 2 was never marked watched"
                Expect.isFalse ep1.MetadataPending "source defaults to 'tmdb' via upsertSeasonEpisodeCache — not pending"

        testCase "an empty cache degrades gracefully: identity card intact, third-party fields None, seasons empty" <| fun _ ->
            use conn = newConn ()
            // No seasons seeded into the cache at all, and no series_metadata_cache row.
            seedSeries conn "cold-show" 200 (Some "posters/cold-show.jpg") []

            let result = SeriesProjection.getBySlug conn "cold-show" None
            match result with
            | None -> failtest "expected cold-show to be found"
            | Some detail ->
                Expect.equal detail.Name "Show cold-show" "identity card: Name still comes from the projection"
                Expect.equal detail.Year 2010 "identity card: Year still comes from the projection"
                Expect.equal detail.PosterRef (Some "posters/cold-show.jpg") "identity card: PosterRef still comes from the projection"
                Expect.equal detail.TmdbRating None "TmdbRating is None on a cache miss — never a synchronous fetch"
                Expect.equal detail.Overview "" "Overview is empty on a cache miss"
                Expect.equal detail.EpisodeRuntime None "EpisodeRuntime is None on a cache miss"
                Expect.isEmpty detail.Seasons "no seasons were ever materialized into the cache"
                // MetadataPending generalizes to "no third-party metadata yet"
                // (episode-level vocabulary, ADR-0012). With zero episodes,
                // "every episode reports pending" holds vacuously — there is
                // nothing that contradicts it.
                Expect.isTrue
                    (detail.Seasons |> List.forall (fun s -> s.Episodes |> List.forall (fun e -> e.MetadataPending)))
                    "vacuously true over an empty season list — no episode exists that is NOT metadata-pending"
    ]

[<Tests>]
let getDashboardSeriesNextUpTests =
    testList "SeriesProjection.getDashboardSeriesNextUp composes the next-up tuple from the view" [

        testCase "next-up matches the pre-refactor materialized-column semantics across multiple rewatch sessions" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "multi-session-show" 300 (Some "posters/multi.jpg") [ mkSeason 1 2; mkSeason 2 1 ]
            createRewatchSession conn "multi-session-show" "with-alice"
            // Episode (1,1) watched under BOTH rewatch sessions; (1,2) and
            // (2,1) never watched by anyone. "Watched" for next-up purposes
            // is NOT scoped to a single rewatch session (ADR-0046) — the
            // first still-unwatched-by-anyone episode is (1,2).
            markWatched conn "multi-session-show" "default" 1 1 "2024-01-01"
            markWatched conn "multi-session-show" "with-alice" 1 1 "2024-01-02"

            let results = SeriesProjection.getDashboardSeriesNextUp conn None
            let entry = results |> List.tryFind (fun r -> r.Slug = "multi-session-show")
            match entry with
            | None -> failtest "expected multi-session-show to surface on the dashboard (it has an unwatched episode)"
            | Some dto ->
                Expect.equal dto.NextUpSeason 1 "next-up season should be 1 — episode (1,1) is watched by everyone, (1,2) is not"
                Expect.equal dto.NextUpEpisode 2 "next-up episode should be 2 — the first episode nobody has watched"
                Expect.equal dto.EpisodeCount 3 "episode count composed from series_episode_counts across both seasons"
    ]

[<Tests>]
let seriesNextUpFrontierTests =
    testList "series-k4zpn: series_next_up follows the furthest-watched episode, not the first unwatched one" [

        testCase "a gap behind the frontier is skipped — next up is the first unwatched episode past the furthest watched" <| fun _ ->
            use conn = newConn ()
            // Season 1 has 11 episodes. (1,3) is a skipped gap; (1,4)-(1,10)
            // are watched, making (1,10) the frontier. (1,11) is the first
            // unwatched episode strictly after the frontier — the old rule
            // would have returned (1,3) forever.
            seedSeries conn "gapped-show" 400 None [ mkSeason 1 11 ]
            for ep in [ 4 .. 10 ] do
                markWatched conn "gapped-show" "default" 1 ep "2024-06-01"

            let result = SeriesProjection.getAll conn |> List.find (fun s -> s.Slug = "gapped-show")
            match result.NextUp with
            | None -> failtest "expected a next-up episode past the frontier"
            | Some nextUp ->
                Expect.equal nextUp.SeasonNumber 1 "next-up season should still be 1"
                Expect.equal nextUp.EpisodeNumber 11 "next-up episode should be 11 (the frontier is (1,10)), not 3 (the gap behind it)"

        testCase "furthest-watched at the very last episode yields no next up, even with gaps behind it" <| fun _ ->
            use conn = newConn ()
            // Season 1 has 5 episodes, season 2 has 3. (1,3) is a skipped
            // gap. The frontier is (2,3) — the last episode of the last
            // season — so there is nothing left to recommend, regardless of
            // the gap sitting behind the frontier.
            seedSeries conn "finished-with-gap" 401 None [ mkSeason 1 5; mkSeason 2 3 ]
            for ep in [ 1; 2; 4; 5 ] do
                markWatched conn "finished-with-gap" "default" 1 ep "2024-06-01"
            for ep in [ 1 .. 3 ] do
                markWatched conn "finished-with-gap" "default" 2 ep "2024-06-02"

            let result = SeriesProjection.getAll conn |> List.find (fun s -> s.Slug = "finished-with-gap")
            Expect.isNone result.NextUp "no episode exists past the frontier (2,3) — gap at (1,3) is history, not a queue"

        testCase "no watch records at all — the frontier degenerates to the first episode overall" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "never-started-show" 402 None [ mkSeason 1 3 ]

            let result = SeriesProjection.getAll conn |> List.find (fun s -> s.Slug = "never-started-show")
            match result.NextUp with
            | None -> failtest "expected the first episode overall when nothing has been watched"
            | Some nextUp ->
                Expect.equal nextUp.SeasonNumber 1 "no frontier — falls back to season 1"
                Expect.equal nextUp.EpisodeNumber 1 "no frontier — falls back to episode 1"

        testCase "a contiguous watch run still returns the episode immediately after it" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "contiguous-show" 403 None [ mkSeason 1 5 ]
            for ep in [ 1 .. 2 ] do
                markWatched conn "contiguous-show" "default" 1 ep "2024-06-01"

            let result = SeriesProjection.getAll conn |> List.find (fun s -> s.Slug = "contiguous-show")
            match result.NextUp with
            | None -> failtest "expected episode 3 to be next up"
            | Some nextUp ->
                Expect.equal nextUp.SeasonNumber 1 "still season 1"
                Expect.equal nextUp.EpisodeNumber 3 "the episode immediately after the contiguous run (1,1)-(1,2)"
    ]

[<Tests>]
let dashboardSeasonEpisodeDotsTests =
    testList "series-ww1rb: getDashboardSeriesNextUp carries per-season/per-episode watch state" [

        testCase "holes in the middle of the current season are preserved, not collapsed to a count" <| fun _ ->
            use conn = newConn ()
            // Season 1 has 8 episodes; 1-3 and 6-7 watched, 4-5 and 8 not.
            // The frontier (series-k4zpn) is the max watched tuple (1,7), so
            // NextUp is (1,8) — CurrentSeasonNumber comes from NextUpSeason,
            // not the fallback path, exercising the primary branch.
            seedSeries conn "holey-show" 500 None [ mkSeason 1 8 ]
            for ep in [ 1; 2; 3; 6; 7 ] do
                markWatched conn "holey-show" "default" 1 ep "2024-06-01"

            let dto = SeriesProjection.getDashboardSeriesNextUp conn None |> List.find (fun r -> r.Slug = "holey-show")
            Expect.equal dto.CurrentSeasonNumber 1 "current season is 1 (the only season, and NextUpSeason agrees)"
            Expect.equal
                dto.CurrentSeasonWatched
                [ true; true; true; false; false; true; true; false ]
                "the gaps at episodes 4-5 must survive as `false` entries, not be collapsed into a watched count"

        testCase "SeasonsTouched marks only the season with at least one watched episode" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "touched-middle-show" 501 None [ mkSeason 1 2; mkSeason 2 2; mkSeason 3 2 ]
            markWatched conn "touched-middle-show" "default" 2 1 "2024-06-01"

            let dto = SeriesProjection.getDashboardSeriesNextUp conn None |> List.find (fun r -> r.Slug = "touched-middle-show")
            Expect.equal dto.SeasonsTouched [ false; true; false ] "only season 2 has any watched episode"
            // The frontier (series-k4zpn) is the only watched tuple, (2,1), so
            // NextUp is (2,2) — NextUpSeason is 2, while the highest season in
            // the series is 3. CurrentSeasonNumber must follow NextUpSeason
            // here, not the highest-season fallback, and CurrentSeasonWatched
            // must reflect season 2's own two episodes, not season 3's.
            Expect.equal dto.CurrentSeasonNumber 2 "current season comes from NextUpSeason (2), not the highest season (3)"
            Expect.equal dto.CurrentSeasonWatched [ true; false ] "season 2 has episode 1 watched and episode 2 not — the hole is preserved"

        testCase "SeasonsTouched reports true for both a fully-watched and a partially-watched season — two states, not three" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "two-state-show" 502 None [ mkSeason 1 2; mkSeason 2 2 ]
            markWatched conn "two-state-show" "default" 1 1 "2024-06-01"
            markWatched conn "two-state-show" "default" 1 2 "2024-06-01"
            markWatched conn "two-state-show" "default" 2 1 "2024-06-02"

            let dto = SeriesProjection.getDashboardSeriesNextUp conn None |> List.find (fun r -> r.Slug = "two-state-show")
            Expect.equal dto.SeasonsTouched [ true; true ] "season 1 is fully watched, season 2 is partially watched — both report true"

        testCase "CurrentSeasonNumber falls back to the highest season when there is no Next Up (fully watched)" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "finished-show" 503 None [ mkSeason 1 2; mkSeason 2 3 ]
            for ep in [ 1; 2 ] do
                markWatched conn "finished-show" "default" 1 ep "2024-06-01"
            for ep in [ 1; 2; 3 ] do
                markWatched conn "finished-show" "default" 2 ep "2024-06-02"
            // Fully watched and not recently watched enough to satisfy the
            // dashboard's own "recent" visibility rule — force visibility
            // via in-focus instead (see markInFocus's doc comment).
            markInFocus conn "finished-show"

            let dto = SeriesProjection.getDashboardSeriesNextUp conn None |> List.find (fun r -> r.Slug = "finished-show")
            Expect.equal dto.NextUpSeason 0 "no Next Up episode remains — the series is fully watched"
            Expect.equal dto.CurrentSeasonNumber 2 "falls back to the highest-numbered season (2), not the Next Up season"

        testCase "an episode watched only in a non-default rewatch session still reports true" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "alt-session-show" 504 None [ mkSeason 1 3 ]
            createRewatchSession conn "alt-session-show" "with-bob"
            markWatched conn "alt-session-show" "with-bob" 1 2 "2024-06-01"

            let dto = SeriesProjection.getDashboardSeriesNextUp conn None |> List.find (fun r -> r.Slug = "alt-session-show")
            Expect.equal dto.CurrentSeasonWatched [ false; true; false ] "episode 2, watched only under 'with-bob', still counts as watched — distinct across all rewatch sessions"
            Expect.equal dto.SeasonsTouched [ true ] "season 1 is touched by the with-bob session's watch"

        testCase "a series with no series_episode_cache rows returns CurrentSeasonNumber 0 and empty lists" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "cache-miss-show" 505 None []
            markInFocus conn "cache-miss-show"

            let dto = SeriesProjection.getDashboardSeriesNextUp conn None |> List.find (fun r -> r.Slug = "cache-miss-show")
            Expect.equal dto.CurrentSeasonNumber 0 "no cache data at all — CurrentSeasonNumber defaults to 0"
            Expect.isEmpty dto.CurrentSeasonWatched "no cache data at all — CurrentSeasonWatched is empty"
            Expect.isEmpty dto.SeasonsTouched "no cache data at all — SeasonsTouched is empty"
    ]

/// Counts the number of `IDbCommand`s created against the underlying
/// connection — each `Db.newCommand |> ... |> Db.query/querySingle/exec`
/// call creates exactly one, so this is a direct proxy for "how many
/// round trips did this function issue".
type private CountingConnection(connectionString: string) =
    inherit SqliteConnection(connectionString)
    let mutable count = 0
    member _.CommandCount = count
    member _.ResetCount() = count <- 0
    override this.CreateDbCommand() =
        count <- count + 1
        base.CreateDbCommand()

[<Tests>]
let getAllQueryCountTests =
    testList "SeriesProjection.getAll composes the cache/view joins in a single statement" [

        testCase "adding the series_metadata_cache + view joins does not turn getAll into a per-row fan-out" <| fun _ ->
            let conn = new CountingConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            MetadataCache.initialize conn
            SeriesProjection.handler.Init conn

            conn.ResetCount()
            let zeroRowResult = SeriesProjection.getAll conn
            Expect.isEmpty zeroRowResult "sanity: no series seeded yet"
            Expect.equal conn.CommandCount 1
                "with zero series_list rows, getAll's composed SELECT (identity + cache join + both views) is exactly one statement — no per-row fan-out to account for"

            seedSeries conn "show-a" 1 None [ mkSeason 1 1 ]
            seedSeries conn "show-b" 2 None [ mkSeason 1 1 ]
            seedSeries conn "show-c" 3 None [ mkSeason 1 1 ]

            conn.ResetCount()
            let threeRowResult = SeriesProjection.getAll conn
            Expect.equal (List.length threeRowResult) 3 "sanity: three series seeded"
            // The main composed SELECT is still exactly 1 statement, regardless
            // of row count — proving the cache/view joins didn't fan out. The
            // remaining 2 commands per row are the pre-existing, unrelated
            // getNextAirDate seam (getNextEpisodeAirDate + getNextSeasonAirDate,
            // both run to completion here since no fixture episode/season has
            // an air_date set) — this task neither improves nor worsens that.
            Expect.equal conn.CommandCount (1 + 3 * 2)
                "getAll's composed SELECT stays at 1 statement regardless of row count; the per-row cost is the pre-existing NextAirDate seam, unchanged by this task"
    ]
