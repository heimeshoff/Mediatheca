module Mediatheca.Tests.ProjectionRebuildTests

open System
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server

/// Projection dashboard's rebuild command (administration-qjcp4): drop +
/// replay must land the projection in the same state as ordinary incremental
/// catch-up would (ADR-0002 — projections are disposable, rebuildable read
/// models). These tests exercise `Projection.rebuildProjectionWithProgress`
/// directly rather than through the SSE route, since the route is a thin
/// wrapper (streaming + a concurrency guard) over this function.
let private createInMemoryConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    conn

let private addFriend (conn: SqliteConnection) (name: string) =
    let event = Friends.Friend_added { Name = name; ImageRef = None }
    let eventData = Friends.Serialization.toEventData event
    EventStore.appendToStream conn (Friends.streamId (name.ToLowerInvariant())) -1L [ eventData ] |> ignore

[<Tests>]
let projectionRebuildTests =
    testList "Projection rebuild" [

        testCase "rebuildProjectionWithProgress produces the same read-model rows as incremental catch-up" <| fun _ ->
            let conn = createInMemoryConnection ()
            FriendProjection.handler.Init conn

            addFriend conn "Marco"
            addFriend conn "Alice"
            addFriend conn "Bob"

            // Incremental catch-up first, capture the resulting rows.
            Projection.runProjection conn FriendProjection.handler
            let incrementalRows =
                FriendProjection.getAll conn
                |> List.map (fun f -> f.Slug, f.Name)
                |> List.sort

            // Now rebuild (drop + replay from position 0), capturing progress.
            let progressSnapshots = System.Collections.Generic.List<Projection.RebuildProgress>()
            Projection.rebuildProjectionWithProgress conn FriendProjection.handler (fun p -> progressSnapshots.Add p)

            let rebuiltRows =
                FriendProjection.getAll conn
                |> List.map (fun f -> f.Slug, f.Name)
                |> List.sort

            Expect.equal rebuiltRows incrementalRows "Rebuild should produce the same rows as incremental catch-up"
            Expect.isNonEmpty (List.ofSeq progressSnapshots) "Rebuild should report at least one progress snapshot"

            let final = progressSnapshots |> Seq.last
            Expect.equal final.Position final.Head "Final progress position should reach the store head"
            Expect.equal final.EventsProcessed 3L "All 3 friend-added events should have been processed"
            Expect.equal (Projection.getCheckpoint conn FriendProjection.handler.Name) final.Head "Checkpoint should be saved at the head position after rebuild"

        testCase "rebuildProjectionWithProgress fixes Head at the store's tip for the whole rebuild" <| fun _ ->
            let conn = createInMemoryConnection ()
            FriendProjection.handler.Init conn
            addFriend conn "Marco"

            let progressSnapshots = System.Collections.Generic.List<Projection.RebuildProgress>()
            Projection.rebuildProjectionWithProgress conn FriendProjection.handler (fun p -> progressSnapshots.Add p)

            Expect.isTrue (progressSnapshots |> Seq.forall (fun p -> p.Head = 1L)) "Head should be fixed at 1 (one event in the store) for every progress snapshot"

        testCase "rebuildProjectionWithProgress reports position 0 before any batch is processed" <| fun _ ->
            let conn = createInMemoryConnection ()
            FriendProjection.handler.Init conn
            addFriend conn "Marco"
            addFriend conn "Alice"

            let progressSnapshots = System.Collections.Generic.List<Projection.RebuildProgress>()
            Projection.rebuildProjectionWithProgress conn FriendProjection.handler (fun p -> progressSnapshots.Add p)

            let first = progressSnapshots |> Seq.head
            Expect.equal first.Position 0L "First snapshot (emitted before replay starts) should be at position 0"
            Expect.equal first.EventsProcessed 0L "First snapshot should report no events processed yet"

        // series-d5tpn retires the lossy-rebuild guard (administration-kv7dp,
        // ADR-0049, superseded by ADR-0051): the column drop + compensating
        // events made `checkProjectionDrift` report zero for SeriesProjection,
        // and `SeriesProjection.dropTables` no longer drops the season/episode
        // cache tier (series-m7fdk reclassified those tables `Cache`, owned
        // by `MetadataCache.fs`) — so a rebuild can no longer destroy the data
        // the guard used to protect. These tests replace the 3 removed
        // lossy-rebuild-guard tests above, proving the retirement rather than
        // leaving green tests that assert a deleted mechanism still exists.
        testCase "SeriesProjection is no longer special-cased: a rebuild request claims the single-flight guard exactly like any other projection" <| fun _ ->
            let conn = createInMemoryConnection ()
            SeriesProjection.handler.Init conn

            let guards = Administration.makeGuards ()
            match Administration.decideAndClaimRebuildGuard guards "SeriesProjection" with
            | None -> ()
            | other -> failtest (sprintf "Expected the rebuild to be allowed (None), got %A" other)
            Expect.isTrue (guards.RebuildingProjections.ContainsKey("SeriesProjection")) "The single-flight guard must be claimed for SeriesProjection, same as any other projection"
            guards.RebuildingProjections.TryRemove("SeriesProjection") |> ignore

        testCase "'Rebuild all' completes all six handlers, including SeriesProjection — no skip" <| fun _ ->
            let conn = createInMemoryConnection ()
            let allHandlers = [
                MovieProjection.handler
                FriendProjection.handler
                ContentBlockProjection.handler
                CatalogProjection.handler
                SeriesProjection.handler
                GameProjection.handler
            ]
            for handler in allHandlers do
                handler.Init conn

            let guards = Administration.makeGuards ()
            let completed = ResizeArray<string>()
            for handler in allHandlers do
                match Administration.decideAndClaimRebuildGuard guards handler.Name with
                | Some other ->
                    failtest (sprintf "%s was unexpectedly rejected with %A" handler.Name other)
                | None ->
                    try
                        Projection.rebuildProjectionWithProgress conn handler (fun _ -> ())
                        completed.Add(handler.Name)
                    finally
                        guards.RebuildingProjections.TryRemove(handler.Name) |> ignore

            Expect.equal (List.ofSeq completed |> List.sort) (allHandlers |> List.map (fun h -> h.Name) |> List.sort) "All six handlers, including SeriesProjection, should complete their rebuild"

        testCase "series-d5tpn: rebuilding SeriesProjection leaves series_metadata_cache/series_season_cache/series_episode_cache row counts unchanged" <| fun _ ->
            let conn = createInMemoryConnection ()
            MetadataCache.initialize conn
            SeriesProjection.handler.Init conn

            let seriesData: Series.SeriesAddedData = {
                Name = "Silo"; Year = 2023; Overview = ""; Genres = [ "Drama" ]; Status = "Returning"
                PosterRef = None; BackdropRef = None; TmdbId = 2867; TmdbRating = None; EpisodeRuntime = None
                Seasons = [ { SeasonNumber = 1; Name = "Season 1"; Overview = ""; PosterRef = None; AirDate = None; Episodes = [] } ]
            }
            EventStore.appendToStream conn (Series.streamId "silo-2023") -1L [ Series.Serialization.toEventData (Series.Series_added_to_library seriesData) ] |> ignore
            Projection.runProjection conn SeriesProjection.handler

            // Cache-tier data a rebuild must never touch. `series_detail` no
            // longer has overview/tmdb_rating/episode_runtime to seed from
            // (series-d5tpn dropped them) — MetadataCache.seedFromProjections
            // is a legacy-database-only path now (see MetadataCache.fs), so
            // seed series_metadata_cache directly here, plus a direct
            // Jellyfin-style materialization, exactly like the drift fixture
            // above.
            use seedCmd = conn.CreateCommand()
            seedCmd.CommandText <- "INSERT INTO series_metadata_cache (series_slug, overview, backdrop_ref, tmdb_rating, episode_runtime, fetched_at) VALUES ('silo-2023', 'A cop show', NULL, 8.2, 50, NULL)"
            seedCmd.ExecuteNonQuery() |> ignore
            SeriesProjection.materializeSeason conn "silo-2023" 2
            SeriesProjection.materializeEpisode conn "silo-2023" {
                JellyfinImport.MaterializedEpisode.SeasonNumber = 2
                EpisodeNumber = 1
                Name = "The Silence"; Overview = ""; Runtime = None; AirDate = None; StillRef = None
            }

            let rowCount (table: string) =
                use cmd = conn.CreateCommand()
                cmd.CommandText <- sprintf "SELECT COUNT(*) FROM %s" table
                cmd.ExecuteScalar() :?> int64

            let cacheTables = [ "series_metadata_cache"; "series_season_cache"; "series_episode_cache" ]
            let preCounts = cacheTables |> List.map (fun t -> t, rowCount t)
            Expect.isTrue (preCounts |> List.forall (fun (_, c) -> c > 0L)) "Sanity: every cache table should hold at least one row before the rebuild"

            Projection.rebuildProjection conn SeriesProjection.handler

            let postCounts = cacheTables |> List.map (fun t -> t, rowCount t)
            Expect.equal postCounts preCounts "Rebuilding SeriesProjection must leave every cache-tier table's row count unchanged"
    ]
