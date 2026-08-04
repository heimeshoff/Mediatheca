module Mediatheca.Tests.AdminPlaySessionMigrationTests

// games-h4mrd: the DB-touching shell around `PlaySessionMigration.plan` —
// `Administration.runPlaySessionMigration` (backup, per-stream idempotent
// append, checkpoint rewind, completion-marker write, orphaned
// steam_playtime_snapshot cleanup), `Administration.decideAndClaimPlaySessionMigrationGuard`
// (the three-way mutual exclusion with rebuild/wipe-import), and the
// deploy-window race `PlaytimeTracker.syncGateOpen` closes once the
// migration's completion marker lands. Exercises the underlying functions
// directly rather than through the SSE route, the same "test the function,
// not the wrapper" shape `AdminWipeImportTests.fs` and `ProjectionDriftTests.fs`
// established. Fixtures use a REAL file-backed dbPath (`TestDb`) since
// `VACUUM INTO` needs a real sibling directory to write `backups/` into.

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private allProjectionHandlers = [
    ContentBlockProjection.handler
    GameProjection.handler
    PlaySessionProjection.handler
]

/// Bootstraps EventStore + settings + ContentBlockProjection/GameProjection,
/// but deliberately does NOT call `PlaySessionProjection.handler.Init` — a
/// real pre-migration store has `game_play_session` sitting on disk in the
/// OLD, non-event-sourced schema (`id, game_slug, steam_app_id, date,
/// minutes_played, created_at`), not the new projection's schema, and
/// `CREATE TABLE IF NOT EXISTS` would be a no-op against it anyway. Tests
/// that need the migrated store's read models call `Projection.rebuildProjection`
/// themselves — the literal "operator-run Rebuild-all" cutover the task
/// describes, which drops the old-schema table and recreates it fresh.
let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    // games-a7dqx: GameProjection.getBySlug/getAll now LEFT JOIN
    // game_metadata_cache — must exist even though this fixture doesn't
    // seed it, mirroring Composition.buildApp's real startup order.
    MetadataCache.initialize conn

let private createLegacyPlaySessionTable (conn: SqliteConnection) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- """
        CREATE TABLE game_play_session (
            id             INTEGER PRIMARY KEY AUTOINCREMENT,
            game_slug      TEXT NOT NULL,
            steam_app_id   INTEGER NOT NULL,
            date           TEXT NOT NULL,
            minutes_played INTEGER NOT NULL,
            created_at     TEXT NOT NULL
        );
    """
    cmd.ExecuteNonQuery() |> ignore

let private insertLegacyRow (conn: SqliteConnection) (slug: string) (steamAppId: int) (date: string) (minutes: int) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "INSERT INTO game_play_session (game_slug, steam_app_id, date, minutes_played, created_at) VALUES (@slug, @appId, @date, @minutes, @createdAt)"
    cmd.Parameters.AddWithValue("@slug", slug) |> ignore
    cmd.Parameters.AddWithValue("@appId", steamAppId) |> ignore
    cmd.Parameters.AddWithValue("@date", date) |> ignore
    cmd.Parameters.AddWithValue("@minutes", minutes) |> ignore
    cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o")) |> ignore
    cmd.ExecuteNonQuery() |> ignore

let private createSnapshotTable (conn: SqliteConnection) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- """
        CREATE TABLE steam_playtime_snapshot (
            steam_app_id  INTEGER PRIMARY KEY,
            game_slug     TEXT NOT NULL,
            total_minutes INTEGER NOT NULL,
            updated_at    TEXT NOT NULL
        );
    """
    cmd.ExecuteNonQuery() |> ignore

let private insertSnapshotRow (conn: SqliteConnection) (steamAppId: int) (slug: string) (totalMinutes: int) =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "INSERT INTO steam_playtime_snapshot (steam_app_id, game_slug, total_minutes, updated_at) VALUES (@appId, @slug, @total, @updatedAt)"
    cmd.Parameters.AddWithValue("@appId", steamAppId) |> ignore
    cmd.Parameters.AddWithValue("@slug", slug) |> ignore
    cmd.Parameters.AddWithValue("@total", totalMinutes) |> ignore
    cmd.Parameters.AddWithValue("@updatedAt", DateTime.UtcNow.ToString("o")) |> ignore
    cmd.ExecuteNonQuery() |> ignore

let private tableExistsInTest (conn: SqliteConnection) (tableName: string) : bool =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name"
    cmd.Parameters.AddWithValue("@name", tableName) |> ignore
    use reader = cmd.ExecuteReader()
    reader.Read()

