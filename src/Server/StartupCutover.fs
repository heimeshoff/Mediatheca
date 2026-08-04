namespace Mediatheca.Server

open System
open System.IO
open Microsoft.Data.Sqlite
open Thoth.Json.Net

/// One-boot, zero-button automation of the series + play-session cutover
/// (plan.md Phases 3–5): the drift-check → compensate → rebuild-SeriesProjection
/// sequence and the play-session dry-run → migrate → rebuild-all → drift-check
/// sequence, executed unattended during `Composition.buildApp`'s single-threaded
/// startup window — after projections have caught up, before the web server
/// serves a single request and before any scheduled job starts.
///
/// Safety posture:
/// - A whole-database `VACUUM INTO` backup is taken before this release's
///   silent migrations first touch an existing store (`backupIfPending`), and
///   `Administration.runPlaySessionMigration` takes its own second backup
///   (ADR-0034 guardrail 1) right before the irreversible append.
/// - Every gate failure ABORTS the cutover without running the destructive
///   step it guards: no rebuild on unexplained drift, no migration on an
///   integrity-gate failure. The app then boots normally on the old data;
///   the Steam-sync gate (`PlaytimeTracker.syncGateOpen`) stays closed, and
///   the next boot retries from the top — every step is idempotent
///   (compensating events only appended for drift actually found, migration
///   appends are per-stream idempotent via `streamAlreadyMigrated`, rebuilds
///   are drop+replay).
/// - The one genuinely dangerous crash window — after the migration rewinds
///   GameProjection/PlaySessionProjection checkpoints to 0, before rebuild-all
///   restores consistency — is closed by a phase marker: `ensureSafeCatchUp`
///   sees it on the next boot and rebuilds (drop + replay, always safe)
///   instead of letting incremental catch-up replay the whole log INTO
///   already-populated tables (`PlaySessionProjection.mergeSession` SUMS on
///   conflict — that would double-count every session).
///
/// Compensating events (the ADR-0051 composer, now automated): a `status` or
/// `genres` columnMismatch on `series_list`/`series_detail` means the retired
/// pre-series-r2xhv imperative refresh writer captured a TMDB-side change
/// (an un-cancellation, a genre reclassification) that never got an event —
/// live holds the fresher truth, so the fix is appending the event that
/// records what live already shows (`Series_refreshed` / `Series_categorized`),
/// stamped `{"source":"startup-cutover"}` per ADR-0051's correction note so
/// they stay auditable. Any other discrepancy shape is NOT auto-fixable and
/// aborts the cutover for human inspection.
module StartupCutover =

    /// Written only after the final drift check reads zero — the whole
    /// cutover is skipped on every boot thereafter.
    let completedMarkerKey = "startup_cutover_2026_08_completed"

    /// Set immediately before `runPlaySessionMigration`, cleared only after
    /// rebuild-all completes — see `ensureSafeCatchUp`. Public so tests can
    /// arrange the crashed-mid-cutover state directly.
    let phaseMarkerKey = "startup_cutover_phase"

    let private log fmt = Printf.kprintf (fun s -> eprintfn "[StartupCutover] %s" s) fmt

    /// Whole-database safety copy (plan.md Phase 3 step 6, made single-step):
    /// `VACUUM INTO` a dated file under `<data-dir>/backups/`, taken BEFORE
    /// any of this release's silent migrations (the cache-tier renames, the
    /// one-time seed, the deprecated-column drops) touch an existing store.
    /// Consistent by construction (ADR-0034) — no WAL sidecars to manage.
    /// Skipped once the cutover has completed, and on a fresh install (an
    /// empty event log has nothing to protect). `Error` means the cutover
    /// must not run this boot; the caller boots the app normally regardless.
    let backupIfPending (conn: SqliteConnection) (dbPath: string) : Result<string option, string> =
        match SettingsStore.getSetting conn completedMarkerKey with
        | Some _ -> Ok None
        | None ->
            if EventStore.getMaxGlobalPosition conn = 0L then Ok None
            else
                let backupDir = Path.Combine(Path.GetDirectoryName(dbPath), "backups")
                Directory.CreateDirectory(backupDir) |> ignore
                let backupPath =
                    Path.Combine(backupDir, sprintf "pre-cutover-%s.db" (DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")))
                match EventStore.vacuumIntoBackup conn backupPath with
                | Ok () ->
                    log "pre-cutover backup written: %s" backupPath
                    Ok (Some backupPath)
                | Error reason -> Error reason

    /// Boot-time replacement for `Projection.startAllProjections`: identical
    /// behavior on every normal boot, but if the previous process died inside
    /// the migrate→rebuild window (phase marker still set), incremental
    /// catch-up would replay the whole log into already-populated tables —
    /// so this boot rebuilds every projection (drop + replay, always safe)
    /// and clears the marker; the cutover then re-runs and self-heals.
    let ensureSafeCatchUp (conn: SqliteConnection) (handlers: Projection.ProjectionHandler list) : unit =
        match SettingsStore.getSetting conn phaseMarkerKey with
        | Some phase ->
            log "interrupted cutover detected (phase: %s) — rebuilding all projections instead of incremental catch-up" phase
            for handler in handlers do
                log "  rebuilding %s ..." handler.Name
                Projection.rebuildProjection conn handler
            SettingsStore.deleteSetting conn phaseMarkerKey
        | None ->
            Projection.startAllProjections conn handlers

    /// One fixable slug: which drifted table to source the live truth from,
    /// per fixable column. `None` = that column did not drift for this slug.
    type SeriesFix = {
        Slug: string
        StatusTable: string option
        GenresTable: string option
    }

    let private seriesTables = [ "series_list"; "series_detail" ]
    let private fixableColumns = [ "status"; "genres" ]

    /// Pure classification of SeriesProjection drift: `status`/`genres`
    /// columnMismatches on the two series tables are fixable by compensating
    /// events; anything else (rows only in live/shadow, any other column)
    /// is not — `Error` lists them verbatim and the cutover aborts before
    /// any rebuild can erase live's values. Public for direct unit testing.
    let classifySeriesDrift (discrepancies: Administration.DriftDiscrepancy list) : Result<SeriesFix list, string> =
        let parseSlug (pk: string) =
            if pk.StartsWith("slug=") then Some (pk.Substring(5)) else None
        let isFixable (d: Administration.DriftDiscrepancy) =
            d.Kind = "columnMismatch"
            && List.contains d.Table seriesTables
            && not (List.isEmpty d.Columns)
            && d.Columns |> List.forall (fun c -> List.contains c fixableColumns)
            && (parseSlug d.PrimaryKey).IsSome
        match discrepancies |> List.filter (isFixable >> not) with
        | [] ->
            discrepancies
            |> List.choose (fun d -> parseSlug d.PrimaryKey |> Option.map (fun slug -> slug, d))
            |> List.groupBy fst
            |> List.map (fun (slug, pairs) ->
                let ds = pairs |> List.map snd
                let tableFor column =
                    ds
                    |> List.tryFind (fun d -> List.contains column d.Columns)
                    |> Option.map (fun d -> d.Table)
                { Slug = slug
                  StatusTable = tableFor "status"
                  GenresTable = tableFor "genres" })
            |> Ok
        | unfixable ->
            unfixable
            |> List.map (fun d ->
                sprintf "%s [%s] %s (columns: %s)" d.Table d.Kind d.PrimaryKey (String.concat ", " d.Columns))
            |> String.concat "; "
            |> sprintf "SeriesProjection drift not auto-fixable by compensating events: %s"
            |> Error

    let private readColumn (conn: SqliteConnection) (table: string) (slug: string) (column: string) : string option =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- sprintf "SELECT %s FROM %s WHERE slug = @slug" column table
        cmd.Parameters.AddWithValue("@slug", slug) |> ignore
        match cmd.ExecuteScalar() with
        | null -> None
        | :? DBNull -> None
        | v -> Some (Convert.ToString(v, Globalization.CultureInfo.InvariantCulture))

    /// Compose the compensating events for one fixable slug from the live
    /// values (the fresher truth) and the shadow values (what replay derives
    /// today — the `PreviousStatus` a `Series_refreshed` event records).
    let private composeFixEvents
        (liveConn: SqliteConnection)
        (shadowConn: SqliteConnection)
        (fix: SeriesFix)
        : Result<Series.SeriesEvent list, string> =
        let statusEvent =
            match fix.StatusTable with
            | None -> Ok []
            | Some table ->
                match readColumn liveConn table fix.Slug "status", readColumn shadowConn table fix.Slug "status" with
                | Some liveStatus, Some shadowStatus when liveStatus <> shadowStatus ->
                    Ok [ Series.Series_refreshed { PreviousStatus = Some shadowStatus; NewStatus = Some liveStatus } ]
                | Some _, Some _ -> Ok [] // values agree after all — nothing to compensate
                | _ -> Error (sprintf "%s: could not read status from %s on both sides" fix.Slug table)
        let genresEvent =
            match fix.GenresTable with
            | None -> Ok []
            | Some table ->
                match readColumn liveConn table fix.Slug "genres" with
                | None -> Error (sprintf "%s: could not read live genres from %s" fix.Slug table)
                | Some liveJson ->
                    match Decode.fromString (Decode.list Decode.string) liveJson with
                    | Error e -> Error (sprintf "%s: live genres do not decode as a JSON string list (%s)" fix.Slug e)
                    | Ok genres -> Ok [ Series.Series_categorized genres ]
        match statusEvent, genresEvent with
        | Ok s, Ok g -> Ok (s @ g)
        | Error e, _ | _, Error e -> Error e

    /// Append one slug's compensating events with the auditability marker
    /// ADR-0051's correction note mandates for exactly this kind of script.
    let private appendCompensating
        (conn: SqliteConnection)
        (slug: string)
        (events: Series.SeriesEvent list)
        : Result<unit, string> =
        if List.isEmpty events then Ok ()
        else
            let streamId = Series.streamId slug
            let eventDataList =
                events
                |> List.map (fun e ->
                    let eventType, data = Series.Serialization.serialize e
                    let eventData: EventStore.EventData = {
                        EventType = eventType
                        Data = data
                        Metadata = "{\"source\":\"startup-cutover\"}"
                    }
                    eventData)
            let position = EventStore.getStreamPosition conn streamId
            match EventStore.appendToStream conn streamId position eventDataList with
            | EventStore.ConcurrencyConflict _ ->
                Error (sprintf "concurrency conflict appending compensating events to %s" streamId)
            | EventStore.Success _ ->
                log "  %s: appended %s" slug (events |> List.map (fun e -> (Series.Serialization.serialize e |> fst)) |> String.concat " + ")
                Ok ()

    let private driftCheck
        (conn: SqliteConnection)
        (handlers: Projection.ProjectionHandler list)
        : Administration.ProjectionDrift list =
        use shadowConn = new SqliteConnection("Data Source=:memory:")
        shadowConn.Open()
        Administration.checkProjectionDrift conn shadowConn handlers (fun name -> log "  replayed %s into shadow" name)

    let private logDrift (drifts: Administration.ProjectionDrift list) : int =
        let total = drifts |> List.sumBy (fun p -> List.length p.Discrepancies)
        for p in drifts do
            for d in p.Discrepancies do
                log "  DRIFT %s / %s [%s] %s (columns: %s)" p.Name d.Table d.Kind d.PrimaryKey (String.concat ", " d.Columns)
        total

    /// Phase 4 (plan.md steps 9–11): drift check, compensate the known-fixable
    /// shapes, verify zero, then the one deliberate SeriesProjection rebuild.
    ///
    /// Scope note: GameProjection and PlaySessionProjection are EXCLUDED here
    /// by design. Pre-migration, GameProjection's drift is expected
    /// (`Game_play_time_set` replays as a mandatory no-op since games-p6vkz —
    /// reconstructing those totals is exactly what the play-session migration
    /// is for), and PlaySessionProjection's live table still has the legacy
    /// schema (`steam_app_id` et al.), so diffing it against the new-schema
    /// shadow would throw. Both are rebuilt and drift-checked at the end of
    /// `playSessionPhase`.
    let private seriesPhase
        (conn: SqliteConnection)
        (handlers: Projection.ProjectionHandler list)
        : Result<unit, string> =
        let preMigrationHandlers =
            handlers |> List.filter (fun h -> h.Name <> "GameProjection" && h.Name <> "PlaySessionProjection")
        log "Phase 4 (1/3): drift check before the SeriesProjection rebuild ..."
        use shadowConn = new SqliteConnection("Data Source=:memory:")
        shadowConn.Open()
        let drifts = Administration.checkProjectionDrift conn shadowConn preMigrationHandlers (fun name -> log "  replayed %s into shadow" name)
        let seriesDiscrepancies =
            drifts
            |> List.tryFind (fun p -> p.Name = "SeriesProjection")
            |> Option.map (fun p -> p.Discrepancies)
            |> Option.defaultValue []
        let othersDirty =
            drifts |> List.filter (fun p -> p.Name <> "SeriesProjection" && not (List.isEmpty p.Discrepancies))
        if not (List.isEmpty othersDirty) then
            logDrift othersDirty |> ignore
            Error
                (sprintf "unexpected drift outside SeriesProjection (%s) — rebuild-all later in the cutover would lose those live values"
                    (othersDirty |> List.map (fun p -> sprintf "%s: %d" p.Name (List.length p.Discrepancies)) |> String.concat ", "))
        else
            let rebuildSeries () =
                let seriesHandler = handlers |> List.find (fun h -> h.Name = "SeriesProjection")
                log "Phase 4 (3/3): rebuilding SeriesProjection (metadata lives in the cache tier and survives) ..."
                Projection.rebuildProjection conn seriesHandler
                Ok ()
            match classifySeriesDrift seriesDiscrepancies with
            | Error msg ->
                logDrift [ { Name = "SeriesProjection"; Discrepancies = seriesDiscrepancies } ] |> ignore
                Error msg
            | Ok [] ->
                log "SeriesProjection drift: 0 discrepancies — no compensating events needed"
                rebuildSeries ()
            | Ok fixes ->
                log "Phase 4 (2/3): SeriesProjection drift on %d slug(s) — composing compensating events (ADR-0051) ..." (List.length fixes)
                let composed =
                    fixes
                    |> List.map (fun fix -> composeFixEvents conn shadowConn fix |> Result.map (fun events -> fix.Slug, events))
                    |> List.fold (fun acc r ->
                        match acc, r with
                        | Ok items, Ok item -> Ok (item :: items)
                        | Error e, _ | _, Error e -> Error e) (Ok [])
                match composed with
                | Error msg -> Error msg
                | Ok slugEvents ->
                    let appendResult =
                        slugEvents
                        |> List.fold (fun acc (slug, events) ->
                            match acc with
                            | Error e -> Error e
                            | Ok () -> appendCompensating conn slug events) (Ok ())
                    match appendResult with
                    | Error msg -> Error msg
                    | Ok () ->
                        // Catch every projection up over the new events, then
                        // verify convergence BEFORE the irreversible rebuild.
                        for handler in handlers do
                            Projection.runProjection conn handler
                        log "  verifying drift is now zero before rebuilding ..."
                        let verify = driftCheck conn preMigrationHandlers
                        let remaining = logDrift verify
                        if remaining > 0 then
                            Error (sprintf "%d discrepancies remain after compensating events — refusing to rebuild SeriesProjection" remaining)
                        else
                            rebuildSeries ()

    /// Phase 5 (plan.md steps 12–15) — reduced to a guard, not deleted
    /// (administration-z6ymt): this phase used to drive
    /// `Administration.previewPlaySessionMigration`/`runPlaySessionMigration`,
    /// which administration-z6ymt retired outright (ADR-0052's "a completed
    /// one-time migration's machinery can be deleted wholesale once it has
    /// fired in production" precedent — the migration completed 2026-08-02).
    /// That deletion is a hard compile-time dependency on this call site,
    /// forcing SOME edit here even though StartupCutover.fs's own retirement
    /// is explicitly out of administration-z6ymt's scope. `Game_play_time_set`
    /// can never be emitted again (games-v4nqe demoted its only writer), so
    /// "no legacy events present" is now an invariant this phase verifies
    /// rather than a migration it drives — every reachable boot (any fresh
    /// install, and the already-cutover production store once
    /// administration-z6ymt's purge lands) satisfies it trivially. The
    /// `Some _` arm should be unreachable; it fails loudly rather than
    /// silently no-op-ing, per this task's "no route left wired whose re-run
    /// would silently no-op" discipline.
    let private playSessionPhase
        (conn: SqliteConnection)
        (_dbPath: string)
        (_handlers: Projection.ProjectionHandler list)
        : Result<unit, string> =
        match EventStore.getSampleEventForType conn "Game_play_time_set" with
        | None ->
            log "Phase 5: no legacy Game_play_time_set events present — the play-session migration this phase used to drive was retired (administration-z6ymt); nothing to do"
            Ok ()
        | Some _ ->
            Error "legacy Game_play_time_set events are present, but the play-session migration machinery that reconstructs them was retired (administration-z6ymt) — restore administration-n8kqw's Administration.runPlaySessionMigration before retrying"

    /// The whole unattended cutover. Runs exactly once (completion marker);
    /// aborts loudly but non-fatally on any gate failure — the app still
    /// boots, the Steam-sync gate stays closed, and the next boot retries.
    let run
        (conn: SqliteConnection)
        (dbPath: string)
        (handlers: Projection.ProjectionHandler list)
        : unit =
        match SettingsStore.getSetting conn completedMarkerKey with
        | Some completedAt ->
            log "cutover already completed (%s) — skipping" completedAt
        | None ->
            log "=== automated series + play-session cutover starting (plan.md Phases 4-5) ==="
            match seriesPhase conn handlers with
            | Error msg ->
                log "!!! CUTOVER ABORTED (series phase): %s" msg
                log "!!! No irreversible step has run. The app boots normally; the Steam-sync gate stays closed. Fix the cause and restart to retry."
            | Ok () ->
                match playSessionPhase conn dbPath handlers with
                | Error msg ->
                    log "!!! CUTOVER ABORTED (play-session phase): %s" msg
                    log "!!! The app boots normally; the Steam-sync gate opens only once the migration has completed. Restarting retries idempotently."
                | Ok () ->
                    SettingsStore.setSetting conn completedMarkerKey (DateTime.UtcNow.ToString("o"))
                    log "=== cutover COMPLETE — series metadata survives rebuilds, play-session history is event-sourced, Steam-sync gate is open ==="
