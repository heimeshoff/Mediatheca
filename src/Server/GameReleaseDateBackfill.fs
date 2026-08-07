namespace Mediatheca.Server

open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite

/// Resumable, throttled backfill of Steam's release-date facts into
/// `game_metadata_cache` (games-ev65k, ADR-0043/ADR-0045). Same shape as
/// `GameFacetBackfill.fs`/`GameDeckCompatBackfill.fs` — the sibling jobs
/// this task's prior art (games-a7dqx/games-b8xnw) established.
///
/// Walks `MetadataCache.findGamesNeedingReleaseDateBackfill`'s own cursor
/// (never-fetched, still coming-soon, or still future/unparseable-dated —
/// see that function's own doc comment for why this differs from the
/// simpler "fetched_at IS NULL forever" shape the other two backfills use),
/// fetches `appdetails` (English locale), parses the raw release-date
/// string via `ReleaseDateParsing.tryParseSortable`, and writes both the raw
/// string and the parsed date back via `MetadataCache.upsertGameReleaseDate`.
/// Writes only `game_metadata_cache`; never touches `game_detail` (there is
/// no override tier for a release date — ADR-0043 doesn't need one here,
/// unlike play facets).
module GameReleaseDateBackfill =

    type BackfillResult = {
        Processed: int
        Succeeded: int
        Errors: int
    }

    /// Same "acquire only around the brief DB moment, never across an
    /// awaited HTTP call" discipline as `GameFacetBackfill.withLock`/
    /// `GameDeckCompatBackfill.withLock` (ADR-0028).
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    let runBackfill (conn: SqliteConnection) (jobLock: SemaphoreSlim) (httpClient: HttpClient) : Async<BackfillResult> =
        async {
            let candidates = withLock jobLock (fun () -> MetadataCache.findGamesNeedingReleaseDateBackfill conn)
            let mutable succeeded = 0
            let mutable errors = 0
            for (slug, steamAppId) in candidates do
                try
                    do! Async.Sleep 300 // Rate limit Steam Store API calls, mirrors GameFacetBackfill's/GameDeckCompatBackfill's throttle
                    let! storeDetails = Steam.getSteamStoreDetails httpClient steamAppId
                    match storeDetails with
                    | Ok details ->
                        let parsed = ReleaseDateParsing.tryParseSortable details.ReleaseDateRaw
                        withLock jobLock (fun () ->
                            MetadataCache.upsertGameReleaseDate conn slug details.ReleaseDateRaw parsed details.ComingSoon)
                        succeeded <- succeeded + 1
                    | Error _ ->
                        // Steam had nothing for this appId right now — leave
                        // release_date_fetched_at NULL so the next run
                        // retries it (the resumability the WHERE-clause
                        // cursor promises).
                        ()
                with _ ->
                    errors <- errors + 1
            return { Processed = List.length candidates; Succeeded = succeeded; Errors = errors }
        }
