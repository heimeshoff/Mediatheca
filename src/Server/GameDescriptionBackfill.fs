namespace Mediatheca.Server

open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite

/// Resumable, throttled re-sanitization backfill for
/// `game_metadata_cache.description` (games-fffvm, ADR-0043/ADR-0045). Same
/// shape as `GameFacetBackfill.fs`/`GameDeckCompatBackfill.fs`/
/// `GameReleaseDateBackfill.fs` — the sibling jobs this task's prior art
/// established.
///
/// Walks `MetadataCache.findGamesNeedingDescriptionBackfill`'s own
/// `description_fetched_at IS NULL` cursor (a LEFT JOIN, so it also covers
/// the RAWG-only legacy population with no cache row at all), re-fetches
/// each candidate's description through the same third party that
/// originally sourced it (Steam's storefront or RAWG's game-details
/// endpoint), and writes the sanitized result back via the read-modify-write
/// pattern (`MetadataCache.tryGetGameIdentityCard` + `upsertGameIdentityCard`)
/// so an existing `short_description`/`website_url` is never blanked.
/// Stamps `description_fetched_at` on every genuine write via
/// `MetadataCache.stampDescriptionFetched` — one stamp per row as it lands,
/// never a batch stamp at the end, so a mid-run failure leaves already-
/// processed rows durably stamped.
module GameDescriptionBackfill =

    type BackfillResult = {
        Processed: int
        Succeeded: int
        Errors: int
        /// RAWG candidates skipped because `RawgConfig.ApiKey` is blank —
        /// no HTTP call attempted, row left unstamped. Neither an error nor
        /// a success: counted separately so an operator can tell "RAWG key
        /// missing" apart from "RAWG fetch failed".
        Skipped: int
    }

    /// Same "acquire only around the brief DB moment, never across an
    /// awaited HTTP call" discipline as the three sibling backfills
    /// (ADR-0028).
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    let runBackfill
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        : Async<BackfillResult> =
        async {
            let candidates = withLock jobLock (fun () -> MetadataCache.findGamesNeedingDescriptionBackfill conn)
            let mutable succeeded = 0
            let mutable errors = 0
            let mutable skipped = 0
            for (slug, source) in candidates do
                try
                    match source with
                    | MetadataCache.SteamApp appId ->
                        // Pacing lives inside Steam.getSteamStoreDetails itself
                        // (integration-w7ktb's Adapter-owned storefront
                        // throttle) -- callers no longer pace themselves.
                        let! storeDetails = Steam.getSteamStoreDetails httpClient appId
                        match storeDetails with
                        | Ok details ->
                            // Steam.storeDescription is already sanitized at
                            // decode time (games-r1tx4) -- no re-sanitization
                            // needed here. An Ok fetch with an empty
                            // about_the_game/detailed_description still
                            // stamps: the source genuinely has nothing, and
                            // re-polling forever would be the release-date
                            // job's semantics, which this cursor doesn't share.
                            withLock jobLock (fun () ->
                                let current = MetadataCache.tryGetGameIdentityCard conn slug
                                MetadataCache.upsertGameIdentityCard conn slug {
                                    current with
                                        Description = Steam.storeDescription details
                                        ShortDescription = details.ShortDescription
                                }
                                MetadataCache.stampDescriptionFetched conn slug)
                            succeeded <- succeeded + 1
                        | Error _ ->
                            // Steam had nothing for this appId right now --
                            // leave description_fetched_at NULL so the next
                            // run retries it, and leave the old description
                            // in the cache row untouched.
                            ()
                    | MetadataCache.RawgGame rawgId ->
                        let rawgConfig = getRawgConfig()
                        if System.String.IsNullOrWhiteSpace(rawgConfig.ApiKey) then
                            // No API key configured -- skip without an HTTP
                            // call and without stamping (neither an error
                            // nor a success; retried once a key is set).
                            skipped <- skipped + 1
                        else
                            let! details = Rawg.getGameDetails httpClient rawgConfig rawgId
                            let description =
                                if details.Description <> "" then DescriptionSanitizer.sanitize details.Description
                                elif details.DescriptionRaw <> "" then DescriptionSanitizer.sanitize details.DescriptionRaw
                                else ""
                            withLock jobLock (fun () ->
                                let current = MetadataCache.tryGetGameIdentityCard conn slug
                                MetadataCache.upsertGameIdentityCard conn slug { current with Description = description }
                                MetadataCache.stampDescriptionFetched conn slug)
                            succeeded <- succeeded + 1
                with _ ->
                    errors <- errors + 1
            return { Processed = List.length candidates; Succeeded = succeeded; Errors = errors; Skipped = skipped }
        }
