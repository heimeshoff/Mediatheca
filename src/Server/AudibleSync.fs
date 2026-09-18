namespace Mediatheca.Server

open System
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open Mediatheca.Shared

/// The Audible progress sync (integration-jjvg2, ADR-0074/ADR-0076; reversed
/// by ADR-0082/integration-dvbjp; minutes-based percent added by
/// integration-fn3yx/ADR-0086): reads `/1.0/library`'s `is_finished` (the
/// `Finished` flag) and, for EVERY item, `Audible.getLastPositionHeard`'s
/// true `position_ms` -- the library listing's own `percent_complete` is no
/// longer trusted as a position, since it resets to 0 the moment playback
/// starts. Minutes decide: `Position = Minutes (position_ms / 60000, Some
/// runtime)` and `Percent` is CALCULATED from that position (floored,
/// clamped 0-100), never fetched from the listing, whenever both a real
/// position and the item's own `RuntimeMinutes` are known. An ASIN not yet
/// matched to a book is CREATED by this module itself (via the `createBook`
/// function `runProgressSync` takes as a parameter --
/// `Api.createBookFromAudibleItem`, wired in from `Composition.fs`, since
/// this module compiles BEFORE `Api.fs` and can't reference it directly),
/// then observed like any other item: a new purchase heard yesterday is a
/// real listening day, never a prior. "Import library"
/// (`Api.importAudibleLibrary`) is the only Audible-sourced writer of
/// `Record_prior_reading_progress` -- this module NEVER records a prior,
/// even though it now calls `getLastPositionHeard` for every item (reusing
/// the SAME access token `Audible.withAccessToken` already minted for the
/// library fetch, through the existing `Audible.throttleMetadataCall` gate).
/// Compiled BEFORE `Api.fs` (`Server.fsproj`), so -- same as
/// `PlaytimeTracker.fs` above it -- this module carries its own local
/// command-execution helper rather than reaching into `Api.fs`'s private
/// one. `observationFor` is the ONE pure decision `Api.importAudibleLibrary`
/// also calls, so import and the daily sync are the same writer of
/// `Reading_progress_observed` (ADR-0076's own text: "this task is the only
/// Audible-sourced writer of that event").
module AudibleSync =

    /// ADR-0028/administration-tj8n2: acquired only around a brief DB
    /// moment, never across an awaited HTTP call -- the same discipline
    /// `PlaytimeTracker.withLock` establishes.
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    let private executeBookCommand
        (conn: SqliteConnection)
        (slug: string)
        (command: Books.BookCommand)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<Books.BookEvent list, string> =
        let streamId = Books.streamId slug
        let storedEvents = EventStore.readStream conn streamId
        // books-wk67x: positioned, like `Api.fs`'s `executeBookCommandWithEvents`
        // — each history entry's id must be its real store position so it
        // matches what the projection/DTO expose for the same entry.
        let events =
            storedEvents
            |> List.choose (fun se -> Books.Serialization.fromStoredEvent se |> Option.map (fun e -> se.GlobalPosition, e))
        let state = Books.reconstitute events
        let currentPosition = EventStore.getStreamPosition conn streamId
        match Books.decide state command with
        | Error e -> Error e
        | Ok [] -> Ok []
        | Ok newEvents ->
            let eventDataList = newEvents |> List.map Books.Serialization.toEventData
            match EventStore.appendToStream conn streamId currentPosition eventDataList with
            | EventStore.ConcurrencyConflict _ -> Error "Concurrency conflict"
            | EventStore.Success _ ->
                for handler in projectionHandlers do
                    Projection.runProjection conn handler
                Ok newEvents

    /// This task's own "What" section: floor a source-reported float percent
    /// -- 99.6 must floor to 99, never round to 100, so rounding alone can
    /// never auto-finish a title Audible hasn't itself marked finished. An
    /// `IsFinished = true` item with no percent at all is treated as 100 --
    /// the source's own explicit signal, not a rounding artifact.
    /// integration-fn3yx/ADR-0086: this is now only ever the FALLBACK
    /// `observationFor` reaches for -- `RuntimeMinutes` unknown (a real
    /// position has nothing to be placed against), or `lastListened` was
    /// never fetched at all -- never the primary source of percent once a
    /// real `position_ms` is known.
    let percentOf (item: Audible.AudibleLibraryItem) : int option =
        match item.PercentComplete with
        | Some p -> Some (int (floor p))
        | None -> if item.IsFinished then Some 100 else None

    /// integration-fn3yx/ADR-0086: floors and clamps 0-100. `minutes` is the
    /// ALREADY int-divided position (`position_ms / 60000`) -- the exact
    /// value the observation's own `Minutes` position carries -- matching
    /// this task's own "What" section formula verbatim (`floor (minutes /
    /// runtimeMinutes x 100)`).
    let private calculatePercent (minutes: int) (runtimeMinutes: int) : int =
        if runtimeMinutes <= 0 then 0
        else (float minutes / float runtimeMinutes * 100.0) |> floor |> int |> max 0 |> min 100

    /// The one pure decision both the import and the daily sync funnel
    /// through (ADR-0076, amended by ADR-0082/ADR-0086). `lastListened =
    /// None` means "never fetched at all" -- the ONLY remaining caller of
    /// this shape is `Api.importAudibleLibraryImpl`'s already-has-an-
    /// Audible-row re-observation branch, which keeps the pre-fn3yx fallback
    /// verbatim (the listing's own percent, a `percent x runtime` position
    /// estimate). Every other caller -- `runProgressSync`, always; the
    /// import's first-ever-prior branch -- now always supplies `Some`, the
    /// result of a real `Audible.getLastPositionHeard` call:
    ///
    /// - `PositionMs` AND `RuntimeMinutes` both known: MINUTES DECIDE. The
    ///   percent is CALCULATED from the true position
    ///   (`calculatePercent`), never fetched from the listing's unreliable
    ///   `percent_complete` -- the exact value this task exists to stop
    ///   trusting. `Position = Minutes (positionMs / 60000, Some runtime)`.
    /// - `PositionMs` known, `RuntimeMinutes` unknown: the only remaining
    ///   use of the listing's percent (`percentOf`); `Position = None`
    ///   since there is no total to place it against.
    /// - `PositionMs = None` (`status = "DoesNotExist"`, or an undecodable
    ///   body -- both collapse to `emptyLastPositionHeard` inside
    ///   `Audible.getLastPositionHeard` itself): Audible has no position to
    ///   report at all. NEVER guessed from the listing's percent -- no
    ///   observation, unless the item's own explicit `IsFinished` flag says
    ///   otherwise, in which case `Percent = 100, Position = None`.
    ///
    /// `ObservedOn` always prefers `lastListened`'s own last-updated day
    /// over `today` when a `Some` was supplied.
    let observationFor (item: Audible.AudibleLibraryItem) (today: string) (lastListened: Audible.LastPositionHeard option) : Books.ReadingProgressObservedData option =
        match lastListened with
        | None ->
            percentOf item
            |> Option.map (fun percent ->
                let position =
                    item.RuntimeMinutes
                    |> Option.map (fun total -> Minutes (int (Math.Round(float percent / 100.0 * float total)), Some total))
                { Percent = percent
                  Position = position
                  Source = ProgressSource.Audible
                  ObservedOn = today
                  Finished = item.IsFinished })
        | Some lp ->
            let observedOn = lp.LastUpdatedOn |> Option.defaultValue today
            match lp.PositionMs, item.RuntimeMinutes with
            | Some positionMs, Some total ->
                let minutes = int (positionMs / 60000L)
                Some
                    { Percent = calculatePercent minutes total
                      Position = Some (Minutes (minutes, Some total))
                      Source = ProgressSource.Audible
                      ObservedOn = observedOn
                      Finished = item.IsFinished }
            | Some _, None ->
                percentOf item
                |> Option.map (fun percent ->
                    { Percent = percent
                      Position = None
                      Source = ProgressSource.Audible
                      ObservedOn = observedOn
                      Finished = item.IsFinished })
            | None, _ ->
                if item.IsFinished then
                    Some
                        { Percent = 100
                          Position = None
                          Source = ProgressSource.Audible
                          ObservedOn = observedOn
                          Finished = true }
                else
                    None

    let formatResult (r: AudibleProgressSyncResult) : string =
        let base_ = sprintf "%d observed, %d created" r.Observed r.Created
        if List.isEmpty r.Errors then base_
        else sprintf "%s (%d item error(s))" base_ (List.length r.Errors)

    /// The plain, human-readable summary `Api.importAudibleLibrary` persists
    /// under `audible_last_import_result` -- same convention as
    /// `formatResult` above (a formatted string, not literal JSON, despite
    /// this task's own "What" section saying "JSON"). integration-dtdbb adds
    /// the priors-from-Audible/priors-dated-today split (so a silent 401 on
    /// the metadata endpoint stays visible) and the legacy-repair count.
    let formatImportResult (r: AudibleImportResult) : string =
        let base_ =
            sprintf
                "%d total, %d created, %d already known, %d progress observed, %d priors from Audible, %d priors dated today, %d repaired"
                r.Total r.Created r.AlreadyKnown r.ProgressObserved r.PriorsFromAudible r.PriorsToday r.Repaired
        if List.isEmpty r.Errors then base_
        else sprintf "%s (%d item error(s))" base_ (List.length r.Errors)

    /// Runs the daily progress sync: a matched ASIN is observed as before; an
    /// unmatched ASIN is CREATED via `createBook` (`Api.
    /// createBookFromAudibleItem`, wired in from `Composition.fs` since this
    /// module compiles before `Api.fs`) and then observed exactly the same
    /// way -- an ordinary `Observe_reading_progress`, never a prior
    /// (ADR-0082/integration-dvbjp reverses integration-jjvg2's "this job
    /// never creates a book" rule). integration-fn3yx/ADR-0086: every item
    /// now ALSO gets its own `Audible.getLastPositionHeard` fetch, reusing
    /// the SAME access token `Audible.withAccessToken` minted for the
    /// `/1.0/library` call above (no separate per-item refresh/retry
    /// orchestration -- `Api.importAudibleLibraryImpl`'s own precedent), so
    /// `observationFor` can calculate the real percent from the true
    /// position instead of trusting the listing's own `percent_complete`. A
    /// metadata-call failure for one item (network, 401, 5xx) skips that
    /// item for this run and is reported in `Errors` -- it never falls back
    /// to the listing's percent, the exact value this task exists to stop
    /// trusting. `Error` carries `Audible.authFileRejectedPrefix` when the
    /// auth file itself was rejected -- the caller (`Composition.fs`'s job
    /// body) must surface THAT as a genuine job failure, never a `Skipped`
    /// disposition; any other `Error` means "not configured".
    let runProgressSync
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (getAudibleConfig: unit -> Audible.AudibleConfig)
        (persistAccessToken: Audible.AudibleAccessToken -> unit)
        (createBook: Audible.AudibleLibraryItem -> Async<Result<AddBookOutcome, string>>)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<Result<AudibleProgressSyncResult, string>> =
        async {
            let config = getAudibleConfig ()
            match config.AuthFile with
            | None -> return Error "Audible is not configured -- paste an auth file in Settings"
            | Some authFile ->
                let host = Audible.marketplaceHost authFile.LocaleCode
                let cached =
                    match config.CachedAccessToken, config.CachedAccessTokenExpiresAt with
                    | Some t, Some e -> Some (t, e)
                    | _ -> None
                // integration-fn3yx (ADR-0086): the token actually used to
                // authenticate the (possibly retried-after-401) library
                // fetch, captured as a side effect so the per-item
                // `getLastPositionHeard` calls below reuse it directly
                // rather than re-running `withAccessToken`'s whole
                // cache/refresh/retry orchestration for every title --
                // `Api.importAudibleLibraryImpl`'s own precedent
                // (integration-dtdbb).
                let mutable currentAccessToken : string option = None
                let! libraryResult =
                    Audible.withAccessToken
                        cached
                        (fun () -> Audible.refreshAccessToken httpClient authFile)
                        persistAccessToken
                        (fun token ->
                            currentAccessToken <- Some token
                            Audible.getLibrary httpClient host token)
                match libraryResult with
                | Error msg ->
                    if msg.StartsWith(Audible.authFileRejectedPrefix) then
                        withLock jobLock (fun () -> SettingsStore.setSetting conn "audible_last_error" msg)
                    return Error msg
                | Ok items ->
                    let today = DateTime.Now.ToString("yyyy-MM-dd")
                    let mutable observed = 0
                    let mutable created = 0
                    let mutable errors : string list = []
                    for item in items do
                        try
                            let existingSlug = withLock jobLock (fun () -> BookProjection.findByExternalId conn (AudibleAsin item.Asin))
                            // ADR-0082/integration-dvbjp: an unmatched ASIN is
                            // CREATED here (via `createBook`, wired to
                            // `Api.createBookFromAudibleItem`) instead of
                            // being counted `Unmatched` and skipped --
                            // integration-jjvg2's "this job never creates a
                            // book" rule is reversed. `Duplicate_found` should
                            // not happen in practice (the create path always
                            // sets `SkipDuplicateCheck = true`), but is
                            // handled the same as a fresh create -- both
                            // resolve to a slug to observe.
                            let! slugToObserve =
                                match existingSlug with
                                | Some slug -> async { return Some slug }
                                | None ->
                                    async {
                                        match! createBook item with
                                        | Ok (AddBookOutcome.Book_added slug) ->
                                            created <- created + 1
                                            return Some slug
                                        | Ok (AddBookOutcome.Duplicate_found (slug, _)) ->
                                            return Some slug
                                        | Error e ->
                                            errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin e ]
                                            return None
                                    }
                            match slugToObserve with
                            | None -> ()
                            | Some slug ->
                                // integration-fn3yx (ADR-0086): every item --
                                // matched, or one this loop just created --
                                // gets its own last-position fetch now,
                                // through the same metadata throttle as
                                // Api.fs's prior path. The sync still never
                                // records a prior; a fresh purchase heard
                                // yesterday is a real listening day.
                                match currentAccessToken with
                                | None ->
                                    errors <- errors @ [ sprintf "%s (%s): no access token available to fetch the last-listened position" item.Title item.Asin ]
                                | Some token ->
                                    match! Audible.getLastPositionHeard httpClient host token item.Asin with
                                    | Error e ->
                                        errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin e ]
                                    | Ok lastPositionHeard ->
                                        match observationFor item today (Some lastPositionHeard) with
                                        | None -> ()
                                        | Some data ->
                                            let result = withLock jobLock (fun () -> executeBookCommand conn slug (Books.Observe_reading_progress data) projectionHandlers)
                                            match result with
                                            | Ok events when not (List.isEmpty events) -> observed <- observed + 1
                                            | Ok _ -> ()
                                            | Error e -> errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin e ]
                        with ex ->
                            errors <- errors @ [ sprintf "%s (%s): %s" item.Title item.Asin ex.Message ]
                    let result = { Observed = observed; Created = created; Errors = errors }
                    withLock jobLock (fun () ->
                        SettingsStore.setSetting conn "audible_last_sync" (DateTime.UtcNow.ToString("o"))
                        SettingsStore.setSetting conn "audible_last_sync_result" (formatResult result)
                        // integration-k4vqm's lesson (ADR-0068), bound by this
                        // task's own Notes: an empty-but-200 library response
                        // is inconclusive, not evidence the auth file is
                        // fine again -- only a genuinely populated response
                        // clears a standing `audible_last_error` notice.
                        if not (List.isEmpty items) then
                            SettingsStore.deleteSetting conn "audible_last_error")
                    return Ok result
        }
