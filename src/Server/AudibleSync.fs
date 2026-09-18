namespace Mediatheca.Server

open System
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open Mediatheca.Shared

/// The Audible progress sync (integration-jjvg2, ADR-0074/ADR-0076; reversed
/// by ADR-0082/integration-dvbjp): reads `/1.0/library`'s
/// `percent_complete`/`is_finished` and turns each book's reading position
/// into an `Observe_reading_progress` command. An ASIN not yet matched to a
/// book is now CREATED by this module itself (via the `createBook` function
/// `runProgressSync` takes as a parameter -- `Api.createBookFromAudibleItem`,
/// wired in from `Composition.fs`, since this module compiles BEFORE `Api.fs`
/// and can't reference it directly), then observed like any other item: a
/// new purchase heard yesterday is a real listening day, never a prior.
/// "Import library" (`Api.importAudibleLibrary`) is the only Audible-sourced
/// writer of `Record_prior_reading_progress` -- this module NEVER records a
/// prior and NEVER calls `getLastPositionHeard`. Compiled BEFORE `Api.fs`
/// (`Server.fsproj`), so -- same as `PlaytimeTracker.fs` above it -- this
/// module carries its own local command-execution helper
/// rather than reaching into `Api.fs`'s private one. `observationFor` is the
/// ONE pure decision `Api.importAudibleLibrary` also calls, so import and
/// the daily sync are the same writer of `Reading_progress_observed`
/// (ADR-0076's own text: "this task is the only Audible-sourced writer of
/// that event").
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
        let events = storedEvents |> List.choose Books.Serialization.fromStoredEvent
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
    let percentOf (item: Audible.AudibleLibraryItem) : int option =
        match item.PercentComplete with
        | Some p -> Some (int (floor p))
        | None -> if item.IsFinished then Some 100 else None

    /// The one pure decision both the import and the daily sync funnel
    /// through (ADR-0076, amended by ADR-0082/integration-dtdbb): `None`
    /// when the item carries neither a percent nor an explicit finished
    /// flag -- nothing to observe. `lastListened` is `None` on every call
    /// from `runProgressSync` (the nightly sync never fetches it) and
    /// `Some` (possibly both-`None` fields) only from
    /// `Api.importAudibleLibraryImpl`'s prior-recording path.
    /// `ObservedOn` prefers `lastListened`'s own last-updated day over
    /// `today`; `Position` prefers `Minutes (int (positionMs / 60000L), Some
    /// runtime)` -- Audible's own reported elapsed time -- over the
    /// `percent x runtime` estimate when a `PositionMs` is present.
    let observationFor (item: Audible.AudibleLibraryItem) (today: string) (lastListened: Audible.LastPositionHeard option) : Books.ReadingProgressObservedData option =
        percentOf item
        |> Option.map (fun percent ->
            let observedOn =
                lastListened |> Option.bind (fun l -> l.LastUpdatedOn) |> Option.defaultValue today
            let position =
                item.RuntimeMinutes
                |> Option.map (fun total ->
                    match lastListened |> Option.bind (fun l -> l.PositionMs) with
                    | Some positionMs -> Minutes (int (positionMs / 60000L), Some total)
                    | None -> Minutes (int (Math.Round(float percent / 100.0 * float total)), Some total))
            { Percent = percent
              Position = position
              Source = ProgressSource.Audible
              ObservedOn = observedOn
              Finished = item.IsFinished })

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
    /// unmatched ASIN is now CREATED via `createBook` (`Api.
    /// createBookFromAudibleItem`, wired in from `Composition.fs` since this
    /// module compiles before `Api.fs`) and then observed exactly the same
    /// way -- an ordinary `Observe_reading_progress`, never a prior, never a
    /// `getLastPositionHeard` call (ADR-0082/integration-dvbjp reverses
    /// integration-jjvg2's "this job never creates a book" rule). `Error`
    /// carries `Audible.authFileRejectedPrefix` when the auth file itself was
    /// rejected -- the caller (`Composition.fs`'s job body) must surface THAT
    /// as a genuine job failure, never a `Skipped` disposition; any other
    /// `Error` means "not configured".
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
                let! libraryResult =
                    Audible.withAccessToken
                        cached
                        (fun () -> Audible.refreshAccessToken httpClient authFile)
                        persistAccessToken
                        (fun token -> Audible.getLibrary httpClient host token)
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
                                // integration-dtdbb, ADR-0082 §2: the nightly
                                // sync never fetches a last-listened date and
                                // never records a prior -- `lastListened` is
                                // always `None` here, for a matched book AND
                                // for one it just created.
                                match observationFor item today None with
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
