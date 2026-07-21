namespace Mediatheca.Server

open System
open System.IO
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

    /// Names of projections currently mid-rebuild. Guards the "reject a
    /// second concurrent rebuild of the same projection" acceptance
    /// criterion. Module-level mutable state, scoped to the server process's
    /// lifetime — same shape as other singleton server state in this
    /// codebase (e.g. JellyfinSync's last-sync-time cache).
    let private rebuildingProjections = System.Collections.Concurrent.ConcurrentDictionary<string, unit>()

    let private buildProjectionStats (conn: SqliteConnection) (projectionHandlers: Projection.ProjectionHandler list) : ProjectionStatRow list =
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
              IsRebuilding = rebuildingProjections.ContainsKey(handler.Name) })

    // ── Image cache admin (administration-xx3mw) ──

    /// The not-dirty guard (ADR-0025): names of the six checkpoint-tracked
    /// projections that are either mid-rebuild or lagging behind the store
    /// head. Empty = clean, safe to trust the projection tables as the live
    /// ref set. `cast_members`/`game_journal_blocks` are imperative writes
    /// (CastStore.fs/GameJournal.fs) — never rebuilt, never lag — so they
    /// need no gating here.
    let isAnyProjectionDirty (conn: SqliteConnection) (projectionHandlers: Projection.ProjectionHandler list) : string list =
        let head = EventStore.getMaxGlobalPosition conn
        projectionHandlers
        |> List.filter (fun handler ->
            rebuildingProjections.ContainsKey(handler.Name) || checkpointLag conn head handler > 0L)
        |> List.map (fun handler -> handler.Name)

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
        (conn: SqliteConnection)
        (projectionHandlers: Projection.ProjectionHandler list)
        : HttpHandler =
        routef "/api/stream/rebuild-projection/%s" (fun projectionName ->
            fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
                task {
                    ctx.Response.Headers.["Content-Type"] <- Microsoft.Extensions.Primitives.StringValues("text/event-stream")
                    ctx.Response.Headers.["Cache-Control"] <- Microsoft.Extensions.Primitives.StringValues("no-cache")
                    ctx.Response.Headers.["Connection"] <- Microsoft.Extensions.Primitives.StringValues("keep-alive")

                    let writer = ctx.Response

                    let writeEvent (eventType: string) (json: string) = task {
                        let line = sprintf "data: {\"type\":\"%s\",%s}\n\n" eventType (json.TrimStart('{').TrimEnd('}'))
                        let bytes = System.Text.Encoding.UTF8.GetBytes(line)
                        do! writer.Body.WriteAsync(bytes, 0, bytes.Length)
                        do! writer.Body.FlushAsync()
                    }

                    match projectionHandlers |> List.tryFind (fun h -> h.Name = projectionName) with
                    | None ->
                        do! writeEvent "error" (sprintf "{\"message\":\"Unknown projection '%s'\"}" projectionName)
                    | Some handler ->
                        if not (rebuildingProjections.TryAdd(projectionName, ())) then
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
                                rebuildingProjections.TryRemove(projectionName) |> ignore

                    return! earlyReturn ctx
                }
        )

    /// `dbPath`/`imagesDir` are the same paths Program.fs computes from
    /// DATA_DIR (mediatheca.db and the images/ cache) — passed through here
    /// so the Health tab's storage stats reflect the actual data dir rather
    /// than duplicating DATA_DIR resolution logic. `projectionHandlers` is
    /// the same registry Composition.fs passes to Api.create — reused here
    /// for the Projections tab's checkpoint/lag/row-count listing.
    let create (conn: SqliteConnection) (dbPath: string) (imagesDir: string) (projectionHandlers: Projection.ProjectionHandler list) : IAdminApi =
        {
            getEventPage = fun query -> async {
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

            getEventStreams = fun () -> async {
                return EventStore.getDistinctStreams conn
            }

            getEventTypes = fun () -> async {
                return EventStore.getDistinctEventTypes conn
            }

            getBoundedContexts = fun () -> async {
                return boundedContextPrefixes |> List.map fst
            }

            getStreamDetail = fun streamId -> async {
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
                return buildHealthStats conn dbPath imagesDir
            }

            getProjectionStats = fun () -> async {
                return buildProjectionStats conn projectionHandlers
            }

            getImageCacheStats = fun () -> async {
                return buildImageCacheStats imagesDir
            }

            listOrphanedImages = fun () -> async {
                match isAnyProjectionDirty conn projectionHandlers with
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
                match isAnyProjectionDirty conn projectionHandlers with
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
        }
