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
    let exportEventsStreamHandler (conn: SqliteConnection) : HttpHandler =
        fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
            task {
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
    let importEventsStreamHandler (conn: SqliteConnection) : HttpHandler =
        fun (next: HttpFunc) (ctx: Microsoft.AspNetCore.Http.HttpContext) ->
            task {
                allowSynchronousIO ctx
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

                use reader = new StreamReader(ctx.Request.Body)
                match EventStore.importNdjson conn reader with
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

    // ── Job runs console (administration-yamm5, ADR-0026) ──

    /// Names of jobs currently mid-run, for either trigger. Exact structural
    /// copy of `rebuildingProjections` above — same "name-keyed in-memory
    /// guard" shape, module-level, process-lifetime. The single source of
    /// truth for the concurrent-trigger refusal: a scheduled fire and a
    /// manual "Run now" of the SAME job name can never both hold it.
    let private runningJobs = System.Collections.Concurrent.ConcurrentDictionary<string, unit>()

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

    /// Builds the `ScheduledJobs.JobRunRecorder` seam. Closures over `conn`,
    /// `jobLock`, and the private `runningJobs` guard above, so every recorder
    /// built from the same `conn`/`jobLock` pair shares the same guard state
    /// and the same per-command serialization (Composition.fs builds exactly
    /// one recorder — over the dedicated job connection and its lock — and
    /// passes it to both `ScheduledJobs.startAll` and `create`, per ADR-0026).
    let makeJobRunRecorder (conn: SqliteConnection) (jobLock: SemaphoreSlim) : ScheduledJobs.JobRunRecorder =
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
    let create
        (conn: SqliteConnection)
        (dbPath: string)
        (imagesDir: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (scheduledJobs: ScheduledJobs.JobSpec list)
        (recorder: ScheduledJobs.JobRunRecorder)
        : IAdminApi =
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

            getJobStatuses = fun () -> async {
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
        }