let private sampleGameData: Games.GameAddedData = {
    Name = "Test Game"
    Year = 2024
    Genres = [ "Action" ]
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private appendGameAdded (conn: SqliteConnection) (slug: string) =
    EventStore.appendToStream conn (Games.streamId slug) -1L [ Games.Serialization.toEventData (Games.Game_added_to_library sampleGameData) ] |> ignore

let private appendCumulativeTotal (conn: SqliteConnection) (slug: string) (totalMinutes: int) =
    let streamId = Games.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Games.Serialization.toEventData (Games.Game_play_time_set totalMinutes) ] |> ignore

let private backupsDirFor (dbPath: string) = Path.Combine(Path.GetDirectoryName(dbPath), "backups")
let private cleanupBackups (dbPath: string) =
    let dir = backupsDirFor dbPath
    if Directory.Exists(dir) then try Directory.Delete(dir, true) with _ -> ()

/// Seeds the full Grounded fixture (table-covered, 8-row table slice, a
/// mismatched snapshot row) plus a reconstruction-only game "solo" — the
/// same numbers `PlaySessionMigrationTests.fs`'s pure fixture uses, here
/// wired through real events + a real legacy table so the migration's whole
/// DB-touching path is exercised end to end.
let private seedFixture (conn: SqliteConnection) =
    appendGameAdded conn "grounded"
    for total in [ 509; 570; 1250; 1900; 2952; 2282 ] do
        appendCumulativeTotal conn "grounded" total

    appendGameAdded conn "solo"
    for total in [ 100; 250 ] do
        appendCumulativeTotal conn "solo" total

    createLegacyPlaySessionTable conn
    for (date, minutes) in [
        "2026-01-05", 120; "2026-01-12", 200; "2026-01-20", 400; "2026-01-28", 300
        "2026-02-04", 250; "2026-02-11", 180; "2026-02-15", 410; "2026-02-19", 422
    ] do
        insertLegacyRow conn "grounded" 100 date minutes // steam_app_id <> 0 -> SteamSync

    createSnapshotTable conn
    insertSnapshotRow conn 100 "grounded" 2952

/// `seedFixture` plus a table-covered slug whose table total (150) does NOT
/// equal its last cumulative-event total (300) — the `Σ table rows = t_last`
/// integrity gate's refusal case (games-h4mrd iteration 2: this must be
/// visible in both the preview and the apply outcome, never silently
/// dropped).
let private seedFixtureWithIntegrityFailure (conn: SqliteConnection) =
    seedFixture conn
    appendGameAdded conn "broken"
    for total in [ 100; 300 ] do
        appendCumulativeTotal conn "broken" total
    insertLegacyRow conn "broken" 200 "2026-03-01" 150

