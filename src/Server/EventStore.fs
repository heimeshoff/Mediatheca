namespace Mediatheca.Server

open System
open System.Data
open System.IO
open Microsoft.Data.Sqlite
open Donald
open Thoth.Json.Net

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

    /// Per-connection pragma block. Every `SqliteConnection` object defaults
    /// to unsafe pragma values on open — this is NOT one-time database state,
    /// it must be re-applied on **every** connection, whether the one-time
    /// startup `conn` or a per-request `factory()` connection. Table/FTS
    /// creation (below) is the true one-time step and must never be re-run
    /// per request.
    let configureConnection (conn: SqliteConnection) =
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
        configureConnection conn
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

    /// Shared condition-building for `filter`, used by both `queryEventPage`
    /// (newest-first, keyset-paginated) and `queryEventsAfter` (ascending
    /// live-tail, administration-mtf1f) — the two differ only in direction and
    /// bound, not in what counts as a match. Returns (WHERE conditions, SQL
    /// parameters), both already accounting for which filter fields are set.
    let private buildFilterConditions (filter: QueryFilter) : string list * (string * SqlType) list =
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

        (conditions |> List.rev), paramList

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
        let baseConditions, baseParams = buildFilterConditions filter

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

    /// Ascending "everything after global position `after`, matching `filter`"
    /// query for live-tail polling (administration-mtf1f, Follow mode). Reuses
    /// the exact same condition-building as `queryEventPage`
    /// (`buildFilterConditions`) and differs only in direction (ascending, not
    /// descending) and bound (`global_position > after`, not a keyset `before`
    /// cursor) — see ADR-0023. `limit` bounds a single poll response.
    let queryEventsAfter (conn: SqliteConnection) (filter: QueryFilter) (after: int64) (limit: int) : StoredEvent list =
        let baseConditions, baseParams = buildFilterConditions filter
        let conditions = "e.global_position > @after" :: baseConditions
        let whereClause = " WHERE " + String.concat " AND " conditions
        let queryParams = ("after", SqlType.Int64 after) :: ("fetch_limit", SqlType.Int32 limit) :: baseParams

        conn
        |> Db.newCommand ($"SELECT e.global_position, e.stream_id, e.stream_position, e.event_type, e.data, e.metadata, e.timestamp FROM events e{whereClause} ORDER BY e.global_position ASC LIMIT @fetch_limit")
        |> Db.setParams queryParams
        |> Db.query readEvent

    let getDistinctStreams (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand "SELECT DISTINCT stream_id FROM events ORDER BY stream_id"
        |> Db.query (fun rd -> rd.ReadString "stream_id")

    let getDistinctEventTypes (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand "SELECT DISTINCT event_type FROM events ORDER BY event_type"
        |> Db.query (fun rd -> rd.ReadString "event_type")

    /// Every distinct event type seen anywhere under a stream_id `prefix`
    /// (e.g. "Movie-") — the compensating-event composer's "types seen"
    /// picker (administration-xjmda): an operator composing a corrective
    /// event on one stream may clone a type that only exists on a sibling
    /// stream of the same bounded context. `prefix%` is index-backed via
    /// `idx_events_stream_id` (a plain prefix LIKE, no leading wildcard).
    let getDistinctEventTypesForPrefix (conn: SqliteConnection) (prefix: string) : string list =
        conn
        |> Db.newCommand "SELECT DISTINCT event_type FROM events WHERE stream_id LIKE @prefix ORDER BY event_type"
        |> Db.setParams [ "prefix", SqlType.String (prefix + "%") ]
        |> Db.query (fun rd -> rd.ReadString "event_type")

    /// "Clone a real event" pre-fill for the compensating-event composer
    /// (administration-xjmda): the most recent instance of `eventType` on
    /// `streamId` itself if one exists, else the most recent instance
    /// anywhere under `streamId`'s own bounded-context `prefix`. Two
    /// separate queries (not one UNION+ORDER) so the this-stream-first
    /// tiebreak is explicit rather than implied by ordering.
    let getMostRecentEventOfType (conn: SqliteConnection) (streamId: string) (prefix: string) (eventType: string) : StoredEvent option =
        let onThisStream =
            conn
            |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events WHERE stream_id = @stream_id AND event_type = @event_type ORDER BY global_position DESC LIMIT 1"
            |> Db.setParams [ "stream_id", SqlType.String streamId; "event_type", SqlType.String eventType ]
            |> Db.querySingle readEvent
        match onThisStream with
        | Some _ -> onThisStream
        | None ->
            conn
            |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events WHERE stream_id LIKE @prefix AND event_type = @event_type ORDER BY global_position DESC LIMIT 1"
            |> Db.setParams [ "prefix", SqlType.String (prefix + "%"); "event_type", SqlType.String eventType ]
            |> Db.querySingle readEvent

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

    /// Store head — the highest `global_position` currently in the log. Used
    /// by the projection dashboard (administration-qjcp4) to compute lag
    /// (head - checkpoint) and to size a rebuild's progress bar. 0 for an
    /// empty store. Deliberately `MAX(global_position)` rather than
    /// `COUNT(*)` — the two coincide only in the gap-free common case, and
    /// `global_position` is the actual quantity checkpoints are measured
    /// against.
    let getMaxGlobalPosition (conn: SqliteConnection) : int64 =
        conn
        |> Db.newCommand "SELECT MAX(global_position) as head FROM events"
        |> Db.querySingle (fun rd -> if rd.IsDBNull(rd.GetOrdinal("head")) then 0L else rd.ReadInt64 "head")
        |> Option.defaultValue 0L

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

    /// One representative stored event for a given event type, lowest
    /// `global_position` first — an indexed point lookup via
    /// idx_events_event_type, LIMIT 1. Used by the Health tab's unknown-event
    /// report (administration-gxd6e) to attach a raw-JSON sample to each
    /// flagged event type without loading every occurrence.
    let getSampleEventForType (conn: SqliteConnection) (eventType: string) : StoredEvent option =
        conn
        |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events WHERE event_type = @event_type ORDER BY global_position LIMIT 1"
        |> Db.setParams [ "event_type", SqlType.String eventType ]
        |> Db.querySingle readEvent

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

    // NDJSON export/import (administration-vrc56, ADR-0029) — the event
    // log's portable form: a backup/restore substrate and the base for
    // future copy-on-write log transformations. See ADR-0029 for why the
    // payload columns are embedded as opaque JSON-escaped strings rather
    // than re-nested as JSON objects, why `global_position` is preserved via
    // an explicit-rowid INSERT bypassing `appendToStream`, and why import
    // deliberately leaves projections dirty instead of self-triggering a
    // rebuild.

    /// Streams the full event log as NDJSON (one JSON object per line, ascending
    /// `global_position`) onto `writer`, walking `readAllForward`'s existing
    /// batching so the whole log is never materialized as one in-memory
    /// collection or string. Field order is fixed:
    /// globalPosition, streamId, streamPosition, eventType, data, metadata, timestamp.
    /// `data`/`metadata` are embedded as JSON-escaped STRING values holding the
    /// literal `events.data`/`events.metadata` TEXT content, not reparsed and
    /// re-nested as JSON objects — a JSON string escape/unescape is a lossless
    /// bijection, so the round-trip doesn't depend on canonical-JSON matching
    /// between whatever wrote the original payload and whatever writes it back.
    /// `globalPosition`/`streamPosition` are written as bare JSON numbers (via
    /// `sprintf "%d"`, not `Encode.int64` — Thoth encodes int64 as a JSON
    /// string to protect JS number precision, which this schema doesn't need).
    let exportNdjson (conn: SqliteConnection) (writer: TextWriter) : unit =
        let batchSize = 500

        let escapeString (s: string) : string =
            Encode.string s |> Encode.toString 0

        let writeLine (ev: StoredEvent) =
            let line =
                sprintf "{\"globalPosition\":%d,\"streamId\":%s,\"streamPosition\":%d,\"eventType\":%s,\"data\":%s,\"metadata\":%s,\"timestamp\":%s}"
                    ev.GlobalPosition
                    (escapeString ev.StreamId)
                    ev.StreamPosition
                    (escapeString ev.EventType)
                    (escapeString ev.Data)
                    (escapeString ev.Metadata)
                    (escapeString (ev.Timestamp.ToString("o")))
            writer.WriteLine(line: string)

        let rec loop (fromPosition: int64) =
            match readAllForward conn fromPosition batchSize with
            | [] -> ()
            | batch ->
                batch |> List.iter writeLine
                loop (List.last batch).GlobalPosition

        loop 0L

    /// Result of a successful `importNdjson` call.
    type ImportOutcome = { EventsImported: int }

    /// Why an import didn't happen (or didn't finish).
    type ImportFailure =
        /// The target store already has events — import into a non-empty
        /// store is a separate, more dangerous operation (administration-n8kqw).
        | StoreNotEmpty
        /// `lineNumber` is 1-based over non-blank lines seen so far;
        /// `message` is the underlying JSON-decode error.
        | MalformedLine of lineNumber: int * message: string

    let private ndjsonLineDecoder : Decoder<int64 * string * int64 * string * string * string * string> =
        Decode.object (fun get ->
            get.Required.Field "globalPosition" Decode.int64,
            get.Required.Field "streamId" Decode.string,
            get.Required.Field "streamPosition" Decode.int64,
            get.Required.Field "eventType" Decode.string,
            get.Required.Field "data" Decode.string,
            get.Required.Field "metadata" Decode.string,
            get.Required.Field "timestamp" Decode.string
        )

    /// Read-line/decode/explicit-rowid-INSERT loop, extracted from
    /// `importNdjson` (administration-n8kqw) so the wipe-first re-import path
    /// can share it: this function does NOT open its own transaction — the
    /// caller owns commit/rollback, since `runWipeAndImport` needs this same
    /// loop to share ONE transaction with `deleteAllEvents`/`rebuildFtsIndex`/
    /// the checkpoint rewind. The inline try/with that wraps a mid-loop
    /// exception as `MalformedLine(lineNumber, ex.Message)` is load-bearing
    /// and moved here with the loop — it protects only the read/decode/insert
    /// loop itself; a caller-side transaction commit/rollback is the caller's
    /// own responsibility. Preserves each line's `global_position` exactly
    /// via an explicit-rowid INSERT (bypassing `appendToStream`, which
    /// recomputes stream position and timestamp and has no notion of
    /// "preserve this exact position"); SQLite's AUTOINCREMENT bookkeeping
    /// (`sqlite_sequence`) advances to match the highest explicit rowid
    /// inserted.
    let importNdjsonRows (conn: SqliteConnection) (reader: TextReader) : Result<ImportOutcome, ImportFailure> =
        let mutable lineNumber = 0
        let mutable count = 0
        try
            let mutable outcome : Result<ImportOutcome, ImportFailure> option = None

            while outcome.IsNone do
                match reader.ReadLine() with
                | null ->
                    outcome <- Some (Ok { EventsImported = count })
                | line when line.Trim() = "" ->
                    lineNumber <- lineNumber + 1
                | line ->
                    lineNumber <- lineNumber + 1
                    match Decode.fromString ndjsonLineDecoder line with
                    | Error err ->
                        outcome <- Some (Error (MalformedLine(lineNumber, err)))
                    | Ok (globalPosition, streamId, streamPosition, eventType, data, metadata, timestamp) ->
                        conn
                        |> Db.newCommand """
                            INSERT INTO events (global_position, stream_id, stream_position, event_type, data, metadata, timestamp)
                            VALUES (@global_position, @stream_id, @stream_position, @event_type, @data, @metadata, @timestamp)
                        """
                        |> Db.setParams [
                            "global_position", SqlType.Int64 globalPosition
                            "stream_id", SqlType.String streamId
                            "stream_position", SqlType.Int64 streamPosition
                            "event_type", SqlType.String eventType
                            "data", SqlType.String data
                            "metadata", SqlType.String metadata
                            "timestamp", SqlType.String timestamp
                        ]
                        |> Db.exec
                        count <- count + 1

            match outcome with
            | Some result -> result
            | None ->
                // Unreachable: the loop only exits via one of the two
                // `outcome <- Some ...` assignments above.
                Error (MalformedLine(lineNumber, "Unexpected end of import"))
        with ex ->
            Error (MalformedLine(lineNumber, ex.Message))

    /// Imports an NDJSON event log produced by `exportNdjson` into the target
    /// store, reading `reader` line-by-line so the upload is never buffered
    /// whole. Refuses immediately (`StoreNotEmpty`) if the target store
    /// already has events, before a single line is read from `reader`. The
    /// whole import runs in one transaction around `importNdjsonRows` (see
    /// its doc comment for the extracted loop and position-preservation
    /// details): a malformed line partway through rolls back everything,
    /// leaving the target store empty rather than partially populated. Does
    /// not touch `projection_checkpoints` — the store reads as dirty via the
    /// existing lag-detection until the operator runs the existing
    /// Rebuild-all control (administration-qjcp4, ADR-0025). On THIS
    /// empty-store path, SQLite's AUTOINCREMENT bookkeeping continues a
    /// subsequent ordinary append from `(imported max global_position) + 1`
    /// — the wipe-first path (administration-n8kqw) deliberately does not
    /// reset `sqlite_sequence`, so that claim holds only here, not there.
    let importNdjson (conn: SqliteConnection) (reader: TextReader) : Result<ImportOutcome, ImportFailure> =
        if getTotalEventCount conn > 0 then
            Error StoreNotEmpty
        else
            use tx = conn.BeginTransaction()
            match importNdjsonRows conn reader with
            | Ok result ->
                tx.Commit()
                Ok result
            | Error failure ->
                tx.Rollback()
                Error failure

    // Event surgery (administration-wwc36, ADR-0034) — the raw-log escape
    // hatch for cases the compensating-event composer (ADR-0032) can't reach:
    // a genuinely wrong-payload event, or a stranded event-type name left by
    // a code-side DU rename. Every primitive below breaks the "events never
    // change or disappear" assumption `createFtsIndex`'s own doc comment
    // states — callers MUST follow an edit/delete with `rebuildFtsIndex` (the
    // insert-only `events_fts_ai` trigger never covers an UPDATE or a
    // vanished row) and rewind `projection_checkpoints` (Administration's
    // `isAnyProjectionDirty`, ADR-0025) so every dependent projection reads
    // as dirty until the operator reruns Rebuild-all. See ADR-0034 for the
    // full guardrail protocol (backup / preview+confirm / dirty signal).

    /// One event row by exact `global_position` — event surgery's "the one
    /// targeted row" preview fetch. `None` for a position that doesn't exist
    /// (already deleted, or never existed).
    let getEventByGlobalPosition (conn: SqliteConnection) (globalPosition: int64) : StoredEvent option =
        conn
        |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events WHERE global_position = @gp"
        |> Db.setParams [ "gp", SqlType.Int64 globalPosition ]
        |> Db.querySingle readEvent

    /// Exact count of rows at `eventType` — the rename preview's headline
    /// number (a rename can touch far more rows than any bounded sample
    /// should render).
    let countEventsOfType (conn: SqliteConnection) (eventType: string) : int =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM events WHERE event_type = @event_type"
        |> Db.setParams [ "event_type", SqlType.String eventType ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
        |> Option.defaultValue 0

    /// A bounded sample of rows at `eventType`, oldest-first — the rename
    /// preview's "here's what you're about to touch" spot-check, capped so a
    /// rename over thousands of rows doesn't try to render them all.
    let sampleEventsOfType (conn: SqliteConnection) (eventType: string) (limit: int) : StoredEvent list =
        conn
        |> Db.newCommand "SELECT global_position, stream_id, stream_position, event_type, data, metadata, timestamp FROM events WHERE event_type = @event_type ORDER BY global_position LIMIT @limit"
        |> Db.setParams [ "event_type", SqlType.String eventType; "limit", SqlType.Int32 limit ]
        |> Db.query readEvent

    /// Updates one event's data+metadata by exact `global_position`. Returns
    /// rows affected (0 or 1 — global_position is the events table's primary
    /// key). Callers MUST follow this with `rebuildFtsIndex` inside the same
    /// transaction (see module doc above).
    let editEventData (conn: SqliteConnection) (globalPosition: int64) (newData: string) (newMetadata: string) : int =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "UPDATE events SET data = @data, metadata = @metadata WHERE global_position = @gp"
        cmd.Parameters.AddWithValue("@data", newData) |> ignore
        cmd.Parameters.AddWithValue("@metadata", newMetadata) |> ignore
        cmd.Parameters.AddWithValue("@gp", globalPosition) |> ignore
        cmd.ExecuteNonQuery()

    /// Deletes one event by exact `global_position`. Leaves `stream_position`/
    /// `global_position` GAPS by design — no renumbering. Verified safe:
    /// `appendToStream` re-reads `MAX(stream_position)` fresh via
    /// `getStreamPosition` immediately before each append, `getMaxGlobalPosition`
    /// is deliberately `MAX` not `COUNT`, and the keyset (`queryEventPage`) /
    /// live-tail (`queryEventsAfter`) cursors use strict `<`/`>` only, never
    /// assuming contiguity. Returns rows affected (0 or 1). Callers MUST
    /// follow this with `rebuildFtsIndex` inside the same transaction — the
    /// insert-only `events_fts_ai` trigger never covers a vanished row.
    let deleteEventRow (conn: SqliteConnection) (globalPosition: int64) : int =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "DELETE FROM events WHERE global_position = @gp"
        cmd.Parameters.AddWithValue("@gp", globalPosition) |> ignore
        cmd.ExecuteNonQuery()

    /// Renames an event type store-wide — the schema-migration verb for a
    /// code-side DU rename that left old-named rows stranded. Reflected
    /// automatically by `getDistinctEventTypes`/`getDistinctEventTypesForPrefix`
    /// (both live `SELECT DISTINCT`, no cache). No FTS action needed — FTS
    /// indexes `data`, not `event_type`. Returns rows affected (the rename
    /// count).
    let renameEventTypeRows (conn: SqliteConnection) (oldType: string) (newType: string) : int =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "UPDATE events SET event_type = @new WHERE event_type = @old"
        cmd.Parameters.AddWithValue("@new", newType) |> ignore
        cmd.Parameters.AddWithValue("@old", oldType) |> ignore
        cmd.ExecuteNonQuery()

    /// Re-syncs `events_fts` after an edit or delete — the exact `('rebuild')`
    /// idiom `createFtsIndex`'s own backfill path uses. MUST run AFTER the
    /// mutation, inside the SAME transaction (ADR-0034), so a full FTS
    /// rebuild sees post-mutation `events` content — for a delete this is the
    /// whole point, since the insert-only trigger never covers a vanished row.
    let rebuildFtsIndex (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand "INSERT INTO events_fts(events_fts) VALUES ('rebuild')"
        |> Db.exec

    /// `VACUUM INTO` backup (ADR-0034/ADR-0003): a transactionally-consistent,
    /// WAL-aware, one-statement snapshot of the live store to `backupPath`.
    /// MUST run in autocommit — BEFORE any `conn.BeginTransaction()` — since
    /// SQLite refuses `VACUUM` inside a transaction. `VACUUM INTO` also
    /// happens to yield a plain non-WAL standalone file, exactly what a
    /// portable backup should be. Verifies the result by opening it on a
    /// THROWAWAY, unconfigured connection (deliberately NOT
    /// `configureConnection` — that would flip a plain file to WAL mode and
    /// spawn `-wal`/`-shm` sidecars next to a supposedly-inert backup file)
    /// and running `PRAGMA integrity_check` plus a `SELECT COUNT(*) FROM
    /// events`. `Error` on either the VACUUM itself or the verify step; the
    /// caller MUST abort with no row touched — see `Administration`'s
    /// commit-op wiring.
    let vacuumIntoBackup (conn: SqliteConnection) (backupPath: string) : Result<unit, string> =
        try
            use cmd = conn.CreateCommand()
            cmd.CommandText <- "VACUUM INTO @path"
            cmd.Parameters.AddWithValue("@path", backupPath) |> ignore
            cmd.ExecuteNonQuery() |> ignore

            use verifyConn = new SqliteConnection($"Data Source={backupPath}")
            verifyConn.Open()
            use integrityCmd = verifyConn.CreateCommand()
            integrityCmd.CommandText <- "PRAGMA integrity_check"
            let integrityResult = integrityCmd.ExecuteScalar() :?> string
            if integrityResult <> "ok" then
                Error (sprintf "Backup integrity check failed: %s" integrityResult)
            else
                use countCmd = verifyConn.CreateCommand()
                countCmd.CommandText <- "SELECT COUNT(*) FROM events"
                countCmd.ExecuteScalar() |> ignore
                Ok ()
        with ex ->
            Error ex.Message

    // ── Wipe-first event log import (administration-n8kqw, ADR-0038) ──
    // Overwriting a non-empty store: `deleteAllEvents` + `importNdjsonRows`
    // (above) + `rebuildFtsIndex` (above) share ONE transaction, orchestrated
    // by `Administration.runWipeAndImport`. This module owns only the
    // storage-layer verbs; the guardrail protocol (VACUUM INTO backup first,
    // preview+confirm, the wipe-import/rebuild mutual-exclusion guard) lives
    // in `Administration.fs`, which needs `dbPath`/`projectionHandlers` —
    // neither a storage-layer concept.

    /// `DELETE FROM events` (administration-n8kqw) — clears the log while
    /// preserving schema, the `events_fts` shadow tables, and the
    /// `events_fts_ai` AFTER-INSERT trigger. Deliberately NOT drop/recreate:
    /// a wipe-first re-import runs `rebuildFtsIndex` afterward, in the SAME
    /// transaction, to resync `events_fts` against the newly-imported rows —
    /// mirroring the mutate-then-rebuild order ADR-0034 established for
    /// edit/delete. Does NOT touch `sqlite_sequence` — see
    /// `Administration.runWipeAndImport`'s doc comment for why that's
    /// deliberate. Returns rows deleted.
    let deleteAllEvents (conn: SqliteConnection) : int =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "DELETE FROM events"
        cmd.ExecuteNonQuery()

    /// Discard-side aggregate stats for the wipe-import confirm dialog
    /// (administration-n8kqw): exact event count, distinct stream count, and
    /// oldest/newest timestamp — one indexed-scan query, not a `getHealthStats`-
    /// shaped (90-day-bounded, images-directory-walking) or `getDistinctStreams`-
    /// shaped (materializes every stream id) query, both of which are the
    /// wrong cost/shape for a confirm dialog that just needs four numbers.
    /// `MIN`/`MAX` over the `timestamp` TEXT column ARE chronologically
    /// correct here, not merely lexicographic coincidence: every writer
    /// stamps `DateTimeOffset.ToString("o")` (ISO-8601 round-trip, fixed-
    /// width, lexicographically sortable in timestamp order) — do not "fix"
    /// this into `datetime()`, which would not change correctness but would
    /// lose the free index-friendliness of a plain TEXT comparison. `None`
    /// timestamps and a zero count for an empty store (an aggregate query
    /// with no `GROUP BY` always returns exactly one row, with `NULL`
    /// `MIN`/`MAX` over zero matching rows).
    type EventStoreSummary = {
        EventCount: int
        DistinctStreamCount: int
        OldestTimestamp: string option
        NewestTimestamp: string option
    }

    let getEventStoreSummary (conn: SqliteConnection) : EventStoreSummary =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt, COUNT(DISTINCT stream_id) as streams, MIN(timestamp) as oldest, MAX(timestamp) as newest FROM events"
        |> Db.querySingle (fun rd ->
            { EventCount = rd.ReadInt32 "cnt"
              DistinctStreamCount = rd.ReadInt32 "streams"
              OldestTimestamp = if rd.IsDBNull(rd.GetOrdinal("oldest")) then None else Some (rd.ReadString "oldest")
              NewestTimestamp = if rd.IsDBNull(rd.GetOrdinal("newest")) then None else Some (rd.ReadString "newest") })
        |> Option.defaultValue { EventCount = 0; DistinctStreamCount = 0; OldestTimestamp = None; NewestTimestamp = None }
