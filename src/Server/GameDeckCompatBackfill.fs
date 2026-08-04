namespace Mediatheca.Server

open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite

/// Resumable, throttled backfill of Steam's Deck-compatibility verdict into
/// `game_metadata_cache.deck_compat` (games-b8xnw, ADR-0043/ADR-0045). Same
/// shape as `GameFacetBackfill.fs` — the sibling job this task's `depends_on`
/// exists to reuse rather than inventing a second resumable-walk mechanism.
///
/// Walks every game whose cache row's `deck_compat_fetched_at` is still NULL
/// (`MetadataCache.findGamesNeedingDeckCompatBackfill`'s own cursor,
/// deliberately separate from the play-facets `fetched_at` column — see
/// `MetadataCache.initialize`'s doc comment) and has a linked Steam app id,
/// fetches the Deck-compatibility verdict via `Steam.getDeckCompatibility`
/// (the store app-page HTML scrape that replaces the dead
/// `ajaxgetdeckappcompatibilityreport` endpoint — see `Steam.fs`'s module
/// doc comment), and writes it back via `MetadataCache.upsertGameDeckCompat`.
/// The `WHERE deck_compat_fetched_at IS NULL` clause IS the resume cursor,
/// exactly like `GameFacetBackfill`'s.
module GameDeckCompatBackfill =

    type BackfillResult = {
        Processed: int
        Succeeded: int
        Errors: int
    }

    /// Same "acquire only around the brief DB moment, never across an
    /// awaited HTTP call" discipline as `GameFacetBackfill.withLock`
    /// (ADR-0028).
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    let runBackfill (conn: SqliteConnection) (jobLock: SemaphoreSlim) (httpClient: HttpClient) : Async<BackfillResult> =
        async {
            let candidates = withLock jobLock (fun () -> MetadataCache.findGamesNeedingDeckCompatBackfill conn)
            let mutable succeeded = 0
            let mutable errors = 0
            for (slug, steamAppId) in candidates do
                try
                    do! Async.Sleep 300 // Rate limit Steam Store page fetches, mirrors GameFacetBackfill's throttle
                    let! compatResult = Steam.getDeckCompatibility httpClient steamAppId
                    match compatResult with
                    | Ok compat ->
                        withLock jobLock (fun () -> MetadataCache.upsertGameDeckCompat conn slug compat)
                        succeeded <- succeeded + 1
                    | Error _ ->
                        // Steam had nothing usable for this appId right now —
                        // leave deck_compat_fetched_at NULL so the next run
                        // retries it (the resumability the WHERE-clause
                        // cursor promises).
                        ()
                with _ ->
                    errors <- errors + 1
            return { Processed = List.length candidates; Succeeded = succeeded; Errors = errors }
        }
