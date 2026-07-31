namespace Mediatheca.Server

open System
open System.IO
open System.Threading
open Microsoft.Data.Sqlite
open Giraffe
open Mediatheca.Shared

/// Server-side implementation of IAdminApi — the Administration console's
/// Remoting contract. Kept separate from Api.fs (ADR-0004: multiple Fable.Remoting
/// APIs are supported) so admin plumbing (event store browser today; projection
/// dashboard, health, jobs, and surgery tooling in follow-up tasks) doesn't bloat
/// IMediathecaApi. Mounted under /api/admin/{Method} via AdminRoute.builder.
module Administration =

    /// Bounded-context name -> stream_id prefix, for the event explorer's BC
    /// filter (administration-g5dfy). Mirrors each BC's own `streamId` helper
    /// (Movies.streamId, Series.streamId, Games.streamId, Friends.streamId,
    /// Catalogs.streamId, ContentBlocks.streamId) rather than referencing those
    /// modules directly, so EventStore.fs (and this lookup) stays decoupled from
    /// domain BCs — this is admin-console-only knowledge of the naming
    /// convention. Keep in sync if a BC's streamId prefix ever changes.
    let boundedContextPrefixes = [
        "Movies", "Movie-"
        "Series", "Series-"
        "Games", "Game-"
        "Friends", "Friend-"
        "Catalogs", "Catalog-"
        "ContentBlocks", "ContentBlocks-"
    ]

    let private prefixForBoundedContext (bc: string option) : string option =
        bc |> Option.bind (fun name -> boundedContextPrefixes |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd)

    /// The inverse direction of `prefixForBoundedContext`: which known prefix
    /// (if any) a concrete `streamId` starts with. Same
    /// `boundedContextPrefixes` registry, same "admin-console-only knowledge
    /// of a BC's stream-id naming convention" as everywhere else this list
    /// is consulted (`EventFormatting.formatEvent`'s own `if/elif StartsWith`
    /// chain is the domain-formatting sibling of this lookup).
    let private prefixForStreamId (streamId: string) : string option =
        boundedContextPrefixes
        |> List.tryFind (fun (_, prefix) -> streamId.StartsWith(prefix))
        |> Option.map snd

    /// Compensating-event composer (administration-xjmda, ADR-0032): one
    /// BC-agnostic codec per bounded context, each wrapping that BC's own
    /// PUBLIC `Serialization.serialize`/`deserialize` seam (no reflection
    /// over event DUs — the wire format can diverge from the DU shape, e.g.
    /// `Games.Serialization`'s `Game_status_changed` encodes `GameStatus` as
    /// a nested string via `encodeGameStatus`, not the DU's own shape; see
    /// `EventFormatting.fs`'s `formatGameEvent` for why reflection would be
    /// dishonest about that). Composing `deserialize eventType data |>
    /// Option.map serialize` is simultaneously the validation gate (a
    /// payload that doesn't parse yields `None`) and the canonicalization
    /// step (the re-serialized form is what actually gets appended) — see
    /// `canonicalizeCompensatingEvent` below. Prefix strings are the SAME
    /// literal strings as `boundedContextPrefixes` — keep in sync if a BC's
    /// stream-id prefix ever changes (same convention `projectionTables`
    /// documents).
    let private eventCodecs : (string * (string -> string -> (string * string) option)) list = [
        "Movie-", fun eventType data -> Movies.Serialization.deserialize eventType data |> Option.map Movies.Serialization.serialize
        "Series-", fun eventType data -> Series.Serialization.deserialize eventType data |> Option.map Series.Serialization.serialize
        "Game-", fun eventType data -> Games.Serialization.deserialize eventType data |> Option.map Games.Serialization.serialize
        "Friend-", fun eventType data -> Friends.Serialization.deserialize eventType data |> Option.map Friends.Serialization.serialize
        "Catalog-", fun eventType data -> Catalogs.Serialization.deserialize eventType data |> Option.map Catalogs.Serialization.serialize
        "ContentBlocks-", fun eventType data -> ContentBlocks.Serialization.deserialize eventType data |> Option.map ContentBlocks.Serialization.serialize
    ]

    /// Round-trip validate + canonicalize `rawData` as an instance of
    /// `eventType`, dispatched to the right BC's codec by `streamId`'s
    /// prefix (mirrors `EventFormatting.formatEvent`'s dispatch idiom).
    /// `None` when the prefix matches no known bounded context, or when
    /// that BC's `deserialize` refuses the payload — either way the caller
    /// must refuse to append, never fall back to storing the raw edit.
    let private canonicalizeCompensatingEvent (streamId: string) (eventType: string) (rawData: string) : (string * string) option =
        eventCodecs
        |> List.tryFind (fun (prefix, _) -> streamId.StartsWith(prefix))
        |> Option.bind (fun (_, codec) -> codec eventType rawData)

    /// Commits a compensating event (administration-xjmda, ADR-0032): the
    /// idiomatic event-sourcing fix for bad data is appending a corrective
    /// event, not mutating history (ADR-0002). Mirrors `Api.fs`'s
    /// `executeCommandCore` idiom — expected-position append via
    /// `EventStore.appendToStream` (never the explicit-rowid path importNdjson
    /// uses), then catch-up over every registered `projectionHandlers` entry
    /// — but re-validates `rawData` here too (independent of any earlier
    /// `previewCompensatingEvent` call) so this function alone guarantees the
    /// "never stores an unparseable payload" invariant regardless of caller.
    /// `expectedPosition` is caller-supplied rather than freshly read here:
    /// it is the position an earlier preview observed, so a stale value
    /// correctly surfaces as `EventStore.ConcurrencyConflict` if another
    /// append landed on this stream since. administration-mz6kp (ADR-0033):
    /// `conn` is now a per-request connection opened by the caller via the
    /// shared factory — no other in-flight request shares this connection
    /// object, so the process-wide `dbLock` ADR-0030 threaded through here is
    /// retired along with the shared connection it guarded.
    let private appendCompensatingEventCore
        (conn: SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        (streamId: string)
        (eventType: string)
        (rawData: string)
        (expectedPosition: int64)
        : Result<unit, string> =
        match canonicalizeCompensatingEvent streamId eventType rawData with
        | None ->
            Error (sprintf "Payload does not deserialize as a valid '%s' event - refusing to append" eventType)
        | Some (canonicalEventType, canonicalData) ->
            let eventData : EventStore.EventData = {
                EventType = canonicalEventType
                Data = canonicalData
                Metadata = "{\"source\":\"admin-console\"}"
            }
            match EventStore.appendToStream conn streamId expectedPosition [ eventData ] with
            | EventStore.ConcurrencyConflict(expected, actual) ->
                Error (sprintf "Concurrency conflict: expected stream position %d but it is now %d - reload and retry" expected actual)
            | EventStore.Success _ ->
                for handler in projectionHandlers do
                    Projection.runProjection conn handler
                Ok ()

    /// Bounded-context name -> the hand-maintained `handledEventTypes` list
    /// mirroring that BC's `Serialization.deserialize` match arms
    /// (administration-gxd6e). Same admin-console-only-knowledge shape as
    /// `boundedContextPrefixes` above — kept as a separate registry since a
    /// BC's set of handled event types is a different fact than its stream
    /// prefix.
    let private handledEventTypesByBoundedContext = [
        "Movies", Movies.Serialization.handledEventTypes
        "Series", Series.Serialization.handledEventTypes
        "Games", Games.Serialization.handledEventTypes
        "Friends", Friends.Serialization.handledEventTypes
        "Catalogs", Catalogs.Serialization.handledEventTypes
        "ContentBlocks", ContentBlocks.Serialization.handledEventTypes
    ]

    /// True if `eventType` is a known match-arm string for `bcName`'s
    /// deserializer. False for an unrecognized `bcName` (shouldn't happen —
    /// callers only pass names already resolved via `boundedContextPrefixes`).
    let private isHandledByBoundedContext (bcName: string) (eventType: string) : bool =
        handledEventTypesByBoundedContext
        |> List.tryFind (fun (name, _) -> name = bcName)
        |> Option.map (fun (_, types) -> List.contains eventType types)
        |> Option.defaultValue false

    /// The Health tab's unknown-event report (administration-gxd6e): two
    /// independent checks per distinct `(eventType, count)` from
    /// `EventStore.getEventCountsByType` (already an index-only scan, no new
    /// query cost per ADR-0021) —
    ///   - unhandled: the type's owning BC (resolved via stream prefix on one
    ///     sample event) doesn't list it in `handledEventTypesByBoundedContext`,
    ///     or its prefix matches no known BC at all.
    ///   - unformattable: that same sample event, run through
    ///     `EventFormatting.formatEvent`, returns None.
    /// A type can land in neither, either, or both lists — they are not
    /// aliases of each other (a type can be handled by its BC's deserializer
    /// yet still have no formatter case, or vice versa).
    let private buildUnknownEventReport (conn: SqliteConnection) (typeCounts: (string * int) list) : UnknownEventTypeRow list * UnknownEventTypeRow list =
        let unhandled = ResizeArray()
        let unformattable = ResizeArray()
        for (eventType, count) in typeCounts do
            match EventStore.getSampleEventForType conn eventType with
            | None -> ()
            | Some sample ->
                let row = { EventType = eventType; Count = count; SampleData = sample.Data }
                let owningBc =
                    boundedContextPrefixes
                    |> List.tryFind (fun (_, prefix) -> sample.StreamId.StartsWith(prefix))
                    |> Option.map fst
                let isHandled =
                    match owningBc with
                    | None -> false
                    | Some bc -> isHandledByBoundedContext bc eventType
                if not isHandled then unhandled.Add(row)
                if EventFormatting.formatEvent sample |> Option.isNone then unformattable.Add(row)
        List.ofSeq unhandled, List.ofSeq unformattable

    let private toEventDto (e: EventStore.StoredEvent) : Mediatheca.Shared.EventDto =
        { GlobalPosition = e.GlobalPosition
          StreamId = e.StreamId
          StreamPosition = e.StreamPosition
          EventType = e.EventType
          Data = e.Data
          Timestamp = e.Timestamp.ToString("o") }

    let private optToString (v: 'a option) : string =
        v |> Option.map string |> Option.defaultValue "-"

    /// Stream drill-in's "current state" panel (administration-v4y9g): dispatch
    /// on stream_id prefix to the matching per-BC projection's getBySlug, and
    /// flatten its typed detail DTO into loose display fields. Only the five
    /// BCs the task calls out (Movie/Series/Game/Friend/Catalog) get a row —
    /// other stream kinds (e.g. ContentBlocks-) simply have no projection panel.
    let private projectionRowFor (conn: SqliteConnection) (streamId: string) : Mediatheca.Shared.ProjectionStateRow option =
        if streamId.StartsWith("Movie-") then
            let slug = streamId.Substring("Movie-".Length)
            MovieProjection.getBySlug conn slug
            |> Option.map (fun m ->
                { Mediatheca.Shared.ProjectionStateRow.Kind = "Movie"
                  Fields = [
                      "Name", m.Name
                      "Year", string m.Year
                      "Genres", String.concat ", " m.Genres
                      "Personal rating", optToString m.PersonalRating
                      "TMDB rating", optToString m.TmdbRating
                      "In focus", string m.InFocus
                  ]
                  DetailLink = Some ("movies", slug) })
        elif streamId.StartsWith("Series-") then
            let slug = streamId.Substring("Series-".Length)
            SeriesProjection.getBySlug conn slug None
            |> Option.map (fun s ->
                { Mediatheca.Shared.ProjectionStateRow.Kind = "Series"
                  Fields = [
                      "Name", s.Name
                      "Year", string s.Year
                      "Status", string s.Status
                      "Personal rating", optToString s.PersonalRating
                      "Abandoned", string s.IsAbandoned
                      "In focus", string s.InFocus
                  ]
                  DetailLink = Some ("series", slug) })
        elif streamId.StartsWith("Game-") then
            let slug = streamId.Substring("Game-".Length)
            GameProjection.getBySlug conn slug
            |> Option.map (fun g ->
                { Mediatheca.Shared.ProjectionStateRow.Kind = "Game"
                  Fields = [
                      "Name", g.Name
                      "Year", string g.Year
                      "Status", string g.Status
                      "Personal rating", optToString g.PersonalRating
                      "Total play time (min)", string g.TotalPlayTimeMinutes
                  ]
                  DetailLink = Some ("games", slug) })
        elif streamId.StartsWith("Friend-") then
            let slug = streamId.Substring("Friend-".Length)
            FriendProjection.getBySlug conn slug
            |> Option.map (fun f ->
                { Mediatheca.Shared.ProjectionStateRow.Kind = "Friend"
                  Fields = [
                      "Name", f.Name
                      "Has image", string f.ImageRef.IsSome
                  ]
                  DetailLink = Some ("friends", slug) })
        elif streamId.StartsWith("Catalog-") then
            let slug = streamId.Substring("Catalog-".Length)
            CatalogProjection.getBySlug conn slug
            |> Option.map (fun c ->
                { Mediatheca.Shared.ProjectionStateRow.Kind = "Catalog"
                  Fields = [
                      "Name", c.Name
                      "Description", c.Description
                      "Sorted", string c.IsSorted
                      "Entry count", string (List.length c.Entries)
                  ]
                  DetailLink = Some ("catalogs", slug) })
        else
            None

    let private toTimelineEntry (e: EventStore.StoredEvent) : Mediatheca.Shared.StreamTimelineEntry =
        let formatted = EventFormatting.formatEvent e
        { GlobalPosition = e.GlobalPosition
          StreamPosition = e.StreamPosition
          EventType = e.EventType
          Timestamp = e.Timestamp.ToString("o")
          Data = e.Data
          Metadata = e.Metadata
          FormattedLabel = formatted |> Option.map (fun f -> f.Label)
          FormattedDetails = formatted |> Option.map (fun f -> f.Details) |> Option.defaultValue []
          CrossLinks =
            EventFormatting.crossLinksFromPayload e.Data
            |> List.map (fun (kind, target) -> { Mediatheca.Shared.StreamCrossLink.Kind = kind; TargetStreamId = target }) }

    /// File size in bytes, or 0 if the file doesn't exist (e.g. no WAL
    /// sidecar currently checkpointed out, or an in-memory test database
    /// with no backing file at all).
    let private fileSizeOrZero (path: string) : int64 =
        if File.Exists(path) then FileInfo(path).Length else 0L

    /// Total size and file count of a directory, walked recursively (the
    /// image cache stores files under per-media subdirectories). 0, 0 if the
    /// directory doesn't exist yet.
    let private directoryStats (path: string) : int64 * int =
        if not (Directory.Exists(path)) then
            0L, 0
        else
            let files = Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            let totalBytes = files |> Array.sumBy (fun f -> FileInfo(f).Length)
            totalBytes, files.Length

    /// Health tab's health-stats query (administration-hw74a). Builds one
    /// aggregate DTO from a handful of cheap, index-backed scans — see
    /// EventStore.getEventCountsByStream/getEventCountsByType/
    /// getDailyEventCounts doc comments and ADR-0021 for the cost reasoning.
    let private buildHealthStats (conn: SqliteConnection) (dbPath: string) (imagesDir: string) : HealthStats =
        let totalEvents = EventStore.getTotalEventCount conn
        let streamCounts = EventStore.getEventCountsByStream conn
        let typeCounts = EventStore.getEventCountsByType conn

        let bcCounts =
            boundedContextPrefixes
            |> List.map (fun (name, prefix) ->
                let count =
                    streamCounts
                    |> List.filter (fun (streamId, _) -> streamId.StartsWith(prefix))
                    |> List.sumBy snd
                { BoundedContext = name; Count = count })
        let otherCount = totalEvents - (bcCounts |> List.sumBy (fun c -> c.Count))
        let bcCounts =
            if otherCount > 0 then bcCounts @ [ { BoundedContext = "Other"; Count = otherCount } ]
            else bcCounts

        let topStreams =
            streamCounts
            |> List.sortByDescending snd
            |> List.truncate 10
            |> List.map (fun (streamId, count) -> { StreamId = streamId; Count = count })

        let topEventTypes =
            typeCounts
            |> List.sortByDescending snd
            |> List.truncate 10
            |> List.map (fun (eventType, count) -> { EventType = eventType; Count = count })

        // Zero-filled 90-day window, oldest first, so the sparkline gets an
        // even series regardless of which days actually had activity.
        let today = DateTime.UtcNow.Date
        let windowStart = today.AddDays(-89.0)
        let sinceIso = windowStart.ToString("yyyy-MM-dd") + "T00:00:00.0000000+00:00"
        let dailyRows = EventStore.getDailyEventCounts conn sinceIso |> Map.ofList
        let dailyCounts =
            [ for offset in 0 .. 89 ->
                let day = windowStart.AddDays(float offset)
                let key = day.ToString("yyyy-MM-dd")
                { Date = key; Count = dailyRows |> Map.tryFind key |> Option.defaultValue 0 } ]

        let imagesBytes, imagesFileCount = directoryStats imagesDir

        let unhandledEventTypes, unformattableEventTypes = buildUnknownEventReport conn typeCounts

        {
            TotalEventCount = totalEvents
            BoundedContextCounts = bcCounts
            DailyCounts = dailyCounts
            TopStreams = topStreams
            DistinctEventTypeCount = List.length typeCounts
            TopEventTypes = topEventTypes
            Storage = {
                DbSizeBytes = fileSizeOrZero dbPath
                WalSizeBytes = fileSizeOrZero (dbPath + "-wal")
                ImagesSizeBytes = imagesBytes
                ImagesFileCount = imagesFileCount
            }
            UnhandledEventTypes = unhandledEventTypes
            UnformattableEventTypes = unformattableEventTypes
        }

    // ── Projection dashboard (administration-qjcp4) ──

    /// Table(s) each projection owns, for the dashboard's per-table row
    /// counts. Admin-console-only knowledge of each projection's schema —
    /// same pattern as boundedContextPrefixes above. Keep in sync if a
    /// projection's owned tables ever change.
    let private projectionTables = [
        "MovieProjection", [ "movie_list"; "movie_detail"; "watch_sessions" ]
        "FriendProjection", [ "friend_list" ]
        "ContentBlockProjection", [ "content_blocks" ]
        "CatalogProjection", [ "catalog_list"; "catalog_entries" ]
        "SeriesProjection", [ "series_list"; "series_detail"; "series_seasons"; "series_episodes"; "series_rewatch_sessions"; "series_episode_progress" ]
        "GameProjection", [ "game_list"; "game_detail" ]
    ]

    let private tableRowCount (conn: SqliteConnection) (tableName: string) : int =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- $"SELECT COUNT(*) FROM {tableName}"
        cmd.ExecuteScalar() :?> int64 |> int

    /// True if `tableName` exists in this connection's schema. Guards the
    /// image-ref registry queries below: `cast_members` (CastStore.fs) and
    /// `game_journal_blocks` (GameJournal.fs) are imperative tables, not
    /// registered in `projectionTables`/`projectionHandlers`, and aren't
    /// guaranteed present in minimal/test fixtures.
    let private tableExists (conn: SqliteConnection) (tableName: string) : bool =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name"
        cmd.Parameters.AddWithValue("@name", tableName) |> ignore
        use reader = cmd.ExecuteReader()
        reader.Read()

    let private checkpointLag (conn: SqliteConnection) (head: int64) (handler: Projection.ProjectionHandler) : int64 =
        let position, _ = Projection.getCheckpointInfo conn handler.Name
        max 0L (head - position)

    /// Per-instance single-flight guards for the two projection-guard
    /// families (rebuild + drift-check). Constructed once at the
    /// composition root and threaded explicitly to every consumer
    /// (ADR-0035) rather than held as module-level ambient state shared by
    /// the whole process regardless of which caller is asking — that shape
    /// is invisible in a server process (there's only ever one), but in the
    /// test assembly, where Expecto runs test cases across files in
    /// parallel, module-level singletons collide across unrelated test
    /// files that happen to reuse the same projection name. See
    /// `makeJobRunRecorder` below for the job-guard half, which has a
    /// different natural owner (the recorder's own closure) and so isn't
    /// folded into this record.
    type AdminGuards = {
        RebuildingProjections: System.Collections.Concurrent.ConcurrentDictionary<string, unit>
        DriftCheckInProgress: System.Collections.Concurrent.ConcurrentDictionary<string, unit>
    }

    /// Builds one fresh, independently-owned `AdminGuards`. The composition
    /// root calls this exactly once and passes the same value to `create`,
    /// `projectionRebuildStreamHandler`, and `driftCheckStreamHandler`, so
    /// "one guard per process" is a property of the wiring rather than of
    /// this module.
    let makeGuards () : AdminGuards =
        { RebuildingProjections = System.Collections.Concurrent.ConcurrentDictionary<string, unit>()
          DriftCheckInProgress = System.Collections.Concurrent.ConcurrentDictionary<string, unit>() }

    let private buildProjectionStats (conn: SqliteConnection) (projectionHandlers: Projection.ProjectionHandler list) (guards: AdminGuards) : ProjectionStatRow list =
        let head = EventStore.getMaxGlobalPosition conn
        projectionHandlers
        |> List.map (fun handler ->
            let position, updatedAt = Projection.getCheckpointInfo conn handler.Name
            let tables =
                projectionTables
                |> List.tryFind (fun (name, _) -> name = handler.Name)
                |> Option.map snd
                |> Option.defaultValue []
            { Name = handler.Name
              CheckpointPosition = position
              Lag = checkpointLag conn head handler
              UpdatedAt = updatedAt
              TableCounts = tables |> List.map (fun t -> { TableName = t; RowCount = tableRowCount conn t })
              IsRebuilding = guards.RebuildingProjections.ContainsKey(handler.Name) })

    // ── Image cache admin (administration-xx3mw) ──

    /// The not-dirty guard (ADR-0025): names of the six checkpoint-tracked
    /// projections that are either mid-rebuild or lagging behind the store
    /// head. Empty = clean, safe to trust the projection tables as the live
    /// ref set. `cast_members`/`game_journal_blocks` are imperative writes
    /// (CastStore.fs/GameJournal.fs) — never rebuilt, never lag — so they
    /// need no gating here.
    let isAnyProjectionDirty (conn: SqliteConnection) (projectionHandlers: Projection.ProjectionHandler list) (guards: AdminGuards) : string list =
        let head = EventStore.getMaxGlobalPosition conn
        projectionHandlers
        |> List.filter (fun handler ->
            guards.RebuildingProjections.ContainsKey(handler.Name) || checkpointLag conn head handler > 0L)
        |> List.map (fun handler -> handler.Name)

    // ── Shadow-table replay drift detector (administration-btvqa, ADR-0031) ──

    /// The drift-check single-flight guard lives on `AdminGuards.DriftCheckInProgress`
    /// — NOT `RebuildingProjections`, whose meaning ("live tables are being
    /// written") is never true here (the live connection is only ever read
    /// from). Same TryAdd/TryRemove shape, keyed on a single fixed name since
    /// the whole check (all six projections) runs as one operation, not one
    /// per projection.
    let private driftCheckKey = "drift-check"

    let private escapeJson (s: string) = s.Replace("\\", "\\\\").Replace("\"", "\\\"")

    /// Not private — `checkProjectionDrift` below is the direct test seam
    /// (ProjectionDriftTests.fs), the same "test the underlying function, not
    /// the SSE route" shape `ProjectionRebuildTests.fs` established for
    /// `rebuildProjectionWithProgress`.
    type DriftDiscrepancy = {
        Table: string
        PrimaryKey: string
        Kind: string // "onlyInLive" | "onlyInShadow" | "columnMismatch"
        Columns: string list
    }

    type ProjectionDrift = {
        Name: string
        Discrepancies: DriftDiscrepancy list
    }

    /// Primary-key columns (in declared PK order) and all other columns for
    /// `table`, read from SQLite's own `PRAGMA table_info` rather than a
    /// hand-maintained PK registry alongside each `*Projection.fs`'s own
    /// `CREATE TABLE ... PRIMARY KEY (...)` declaration — the same schema
    /// info, without duplicating it.
    let private tableColumnInfo (conn: SqliteConnection) (table: string) : string list * string list =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- sprintf "PRAGMA table_info(%s)" table
        use reader = cmd.ExecuteReader()
        let rows =
            [ while reader.Read() do
                yield reader.GetString(reader.GetOrdinal("name")), reader.GetInt32(reader.GetOrdinal("pk")) ]
        let pkCols = rows |> List.filter (fun (_, pk) -> pk > 0) |> List.sortBy snd |> List.map fst
        let otherCols = rows |> List.filter (fun (_, pk) -> pk = 0) |> List.map fst
        pkCols, otherCols

    /// Every row of `table`, keyed by its primary-key tuple (joined into one
    /// string with a separator that can't appear in a column value) to a
    /// human-readable display string plus a map of non-PK column -> value
    /// (`None` = SQL NULL).
    let private readRows (conn: SqliteConnection) (table: string) (pkCols: string list) (otherCols: string list) : Map<string, string * Map<string, string option>> =
        let allCols = pkCols @ otherCols
        use cmd = conn.CreateCommand()
        cmd.CommandText <- sprintf "SELECT %s FROM %s" (String.concat ", " allCols) table
        use reader = cmd.ExecuteReader()
        let mutable result = Map.empty
        while reader.Read() do
            let valueOf (col: string) : string option =
                let idx = reader.GetOrdinal(col)
                if reader.IsDBNull(idx) then None
                else Some (System.Convert.ToString(reader.GetValue(idx), System.Globalization.CultureInfo.InvariantCulture))
            let keyParts = pkCols |> List.map (fun c -> valueOf c |> Option.defaultValue "")
            let key = String.concat "" keyParts
            let display = (pkCols, keyParts) ||> List.map2 (fun c v -> sprintf "%s=%s" c v) |> String.concat ", "
            let cols = otherCols |> List.map (fun c -> c, valueOf c) |> Map.ofList
            result <- Map.add key (display, cols) result
        result

    /// Rows-only-in-live, rows-only-in-shadow, and same-key-differing-column
    /// discrepancies for one table, comparing `liveConn` (unmodified — read
    /// only) against `shadowConn` (the freshly-replayed shadow copy).
    let private diffTable (liveConn: SqliteConnection) (shadowConn: SqliteConnection) (table: string) : DriftDiscrepancy list =
        if not (tableExists liveConn table) || not (tableExists shadowConn table) then
            []
        else
            let pkCols, otherCols = tableColumnInfo liveConn table
            let liveRows = readRows liveConn table pkCols otherCols
            let shadowRows = readRows shadowConn table pkCols otherCols
            let allKeys = Set.union (liveRows |> Map.toSeq |> Seq.map fst |> Set.ofSeq) (shadowRows |> Map.toSeq |> Seq.map fst |> Set.ofSeq)
            [ for key in allKeys do
                match Map.tryFind key liveRows, Map.tryFind key shadowRows with
                | Some (display, _), None ->
                    yield { Table = table; PrimaryKey = display; Kind = "onlyInLive"; Columns = [] }
                | None, Some (display, _) ->
                    yield { Table = table; PrimaryKey = display; Kind = "onlyInShadow"; Columns = [] }
                | Some (display, liveCols), Some (_, shadowCols) ->
                    let differingColumns =
                        otherCols
                        |> List.filter (fun c -> Map.tryFind c liveCols <> Map.tryFind c shadowCols)
                    if not (List.isEmpty differingColumns) then
                        yield { Table = table; PrimaryKey = display; Kind = "columnMismatch"; Columns = differingColumns }
                | None, None -> () ]

    let private discrepancyJson (d: DriftDiscrepancy) =
        let columnsJson = d.Columns |> List.map (fun c -> sprintf "\"%s\"" (escapeJson c)) |> String.concat ","
        sprintf "{\"table\":\"%s\",\"primaryKey\":\"%s\",\"kind\":\"%s\",\"columns\":[%s]}"
            (escapeJson d.Table) (escapeJson d.PrimaryKey) d.Kind columnsJson

    let private projectionDriftJson (p: ProjectionDrift) =
        let discJson = p.Discrepancies |> List.map discrepancyJson |> String.concat ","
        sprintf "{\"name\":\"%s\",\"discrepancies\":[%s]}" (escapeJson p.Name) discJson

    /// The drift check itself: for every handler, in registration order,
    /// `Projection.replayIntoShadow` drop+inits+replays the FULL live event
    /// log into the shadow connection (load-bearing order — FriendProjection's
    /// Friend_removed case scrubs movie_detail/watch_sessions and needs those
    /// tables to already exist, same as live catch-up's own registration
    /// order). ALL handlers finish replaying before any diffing starts — a
    /// cross-projection write (Friend_removed scrubbing movie_detail) must be
    /// allowed to land before movie_detail is compared, or MovieProjection
    /// (which replays first) would be diffed against its own not-yet-scrubbed
    /// shadow state and report false drift. Diffing then walks each
    /// projection's owned tables (`projectionTables`) between `liveConn`
    /// (read-only) and `shadowConn`.
    let checkProjectionDrift (liveConn: SqliteConnection) (shadowConn: SqliteConnection) (projectionHandlers: Projection.ProjectionHandler list) (onProgress: string -> unit) : ProjectionDrift list =
        for handler in projectionHandlers do
            Projection.replayIntoShadow liveConn shadowConn handler
            onProgress handler.Name

        projectionHandlers
        |> List.map (fun handler ->
            let tables =
                projectionTables
                |> List.tryFind (fun (name, _) -> name = handler.Name)
                |> Option.map snd
                |> Option.defaultValue []
            let discrepancies = tables |> List.collect (diffTable liveConn shadowConn)
            { Name = handler.Name; Discrepancies = discrepancies })

    /// Operator-facing rejection reason (ADR-0025's not-dirty guard) — names
    /// every dirty projection so an operator can tell which rebuild/catch-up
    /// to wait for, rather than a generic "try again later". Exposed (not
    /// inlined into the SSE handler) so its wording is directly unit-testable
    /// without an HttpContext, same test-the-underlying-function shape
    /// `ProjectionRebuildTests.fs` established.
    let driftCheckRejectionMessage (dirtyProjections: string list) : string =
        let names = String.concat ", " dirtyProjections
        let verb = if List.length dirtyProjections = 1 then "is" else "are"
        sprintf "Refused: %s %s dirty (mid-rebuild or lagging) - shadow-at-head vs. live-behind-head would report false drift" names verb

    /// The Projections tab's "Run check" command: a Giraffe SSE route
    /// mirroring `projectionRebuildStreamHandler`'s `progress`/`complete`/
    /// `rejected` framing (ADR-0024), gated by the not-dirty guard (ADR-0025)
    /// since shadow-at-head vs. live-behind-head would report false drift.
    /// Never touches a shared `conn` — the live connection is opened once,
    /// scoped to this handler's whole run (administration-mz6kp, ADR-0033),
    /// and the shadow copy lives entirely in its own throwaway `:memory:`
    /// connection (ADR-0031), so the live tables are provably read-only for
    /// the whole run.
    let driftCheckStreamHandler (factory: unit -> SqliteConnection) (projectionHandlers: Projection.ProjectionHandler list) (guards: AdminGuards) : HttpHandler =
        fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
            task {
                use conn = factory ()
                ctx.Response.Headers.["Content-Type"] <- Microsoft.Extensions.Primitives.StringValues("text/event-stream")
                ctx.Response.Headers.["Cache-Control"] <- Microsoft.Extensions.Primitives.StringValues("no-cache")
                ctx.Response.Headers.["Connection"] <- Microsoft.Extensions.Primitives.StringValues("keep-alive")

                let writer = ctx.Response

                let writeEvent (eventType: string) (json: string) = task {
                    let line = Sse.sseFrame eventType json
                    let bytes = System.Text.Encoding.UTF8.GetBytes(line)
                    do! writer.Body.WriteAsync(bytes, 0, bytes.Length)
                    do! writer.Body.FlushAsync()
                }

                let dirty = isAnyProjectionDirty conn projectionHandlers guards
                if not (List.isEmpty dirty) then
                    do! writeEvent "rejected" (sprintf "{\"message\":\"%s\"}" (escapeJson (driftCheckRejectionMessage dirty)))
                elif not (guards.DriftCheckInProgress.TryAdd(driftCheckKey, ())) then
                    do! writeEvent "rejected" "{\"message\":\"A drift check is already running\"}"
                else
                    try
                        try
                            use shadowConn = new SqliteConnection("Data Source=:memory:")
                            shadowConn.Open()
                            let emit (name: string) =
                                writeEvent "progress" (sprintf "{\"projection\":\"%s\"}" (escapeJson name))
                                |> Async.AwaitTask |> Async.RunSynchronously
                            let results = checkProjectionDrift conn shadowConn projectionHandlers emit
                            let total = results |> List.sumBy (fun p -> List.length p.Discrepancies)
                            let projectionsJson = results |> List.map projectionDriftJson |> String.concat ","
                            do! writeEvent "complete" (sprintf "{\"projections\":[%s],\"totalDiscrepancies\":%d}" projectionsJson total)
                        with ex ->
                            do! writeEvent "error" (sprintf "{\"message\":\"%s\"}" (escapeJson ex.Message))
                    finally
                        guards.DriftCheckInProgress.TryRemove(driftCheckKey) |> ignore

                return! earlyReturn ctx
            }

    /// The fifteen typed ref-bearing `(table, column)` pairs, verified by
    /// reading every projection's INSERT/SELECT statements (ADR-0025) — no
    /// markdown-body scanning needed. LOAD-BEARING: a missed or stale entry
    /// here silently under-counts live refs, which risks a purge deleting a
    /// still-referenced image, not merely mis-reporting a count. Keep this in
    /// lockstep with any ref-bearing column added or renamed anywhere in the
    /// codebase; `imageRefColumnsCoverageTest`-style tests should exercise
    /// every entry.
    let imageRefColumns : (string * string) list = [
        "movie_list", "poster_ref"
        "movie_detail", "poster_ref"
        "movie_detail", "backdrop_ref"
        "series_list", "poster_ref"
        "series_detail", "poster_ref"
        "series_detail", "backdrop_ref"
        "series_seasons", "poster_ref"
        "series_episodes", "still_ref"
        "game_list", "cover_ref"
        "game_detail", "cover_ref"
        "game_detail", "backdrop_ref"
        "friend_list", "image_ref"
        "content_blocks", "image_ref"
        "game_journal_blocks", "image_ref"
        "cast_members", "image_ref"
    ]

    /// Every non-null, non-empty ref value currently held by any
    /// `imageRefColumns` table, as one flat set — the "live" side of the
    /// orphan diff. Missing tables (guarded by `tableExists`) contribute no
    /// refs rather than erroring, since minimal/test fixtures may not have
    /// initialized `cast_members`/`game_journal_blocks`.
    let private getReferencedImageRefs (conn: SqliteConnection) : Set<string> =
        imageRefColumns
        |> List.collect (fun (table, column) ->
            if not (tableExists conn table) then
                []
            else
                use cmd = conn.CreateCommand()
                cmd.CommandText <- $"SELECT DISTINCT {column} FROM {table} WHERE {column} IS NOT NULL AND {column} <> ''"
                use reader = cmd.ExecuteReader()
                [ while reader.Read() do yield reader.GetString(0) ])
        |> Set.ofList

    /// Normalizes a file system path to the same `/`-separated, case-
    /// sensitive form the ref columns store, so a Windows-returned `\`-
    /// separated path still matches its ref (ADR-0025: ordinal comparison,
    /// never case-folded — case-folding would mask a genuine mismatch on the
    /// case-sensitive Linux deploy target).
    let private relativePathOf (imagesDir: string) (fullPath: string) : string =
        Path.GetRelativePath(imagesDir, fullPath).Replace('\\', '/')

    /// First path segment, or "(root)" for a stray loose file directly under
    /// images/ with no subfolder.
    let private subfolderOf (relativePath: string) : string =
        let idx = relativePath.IndexOf('/')
        if idx < 0 then "(root)" else relativePath.Substring(0, idx)

    /// (relativePath, subfolder, sizeBytes) for every file under imagesDir,
    /// walked recursively (same call `directoryStats` uses above). Empty
    /// list if the directory doesn't exist yet.
    let private walkImageFiles (imagesDir: string) : (string * string * int64) list =
        if not (Directory.Exists(imagesDir)) then
            []
        else
            Directory.GetFiles(imagesDir, "*", SearchOption.AllDirectories)
            |> Array.map (fun f ->
                let rel = relativePathOf imagesDir f
                rel, subfolderOf rel, FileInfo(f).Length)
            |> Array.toList

    /// Total size/count plus a per-subfolder breakdown — always available,
    /// no not-dirty guard needed (pure disk footprint, no projection trust
    /// involved).
    let private buildImageCacheStats (imagesDir: string) : ImageCacheStats =
        let files = walkImageFiles imagesDir
        let subfolders =
            files
            |> List.groupBy (fun (_, sub, _) -> sub)
            |> List.map (fun (sub, items) ->
                { Subfolder = sub
                  FileCount = List.length items
                  SizeBytes = items |> List.sumBy (fun (_, _, size) -> size) })
            |> List.sortBy (fun s -> s.Subfolder)
        { TotalBytes = files |> List.sumBy (fun (_, _, size) -> size)
          TotalFileCount = List.length files
          Subfolders = subfolders }

    /// Files on disk whose relative path is absent from `referencedRefs` —
    /// the orphan set, plus its total byte size.
    let private computeOrphans (imagesDir: string) (referencedRefs: Set<string>) : OrphanImage list * int64 =
        let orphans =
            walkImageFiles imagesDir
            |> List.filter (fun (rel, _, _) -> not (Set.contains rel referencedRefs))
            |> List.map (fun (rel, sub, size) -> { RelativePath = rel; Subfolder = sub; SizeBytes = size })
        orphans, orphans |> List.sumBy (fun o -> o.SizeBytes)

    // ── Event log export/import (administration-vrc56, ADR-0029) ──

    /// Kestrel refuses synchronous reads/writes on the request/response body
    /// by default (`InvalidOperationException: Synchronous operations are
    /// disallowed`). `EventStore.exportNdjson`/`importNdjson` are
    /// deliberately synchronous over a plain `TextWriter`/`TextReader` — the
    /// pinned interface that keeps the round-trip logic plain-Expecto
    /// testable with no HTTP pipeline — so the Giraffe wrapper opts back
    /// into synchronous I/O for this one request via Kestrel's own escape
    /// hatch rather than making the storage-layer functions async. This
    /// still streams (no full in-memory buffering): it only relaxes
    /// Kestrel's *thread-starvation* guard against blocking sync calls, it
    /// doesn't materialize the body.
    let private allowSynchronousIO (ctx: Microsoft.AspNetCore.Http.HttpContext) =
        match ctx.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpBodyControlFeature>() with
        | null -> ()
        | feature -> feature.AllowSynchronousIO <- true

    /// Export the full event log as NDJSON: a plain streamed download, not
    /// SSE — SSE's `data: {...}` framing exists for *progress* reporting
    /// (see `importEventsStreamHandler` below), and would force a second
    /// layer of escaping onto every already-JSON NDJSON line. Wired as a raw
    /// Giraffe route (not Remoting — a streamed file download doesn't fit
    /// the request/response RPC shape). `EventStore.exportNdjson` does the
    /// actual work; this handler is a thin wrapper over `ctx.Response.Body`
    /// so the round-trip logic stays plain-Expecto testable with no HTTP
    /// pipeline (see `EventStoreNdjsonTests.fs`).
    let exportEventsStreamHandler (factory: unit -> SqliteConnection) : HttpHandler =
        fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
            task {
                use conn = factory ()
                allowSynchronousIO ctx
                ctx.Response.Headers.["Content-Type"] <- Microsoft.Extensions.Primitives.StringValues("application/x-ndjson")
                ctx.Response.Headers.["Content-Disposition"] <- Microsoft.Extensions.Primitives.StringValues("attachment; filename=\"mediatheca-events.ndjson\"")
                use writer = new StreamWriter(ctx.Response.Body)
                EventStore.exportNdjson conn writer
                do! writer.FlushAsync()
                return! earlyReturn ctx
            }

    /// Import an NDJSON event log into an empty store: the request body
    /// *is* the NDJSON (no multipart wrapper — one file, no companion
    /// fields), read line-by-line by `EventStore.importNdjson` so the
    /// upload is never buffered whole. Response is SSE progress, the same
    /// envelope `Api.steamFamilyImportHandler` and
    /// `projectionRebuildStreamHandler` use — total line count is unknown
    /// up front, so this is a single outcome event rather than a percentage
    /// bar (there is no separate "start" event: unlike those two precedents,
    /// import here is one atomic transaction with no intermediate progress
    /// to report, and an empty-payload `{}` "start" event would round-trip
    /// through this same handler's `{"type":"...",%s}` template as
    /// `{"type":"start",}` — a trailing comma that breaks `JSON.parse` on
    /// the client). A non-empty target store gets a "rejected" event (same
    /// vocabulary `projectionRebuildStreamHandler` uses for its
    /// concurrent-rebuild guard) before a single line of the body is read —
    /// see `EventStore.importNdjson`'s doc comment for why that ordering
    /// holds. Import that overwrites a non-empty store by wiping first is a
    /// separate, more dangerous operation (administration-n8kqw), out of
    /// scope here.
    // administration-mz6kp (ADR-0033): `conn` is a per-request connection
    // opened by this handler alone from the shared factory — no other
    // in-flight request shares it, so the process-wide `dbLock` ADR-0030
    // threaded through here is retired along with the shared connection it
    // guarded.
    let importEventsStreamHandler (factory: unit -> SqliteConnection) : HttpHandler =
        fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
            task {
                use conn = factory ()
                allowSynchronousIO ctx
                ctx.Response.Headers.["Content-Type"] <- Microsoft.Extensions.Primitives.StringValues("text/event-stream")
                ctx.Response.Headers.["Cache-Control"] <- Microsoft.Extensions.Primitives.StringValues("no-cache")
                ctx.Response.Headers.["Connection"] <- Microsoft.Extensions.Primitives.StringValues("keep-alive")

                let writer = ctx.Response

                let writeEvent (eventType: string) (json: string) = task {
                    let line = Sse.sseFrame eventType json
                    let bytes = System.Text.Encoding.UTF8.GetBytes(line)
                    do! writer.Body.WriteAsync(bytes, 0, bytes.Length)
                    do! writer.Body.FlushAsync()
                }

                use reader = new StreamReader(ctx.Request.Body)
                let importResult = EventStore.importNdjson conn reader
                match importResult with
                | Ok outcome ->
                    do! writeEvent "complete" (sprintf "{\"eventsImported\":%d}" outcome.EventsImported)
                | Error EventStore.StoreNotEmpty ->
                    do! writeEvent "rejected" "{\"message\":\"Target store already has events - import into a non-empty store is a separate operation\"}"
                | Error (EventStore.MalformedLine(lineNumber, message)) ->
                    let escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    do! writeEvent "error" (sprintf "{\"lineNumber\":%d,\"message\":\"%s\"}" lineNumber escaped)

                return! earlyReturn ctx
            }

    /// Rebuild-with-live-progress command (the Projections tab's "Rebuild"
    /// button, administration-qjcp4): drop + replay one projection via
    /// `Projection.rebuildProjectionWithProgress`, streaming SSE progress the
    /// same way `Api.steamFamilyImportHandler` streams Steam Family import
    /// progress. Wired as a raw Giraffe route (not through Remoting — SSE is
    /// a long-lived response, not a request/response RPC). A second request
    /// for a projection already rebuilding gets a "rejected" SSE event
    /// instead of running concurrently, per the task's explicit
    /// single-writer-safety acceptance criterion.
    let projectionRebuildStreamHandler
        (factory: unit -> SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        (guards: AdminGuards)
        : HttpHandler =
        routef "/api/stream/rebuild-projection/%s" (fun projectionName ->
            fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
                task {
                    use conn = factory ()
                    ctx.Response.Headers.["Content-Type"] <- Microsoft.Extensions.Primitives.StringValues("text/event-stream")
                    ctx.Response.Headers.["Cache-Control"] <- Microsoft.Extensions.Primitives.StringValues("no-cache")
                    ctx.Response.Headers.["Connection"] <- Microsoft.Extensions.Primitives.StringValues("keep-alive")

                    let writer = ctx.Response

                    let writeEvent (eventType: string) (json: string) = task {
                        let line = Sse.sseFrame eventType json
                        let bytes = System.Text.Encoding.UTF8.GetBytes(line)
                        do! writer.Body.WriteAsync(bytes, 0, bytes.Length)
                        do! writer.Body.FlushAsync()
                    }

                    match projectionHandlers |> List.tryFind (fun h -> h.Name = projectionName) with
                    | None ->
                        do! writeEvent "error" (sprintf "{\"message\":\"Unknown projection '%s'\"}" projectionName)
                    | Some handler ->
                        if not (guards.RebuildingProjections.TryAdd(projectionName, ())) then
                            do! writeEvent "rejected" (sprintf "{\"message\":\"%s is already rebuilding\"}" projectionName)
                        else
                            try
                                try
                                    let emit (progress: Projection.RebuildProgress) =
                                        let json = sprintf "\"position\":%d,\"head\":%d,\"eventsProcessed\":%d" progress.Position progress.Head progress.EventsProcessed
                                        writeEvent "progress" (sprintf "{%s}" json)
                                        |> Async.AwaitTask |> Async.RunSynchronously
                                    Projection.rebuildProjectionWithProgress conn handler emit
                                    do! writeEvent "complete" "{}"
                                with ex ->
                                    let escaped = ex.Message.Replace("\\", "\\\\").Replace("\"", "\\\"")
                                    do! writeEvent "error" (sprintf "{\"message\":\"%s\"}" escaped)
                            finally
                                guards.RebuildingProjections.TryRemove(projectionName) |> ignore

                    return! earlyReturn ctx
                }
        )

    // ── Job runs console (administration-yamm5, ADR-0026) ──

    let private ensureJobRunsTable (conn: SqliteConnection) : unit =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- """
            CREATE TABLE IF NOT EXISTS job_runs (
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                job_name     TEXT NOT NULL,
                trigger      TEXT NOT NULL,
                status       TEXT NOT NULL,
                summary      TEXT,
                started_at   TEXT NOT NULL,
                finished_at  TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_job_runs_name_started ON job_runs (job_name, started_at DESC);
            """
        cmd.ExecuteNonQuery() |> ignore

    /// Startup-only crash reconciliation (ADR-0026): at process start the
    /// in-memory `runningJobs` guard is definitionally empty, so any row
    /// still `running` is orphaned by a hard crash mid-run — never a live
    /// in-flight run, since a live run always holds the guard. Reconciling on
    /// read instead would be unable to tell those two cases apart, so this
    /// only ever runs once, from `initializeJobRuns` at startup.
    let private reconcileInterruptedRuns (conn: SqliteConnection) : unit =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- """
            UPDATE job_runs
            SET status = 'interrupted',
                finished_at = @now,
                summary = 'Interrupted — server restarted while this run was in progress'
            WHERE status = 'running'
            """
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("o")) |> ignore
        cmd.ExecuteNonQuery() |> ignore

    /// Table creation + startup reconciliation. Called once from
    /// Composition.fs's init sequence, before `ScheduledJobs.startAll`.
    let initializeJobRuns (conn: SqliteConnection) : unit =
        ensureJobRunsTable conn
        reconcileInterruptedRuns conn

    /// administration-tj8n2: `insertRunningRow`/`completeRun`/`failRun` are the
    /// recorder's own DB touches (`BeginRun`/`Complete`/`Fail`) — called from
    /// whichever thread is running a job (the scheduled timer or a manual
    /// "Run now"), possibly two at once (catch-up collision, or same-hour
    /// daily fire). They run on the dedicated job connection (`Composition.fs`
    /// no longer shares `conn` with jobs) and are each individually guarded by
    /// `jobLock`, the same lock the job bodies themselves use for their own DB
    /// sections — `Microsoft.Data.Sqlite.SqliteConnection` is not thread-safe
    /// for concurrent command creation/disposal from multiple threads.
    let private insertRunningRow (jobLock: SemaphoreSlim) (conn: SqliteConnection) (jobName: string) (trigger: string) : int64 =
        jobLock.Wait()
        try
            use insertCmd = conn.CreateCommand()
            insertCmd.CommandText <- """
                INSERT INTO job_runs (job_name, trigger, status, summary, started_at, finished_at)
                VALUES (@jobName, @trigger, 'running', NULL, @startedAt, NULL)
                """
            insertCmd.Parameters.AddWithValue("@jobName", jobName) |> ignore
            insertCmd.Parameters.AddWithValue("@trigger", trigger) |> ignore
            insertCmd.Parameters.AddWithValue("@startedAt", DateTime.UtcNow.ToString("o")) |> ignore
            insertCmd.ExecuteNonQuery() |> ignore

            use idCmd = conn.CreateCommand()
            idCmd.CommandText <- "SELECT last_insert_rowid()"
            idCmd.ExecuteScalar() :?> int64
        finally
            jobLock.Release() |> ignore

    let private dispositionToStatus (disposition: ScheduledJobs.JobDisposition) : string =
        match disposition with
        | ScheduledJobs.JobDisposition.Ok -> "ok"
        | ScheduledJobs.JobDisposition.Skipped -> "skipped"

    let private completeRun (jobLock: SemaphoreSlim) (conn: SqliteConnection) (runId: int64) (disposition: ScheduledJobs.JobDisposition) (summary: string) : unit =
        jobLock.Wait()
        try
            use cmd = conn.CreateCommand()
            cmd.CommandText <- "UPDATE job_runs SET status = @status, summary = @summary, finished_at = @finishedAt WHERE id = @id"
            cmd.Parameters.AddWithValue("@status", dispositionToStatus disposition) |> ignore
            cmd.Parameters.AddWithValue("@summary", summary) |> ignore
            cmd.Parameters.AddWithValue("@finishedAt", DateTime.UtcNow.ToString("o")) |> ignore
            cmd.Parameters.AddWithValue("@id", runId) |> ignore
            cmd.ExecuteNonQuery() |> ignore
        finally
            jobLock.Release() |> ignore

    let private failRun (jobLock: SemaphoreSlim) (conn: SqliteConnection) (runId: int64) (message: string) : unit =
        jobLock.Wait()
        try
            use cmd = conn.CreateCommand()
            cmd.CommandText <- "UPDATE job_runs SET status = 'error', summary = @summary, finished_at = @finishedAt WHERE id = @id"
            cmd.Parameters.AddWithValue("@summary", message) |> ignore
            cmd.Parameters.AddWithValue("@finishedAt", DateTime.UtcNow.ToString("o")) |> ignore
            cmd.Parameters.AddWithValue("@id", runId) |> ignore
            cmd.ExecuteNonQuery() |> ignore
        finally
            jobLock.Release() |> ignore

    /// Builds the `ScheduledJobs.JobRunRecorder` seam. Owns a fresh
    /// `runningJobs` guard in its own closure (ADR-0035) — names of jobs
    /// currently mid-run, for either trigger; the single source of truth
    /// for the concurrent-trigger refusal, so a scheduled fire and a manual
    /// "Run now" of the SAME job name can never both hold it. Also closures
    /// over `conn`/`jobLock`, so every recorder built from the same
    /// `conn`/`jobLock` pair shares the same guard state and the same
    /// per-command serialization (Composition.fs builds exactly one
    /// recorder — over the dedicated job connection and its lock — and
    /// passes it to both `ScheduledJobs.startAll` and `create`, per
    /// ADR-0026). Each independently-built recorder gets its own guard, so
    /// two recorders never collide on a shared job name.
    let makeJobRunRecorder (conn: SqliteConnection) (jobLock: SemaphoreSlim) : ScheduledJobs.JobRunRecorder =
        let runningJobs = System.Collections.Concurrent.ConcurrentDictionary<string, unit>()
        { TryClaim = fun jobName -> runningJobs.TryAdd(jobName, ())
          Release = fun jobName -> runningJobs.TryRemove(jobName) |> ignore
          BeginRun = fun jobName trigger -> insertRunningRow jobLock conn jobName trigger
          Complete = fun runId disposition summary -> completeRun jobLock conn runId disposition summary
          Fail = fun runId message -> failRun jobLock conn runId message }

    let private statusFromString (s: string) : JobRunStatus =
        match s with
        | "running" -> RunStatusRunning
        | "ok" -> RunStatusOk
        | "error" -> RunStatusError
        | "skipped" -> RunStatusSkipped
        | "interrupted" -> RunStatusInterrupted
        | other -> failwithf "Unknown job_runs.status value: %s" other

    /// Most recent `limit` runs for one job, newest first — backed by the
    /// `(job_name, started_at DESC)` index. No pruning (ADR-0026): this is a
    /// display cap, not a retention policy.
    let private getRecentRuns (conn: SqliteConnection) (jobName: string) (limit: int) : JobRunDto list =
        use cmd = conn.CreateCommand()
        cmd.CommandText <- """
            SELECT id, job_name, trigger, status, summary, started_at, finished_at
            FROM job_runs
            WHERE job_name = @jobName
            ORDER BY started_at DESC
            LIMIT @limit
            """
        cmd.Parameters.AddWithValue("@jobName", jobName) |> ignore
        cmd.Parameters.AddWithValue("@limit", limit) |> ignore
        use reader = cmd.ExecuteReader()
        [ while reader.Read() do
            yield {
                JobRunDto.Id = reader.GetInt64(0)
                JobName = reader.GetString(1)
                Trigger = reader.GetString(2)
                Status = statusFromString (reader.GetString(3))
                Summary = if reader.IsDBNull(4) then None else Some (reader.GetString(4))
                StartedAt = reader.GetString(5)
                FinishedAt = if reader.IsDBNull(6) then None else Some (reader.GetString(6))
            } ]

    // ── Event surgery (administration-wwc36, ADR-0034) ──
    //
    // The raw-log escape hatch for cases the compensating-event composer
    // (ADR-0032) can't reach: a genuinely wrong-payload event, or a stranded
    // event-type name left by a code-side DU rename. Every commit op below
    // runs the SAME three-guardrail protocol: VACUUM INTO backup first
    // (abort with no row touched on failure) -> mutation + events_fts
    // rebuild + checkpoint rewind sharing one transaction -> dirty signal
    // reused verbatim from `isAnyProjectionDirty` (ADR-0025, no new flag
    // table). See ADR-0034 for the full design and concurrency reasoning.

    /// `<dataDir>/backups/` — a sibling of the live db file, derived from
    /// `dbPath` the same way Composition.fs derives `images/` from the data
    /// dir. Created on first use; never pruned (keep-all retention, locked
    /// at refinement).
    let private ensureBackupsDir (dbPath: string) : string =
        let dir = Path.Combine(Path.GetDirectoryName(dbPath), "backups")
        if not (Directory.Exists(dir)) then Directory.CreateDirectory(dir) |> ignore
        dir

    /// A fresh, collision-free backup file path under `backupsDir`. A
    /// timestamp alone isn't collision-proof across rapid successive
    /// surgeries (several ops fired in the same test run can land in the
    /// same tick) — the short GUID suffix guarantees uniqueness regardless
    /// of timing.
    let private newBackupPath (dbPath: string) : string =
        let dir = ensureBackupsDir dbPath
        let stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfffffff")
        let unique = Guid.NewGuid().ToString("N").Substring(0, 8)
        Path.Combine(dir, sprintf "mediatheca-%s-%s.db" stamp unique)

    let private toSurgeryEventRow (e: EventStore.StoredEvent) : SurgeryEventRow =
        { GlobalPosition = e.GlobalPosition
          StreamId = e.StreamId
          StreamPosition = e.StreamPosition
          EventType = e.EventType
          Data = e.Data
          Metadata = e.Metadata
          Timestamp = e.Timestamp.ToString("o") }

    /// Shared commit-op body for edit/delete/rename (ADR-0034): `VACUUM
    /// INTO` first, in autocommit (SQLite refuses VACUUM inside a
    /// transaction), verified by `EventStore.vacuumIntoBackup`'s own
    /// throwaway-connection check; only on success does `mutate` + (when
    /// `needsFtsRebuild`) the FTS rebuild + the checkpoint rewind run inside
    /// ONE transaction on `conn` — mirrors `appendCompensatingEventCore`'s
    /// per-op-connection, no-lock shape (ADR-0033: `conn` is opened by the
    /// caller via the shared factory, so no other in-flight request shares
    /// this connection object). `mutate` returns the affected-row count.
    let private runSurgeryMutation
        (conn: SqliteConnection)
        (dbPath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (needsFtsRebuild: bool)
        (mutate: unit -> int)
        : SurgeryResult =
        let backupPath = newBackupPath dbPath
        match EventStore.vacuumIntoBackup conn backupPath with
        | Error reason -> BackupFailed reason
        | Ok () ->
            use tx = conn.BeginTransaction()
            try
                let affected = mutate ()
                if needsFtsRebuild then EventStore.rebuildFtsIndex conn
                // Rewind every checkpoint-tracked projection to dirty
                // (ADR-0025's isAnyProjectionDirty then reports all of them
                // non-empty) — reuses Projection.saveCheckpoint's own
                // upsert-to-0, same net effect as the design's literal
                // "UPDATE projection_checkpoints SET last_position = 0" but
                // also correct for a handler that has never checkpointed.
                for handler in projectionHandlers do
                    Projection.saveCheckpoint conn handler.Name 0L
                tx.Commit()
                Applied(backupPath, affected)
            with _ ->
                tx.Rollback()
                reraise ()

    /// Directory walk over `backups/` for the Surgery tab's keep-all
    /// retention panel. Empty stats (not an error) if the directory doesn't
    /// exist yet — i.e. no surgery has ever run against this store.
    let private computeBackupStats (dbPath: string) : BackupStats =
        let dir = Path.Combine(Path.GetDirectoryName(dbPath), "backups")
        if not (Directory.Exists(dir)) then
            { Count = 0; TotalBytes = 0L }
        else
            let files = Directory.GetFiles(dir)
            { Count = files.Length
              TotalBytes = files |> Array.sumBy (fun f -> (FileInfo(f)).Length) }

    /// `dbPath`/`imagesDir` are the same paths Program.fs computes from
    /// DATA_DIR (mediatheca.db and the images/ cache) — passed through here
    /// so the Health tab's storage stats reflect the actual data dir rather
    /// than duplicating DATA_DIR resolution logic. `projectionHandlers` is
    /// the same registry Composition.fs passes to Api.create — reused here
    /// for the Projections tab's checkpoint/lag/row-count listing.
    /// `scheduledJobs` is the same `ScheduledJobs.JobSpec list` registry
    /// Composition.fs passes to `ScheduledJobs.startAll` — reused here so
    /// `getJobStatuses`/`runJobNow` stay in lockstep with whatever jobs are
    /// actually scheduled (a future `JobSpec` auto-appears, no extra wiring).
    /// `recorder` is the SAME `ScheduledJobs.JobRunRecorder` instance passed
    /// to `startAll`, so a manual "Run now" and the scheduled timer share one
    /// guard dictionary and one connection (ADR-0026).
    /// administration-mz6kp (ADR-0033): `factory` builds one fresh
    /// `SqliteConnection` per record member invocation (`use conn = factory()`
    /// at the top of each member below), retiring ADR-0030's process-wide
    /// `requestDbLock` — there is no longer a shared connection object for it
    /// to guard.
    /// `guards` is the SAME `AdminGuards` value the composition root also
    /// passes to `projectionRebuildStreamHandler`/`driftCheckStreamHandler`
    /// (ADR-0035), so a rebuild/drift-check started via the SSE handlers and
    /// the not-dirty guard read here (`isAnyProjectionDirty`) share one
    /// guard state.
    let create
        (factory: unit -> SqliteConnection)
        (dbPath: string)
        (imagesDir: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (scheduledJobs: ScheduledJobs.JobSpec list)
        (recorder: ScheduledJobs.JobRunRecorder)
        (guards: AdminGuards)
        : IAdminApi =
        {
            getEventPage = fun query -> async {
                use conn = factory ()
                let filter: EventStore.QueryFilter = {
                    Search = query.Filter.Search
                    StreamFilter = query.Filter.StreamFilter
                    EventTypeFilter = query.Filter.EventTypeFilter
                    StreamPrefix = prefixForBoundedContext query.Filter.BoundedContext
                    TimestampFrom = query.Filter.TimestampFrom
                    TimestampTo = query.Filter.TimestampTo
                }
                let pageSize = max 1 query.PageSize
                let events, hasMore, total = EventStore.queryEventPage conn filter query.Before pageSize
                return {
                    Mediatheca.Shared.EventPage.Events = events |> List.map toEventDto
                    HasMore = hasMore
                    TotalMatches = total
                }
            }

            getEventsAfter = fun query -> async {
                use conn = factory ()
                let filter: EventStore.QueryFilter = {
                    Search = query.Filter.Search
                    StreamFilter = query.Filter.StreamFilter
                    EventTypeFilter = query.Filter.EventTypeFilter
                    StreamPrefix = prefixForBoundedContext query.Filter.BoundedContext
                    TimestampFrom = query.Filter.TimestampFrom
                    TimestampTo = query.Filter.TimestampTo
                }
                let limit = max 1 query.Limit
                let events = EventStore.queryEventsAfter conn filter query.After limit
                return events |> List.map toEventDto
            }

            getCompensatingEventTypes = fun streamId -> async {
                use conn = factory ()
                match prefixForStreamId streamId with
                | None -> return []
                | Some prefix -> return EventStore.getDistinctEventTypesForPrefix conn prefix
            }

            getCompensatingEventTemplate = fun streamId eventType -> async {
                use conn = factory ()
                match prefixForStreamId streamId with
                | None -> return None
                | Some prefix ->
                    match EventStore.getMostRecentEventOfType conn streamId prefix eventType with
                    | None -> return None
                    | Some e -> return Some { Data = e.Data; FromOtherStream = e.StreamId <> streamId }
            }

            previewCompensatingEvent = fun streamId eventType rawData -> async {
                use conn = factory ()
                match canonicalizeCompensatingEvent streamId eventType rawData with
                | None ->
                    return Error (sprintf "Payload does not deserialize as a valid '%s' event" eventType)
                | Some (canonicalEventType, canonicalData) ->
                    let expectedPosition = EventStore.getStreamPosition conn streamId
                    return Ok {
                        CanonicalEventType = canonicalEventType
                        CanonicalData = canonicalData
                        ExpectedPosition = expectedPosition
                    }
            }

            appendCompensatingEvent = fun streamId eventType rawData expectedPosition -> async {
                use conn = factory ()
                return appendCompensatingEventCore conn projectionHandlers streamId eventType rawData expectedPosition
            }

            getEventStreams = fun () -> async {
                use conn = factory ()
                return EventStore.getDistinctStreams conn
            }

            getEventTypes = fun () -> async {
                use conn = factory ()
                return EventStore.getDistinctEventTypes conn
            }

            getBoundedContexts = fun () -> async {
                return boundedContextPrefixes |> List.map fst
            }

            getStreamDetail = fun streamId -> async {
                use conn = factory ()
                let entries =
                    EventStore.readStream conn streamId
                    |> List.map toTimelineEntry
                let projectionRows =
                    projectionRowFor conn streamId |> Option.toList
                return {
                    Mediatheca.Shared.StreamDetailDto.StreamId = streamId
                    Entries = entries
                    ProjectionRows = projectionRows
                }
            }

            getHealthStats = fun () -> async {
                use conn = factory ()
                return buildHealthStats conn dbPath imagesDir
            }

            getProjectionStats = fun () -> async {
                use conn = factory ()
                return buildProjectionStats conn projectionHandlers guards
            }

            getImageCacheStats = fun () -> async {
                return buildImageCacheStats imagesDir
            }

            listOrphanedImages = fun () -> async {
                use conn = factory ()
                match isAnyProjectionDirty conn projectionHandlers guards with
                | dirty when not (List.isEmpty dirty) ->
                    return OrphanScanBlocked (sprintf "Blocked: waiting on %s to catch up" (String.concat ", " dirty))
                | _ ->
                    let referencedRefs = getReferencedImageRefs conn
                    let orphans, totalBytes = computeOrphans imagesDir referencedRefs
                    return OrphanScanReady (orphans, totalBytes)
            }

            /// TOCTOU-safe (ADR-0025): re-checks the not-dirty guard, then
            /// re-derives the referenced/orphan sets fresh (not the client's
            /// held scan) before deleting, so a path that became referenced
            /// or already vanished between scan and confirm is skipped
            /// rather than wrongly deleted.
            purgeOrphanedImages = fun selection -> async {
                use conn = factory ()
                match isAnyProjectionDirty conn projectionHandlers guards with
                | dirty when not (List.isEmpty dirty) ->
                    return PurgeBlocked (sprintf "Blocked: waiting on %s to catch up" (String.concat ", " dirty))
                | _ ->
                    let referencedRefs = getReferencedImageRefs conn
                    let orphans, _ = computeOrphans imagesDir referencedRefs
                    let orphanPaths = orphans |> List.map (fun o -> o.RelativePath) |> Set.ofList
                    let requested =
                        match selection with
                        | PurgeAll -> orphanPaths
                        | PurgeSpecific paths -> Set.ofList paths
                    let toDelete = Set.intersect requested orphanPaths
                    let alreadySkipped = Set.difference requested orphanPaths

                    let mutable deletedCount = 0
                    let mutable bytesFreed = 0L
                    let mutable raceSkipped = []
                    for path in toDelete do
                        let fullPath = Path.Combine(imagesDir, path)
                        if File.Exists(fullPath) then
                            let size = FileInfo(fullPath).Length
                            ImageStore.deleteImage imagesDir path
                            deletedCount <- deletedCount + 1
                            bytesFreed <- bytesFreed + size
                        else
                            raceSkipped <- path :: raceSkipped

                    let skipped = (Set.toList alreadySkipped) @ (List.rev raceSkipped)
                    return PurgeDone (deletedCount, bytesFreed, skipped)
            }

            getJobStatuses = fun () -> async {
                use conn = factory ()
                return
                    scheduledJobs
                    |> List.map (fun spec ->
                        let recent = getRecentRuns conn spec.Name 10
                        { JobStatusDto.JobName = spec.Name
                          NextFireAt = (ScheduledJobs.nextRun DateTime.Now spec.Hour).ToString("o")
                          LastRun = recent |> List.tryHead
                          RecentRuns = recent })
            }

            /// Fire-and-forget (ADR-0026): starts the job body on its own
            /// async and returns `RunJobStarted runId` immediately, before
            /// the job completes. The tab polls `getJobStatuses` until the
            /// row resolves. `RunJobRejected` covers both an unknown job name
            /// and a job already in flight under either trigger.
            runJobNow = fun jobName -> async {
                match scheduledJobs |> List.tryFind (fun s -> s.Name = jobName) with
                | None -> return RunJobRejected
                | Some spec ->
                    match ScheduledJobs.tryStartJob recorder spec "manual" with
                    | Error () -> return RunJobRejected
                    | Ok (runId, body) ->
                        Async.Start body
                        return RunJobStarted runId
            }

            previewEventEdit = fun globalPosition -> async {
                use conn = factory ()
                return EventStore.getEventByGlobalPosition conn globalPosition |> Option.map toSurgeryEventRow
            }

            previewEventDelete = fun globalPosition -> async {
                use conn = factory ()
                match EventStore.getEventByGlobalPosition conn globalPosition with
                | None -> return None
                | Some e ->
                    let currentPosition = EventStore.getStreamPosition conn e.StreamId
                    return Some { Event = toSurgeryEventRow e; StreamCurrentPosition = currentPosition }
            }

            previewEventTypeRename = fun oldType -> async {
                use conn = factory ()
                let count = EventStore.countEventsOfType conn oldType
                let sample = EventStore.sampleEventsOfType conn oldType 20 |> List.map toSurgeryEventRow
                return { Count = count; Sample = sample }
            }

            editEvent = fun globalPosition newData newMetadata -> async {
                use conn = factory ()
                return runSurgeryMutation conn dbPath projectionHandlers true (fun () -> EventStore.editEventData conn globalPosition newData newMetadata)
            }

            deleteEvent = fun globalPosition -> async {
                use conn = factory ()
                return runSurgeryMutation conn dbPath projectionHandlers true (fun () -> EventStore.deleteEventRow conn globalPosition)
            }

            renameEventType = fun oldType newType -> async {
                use conn = factory ()
                return runSurgeryMutation conn dbPath projectionHandlers false (fun () -> EventStore.renameEventTypeRows conn oldType newType)
            }

            getBackupStats = fun () -> async {
                return computeBackupStats dbPath
            }
        }
