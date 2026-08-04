namespace Mediatheca.Server

open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite

/// Resumable, throttled backfill of Steam-derived play facets into
/// `game_metadata_cache` (games-a7dqx, ADR-0053). Same shape as the
/// existing scheduled-job infrastructure (`ScheduledJobs.fs`) and the
/// `Async.Sleep 300` Steam Store rate-limit throttle `Api.fs`'s description
/// backfill already uses.
///
/// Walks every game whose cache row is still seed-only
/// (`MetadataCache.findGamesNeedingFacetBackfill`'s `fetched_at IS NULL`
/// cursor) and has a linked Steam app id, fetches `appdetails` (English
/// locale), derives facets via `FacetDerivation.deriveFacets`, and writes
/// them back via `MetadataCache.upsertGameFacets`. The `WHERE fetched_at IS
/// NULL` clause IS the resume cursor: a successfully-processed row is
/// stamped by `upsertGameFacets`, so a restart mid-walk (or a run that
/// simply doesn't finish inside one scheduled window) picks up exactly
/// where it left off — no separate cursor table needed. Writes only
/// `game_metadata_cache`; structurally incapable of touching
/// `game_detail`'s `facet_override_*` columns (this module never
/// references them), so no "don't clobber a manual override" guard is
/// needed, per ADR-0053.
module GameFacetBackfill =

    type BackfillResult = {
        Processed: int
        Succeeded: int
        Errors: int
    }

    /// `jobLock` follows the same "acquire only around the brief DB moment,
    /// never across an awaited HTTP call" discipline `PlaytimeTracker.fs`'s
    /// `withLock` establishes (ADR-0028) — the cursor read and each row's
    /// write are locked individually, the Steam fetch and the 300ms
    /// throttle sleep are not.
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    let runBackfill (conn: SqliteConnection) (jobLock: SemaphoreSlim) (httpClient: HttpClient) : Async<BackfillResult> =
        async {
            let candidates = withLock jobLock (fun () -> MetadataCache.findGamesNeedingFacetBackfill conn)
            let mutable succeeded = 0
            let mutable errors = 0
            for (slug, steamAppId) in candidates do
                try
                    do! Async.Sleep 300 // Rate limit Steam Store API calls, mirrors Api.fs's description backfill
                    let! storeDetails = Steam.getSteamStoreDetails httpClient steamAppId
                    match storeDetails with
                    | Ok details ->
                        let facets = FacetDerivation.deriveFacets details.CategoryIds
                        withLock jobLock (fun () -> MetadataCache.upsertGameFacets conn slug facets details.CategoryIds)
                        succeeded <- succeeded + 1
                    | Error _ ->
                        // Steam had nothing for this appId right now — leave
                        // fetched_at NULL so the next run retries it (the
                        // resumability the WHERE-clause cursor promises).
                        ()
                with _ ->
                    errors <- errors + 1
            return { Processed = List.length candidates; Succeeded = succeeded; Errors = errors }
        }
