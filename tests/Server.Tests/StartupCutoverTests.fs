module Mediatheca.Tests.StartupCutoverTests

// The automated one-boot cutover (StartupCutover.fs): the pure drift
// classifier that decides which SeriesProjection discrepancies are fixable
// by compensating events, the series metadata cache's ongoing write path
// (series-t3jkv), the crashed-mid-cutover boot guard (`ensureSafeCatchUp`),
// and the pre-cutover backup's marker gating. Tests the underlying
// functions directly, the same "test the function, not the wrapper" shape
// `AdminPlaySessionMigrationTests.fs` established.

open System
open System.Data
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

let private mismatch table pk columns : Administration.DriftDiscrepancy =
    { Table = table; PrimaryKey = pk; Kind = "columnMismatch"; Columns = columns }

[<Tests>]
let classifyTests =
    testList "StartupCutover.classifySeriesDrift" [
        test "no discrepancies classify to no fixes" {
            Expect.equal (StartupCutover.classifySeriesDrift []) (Ok []) "empty in, empty out"
        }

        test "a status columnMismatch on series_detail is fixable" {
            let result = StartupCutover.classifySeriesDrift [ mismatch "series_detail" "slug=love-death-robots-2019" [ "status" ] ]
            Expect.equal result
                (Ok [ { StartupCutover.Slug = "love-death-robots-2019"
                        StartupCutover.StatusTable = Some "series_detail"
                        StartupCutover.GenresTable = None } ])
                "status mismatch maps to a status fix"
        }

        test "a genres columnMismatch is fixable" {
            let result = StartupCutover.classifySeriesDrift [ mismatch "series_list" "slug=fallout-2024" [ "genres" ] ]
            Expect.equal result
                (Ok [ { StartupCutover.Slug = "fallout-2024"
                        StartupCutover.StatusTable = None
                        StartupCutover.GenresTable = Some "series_list" } ])
                "genres mismatch maps to a genres fix"
        }

        test "status and genres drift across both tables collapse into one fix per slug" {
            let result =
                StartupCutover.classifySeriesDrift [
                    mismatch "series_list" "slug=silo-2023" [ "status" ]
                    mismatch "series_detail" "slug=silo-2023" [ "status"; "genres" ]
                ]
            match result with
            | Ok [ fix ] ->
                Expect.equal fix.Slug "silo-2023" "one fix for the slug"
                Expect.isSome fix.StatusTable "status drifted"
                Expect.isSome fix.GenresTable "genres drifted"
            | other -> failtestf "expected a single fix, got %A" other
        }

        test "an onlyInLive row is not auto-fixable" {
            let d : Administration.DriftDiscrepancy =
                { Table = "series_list"; PrimaryKey = "slug=ghost-2020"; Kind = "onlyInLive"; Columns = [] }
            Expect.isError (StartupCutover.classifySeriesDrift [ d ]) "row-level drift aborts"
        }

        test "a columnMismatch on any other column is not auto-fixable" {
            let result = StartupCutover.classifySeriesDrift [ mismatch "series_detail" "slug=x-2020" [ "status"; "name" ] ]
            Expect.isError result "a name mismatch cannot be compensated automatically"
        }

        test "a columnMismatch outside the series tables is not auto-fixable" {
            let result = StartupCutover.classifySeriesDrift [ mismatch "movie_detail" "slug=x-2020" [ "status" ] ]
            Expect.isError result "only series_list/series_detail are compensatable"
        }
    ]

let private newConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    MetadataCache.initialize conn
    conn

[<Tests>]
let upsertSeriesMetadataTests =
    testList "MetadataCache.upsertSeriesMetadata (series-t3jkv)" [
        test "inserts a fresh row with a real fetched_at" {
            use conn = newConn ()
            MetadataCache.upsertSeriesMetadata conn "silo-2023" "Underground city." (Some "backdrops/silo.jpg") (Some 8.1) (Some 47)
            let row =
                conn
                |> Db.newCommand "SELECT overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at FROM series_metadata_cache WHERE series_slug = 'silo-2023'"
                |> Db.querySingle (fun (rd: IDataReader) ->
                    rd.ReadString "overview", rd.ReadString "backdrop_ref", rd.ReadDouble "tmdb_rating", rd.ReadInt32 "episode_runtime", rd.IsDBNull(rd.GetOrdinal("fetched_at")))
            match row with
            | Some (overview, backdrop, rating, runtime, fetchedAtIsNull) ->
                Expect.equal overview "Underground city." "overview stored"
                Expect.equal backdrop "backdrops/silo.jpg" "backdrop stored"
                Expect.equal rating 8.1 "rating stored"
                Expect.equal runtime 47 "runtime stored"
                Expect.isFalse fetchedAtIsNull "a genuine write stamps fetched_at (the seed's NULL means never-fetched)"
            | None -> failtest "row not written"
        }

        test "replaces an existing row on refresh" {
            use conn = newConn ()
            MetadataCache.upsertSeriesMetadata conn "silo-2023" "Old." None (Some 7.0) None
            MetadataCache.upsertSeriesMetadata conn "silo-2023" "New." None (Some 8.5) (Some 50)
            let rating =
                conn
                |> Db.newCommand "SELECT overview, tmdb_rating FROM series_metadata_cache WHERE series_slug = 'silo-2023'"
                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "overview", rd.ReadDouble "tmdb_rating")
            Expect.equal rating (Some ("New.", 8.5)) "second write wins"
        }
    ]

