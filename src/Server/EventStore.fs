namespace Mediatheca.Server

open System
open System.Data
open Microsoft.Data.Sqlite
open Donald

module EventStore =

    // Types

    type StoredEvent = {
        GlobalPosition: int64
        StreamId: string
        StreamPosition: int64
        EventType: string
        Data: string
        Metadata: string
        Timestamp: DateTimeOffset
    }

    type EventData = {
        EventType: string
        Data: string
        Metadata: string
    }

    type AppendResult =
        | Success of globalPosition: int64
        | ConcurrencyConflict of expected: int64 * actual: int64

    // Database initialization

    let private setPragmas (conn: SqliteConnection) =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
        """
        cmd.ExecuteNonQuery() |> ignore

    let private createTables (conn: SqliteConnection) =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- """
            CREATE TABLE IF NOT EXISTS events (
                global_position  INTEGER PRIMARY KEY AUTOINCREMENT,
                stream_id        TEXT    NOT NULL,
                stream_position  INTEGER NOT NULL,
                event_type       TEXT    NOT NULL,
                data             TEXT    NOT NULL,
                metadata         TEXT    NOT NULL,
                timestamp        TEXT    NOT NULL,
                UNIQUE(stream_id, stream_position)
            );

            CREATE INDEX IF NOT EXISTS idx_events_stream_id ON events(stream_id);
            CREATE INDEX IF NOT EXISTS idx_events_event_type ON events(event_type);
            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp);

            CREATE TABLE IF NOT EXISTS projection_checkpoints (
                projection_name  TEXT PRIMARY KEY,
                last_position    INTEGER NOT NULL DEFAULT 0,
                updated_at       TEXT    NOT NULL
            );
        """
        cmd.ExecuteNonQuery() |> ignore

    let initialize (conn: SqliteConnection) =
        setPragmas conn
        createTables conn

    // Reading

    let private readEvent (rd: IDataReader) : StoredEvent = {
        GlobalPosition = rd.ReadInt64 "global_position"
        StreamId = rd.ReadString "stream_id"
        StreamPosition = rd.ReadInt64 "stream_position"
        EventType = rd.ReadString "event_type"
        Data = rd.ReadString "data"
        Metadata = rd.ReadString "metadata"
        Timestamp = rd.ReadString "timestamp" |> DateTimeOffset.Parse
    }

    let readStream (conn: SqliteConnection) (streamId: string) : StoredEvent list =
        conn
        |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events WHERE stream_id = @stream_id ORDER BY stream_position"
        |> Db.setParams [ "stream_id", SqlType.String streamId ]
        |> Db.query readEvent

    let getStreamPosition (conn: SqliteConnection) (streamId: string) : int64 =
        conn
        |> Db.newCommand "SELECT COALESCE(MAX(stream_position), -1) as pos FROM events WHERE stream_id = @stream_id"
        |> Db.setParams [ "stream_id", SqlType.String streamId ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt64 "pos")
        |> Option.defaultValue -1L

    let readAllForward (conn: SqliteConnection) (fromPosition: int64) (batchSize: int) : StoredEvent list =
        conn
        |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events WHERE global_position > @from_position ORDER BY global_position LIMIT @batch_size"
        |> Db.setParams [
            "from_position", SqlType.Int64 fromPosition
            "batch_size", SqlType.Int32 batchSize
        ]
        |> Db.query readEvent

    // Writing

    let appendToStream (conn: SqliteConnection) (streamId: string) (expectedPosition: int64) (events: EventData list) : AppendResult =
        let currentPosition = getStreamPosition conn streamId

        if currentPosition <> expectedPosition then
            ConcurrencyConflict(expected = expectedPosition, actual = currentPosition)
        else
            use tx = conn.BeginTransaction()
            try
                let mutable lastGlobalPosition = 0L
                let mutable streamPos = expectedPosition

                for event in events do
                    streamPos <- streamPos + 1L
                    let now = DateTimeOffset.UtcNow.ToString("o")

                    conn
                    |> Db.newCommand """
                        INSERT INTO events (stream_id, stream_position, event_type, data, metadata, timestamp)
                        VALUES (@stream_id, @stream_position, @event_type, @data, @metadata, @timestamp)
                    """
                    |> Db.setParams [
                        "stream_id", SqlType.String streamId
                        "stream_position", SqlType.Int64 streamPos
                        "event_type", SqlType.String event.EventType
                        "data", SqlType.String event.Data
                        "metadata", SqlType.String event.Metadata
                        "timestamp", SqlType.String now
                    ]
                    |> Db.exec

                    lastGlobalPosition <-
                        conn
                        |> Db.newCommand "SELECT last_insert_rowid() as id"
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt64 "id")
                        |> Option.defaultValue 0L

                tx.Commit()
                Success lastGlobalPosition
            with _ ->
                tx.Rollback()
                reraise ()
