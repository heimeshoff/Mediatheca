namespace Mediatheca.Server

open System
open System.IO
open Microsoft.Data.Sqlite
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

    /// `dbPath`/`imagesDir` are the same paths Program.fs computes from
    /// DATA_DIR (mediatheca.db and the images/ cache) — passed through here
    /// so the Health tab's storage stats reflect the actual data dir rather
    /// than duplicating DATA_DIR resolution logic.
    let create (conn: SqliteConnection) (dbPath: string) (imagesDir: string) : IAdminApi =
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

            getEventStreams = fun () -> async {
                return EventStore.getDistinctStreams conn
            }

            getEventTypes = fun () -> async {
                return EventStore.getDistinctEventTypes conn
            }

            getBoundedContexts = fun () -> async {
                return boundedContextPrefixes |> List.map fst
            }

            getHealthStats = fun () -> async {
                return buildHealthStats conn dbPath imagesDir
            }
        }
