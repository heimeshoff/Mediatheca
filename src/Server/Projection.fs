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

    let private processBatch (conn: SqliteConnection) (handler: ProjectionHandler) (fromPosition: int64) : int64 option =
        let batchSize = 100
        let events = EventStore.readAllForward conn fromPosition batchSize

        match events with
        | [] -> None
        | events ->
            for event in events do
                handler.Handle conn event

            let lastPosition = (List.last events).GlobalPosition
            saveCheckpoint conn handler.Name lastPosition
            Some lastPosition

    let runProjection (conn: SqliteConnection) (handler: ProjectionHandler) : unit =
        handler.Init conn
        let mutable position = getCheckpoint conn handler.Name
        let mutable keepGoing = true

        while keepGoing do
            match processBatch conn handler position with
            | Some newPosition -> position <- newPosition
            | None -> keepGoing <- false

    let rebuildProjection (conn: SqliteConnection) (handler: ProjectionHandler) : unit =
        handler.Drop conn
        handler.Init conn
        saveCheckpoint conn handler.Name 0L
        let mutable position = 0L
        let mutable keepGoing = true

        while keepGoing do
            match processBatch conn handler position with
            | Some newPosition -> position <- newPosition
            | None -> keepGoing <- false

    let startAllProjections (conn: SqliteConnection) (handlers: ProjectionHandler list) : unit =
        for handler in handlers do
            runProjection conn handler