let private appendPlaySession (conn: SqliteConnection) (slug: string) (day: string) (minutes: int) =
    let streamId = Games.streamId slug
    let event = Games.Play_session_recorded { Day = day; Minutes = minutes; Source = Manual }
    let position = EventStore.getStreamPosition conn streamId
    match EventStore.appendToStream conn streamId position [ Games.Serialization.toEventData event ] with
    | EventStore.Success _ -> ()
    | EventStore.ConcurrencyConflict _ -> failtest "unexpected conflict in fixture"

let private playSessionMinutes (conn: SqliteConnection) (slug: string) (day: string) : int option =
    conn
    |> Db.newCommand "SELECT minutes_played FROM game_play_session WHERE game_slug = @slug AND date = @day"
    |> Db.setParams [ "slug", SqlType.String slug; "day", SqlType.String day ]
    |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "minutes_played")

[<Tests>]
let ensureSafeCatchUpTests =
    testList "StartupCutover.ensureSafeCatchUp" [
        test "a normal boot catches up incrementally" {
            use conn = newConn ()
            PlaySessionProjection.handler.Init conn
            appendPlaySession conn "hades-2020" "2026-01-01" 30
            StartupCutover.ensureSafeCatchUp conn [ PlaySessionProjection.handler ]
            Expect.equal (playSessionMinutes conn "hades-2020" "2026-01-01") (Some 30) "session projected once"
        }

        test "without the guard, catch-up from a rewound checkpoint double-counts (the hazard)" {
            use conn = newConn ()
            PlaySessionProjection.handler.Init conn
            appendPlaySession conn "hades-2020" "2026-01-01" 30
            Projection.startAllProjections conn [ PlaySessionProjection.handler ]
            // Simulate the crash window: checkpoint rewound, tables populated.
            Projection.saveCheckpoint conn "PlaySessionProjection" 0L
            Projection.startAllProjections conn [ PlaySessionProjection.handler ]
            Expect.equal (playSessionMinutes conn "hades-2020" "2026-01-01") (Some 60)
                "mergeSession SUMS on conflict — this is exactly why the phase-marker guard exists"
        }

        test "with the phase marker set, the boot rebuilds instead and clears the marker" {
            use conn = newConn ()
            PlaySessionProjection.handler.Init conn
            appendPlaySession conn "hades-2020" "2026-01-01" 30
            Projection.startAllProjections conn [ PlaySessionProjection.handler ]
            // Simulate the crash window: checkpoint rewound, tables populated,
            // phase marker still set.
            Projection.saveCheckpoint conn "PlaySessionProjection" 0L
            SettingsStore.setSetting conn StartupCutover.phaseMarkerKey "play-session-migration"
            StartupCutover.ensureSafeCatchUp conn [ PlaySessionProjection.handler ]
            Expect.equal (playSessionMinutes conn "hades-2020" "2026-01-01") (Some 30) "rebuild (drop + replay) never double-counts"
            Expect.isNone (SettingsStore.getSetting conn StartupCutover.phaseMarkerKey) "marker cleared once consistency is restored"
        }
    ]

[<Tests>]
let backupTests =
    testList "StartupCutover.backupIfPending" [
        test "skips once the cutover has completed" {
            use db = TestDb.withTempDbFactory (fun conn ->
                EventStore.initialize conn
                SettingsStore.initialize conn)
            SettingsStore.setSetting db.Connection StartupCutover.completedMarkerKey "2026-08-03T00:00:00Z"
            Expect.equal (StartupCutover.backupIfPending db.Connection db.Path) (Ok None) "no backup after completion"
        }

        test "skips on a fresh install with an empty event log" {
            use db = TestDb.withTempDbFactory (fun conn ->
                EventStore.initialize conn
                SettingsStore.initialize conn)
            Expect.equal (StartupCutover.backupIfPending db.Connection db.Path) (Ok None) "an empty store has nothing to protect"
        }

        test "writes a consistent VACUUM INTO copy when the cutover is pending" {
            use db = TestDb.withTempDbFactory (fun conn ->
                EventStore.initialize conn
                SettingsStore.initialize conn)
            appendPlaySession db.Connection "hades-2020" "2026-01-01" 30
            match StartupCutover.backupIfPending db.Connection db.Path with
            | Ok (Some backupPath) ->
                Expect.isTrue (File.Exists backupPath) "backup file exists"
                use backupConn = new SqliteConnection($"Data Source={backupPath}")
                backupConn.Open()
                use cmd = backupConn.CreateCommand()
                cmd.CommandText <- "SELECT COUNT(*) FROM events"
                Expect.equal (cmd.ExecuteScalar() :?> int64) 1L "the appended event is in the backup"
            | other -> failtestf "expected a backup, got %A" other
        }
    ]
