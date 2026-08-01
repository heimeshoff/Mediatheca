module Mediatheca.Tests.TableClassificationTests

open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

/// Registry-honesty tests for `Administration.tableRegistry`
/// (administration-t9bzx): every durable table gets exactly one
/// `Projected | Cache | Imperative` classification, replacing the scattered
/// "this table is imperative, it needs no gating" comments that used to be
/// the only place this fact lived (ADR-0025, ADR-0031).

/// Every durable-table initializer in the codebase, so this fixture's
/// `sqlite_master` has one row per table `tableRegistry` claims to cover —
/// extends `AdministrationTests.fs`'s `bootstrapAdmin` shape with the two
/// modules that fixture doesn't need (SettingsStore, PlaytimeTracker).
let private bootstrapEverything (conn: SqliteConnection) =
    EventStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    GameJournal.initialize conn
    SettingsStore.initialize conn
    PlaytimeTracker.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    CatalogProjection.handler.Init conn
    Administration.initializeJobRuns conn

let private tableNamesInSchema (conn: SqliteConnection) : string list =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- "SELECT name FROM sqlite_master WHERE type = 'table'"
    use reader = cmd.ExecuteReader()
    [ while reader.Read() do yield reader.GetString(0) ]

/// SQLite's own bookkeeping tables (`sqlite_*`), plus the event log/FTS5
/// virtual table family (`events`, `events_fts`, and its `events_fts_*`
/// shadow tables) and `projection_checkpoints` — the task's own exclusion
/// list. Neither family is a domain table a classification registry should
/// describe: `EventStore.fs` is the sole owner of the event log's schema,
/// and `Projection.getCheckpointInfo` is the sole owner of checkpoint
/// schema.
let private isExcludedFromRegistry (name: string) : bool =
    name.StartsWith("sqlite_") || name.StartsWith("events") || name = "projection_checkpoints"

let private allProjectionHandlers = [
    MovieProjection.handler
    FriendProjection.handler
    ContentBlockProjection.handler
    CatalogProjection.handler
    SeriesProjection.handler
    GameProjection.handler
]

[<Tests>]
let tests =
    testList "TableClassification" [
        testCase "tableRegistry covers every non-excluded table in a fully-initialized schema, exactly once" <| fun _ ->
            use conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            bootstrapEverything conn

            let schemaTables =
                tableNamesInSchema conn
                |> List.filter (isExcludedFromRegistry >> not)
                |> Set.ofList
            let registryTableNames = Administration.tableRegistry |> List.map fst
            let registryTableSet = registryTableNames |> Set.ofList

            Expect.equal registryTableSet schemaTables
                "tableRegistry must classify exactly the non-excluded tables present in a fully-initialized schema"
            Expect.equal (List.length registryTableNames) (Set.count registryTableSet)
                "tableRegistry must not list any table more than once"

        testCase "game_play_session and steam_playtime_snapshot are both classified Imperative, naming PlaytimeTracker" <| fun _ ->
            let classificationOf table =
                Administration.tableRegistry |> List.tryFind (fun (t, _) -> t = table) |> Option.map snd

            match classificationOf "game_play_session" with
            | Some (Administration.Imperative writtenBy) ->
                Expect.equal writtenBy "PlaytimeTracker" "game_play_session's writer"
            | other -> failtestf "expected game_play_session to be classified Imperative, got %A" other

            match classificationOf "steam_playtime_snapshot" with
            | Some (Administration.Imperative writtenBy) ->
                Expect.equal writtenBy "PlaytimeTracker" "steam_playtime_snapshot's writer"
            | other -> failtestf "expected steam_playtime_snapshot to be classified Imperative, got %A" other

        testCase "projectionTables derived from tableRegistry is set-equal, per projection, to the original hardcoded list" <| fun _ ->
            // The list Administration.fs hardcoded before this task — kept
            // here as the independent expectation the derivation is checked
            // against, so a future edit to tableRegistry that accidentally
            // drops or misassigns a Projected table is caught.
            let expected = [
                "MovieProjection", [ "movie_list"; "movie_detail"; "watch_sessions" ]
                "FriendProjection", [ "friend_list" ]
                "ContentBlockProjection", [ "content_blocks" ]
                "CatalogProjection", [ "catalog_list"; "catalog_entries" ]
                "SeriesProjection", [ "series_list"; "series_detail"; "series_seasons"; "series_episodes"; "series_rewatch_sessions"; "series_episode_progress" ]
                "GameProjection", [ "game_list"; "game_detail" ]
            ]
            let derivedFromRegistry =
                Administration.tableRegistry
                |> List.choose (fun (table, cls) ->
                    match cls with
                    | Administration.Projected name -> Some (name, table)
                    | _ -> None)
                |> List.groupBy fst
                |> List.map (fun (name, pairs) -> name, pairs |> List.map snd |> Set.ofList)
                |> Map.ofList

            for (name, tables) in expected do
                let actual = derivedFromRegistry |> Map.tryFind name |> Option.defaultValue Set.empty
                Expect.equal actual (Set.ofList tables) (sprintf "%s's derived table set" name)
            Expect.equal (Map.count derivedFromRegistry) (List.length expected)
                "no extra projections should appear in the derivation beyond the six expected"

        testCase "getUnrebuildableTableStats reports Cache and Imperative row counts, and omits every Projected table" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapEverything
            let api =
                Administration.create db.Factory "test-fixtures-do-not-exist/nowhere.db" "test-fixtures-do-not-exist/images"
                    allProjectionHandlers [] (Administration.makeJobRunRecorder db.Connection (new SemaphoreSlim(1, 1))) (Administration.makeGuards ())

            let stats = api.getUnrebuildableTableStats () |> Async.RunSynchronously
            let byTable = stats |> List.map (fun s -> s.TableName, s) |> Map.ofList

            Expect.isTrue (Map.containsKey "settings" byTable) "settings should be reported"
            Expect.equal byTable.["settings"].Classification "Imperative" "settings classification"
            Expect.isTrue (Map.containsKey "jellyfin_movie" byTable) "jellyfin_movie should be reported"
            Expect.equal byTable.["jellyfin_movie"].Classification "Cache" "jellyfin_movie classification"
            Expect.equal byTable.["jellyfin_movie"].Detail "JellyfinSync" "jellyfin_movie's refresher"

            let projectedTableNames =
                Administration.tableRegistry
                |> List.choose (fun (table, cls) -> match cls with Administration.Projected _ -> Some table | _ -> None)
                |> Set.ofList
            for stat in stats do
                Expect.isFalse (Set.contains stat.TableName projectedTableNames)
                    (sprintf "%s is Projected and must not appear in getUnrebuildableTableStats" stat.TableName)
    ]
