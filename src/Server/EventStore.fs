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

    /// FTS5 external-content index over events.data, so the event browser can
    /// full-text search event payloads (administration-g5dfy). External content
    /// (content='events', content_rowid='global_position') means the FTS index
    /// stores no copy of the payload itself — it mirrors `events` by rowid, kept
    /// in sync going forward by an AFTER INSERT trigger (events are append-only;
    /// there is no UPDATE/DELETE trigger to write because rows in `events` never
    /// change or disappear).
    ///
    /// Idempotent across restarts: CREATE ... IF NOT EXISTS is a no-op on an
    /// already-migrated database. Backfills pre-existing rows (events inserted
    /// before this migration existed) by checking, *before* creating it,
    /// whether `events_fts` already exists — if it doesn't, this is either a
    /// brand-new database (nothing to backfill) or a database that predates
    /// this migration (rows already in `events` with no FTS entries yet) —
    /// either way, issuing FTS5's built-in `('rebuild')` command once right
    /// after creation is correct and cheap.
    ///
    /// Deliberately NOT implemented as "rebuild if COUNT(*) FROM events_fts
    /// disagrees with COUNT(*) FROM events": for an external-content FTS5
    /// table, an unfiltered COUNT(*)/SELECT is satisfied by mirroring the
    /// content table's rowids directly — it does not consult the inverted
    /// index — so a freshly created, entirely unindexed events_fts table
    /// already reports the "correct" count and that check never fires.
    let private createFtsIndex (conn: SqliteConnection) =
        let alreadyMigrated =
            conn
            |> Db.newCommand "SELECT COUNT(*) as cnt FROM sqlite_master WHERE type = 'table' AND name = 'events_fts'"
            |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0
            |> fun cnt -> cnt > 0

        use cmd = conn.CreateCommand()
        cmd.CommandText <- """
            CREATE VIRTUAL TABLE IF NOT EXISTS events_fts USING fts5(
                data,
                content='events',
                content_rowid='global_position'
            );

            CREATE TRIGGER IF NOT EXISTS events_fts_ai AFTER INSERT ON events BEGIN
                INSERT INTO events_fts(rowid, data) VALUES (new.global_position, new.data);
            END;
        """
        cmd.ExecuteNonQuery() |> ignore

        if not alreadyMigrated then
            conn
            |> Db.newCommand "INSERT INTO events_fts(events_fts) VALUES ('rebuild')"
            |> Db.exec

    let initialize (conn: SqliteConnection) =
        setPragmas conn
        createTables conn
        createFtsIndex conn

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

    /// Composable filter for `queryEventPage`. Deliberately BC-agnostic at this
    /// layer: `StreamPrefix` is an already-resolved `stream_id` prefix (e.g.
    /// "Movie-"), not a bounded-context name — the name-to-prefix mapping is
    /// admin-console knowledge that lives in `Administration.fs`, not here.
    /// Shared with (not duplicated by) any future "events after global position N
    /// matching this same filter" live-tail query (administration-mtf1f) — that
    /// query reuses this exact record and only swaps the pagination direction.
    type QueryFilter = {
        Search: string option
        StreamFilter: string option
        EventTypeFilter: string option
        StreamPrefix: string option
        TimestampFrom: string option
        TimestampTo: string option
    }

    let emptyQueryFilter : QueryFilter = {
        Search = None
        StreamFilter = None
        EventTypeFilter = None
        StreamPrefix = None
        TimestampFrom = None
        TimestampTo = None
    }

    /// FTS5 MATCH input is a tiny query language of its own (AND/OR/NOT, `-`,
    /// `*`, column filters...). Free-text search boxes shouldn't hand user input
    /// straight to that parser — a term like `blade-runner` or `friend's` would
    /// throw a syntax error instead of matching. Wrapping the whole term as a
    /// quoted FTS5 string turns it into a literal phrase query; embedded quotes
    /// are escaped by doubling, per FTS5 string-literal syntax.
    let private toFtsPhraseQuery (term: string) =
        "\"" + term.Replace("\"", "\"\"") + "\""

    /// Keyset-paginated, filtered event query, newest-first.
    /// `before = None` returns the first (newest) page; `before = Some p`
    /// returns events with global_position strictly less than `p` — i.e. the
    /// page immediately older than whatever page ended at `p`. Callers page
    /// backward by remembering the cursor that produced each page they've
    /// already seen (a client-side cursor stack) rather than via a server-side
    /// "after" direction, so there is exactly one keyset direction to reason
    /// about here.
    /// Returns (page of at most `pageSize` events, hasMore, totalMatches).
    let queryEventPage (conn: SqliteConnection) (filter: QueryFilter) (before: int64 option) (pageSize: int) : StoredEvent list * bool * int =
        let mutable conditions = []
        let mutable paramList = []

        match filter.Search with
        | Some s when s.Trim() <> "" ->
            conditions <- "e.global_position IN (SELECT rowid FROM events_fts WHERE events_fts MATCH @search)" :: conditions
            paramList <- ("search", SqlType.String (toFtsPhraseQuery (s.Trim()))) :: paramList
        | _ -> ()

        match filter.StreamFilter with
        | Some f when f <> "" ->
            conditions <- "e.stream_id LIKE @stream_filter" :: conditions
            paramList <- ("stream_filter", SqlType.String ($"%%{f}%%")) :: paramList
        | _ -> ()

        match filter.EventTypeFilter with
        | Some f when f <> "" ->
            conditions <- "e.event_type LIKE @event_type_filter" :: conditions
            paramList <- ("event_type_filter", SqlType.String ($"%%{f}%%")) :: paramList
        | _ -> ()

        match filter.StreamPrefix with
        | Some prefix when prefix <> "" ->
            conditions <- "e.stream_id LIKE @stream_prefix" :: conditions
            paramList <- ("stream_prefix", SqlType.String ($"{prefix}%%")) :: paramList
        | _ -> ()

        match filter.TimestampFrom with
        | Some ts when ts <> "" ->
            conditions <- "e.timestamp >= @ts_from" :: conditions
            paramList <- ("ts_from", SqlType.String ts) :: paramList
        | _ -> ()

        match filter.TimestampTo with
        | Some ts when ts <> "" ->
            conditions <- "e.timestamp <= @ts_to" :: conditions
            paramList <- ("ts_to", SqlType.String ts) :: paramList
        | _ -> ()

        let baseConditions = conditions |> List.rev
        let baseParams = paramList

        let whereClause (extra: string list) =
            match baseConditions @ extra with
            | [] -> ""
            | all -> " WHERE " + String.concat " AND " all

        let totalMatches =
            conn
            |> Db.newCommand ($"SELECT COUNT(*) as cnt FROM events e{whereClause []}")
            |> Db.setParams baseParams
            |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0

        let pageConditions, pageParams =
            match before with
            | Some p -> [ "e.global_position < @before" ], ("before", SqlType.Int64 p) :: baseParams
            | None -> [], baseParams

        let fetchLimit = pageSize + 1
        let pageParams = ("fetch_limit", SqlType.Int32 fetchLimit) :: pageParams

        let rows =
            conn
            |> Db.newCommand ($"SELECT e.global_position, e.stream_id, e.stream_position, e.event_type, e.data, e.metadata, e.timestamp FROM events e{whereClause pageConditions} ORDER BY e.global_position DESC LIMIT @fetch_limit")
            |> Db.setParams pageParams
            |> Db.query readEvent

        let hasMore = List.length rows > pageSize
        let page = rows |> List.truncate pageSize
        page, hasMore, totalMatches

    let getDistinctStreams (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand "SELECT DISTINCT stream_id FROM events ORDER BY stream_id"
        |> Db.query (fun rd -> rd.ReadString "stream_id")

    let getDistinctEventTypes (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand "SELECT DISTINCT event_type FROM events ORDER BY event_type"
        |> Db.query (fun rd -> rd.ReadString "event_type")

    let getRecentEvents (conn: SqliteConnection) (count: int) : StoredEvent list =
        conn
        |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events ORDER BY global_position DESC LIMIT @count"
        |> Db.setParams [ "count", SqlType.Int32 count ]
        |> Db.query readEvent

    let getTotalEventCount (conn: SqliteConnection) : int =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM events"
        |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    // Health stats (administration-hw74a) — GROUP BY on stream_id/event_type
    // reuses idx_events_stream_id/idx_events_event_type, so these are
    // index-only scans (no row data touched); the day-count query is bounded
    // by an indexed timestamp range rather than scanning the whole table. See
    // ADR-0021 for the full cost reasoning.

    /// Event count per distinct stream, unordered. Callers derive both the
    /// bounded-context breakdown (grouping streams by prefix) and the
    /// top-N-largest-streams list (sorting/truncating) from this one scan.
    let getEventCountsByStream (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand "SELECT stream_id, COUNT(*) as cnt FROM events GROUP BY stream_id"
        |> Db.query (fun rd -> rd.ReadString "stream_id", rd.ReadInt32 "cnt")

    /// Event count per distinct event type, unordered. Callers derive both
    /// the distinct-type count and the top-N-by-frequency list from this one
    /// scan.
    let getEventCountsByType (conn: SqliteConnection) : (string * int) list =
        conn
        |> Db.newCommand "SELECT event_type, COUNT(*) as cnt FROM events GROUP BY event_type"
        |> Db.query (fun rd -> rd.ReadString "event_type", rd.ReadInt32 "cnt")

    /// Per-day event counts for timestamps >= sinceIso (ISO-8601 TEXT, same
    /// format events.timestamp is stored in). Bounded by the indexed
    /// timestamp range rather than a full-table scan, so cost tracks the
    /// window size (e.g. ~90 days), not total store history.
    let getDailyEventCounts (conn: SqliteConnection) (sinceIso: string) : (string * int) list =
        conn
        |> Db.newCommand "SELECT substr(timestamp,1,10) as day, COUNT(*) as cnt FROM events WHERE timestamp >= @since GROUP BY day ORDER BY day"
        |> Db.setParams [ "since", SqlType.String sinceIso ]
        |> Db.query (fun rd -> rd.ReadString "day", rd.ReadInt32 "cnt")

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
