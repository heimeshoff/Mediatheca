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

        // administration-kv7dp (ADR-0049): rebuilding SeriesProjection
        // (Drop; Init; replay) would destroy episode/season metadata written
        // out-of-band by the Series TMDB refresh job and Jellyfin
        // materialization (ADR-0012) — data that lives only in the live
        // tables (now the series_*_cache tier, post series-m7fdk) and cannot
        // be replayed from Series_refreshed. series-d5tpn retires the guard.
        // These tests exercise `Administration.lossyRebuildRejectionMessage`
        // and `Administration.decideAndClaimRebuildGuard` directly, the same
        // "test the underlying function, not the SSE route" shape this file
        // already established for `rebuildProjectionWithProgress`.
        testCase "lossyRebuildRejectionMessage blocks SeriesProjection, allows MovieProjection, and is bypassed by MEDIATHECA_ALLOW_LOSSY_REBUILD=1" <| fun _ ->
            Expect.isSome (Administration.lossyRebuildRejectionMessage "SeriesProjection") "SeriesProjection must be blocked by default"
            Expect.isNone (Administration.lossyRebuildRejectionMessage "MovieProjection") "MovieProjection is not out-of-band-written and must not be blocked"

            Environment.SetEnvironmentVariable("MEDIATHECA_ALLOW_LOSSY_REBUILD", "1")
            try
                Expect.isNone (Administration.lossyRebuildRejectionMessage "SeriesProjection") "The env override must bypass the guard entirely"
            finally
                Environment.SetEnvironmentVariable("MEDIATHECA_ALLOW_LOSSY_REBUILD", null)

        testCase "a rebuild request for SeriesProjection is rejected before it claims the rebuild guard or touches series_episode_cache" <| fun _ ->
            let conn = createInMemoryConnection ()
            SeriesProjection.handler.Init conn
            use insertCmd = conn.CreateCommand()
            insertCmd.CommandText <- "INSERT INTO series_episode_cache (series_slug, season_number, episode_number, name) VALUES ('breaking-bad', 1, 1, 'Pilot')"
            insertCmd.ExecuteNonQuery() |> ignore

            let guards = Administration.makeGuards ()
            match Administration.decideAndClaimRebuildGuard guards "SeriesProjection" with
            | Some (Administration.LossyRebuildBlocked reason) ->
                Expect.stringContains reason "SeriesProjection" "The rejection reason should name the blocked projection"
            | other -> failtest (sprintf "Expected LossyRebuildBlocked, got %A" other)

            Expect.isTrue guards.RebuildingProjections.IsEmpty "The lossy-rebuild guard must never claim the single-flight rebuild lock — it claims nothing"

            use countCmd = conn.CreateCommand()
            countCmd.CommandText <- "SELECT COUNT(*) FROM series_episode_cache"
            let rowCount = countCmd.ExecuteScalar() :?> int64 |> int
            Expect.equal rowCount 1 "series_episode_cache must be untouched by a rejected rebuild request"

        testCase "'Rebuild all' skips SeriesProjection and completes the other five handlers" <| fun _ ->
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
            let skipped = ResizeArray<string>()
            let completed = ResizeArray<string>()
            for handler in allHandlers do
                match Administration.decideAndClaimRebuildGuard guards handler.Name with
                | Some (Administration.LossyRebuildBlocked _) ->
                    skipped.Add(handler.Name)
                | Some other ->
                    failtest (sprintf "%s was unexpectedly rejected with %A" handler.Name other)
                | None ->
                    try
                        Projection.rebuildProjectionWithProgress conn handler (fun _ -> ())
                        completed.Add(handler.Name)
                    finally
                        guards.RebuildingProjections.TryRemove(handler.Name) |> ignore

            Expect.equal (List.ofSeq skipped) [ "SeriesProjection" ] "Only SeriesProjection should be skipped"
            Expect.equal (List.ofSeq completed |> List.sort) (allHandlers |> List.map (fun h -> h.Name) |> List.filter ((<>) "SeriesProjection") |> List.sort) "The other five handlers should all complete their rebuild"
    ]
