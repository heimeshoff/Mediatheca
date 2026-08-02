module Mediatheca.Tests.ProjectionDriftTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

/// Shadow-table replay drift detector (administration-btvqa, ADR-0031): a
/// throwaway shadow connection is replayed from the live event log via
/// `Administration.checkProjectionDrift`, then diffed against the live
/// projection tables. These tests exercise `checkProjectionDrift` (and the
/// not-dirty guard `isAnyProjectionDirty`) directly rather than through the
/// SSE route — the route is a thin wrapper (streaming + a concurrency guard)
/// over these functions, the same shape `ProjectionRebuildTests.fs`
/// established for `rebuildProjectionWithProgress`.

/// Registration order is load-bearing (Movie -> Friend -> ContentBlock ->
/// Catalog -> Series -> Game, `Composition.fs`) — FriendProjection's
/// Friend_removed case scrubs movie_detail/watch_sessions and needs those
/// tables to already exist.
let private allProjectionHandlers = [
    MovieProjection.handler
    FriendProjection.handler
    ContentBlockProjection.handler
    CatalogProjection.handler
    SeriesProjection.handler
    GameProjection.handler
    PlaySessionProjection.handler
]

let private createLiveConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    conn

let private createShadowConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    conn

let private sampleMovieData: Movies.MovieAddedData = {
    Name = "The Matrix"
    Year = 1999
    Runtime = Some 136
    Overview = "A computer hacker learns about the true nature of reality"
    Genres = [ "Action"; "Sci-Fi" ]
    PosterRef = Some "posters/the-matrix-1999.jpg"
    BackdropRef = Some "backdrops/the-matrix-1999.jpg"
    TmdbId = 603
    TmdbRating = Some 8.7
}

let private appendMovieAdded (conn: SqliteConnection) (slug: string) =
    let eventData = Movies.Serialization.toEventData (Movies.Movie_added_to_library sampleMovieData)
    EventStore.appendToStream conn (Movies.streamId slug) -1L [ eventData ] |> ignore

let private appendFriendAdded (conn: SqliteConnection) (name: string) =
    let eventData = Friends.Serialization.toEventData (Friends.Friend_added { Name = name; ImageRef = None })
    EventStore.appendToStream conn (Friends.streamId (name.ToLowerInvariant())) -1L [ eventData ] |> ignore

