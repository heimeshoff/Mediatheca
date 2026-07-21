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
              Lag = max 0L (head - position)
              UpdatedAt = updatedAt
              TableCounts = tables |> List.map (fun t -> { TableName = t; RowCount = tableRowCount conn t })
              IsRebuilding = rebuildingProjections.ContainsKey(handler.Name) })

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
        }
