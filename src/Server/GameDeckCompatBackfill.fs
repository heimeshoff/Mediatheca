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
        /// games-kfpqp (ADR-0087): candidates drawn from the re-check
        /// cohort (`MetadataCache.findGamesNeedingDeckCompatBackfill`'s
        /// `Some verdict` candidates) — already counted once in `Processed`,
        /// this is the subset of `Processed` that came due for a re-check
        /// rather than a first-ever fetch (`Processed - Rechecks` is the
        /// never-fetched cohort's count).
        Rechecks: int
        /// games-kfpqp (ADR-0087): successful re-checks whose fresh verdict
        /// differs from the one on record before this run — the only
        /// evidence there will be for tuning the per-verdict age limits
        /// later.
        VerdictsChanged: int
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
            let mutable rechecks = 0
            let mutable verdictsChanged = 0
            for (slug, steamAppId, recordedVerdict) in candidates do
                // games-kfpqp: `recordedVerdict` is `Some _` exactly for a
                // re-check-cohort candidate (see
                // `MetadataCache.findGamesNeedingDeckCompatBackfill`) —
                // counted here regardless of the outcome below, since
                // `Rechecks` reports how many re-checks were attempted, not
                // how many succeeded.
                if recordedVerdict.IsSome then rechecks <- rechecks + 1
                try
                    // Pacing lives inside Steam.getDeckCompatibility itself now
                    // (integration-w7ktb's Adapter-owned storefront throttle, which
                    // also covers the store-page fetch this backfill drives) --
                    // callers no longer pace themselves.
                    let! compatResult = Steam.getDeckCompatibility httpClient steamAppId
                    match compatResult with
                    | Ok compat ->
                        withLock jobLock (fun () -> MetadataCache.upsertGameDeckCompat conn slug compat now)
                        succeeded <- succeeded + 1
                        // games-kfpqp: only a re-check has a prior recorded
                        // verdict to compare the fresh one against — a
                        // first-ever fetch (`None`) can never have "changed".
                        match recordedVerdict with
                        | Some recorded when recorded <> compat -> verdictsChanged <- verdictsChanged + 1
                        | _ -> ()
                    | Error _ ->
                        // Steam had nothing usable for this appId right now —
                        // leave deck_compat_fetched_at (and, for a re-check,
                        // the already-recorded verdict) untouched so a later
                        // run can retry it (the resumability the WHERE-clause
                        // cursor promises), but record the attempt so
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
                    // failure kind shares one rule. This applies identically
                    // to a re-check candidate: its recorded verdict and stamp
                    // are left untouched, same as an `Error` result above.
                    withLock jobLock (fun () -> MetadataCache.recordDeckCompatFailedAttempt conn slug now)
                    errors <- errors + 1
            return {
                Processed = List.length candidates
                Succeeded = succeeded
                Failed = failed
                Errors = errors
                Rechecks = rechecks
                VerdictsChanged = verdictsChanged
            }
        }
