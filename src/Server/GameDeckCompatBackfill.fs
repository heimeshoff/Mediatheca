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
        /// games-wkyf0: a `Steam.getDeckCompatibility` `Error` result (store
        /// page without the `data-hardwarecompatibility` attribute —
        /// delisted apps, age-gate redirects, tools/DLC) — reported
        /// separately from `Errors` (the exception branch below), which the
        /// previous `Error _ -> ()` no-op silently dropped from every
        /// summary.
        Failed: int
        Errors: int
    }

    /// Same "acquire only around the brief DB moment, never across an
    /// awaited HTTP call" discipline as `GameFacetBackfill.withLock`
    /// (ADR-0028).
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    /// `now` is an injected clock (`Composition.fs` passes `DateTime.UtcNow`
    /// in production) so the retry backoff `MetadataCache.
    /// findGamesNeedingDeckCompatBackfill` applies is testable without
    /// sleeping (games-wkyf0).
    let runBackfill (conn: SqliteConnection) (jobLock: SemaphoreSlim) (httpClient: HttpClient) (now: System.DateTime) : Async<BackfillResult> =
        async {
            let candidates = withLock jobLock (fun () -> MetadataCache.findGamesNeedingDeckCompatBackfill conn now)
            let mutable succeeded = 0
            let mutable failed = 0
            let mutable errors = 0
            for (slug, steamAppId) in candidates do
                try
                    // Pacing lives inside Steam.getDeckCompatibility itself now
                    // (integration-w7ktb's Adapter-owned storefront throttle, which
                    // also covers the store-page fetch this backfill drives) --
                    // callers no longer pace themselves.
                    let! compatResult = Steam.getDeckCompatibility httpClient steamAppId
                    match compatResult with
                    | Ok compat ->
                        withLock jobLock (fun () -> MetadataCache.upsertGameDeckCompat conn slug compat)
                        succeeded <- succeeded + 1
                    | Error _ ->
                        // Steam had nothing usable for this appId right now —
                        // leave deck_compat_fetched_at NULL so a later run can
                        // retry it (the resumability the WHERE-clause cursor
                        // promises), but record the attempt so
                        // findGamesNeedingDeckCompatBackfill's backoff skips
                        // it until min(2^attempts, 30) days have passed
                        // (games-wkyf0) instead of retrying every night
                        // forever.
                        withLock jobLock (fun () -> MetadataCache.recordDeckCompatFailedAttempt conn slug now)
                        failed <- failed + 1
                with _ ->
                    // games-wkyf0: an unhandled exception backs off exactly
                    // like an `Error` result — see MetadataCache.fs's
                    // `deckCompatBackoffDays` doc comment for why every
                    // failure kind shares one rule.
                    withLock jobLock (fun () -> MetadataCache.recordDeckCompatFailedAttempt conn slug now)
                    errors <- errors + 1
            return { Processed = List.length candidates; Succeeded = succeeded; Failed = failed; Errors = errors }
        }
