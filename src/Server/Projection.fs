namespace Mediatheca.Server

open System
open Microsoft.Data.Sqlite
open Donald

module Projection =

    type ProjectionHandler = {
        Name: string
        Handle: SqliteConnection -> EventStore.StoredEvent -> unit
        Init: SqliteConnection -> unit
        Drop: SqliteConnection -> unit
    }

    let getCheckpoint (conn: SqliteConnection) (projectionName: string) : int64 =
        conn
        |> Db.newCommand "SELECT last_position FROM projection_checkpoints WHERE projection_name = @name"
        |> Db.setParams [ "name", SqlType.String projectionName ]
        |> Db.querySingle (fun rd -> rd.ReadInt64 "last_position")
        |> Option.defaultValue 0L

    /// Checkpoint position plus its `updated_at`, for the projection
    /// dashboard's listing (administration-qjcp4). `None` for a projection
    /// that has never checkpointed (position implicitly 0).
    let getCheckpointInfo (conn: SqliteConnection) (projectionName: string) : int64 * string option =
        conn
        |> Db.newCommand "SELECT last_position, updated_at FROM projection_checkpoints WHERE projection_name = @name"
        |> Db.setParams [ "name", SqlType.String projectionName ]
        |> Db.querySingle (fun rd -> rd.ReadInt64 "last_position", rd.ReadString "updated_at")
        |> function
           | Some (position, updatedAt) -> position, Some updatedAt
           | None -> 0L, None

    let saveCheckpoint (conn: SqliteConnection) (projectionName: string) (position: int64) : unit =
        let now = DateTimeOffset.UtcNow.ToString("o")
        conn
        |> Db.newCommand """
            INSERT INTO projection_checkpoints (projection_name, last_position, updated_at)
            VALUES (@name, @position, @updated_at)
            ON CONFLICT(projection_name) DO UPDATE SET
                last_position = @position,
                updated_at = @updated_at
        """
        |> Db.setParams [
            "name", SqlType.String projectionName
            "position", SqlType.Int64 position
            "updated_at", SqlType.String now
        ]
        |> Db.exec

    /// Returns (lastPositionInBatch, eventsInBatch), or None when there is
    /// nothing left to process from `fromPosition`.
    let private processBatch (conn: SqliteConnection) (handler: ProjectionHandler) (fromPosition: int64) : (int64 * int) option =
        let batchSize = 100
        let events = EventStore.readAllForward conn fromPosition batchSize

        match events with
        | [] -> None
        | events ->
            for event in events do
                handler.Handle conn event

            let lastPosition = (List.last events).GlobalPosition
            saveCheckpoint conn handler.Name lastPosition
            Some (lastPosition, List.length events)

    let runProjection (conn: SqliteConnection) (handler: ProjectionHandler) : unit =
        handler.Init conn
        let mutable position = getCheckpoint conn handler.Name
        let mutable keepGoing = true

        while keepGoing do
            match processBatch conn handler position with
            | Some (newPosition, _) -> position <- newPosition
            | None -> keepGoing <- false

    let rebuildProjection (conn: SqliteConnection) (handler: ProjectionHandler) : unit =
        handler.Drop conn
        handler.Init conn
        saveCheckpoint conn handler.Name 0L
        let mutable position = 0L
        let mutable keepGoing = true

        while keepGoing do
            match processBatch conn handler position with
            | Some (newPosition, _) -> position <- newPosition
            | None -> keepGoing <- false

    /// Progress snapshot emitted while a rebuild runs — the projection
    /// dashboard's rebuild-with-live-progress command (administration-qjcp4).
    /// `Head` is fixed at the store's tip when the rebuild starts, so the
    /// progress bar has a stable denominator even if more events are
    /// appended mid-rebuild (those get picked up by the next catch-up run,
    /// same as today's incremental `runProjection`).
    type RebuildProgress = {
        Position: int64
        Head: int64
        EventsProcessed: int64
    }

    /// Same drop+replay as `rebuildProjection`, but reports a `RebuildProgress`
    /// after every batch (and once at the very start, at position 0) so a
    /// caller can stream progress to a client instead of blocking silently
    /// until the whole replay completes.
    let rebuildProjectionWithProgress (conn: SqliteConnection) (handler: ProjectionHandler) (onProgress: RebuildProgress -> unit) : unit =
        handler.Drop conn
        handler.Init conn
        saveCheckpoint conn handler.Name 0L
        let head = EventStore.getMaxGlobalPosition conn
        let mutable position = 0L
        let mutable processed = 0L
        let mutable keepGoing = true

        onProgress { Position = position; Head = head; EventsProcessed = processed }

        while keepGoing do
            match processBatch conn handler position with
            | Some (newPosition, batchCount) ->
                position <- newPosition
                processed <- processed + int64 batchCount
                onProgress { Position = position; Head = head; EventsProcessed = processed }
            | None -> keepGoing <- false

    let startAllProjections (conn: SqliteConnection) (handlers: ProjectionHandler list) : unit =
        for handler in handlers do
            runProjection conn handler

    /// Same drop+replay shape as `rebuildProjection`, but reads events from a
    /// separate connection (`liveConn`) and writes exclusively into
    /// `shadowConn` — the shadow-replay drift detector's throwaway-connection
    /// design (administration-btvqa, ADR-0031): `liveConn` is only ever read
    /// from (via `EventStore.readAllForward`), `shadowConn` is the only
    /// connection ever written to, so "read-only against live" holds by
    /// construction with zero changes to any `*Projection.fs` handler body.
    /// Skips checkpoint writes entirely — the shadow DB never needs `events` /
    /// `projection_checkpoints`, only each handler's own owned tables.
    let replayIntoShadow (liveConn: SqliteConnection) (shadowConn: SqliteConnection) (handler: ProjectionHandler) : unit =
        handler.Drop shadowConn
        handler.Init shadowConn
        let batchSize = 100
        let mutable position = 0L
        let mutable keepGoing = true

        while keepGoing do
            match EventStore.readAllForward liveConn position batchSize with
            | [] -> keepGoing <- false
            | events ->
                for event in events do
                    handler.Handle shadowConn event
                position <- (List.last events).GlobalPosition