[<Tests>]
let adminPlaySessionMigrationTests =
    testList "AdminPlaySessionMigration" [

        testCase "the deploy-window race: the sync gate refuses while legacy events are present and the marker is absent; a successful migration sets the marker and opens the gate" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixture conn

                let hasLegacyBefore = (EventStore.getSampleEventForType conn "Game_play_time_set").IsSome
                let markerBefore = (SettingsStore.getSetting conn PlaytimeTracker.migrationCompletedSettingKey).IsSome
                Expect.isTrue hasLegacyBefore "the fixture has legacy Game_play_time_set events"
                Expect.isFalse markerBefore "the marker is absent before migration"
                Expect.isFalse (PlaytimeTracker.syncGateOpen hasLegacyBefore markerBefore) "the sync gate must refuse before the migration completes"

                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied _ -> ()
                | other -> failtest (sprintf "Expected the migration to succeed, got %A" other)

                let markerAfter = (SettingsStore.getSetting conn PlaytimeTracker.migrationCompletedSettingKey).IsSome
                Expect.isTrue markerAfter "the marker is set after a successful migration"
                Expect.isTrue (PlaytimeTracker.syncGateOpen hasLegacyBefore markerAfter) "the sync gate opens once the marker is set"
            finally
                cleanupBackups db.Path

        testCase "row-count and event-count conservation: post-migration counts match the plan, and the orphaned snapshot table is dropped" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixture conn

                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied outcome ->
                    let plan = outcome.Plan
                    Expect.equal plan.IntegrityFailures [] "Grounded's 8-row table slice sums exactly to t_last (2282)"
                    Expect.equal plan.TableCoveredSlugs [ "grounded" ] "Grounded is table-covered"
                    Expect.equal plan.ReconstructedSlugs [ "solo" ] "solo is reconstruction-only"

                    let sessionCount = EventStore.countEventsOfType conn "Play_session_recorded"
                    let priorCount = EventStore.countEventsOfType conn "Prior_play_time_recorded"
                    let reconciledCount = EventStore.countEventsOfType conn "Steam_observed_total_reconciled"
                    Expect.equal (sessionCount + priorCount) plan.Events.Length "event_type LIKE 'Play_session_%' OR = 'Prior_play_time_recorded' matches plan.Events.Length"
                    Expect.equal sessionCount 9 "8 Grounded table rows + 1 solo session"
                    Expect.equal priorCount 1 "solo's t0 = 100 > 0 becomes one prior-playtime lump"
                    Expect.equal reconciledCount 1 "Grounded's snapshot (2952) disagrees with its derived observed total (2282)"

                    Expect.isFalse (tableExistsInTest conn "steam_playtime_snapshot") "the orphaned snapshot table is dropped once its value has been carried across as a reconciliation event"

                    let gamePos, _ = Projection.getCheckpointInfo conn "GameProjection"
                    let sessionPos, _ = Projection.getCheckpointInfo conn "PlaySessionProjection"
                    Expect.equal gamePos 0L "GameProjection's checkpoint is rewound to 0 — the cutover moment"
                    Expect.equal sessionPos 0L "PlaySessionProjection's checkpoint is rewound to 0 — the cutover moment"
                | other -> failtest (sprintf "Expected the migration to succeed, got %A" other)
            finally
                cleanupBackups db.Path

        testCase "the phantom-session regression, end to end through the migration: after Rebuild-all, Grounded reconstitutes with SteamObservedMinutes = 2952 (via reconciliation) and TotalPlayTimeMinutes = 2282, and a subsequent Record_steam_observed_total 2952 emits zero events" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixture conn
                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied _ -> ()
                | other -> failtest (sprintf "Expected the migration to succeed, got %A" other)

                // Operator-run Rebuild-all (the cutover): drop + recreate +
                // replay purely from the event log — GameProjection first
                // (registration order), matching Composition.fs.
                Projection.rebuildProjection conn GameProjection.handler
                Projection.rebuildProjection conn PlaySessionProjection.handler

                let events = EventStore.readStream conn (Games.streamId "grounded") |> List.choose Games.Serialization.fromStoredEvent
                let state = Games.reconstitute events
                match state with
                | Games.Active game ->
                    Expect.equal game.TotalPlayTimeMinutes 2282 "TotalPlayTimeMinutes reflects the user's real, edited history"
                    Expect.equal game.SteamObservedMinutes 2952 "SteamObservedMinutes carries Steam's stale cursor across the cutover"
                | other -> failtest (sprintf "Expected Grounded to be Active, got %A" other)

                match Games.decide state (Games.Record_steam_observed_total(2952, "2026-06-01")) with
                | Ok [] -> ()
                | Ok events -> failtest (sprintf "Expected zero events (the phantom-session regression), got %A" events)
                | Error e -> failtest (sprintf "Expected Ok [], got Error %s" e)

                // Projection-level totals must agree, game_list included:
                // game_list.total_play_time = game_detail.total_play_time =
                // prior_play_time + Σ game_play_session.minutes_played =
                // Games.reconstitute(stream).TotalPlayTimeMinutes.
                match GameProjection.getBySlug conn "grounded" with
                | Some dto ->
                    Expect.equal dto.TotalPlayTimeMinutes 2282 "game_detail.total_play_time"
                    Expect.equal dto.PriorPlayTimeMinutes 0 "table-covered games get no prior-playtime lump"
                | None -> failtest "grounded should exist in game_detail"
                match GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "grounded") with
                | Some listItem -> Expect.equal listItem.TotalPlayTimeMinutes 2282 "game_list.total_play_time"
                | None -> failtest "grounded should exist in game_list"

                let sessionRows = PlaySessionProjection.getForGame conn "grounded"
                Expect.equal (List.length sessionRows) 8 "post-migration game_play_session row count for grounded"
                Expect.equal (sessionRows |> List.sumBy (fun r -> r.MinutesPlayed)) 2282 "session rows sum to the total"
                Expect.isEmpty (sessionRows |> List.filter (fun r -> r.MinutesPlayed <= 0)) "no non-positive session rows"
            finally
                cleanupBackups db.Path

        testCase "checkProjectionDrift reports zero discrepancies for GameProjection and PlaySessionProjection after Rebuild-all" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixture conn
                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied _ -> ()
                | other -> failtest (sprintf "Expected the migration to succeed, got %A" other)

                Projection.rebuildProjection conn GameProjection.handler
                Projection.rebuildProjection conn PlaySessionProjection.handler

                let shadow = new SqliteConnection("Data Source=:memory:")
                shadow.Open()
                let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())
                let gameDrift = results |> List.find (fun p -> p.Name = "GameProjection")
                let sessionDrift = results |> List.find (fun p -> p.Name = "PlaySessionProjection")
                Expect.equal gameDrift.Discrepancies [] "GameProjection should report zero discrepancies after Rebuild-all"
                Expect.equal sessionDrift.Discrepancies [] "PlaySessionProjection should report zero discrepancies after Rebuild-all"
            finally
                cleanupBackups db.Path

        testCase "a second run is a true no-op: zero events appended, zero rows changed, getMaxGlobalPosition unchanged" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixture conn
                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied _ -> ()
                | other -> failtest (sprintf "Expected the first migration to succeed, got %A" other)

                let maxPositionAfterFirst = EventStore.getMaxGlobalPosition conn
                let sessionCountAfterFirst = EventStore.countEventsOfType conn "Play_session_recorded"
                let priorCountAfterFirst = EventStore.countEventsOfType conn "Prior_play_time_recorded"

                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied outcome ->
                    Expect.equal outcome.EventsAppended 0 "a second run appends zero events — every stream already carries a play-session event"
                | other -> failtest (sprintf "Expected the second run to also report success (with zero new events), got %A" other)

                Expect.equal (EventStore.getMaxGlobalPosition conn) maxPositionAfterFirst "getMaxGlobalPosition is unchanged by a no-op second run"
                Expect.equal (EventStore.countEventsOfType conn "Play_session_recorded") sessionCountAfterFirst "Play_session_recorded count unchanged"
                Expect.equal (EventStore.countEventsOfType conn "Prior_play_time_recorded") priorCountAfterFirst "Prior_play_time_recorded count unchanged"
            finally
                cleanupBackups db.Path

        testCase "row-count conservation: a game with neither cumulative events nor a table row is never touched by the migration" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixture conn
                appendGameAdded conn "untouched"

                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied _ ->
                    let untouchedEvents = EventStore.readStream conn (Games.streamId "untouched")
                    Expect.equal (List.length untouchedEvents) 1 "only its original Game_added_to_library event — nothing appended"
                | other -> failtest (sprintf "Expected success, got %A" other)
            finally
                cleanupBackups db.Path

        testCase "preview leaves the store byte-identical: zero events appended, getMaxGlobalPosition unchanged" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            seedFixture conn

            let maxPositionBefore = EventStore.getMaxGlobalPosition conn
            let sessionCountBefore = EventStore.countEventsOfType conn "Play_session_recorded"
            let priorCountBefore = EventStore.countEventsOfType conn "Prior_play_time_recorded"
            let reconciledCountBefore = EventStore.countEventsOfType conn "Steam_observed_total_reconciled"
            let markerBefore = (SettingsStore.getSetting conn PlaytimeTracker.migrationCompletedSettingKey).IsSome

            let preview = Administration.previewPlaySessionMigration conn

            Expect.equal (EventStore.getMaxGlobalPosition conn) maxPositionBefore "a preview appends nothing to the log"
            Expect.equal (EventStore.countEventsOfType conn "Play_session_recorded") sessionCountBefore "Play_session_recorded count unchanged by a preview"
            Expect.equal (EventStore.countEventsOfType conn "Prior_play_time_recorded") priorCountBefore "Prior_play_time_recorded count unchanged by a preview"
            Expect.equal (EventStore.countEventsOfType conn "Steam_observed_total_reconciled") reconciledCountBefore "Steam_observed_total_reconciled count unchanged by a preview"
            Expect.equal (SettingsStore.getSetting conn PlaytimeTracker.migrationCompletedSettingKey).IsSome markerBefore "a preview never writes the completion marker"
            Expect.isTrue (preview.StreamsToBeTouched > 0) "the preview still reports what an apply would touch"

        testCase "preview reports all seven fields, including a seeded integrity failure" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            seedFixtureWithIntegrityFailure conn

            let preview = Administration.previewPlaySessionMigration conn

            Expect.equal preview.StreamsToBeTouched 2 "grounded (table-covered) + solo (reconstructed) — broken is refused, not counted as touchable"
            Expect.isTrue (preview.EventsToBeAppended > 0) "events to be appended"
            Expect.equal preview.TableCoveredSlugs [ "grounded" ] "table-covered slugs"
            Expect.equal preview.ReconstructedSlugs [ "solo" ] "reconstructed slugs"
            Expect.equal preview.PriorPlayTimeLumpCount 1 "solo's t0 = 100 > 0 becomes one prior-playtime lump"
            Expect.equal preview.ReconciliationCount 1 "grounded's snapshot disagrees with its derived observed total"
            Expect.equal preview.NegativeDeltasSkipped 0 "no negative deltas in this fixture"
            Expect.equal preview.IntegrityFailures [ { PlaySessionMigration.Slug = "broken"; PlaySessionMigration.TableTotal = 150; PlaySessionMigration.LastEventTotal = 300 } ]
                "the broken slug's table total (150) disagrees with its last cumulative total (300) — refused and reported, not silently dropped"

        testCase "apply-after-preview: the preview an operator inspects matches exactly what the apply that follows does" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixture conn

                let preview = Administration.previewPlaySessionMigration conn

                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied outcome ->
                    Expect.equal outcome.StreamsMigrated preview.StreamsToBeTouched "apply migrates exactly the streams the preview reported"
                    Expect.equal outcome.EventsAppended preview.EventsToBeAppended "apply appends exactly the event count the preview reported"
                    Expect.equal outcome.Plan.TableCoveredSlugs preview.TableCoveredSlugs "table-covered slugs match"
                    Expect.equal outcome.Plan.ReconstructedSlugs preview.ReconstructedSlugs "reconstructed slugs match"
                    Expect.equal outcome.Plan.IntegrityFailures preview.IntegrityFailures "integrity failures match"
                | other -> failtest (sprintf "Expected the migration to succeed, got %A" other)
            finally
                cleanupBackups db.Path

        testCase "an integrity-gate refusal is visible in the apply outcome, not silently dropped from a clean-looking result" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let conn = db.Connection
            try
                seedFixtureWithIntegrityFailure conn

                match Administration.runPlaySessionMigration conn db.Path with
                | Administration.MigrationApplied outcome ->
                    Expect.equal outcome.Plan.IntegrityFailures [ { PlaySessionMigration.Slug = "broken"; PlaySessionMigration.TableTotal = 150; PlaySessionMigration.LastEventTotal = 300 } ]
                        "the refusal is present in the apply outcome's plan"

                    let brokenEvents = EventStore.readStream conn (Games.streamId "broken")
                    Expect.isEmpty (brokenEvents |> List.filter (fun e -> e.EventType = "Play_session_recorded" || e.EventType = "Prior_play_time_recorded" || e.EventType = "Steam_observed_total_reconciled"))
                        "the refused slug gets no play-session events appended — refused, not guessed at"
                | other -> failtest (sprintf "Expected the migration to succeed (for the non-refused slugs), got %A" other)
            finally
                cleanupBackups db.Path

        testCase "guard: a rebuild in flight refuses the migration, and the migration in flight refuses both a rebuild and a wipe-import" <| fun _ ->
            let guards = Administration.makeGuards ()
            guards.RebuildingProjections.TryAdd("GameProjection", ()) |> ignore
            Expect.equal (Administration.decideAndClaimPlaySessionMigrationGuard guards) Administration.MigrationRefusedRebuildInFlight "a rebuild in flight refuses the migration"
            Expect.isTrue guards.PlaySessionMigrationInProgress.IsEmpty "the refused attempt must never claim its own guard"

            let guardsTwo = Administration.makeGuards ()
            guardsTwo.WipeImportInProgress.TryAdd("wipe-import", ()) |> ignore
            Expect.equal (Administration.decideAndClaimPlaySessionMigrationGuard guardsTwo) Administration.MigrationRefusedWipeImportInFlight "a wipe-import in flight refuses the migration"

            let guardsThree = Administration.makeGuards ()
            Expect.equal (Administration.decideAndClaimPlaySessionMigrationGuard guardsThree) Administration.MigrationClaimed "the first attempt claims the guard"
            Expect.equal (Administration.decideAndClaimPlaySessionMigrationGuard guardsThree) Administration.MigrationRefusedAlreadyRunning "a second concurrent attempt is refused"
            Expect.isTrue (Administration.playSessionMigrationInFlight guardsThree) "the migration guard reports in-flight"
            Expect.equal (Administration.decideAndClaimRebuildGuard guardsThree "GameProjection") (Some Administration.PlaySessionMigrationInFlight) "a rebuild refuses while the migration is in flight"
            Expect.equal (Administration.decideAndClaimWipeImportGuard guardsThree) Administration.RefusedPlaySessionMigrationInFlight "a wipe-import refuses while the migration is in flight"
    ]