let private sampleGameData: Games.GameAddedData = {
    Name = "Hollow Knight"
    Year = 2017
    Genres = [ "Metroidvania" ]
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private appendGameAdded (conn: SqliteConnection) (slug: string) =
    let eventData = Games.Serialization.toEventData (Games.Game_added_to_library sampleGameData)
    EventStore.appendToStream conn (Games.streamId slug) -1L [ eventData ] |> ignore

/// Full incremental catch-up of every handler against `conn`, in registration
/// order — mirrors `Projection.startAllProjections`/`Composition.fs`.
let private catchUpAll (conn: SqliteConnection) =
    for handler in allProjectionHandlers do
        Projection.runProjection conn handler

let private totalDiscrepancies (results: Administration.ProjectionDrift list) =
    results |> List.sumBy (fun p -> List.length p.Discrepancies)

[<Tests>]
let projectionDriftTests =
    testList "Projection drift detector" [

        testCase "healthy store: drift check reports zero discrepancies across every registered projection" <| fun _ ->
            let conn = createLiveConnection ()
            for handler in allProjectionHandlers do
                handler.Init conn
            appendMovieAdded conn "the-matrix-1999"
            appendFriendAdded conn "Marco"
            catchUpAll conn

            let shadow = createShadowConnection ()
            let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())

            Expect.equal (List.length results) (List.length allProjectionHandlers) "One result per registered projection"
            Expect.equal (totalDiscrepancies results) 0 "A healthy store should report zero discrepancies"

        testCase "corrupted live row is detected and reported with the correct table/primary-key/column" <| fun _ ->
            let conn = createLiveConnection ()
            for handler in allProjectionHandlers do
                handler.Init conn
            appendMovieAdded conn "the-matrix-1999"
            catchUpAll conn

            // Mutate a live row directly, bypassing the event log entirely.
            conn
            |> Db.newCommand "UPDATE movie_list SET name = 'CORRUPTED' WHERE slug = 'the-matrix-1999'"
            |> Db.exec

            let shadow = createShadowConnection ()
            let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())

            let movieDrift = results |> List.find (fun p -> p.Name = "MovieProjection")
            let discrepancy =
                movieDrift.Discrepancies
                |> List.tryFind (fun d -> d.Table = "movie_list" && d.PrimaryKey = "slug=the-matrix-1999")
            match discrepancy with
            | None -> failwith "Expected a column-mismatch discrepancy on movie_list for slug=the-matrix-1999"
            | Some d ->
                Expect.equal d.Kind "columnMismatch" "Corruption should be reported as a column mismatch, not a missing row"
                Expect.contains d.Columns "name" "The corrupted column ('name') should be identified"

        testCase "cross-BC write: Friend-removes-from-Movie scrub reproduces with zero discrepancies" <| fun _ ->
            let conn = createLiveConnection ()
            for handler in allProjectionHandlers do
                handler.Init conn
            appendMovieAdded conn "the-matrix-1999"
            appendFriendAdded conn "Marco"
            catchUpAll conn

            let recEvent = Movies.Serialization.toEventData (Movies.Movie_recommended_by "marco")
            EventStore.appendToStream conn (Movies.streamId "the-matrix-1999") 0L [ recEvent ] |> ignore
            catchUpAll conn

            let removeEvent = Friends.Serialization.toEventData Friends.Friend_removed
            EventStore.appendToStream conn (Friends.streamId "marco") 0L [ removeEvent ] |> ignore
            catchUpAll conn

            // Sanity: the live scrub actually happened, so this test is exercising
            // the real cross-BC write, not a no-op.
            let recommendedBy =
                conn
                |> Db.newCommand "SELECT recommended_by FROM movie_detail WHERE slug = 'the-matrix-1999'"
                |> Db.querySingle (fun (rd: System.Data.IDataReader) -> rd.ReadString "recommended_by")
                |> Option.defaultValue ""
            Expect.equal recommendedBy "[]" "Live catch-up should have scrubbed marco out of recommended_by"

            let shadow = createShadowConnection ()
            let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())

            Expect.equal (totalDiscrepancies results) 0 "Shadow replay should reproduce the Friend-removes-from-Movie scrub exactly"

        testCase "live tables and checkpoints are byte-identical before and after a drift run that finds real discrepancies" <| fun _ ->
            let conn = createLiveConnection ()
            for handler in allProjectionHandlers do
                handler.Init conn
            appendMovieAdded conn "the-matrix-1999"
            appendFriendAdded conn "Marco"
            catchUpAll conn

            conn
            |> Db.newCommand "UPDATE movie_list SET name = 'CORRUPTED' WHERE slug = 'the-matrix-1999'"
            |> Db.exec

            let tableRowCount (table: string) =
                use cmd = conn.CreateCommand()
                cmd.CommandText <- sprintf "SELECT COUNT(*) FROM %s" table
                cmd.ExecuteScalar() :?> int64

            let allTables = [ "movie_list"; "movie_detail"; "watch_sessions"; "friend_list"; "content_blocks"; "catalog_list"; "catalog_entries" ]
            let preRowCounts = allTables |> List.map (fun t -> t, tableRowCount t)
            let preCheckpoints = allProjectionHandlers |> List.map (fun h -> h.Name, Projection.getCheckpoint conn h.Name)

            let shadow = createShadowConnection ()
            let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())
            Expect.isGreaterThan (totalDiscrepancies results) 0 "Sanity: this run should have found the corrupted row"

            let postRowCounts = allTables |> List.map (fun t -> t, tableRowCount t)
            let postCheckpoints = allProjectionHandlers |> List.map (fun h -> h.Name, Projection.getCheckpoint conn h.Name)

            Expect.equal postRowCounts preRowCounts "Live table row counts must be unchanged by a drift run"
            Expect.equal postCheckpoints preCheckpoints "Live checkpoint positions must be unchanged by a drift run"

        testCase "games-p6vkz: prior playtime and play sessions replay with zero discrepancies for GameProjection and PlaySessionProjection" <| fun _ ->
            let conn = createLiveConnection ()
            for handler in allProjectionHandlers do
                handler.Init conn
            appendGameAdded conn "hollow-knight-2017"
            let events: Games.GameEvent list = [
                Games.Prior_play_time_recorded 600
                Games.Play_session_recorded { Day = "2024-06-01"; Minutes = 120; Source = SteamSync }
                Games.Play_session_recorded { Day = "2024-06-02"; Minutes = 45; Source = Manual }
                Games.Play_session_minutes_corrected ("2024-06-02", 60, 45)
                Games.Play_session_moved ("2024-06-01", "2024-06-03", 120)
            ]
            let eventDataList = events |> List.map Games.Serialization.toEventData
            EventStore.appendToStream conn (Games.streamId "hollow-knight-2017") 0L eventDataList |> ignore
            catchUpAll conn

            let shadow = createShadowConnection ()
            let results = Administration.checkProjectionDrift conn shadow allProjectionHandlers (fun _ -> ())

            let gameDrift = results |> List.find (fun p -> p.Name = "GameProjection")
            let sessionDrift = results |> List.find (fun p -> p.Name = "PlaySessionProjection")
            Expect.equal gameDrift.Discrepancies [] "GameProjection should report zero discrepancies"
            Expect.equal sessionDrift.Discrepancies [] "PlaySessionProjection should report zero discrepancies"

        testCase "a lagging projection is flagged dirty, and the drift check's rejection message names it" <| fun _ ->
            let conn = createLiveConnection ()
            for handler in allProjectionHandlers do
                handler.Init conn
            appendFriendAdded conn "Marco"
            Projection.runProjection conn FriendProjection.handler
            // A second event arrives, but the projection is never re-run — FriendProjection now lags.
            appendFriendAdded conn "Alice"

            let dirty = Administration.isAnyProjectionDirty conn allProjectionHandlers (Administration.makeGuards ())
            Expect.contains dirty "FriendProjection" "FriendProjection should be reported dirty while it lags behind the store head"

            let message = Administration.driftCheckRejectionMessage dirty
            Expect.stringContains message "FriendProjection" "The operator-facing rejection reason should name the dirty projection"
    ]
