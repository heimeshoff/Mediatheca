module Mediatheca.Tests.ProjectionRebuildTests

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
    ]
