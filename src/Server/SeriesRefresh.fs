namespace Mediatheca.Server

open System
open System.Data
open System.Net.Http
open System.Threading
open Microsoft.Data.Sqlite
open Donald

/// Refresh TMDB metadata for series already in the library. Used both by
/// the nightly scheduled job and by the manual "Refresh from TMDB" action
/// in the series detail page context menu.
module SeriesRefresh =

    /// administration-tj8n2: the job connection is dedicated to scheduled-job
    /// use but shared by BOTH jobs (Steam sync, Series refresh), which can be
    /// touched by two ThreadPool threads at once (catch-up collision, or
    /// same-hour daily fire). `SqliteConnection` is not thread-safe for
    /// concurrent command creation/disposal, so every synchronous DB-touching
    /// section reached from `runNightlyJob` acquires `jobLock` for just that
    /// section — never across an awaited HTTP call — so the two jobs'
    /// network I/O still overlaps and only their brief DB moments serialize.
    let inline private withLock (jobLock: SemaphoreSlim) (f: unit -> 'a) : 'a =
        jobLock.Wait()
        try f() finally jobLock.Release() |> ignore

    /// Fetch the latest TMDB data for a series and re-download any missing
    /// poster/backdrop/still images. Returns a fully-populated
    /// SeriesAddedData-shaped record plus a count of brand-new episodes
    /// (relative to the existing projection) and the new TMDB status string.
    type RefreshFetchResult = {
        Name: string
        Year: int
        Overview: string
        Genres: string list
        Status: string
        PosterRef: string option
        BackdropRef: string option
        TmdbRating: float option
        EpisodeRuntime: int option
        Seasons: Series.SeasonImportData list
        /// New-episode count relative to what the projection held before.
        NewEpisodeCount: int
    }

    /// Returns the set of (season, episode) keys currently stored in the
    /// projection for a given series.
    let private existingEpisodeKeys (conn: SqliteConnection) (slug: string) : Set<int * int> =
        conn
        |> Db.newCommand "SELECT season_number, episode_number FROM series_episode_cache WHERE series_slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.query (fun (rd: IDataReader) ->
            rd.ReadInt32 "season_number", rd.ReadInt32 "episode_number")
        |> Set.ofList

    /// Fetch TMDB metadata for a series (details + every non-specials season +
    /// episodes). Downloads missing images. Returns a RefreshFetchResult or
    /// an error string.
    let fetchFromTmdb
        (httpClient: HttpClient)
        (tmdbConfig: Tmdb.TmdbConfig)
        (imageBasePath: string)
        (slug: string)
        (tmdbId: int)
        (existingKeys: Set<int * int>)
        : Async<Result<RefreshFetchResult, string>> =
        async {
            let! detailsResult = Tmdb.getTvSeriesDetails httpClient tmdbConfig tmdbId
            match detailsResult with
            | Error e -> return Error e
            | Ok details ->
                let year =
                    details.FirstAirDate
                    |> Option.bind (fun d ->
                        if d.Length >= 4 then
                            match Int32.TryParse(d.[0..3]) with
                            | true, y -> Some y
                            | _ -> None
                        else None)
                    |> Option.defaultValue 0

                // Always re-download poster/backdrop on refresh to catch TMDB
                // image swaps (same filename, new contents).
                let! posterRef, backdropRef =
                    Tmdb.downloadSeriesImages httpClient tmdbConfig slug details.PosterPath details.BackdropPath imageBasePath

                let! seasonsArr =
                    details.Seasons
                    |> List.filter (fun s -> s.SeasonNumber > 0)
                    |> List.map (fun seasonSummary -> async {
                        let! seasonResult = Tmdb.getTvSeasonDetails httpClient tmdbConfig tmdbId seasonSummary.SeasonNumber
                        match seasonResult with
                        | Ok seasonDetails ->
                            let! episodes =
                                seasonDetails.Episodes
                                |> List.map (fun ep -> async {
                                    // Only download still if we don't already
                                    // have it (keyed by slug+SxxEyy).
                                    let stillRef =
                                        let ref = sprintf "stills/%s-s%02de%02d.jpg" slug seasonSummary.SeasonNumber ep.EpisodeNumber
                                        if ImageStore.imageExists imageBasePath ref then
                                            Some ref
                                        else
                                            match ep.StillPath with
                                            | Some stillPath ->
                                                try
                                                    Tmdb.downloadEpisodeStill httpClient tmdbConfig slug seasonSummary.SeasonNumber ep.EpisodeNumber stillPath imageBasePath
                                                    |> Async.RunSynchronously
                                                with _ -> None
                                            | None -> None
                                    let epData: Series.EpisodeImportData = {
                                        EpisodeNumber = ep.EpisodeNumber
                                        Name = ep.Name
                                        Overview = ep.Overview
                                        Runtime = ep.Runtime
                                        AirDate = ep.AirDate
                                        StillRef = stillRef
                                        TmdbRating = if ep.VoteAverage > 0.0 then Some ep.VoteAverage else None
                                    }
                                    return epData
                                })
                                |> Async.Sequential
                            let seasonData: Series.SeasonImportData = {
                                SeasonNumber = seasonSummary.SeasonNumber
                                Name = seasonSummary.Name
                                Overview = seasonSummary.Overview
                                PosterRef = None
                                AirDate = seasonSummary.AirDate
                                Episodes = episodes |> Array.toList
                            }
                            return Some seasonData
                        | Error _ -> return None
                    })
                    |> Async.Sequential

                let validSeasons = seasonsArr |> Array.toList |> List.choose id

                let newEpisodeCount =
                    validSeasons
                    |> List.sumBy (fun s ->
                        s.Episodes
                        |> List.filter (fun e -> not (existingKeys |> Set.contains (s.SeasonNumber, e.EpisodeNumber)))
                        |> List.length)

                let episodeRuntime =
                    match details.EpisodeRunTime with
                    | first :: _ -> Some first
                    | [] -> None

                return Ok {
                    Name = details.Name
                    Year = year
                    Overview = details.Overview
                    Genres = details.Genres |> List.map (fun g -> g.Name)
                    Status = Tmdb.mapSeriesStatus details.Status
                    PosterRef = posterRef
                    BackdropRef = backdropRef
                    TmdbRating = if details.VoteAverage > 0.0 then Some details.VoteAverage else None
                    EpisodeRuntime = episodeRuntime
                    Seasons = validSeasons
                    NewEpisodeCount = newEpisodeCount
                }
        }

    /// Upsert season + episode rows into the cache tier (series-r2xhv). Old
    /// rows for unchanged seasons/episodes are overwritten; progress rows in
    /// series_episode_progress are unaffected since they live in a separate
    /// table. Shared by a TMDB refresh (`applyToProjection`, below) and the
    /// command-time cache seed after `Series_added_to_library`
    /// (`Api.addSeriesToLibraryImpl`) — cache writes never happen from
    /// projection replay (ADR-0043/ADR-0045's cache-tier discipline).
    let upsertSeasonEpisodeCache (conn: SqliteConnection) (slug: string) (seasons: Series.SeasonImportData list) : unit =
        for season in seasons do
            conn
            |> Db.newCommand """
                INSERT OR REPLACE INTO series_season_cache (series_slug, season_number, name, overview, poster_ref, air_date, episode_count)
                VALUES (@series_slug, @season_number, @name, @overview, @poster_ref, @air_date, @episode_count)
            """
            |> Db.setParams [
                "series_slug", SqlType.String slug
                "season_number", SqlType.Int32 season.SeasonNumber
                "name", SqlType.String season.Name
                "overview", SqlType.String season.Overview
                "poster_ref", match season.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
                "air_date", match season.AirDate with Some d -> SqlType.String d | None -> SqlType.Null
                "episode_count", SqlType.Int32 (List.length season.Episodes)
            ]
            |> Db.exec
            for episode in season.Episodes do
                conn
                |> Db.newCommand """
                    INSERT OR REPLACE INTO series_episode_cache (series_slug, season_number, episode_number, name, overview, runtime, air_date, still_ref, tmdb_rating)
                    VALUES (@series_slug, @season_number, @episode_number, @name, @overview, @runtime, @air_date, @still_ref, @tmdb_rating)
                """
                |> Db.setParams [
                    "series_slug", SqlType.String slug
                    "season_number", SqlType.Int32 season.SeasonNumber
                    "episode_number", SqlType.Int32 episode.EpisodeNumber
                    "name", SqlType.String episode.Name
                    "overview", SqlType.String episode.Overview
                    "runtime", match episode.Runtime with Some r -> SqlType.Int32 r | None -> SqlType.Null
                    "air_date", match episode.AirDate with Some d -> SqlType.String d | None -> SqlType.Null
                    "still_ref", match episode.StillRef with Some r -> SqlType.String r | None -> SqlType.Null
                    "tmdb_rating", match episode.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
                ]
                |> Db.exec

    /// Apply a fetch result to the projection: cache-only now (series-r2xhv).
    /// The two Projected series tables are no longer written here — TMDB's
    /// name/overview/poster/genres/rating/status describe a third party, not
    /// the user's own engagement (ADR-0043), so they live only in the cache
    /// tables. The one exception, airing status, survives as a projection
    /// column but travels through the narrowed `Series_refreshed` event
    /// instead (`SeriesProjection.handleEvent`), never through this function.
    let applyToProjection (conn: SqliteConnection) (slug: string) (result: RefreshFetchResult) : unit =
        upsertSeasonEpisodeCache conn slug result.Seasons

    /// What `refreshOne`/`refreshOneForJob` report back per series, replacing
    /// the old `Series.SeriesRefreshedData` return value now that the event
    /// itself no longer carries `NewEpisodeCount` (series-r2xhv). New-episode
    /// volume is TMDB metadata, not an event — it is reported to the caller
    /// directly from the fetch result instead, for `runNightlyJob`'s summary
    /// and (potentially) the manual refresh caller.
    type RefreshOutcome = {
        NewEpisodeCount: int
        /// Mirrors the appended `Series_refreshed` event's payload — both
        /// `Some` if a `Series_refreshed` event was appended, both `None` if
        /// the airing status did not transition (no event appended).
        PreviousStatus: string option
        NewStatus: string option
    }

    /// Refresh a single series: fetches TMDB data, upserts the cache tier,
    /// and appends a Series_refreshed event to the stream only when the
    /// airing status actually transitioned (series-r2xhv narrowing).
    /// `previousStatus` is read from the aggregate (via `Series.reconstitute`),
    /// never from the read model — the read model can lag the aggregate,
    /// which would misfire the transition check the same way
    /// `promoteToInFocusIfNeeded`'s CQRS inversion did.
    let refreshOne
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (tmdbConfig: Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        : Async<Result<RefreshOutcome, string>> =
        async {
            let tmdbIdRow =
                conn
                |> Db.newCommand "SELECT tmdb_id FROM series_detail WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "tmdb_id")
            match tmdbIdRow with
            | None -> return Error (sprintf "Series '%s' not found" slug)
            | Some tmdbId ->
                let streamId = Series.streamId slug
                let storedEvents = EventStore.readStream conn streamId
                let events = storedEvents |> List.choose Series.Serialization.fromStoredEvent
                let state = Series.reconstitute events
                match state with
                | Series.Not_created | Series.Removed ->
                    return Error (sprintf "Series '%s' not found" slug)
                | Series.Active activeSeries ->
                    let previousStatus = activeSeries.Status
                    let existingKeys = existingEpisodeKeys conn slug
                    let! fetchResult = fetchFromTmdb httpClient tmdbConfig imageBasePath slug tmdbId existingKeys
                    match fetchResult with
                    | Error e -> return Error e
                    | Ok result ->
                        applyToProjection conn slug result

                        let statusTransitioned = previousStatus <> result.Status
                        let refreshData: Series.SeriesRefreshedData = {
                            PreviousStatus = if statusTransitioned then Some previousStatus else None
                            NewStatus = if statusTransitioned then Some result.Status else None
                        }
                        let currentPosition = EventStore.getStreamPosition conn streamId
                        match Series.decide state (Series.Refresh_series_from_tmdb refreshData) with
                        | Error e ->
                            return Error e
                        | Ok newEvents ->
                            let eventDataList = newEvents |> List.map Series.Serialization.toEventData
                            match EventStore.appendToStream conn streamId currentPosition eventDataList with
                            | EventStore.ConcurrencyConflict _ ->
                                return Error "Concurrency conflict while appending Series_refreshed"
                            | EventStore.Success _ ->
                                for handler in projectionHandlers do
                                    Projection.runProjection conn handler
                                return Ok {
                                    NewEpisodeCount = result.NewEpisodeCount
                                    PreviousStatus = refreshData.PreviousStatus
                                    NewStatus = refreshData.NewStatus
                                }
        }

    /// Job-connection variant of `refreshOne` (administration-tj8n2), used only
    /// by `runNightlyJob`. Same behavior, but its DB touches are guarded by
    /// `jobLock` so the two scheduled jobs — which share one dedicated job
    /// connection — never touch it at the same instant; `fetchFromTmdb` (no DB
    /// access) is deliberately left unlocked so the other job's network I/O
    /// still overlaps it. Kept separate from `refreshOne` rather than adding a
    /// lock parameter there, because `refreshOne` is also called — unlocked —
    /// from the request-serving connection by the manual "Refresh from TMDB"
    /// action (`Api.fs`), which has no job×job race to guard against.
    let private refreshOneForJob
        (jobLock: SemaphoreSlim)
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (tmdbConfig: Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        : Async<Result<RefreshOutcome, string>> =
        async {
            let streamId = Series.streamId slug
            let looked =
                withLock jobLock (fun () ->
                    let tmdbIdRow =
                        conn
                        |> Db.newCommand "SELECT tmdb_id FROM series_detail WHERE slug = @slug"
                        |> Db.setParams [ "slug", SqlType.String slug ]
                        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "tmdb_id")
                    match tmdbIdRow with
                    | None -> None
                    | Some tmdbId ->
                        let storedEvents = EventStore.readStream conn streamId
                        let events = storedEvents |> List.choose Series.Serialization.fromStoredEvent
                        let state = Series.reconstitute events
                        match state with
                        | Series.Active activeSeries ->
                            Some (tmdbId, state, activeSeries.Status, existingEpisodeKeys conn slug)
                        | Series.Not_created | Series.Removed -> None)
            match looked with
            | None -> return Error (sprintf "Series '%s' not found" slug)
            | Some (tmdbId, state, previousStatus, existingKeys) ->
                let! fetchResult = fetchFromTmdb httpClient tmdbConfig imageBasePath slug tmdbId existingKeys
                match fetchResult with
                | Error e -> return Error e
                | Ok result ->
                    return
                        withLock jobLock (fun () ->
                            applyToProjection conn slug result

                            let statusTransitioned = previousStatus <> result.Status
                            let refreshData: Series.SeriesRefreshedData = {
                                PreviousStatus = if statusTransitioned then Some previousStatus else None
                                NewStatus = if statusTransitioned then Some result.Status else None
                            }
                            let currentPosition = EventStore.getStreamPosition conn streamId
                            match Series.decide state (Series.Refresh_series_from_tmdb refreshData) with
                            | Error e -> Error e
                            | Ok newEvents ->
                                let eventDataList = newEvents |> List.map Series.Serialization.toEventData
                                match EventStore.appendToStream conn streamId currentPosition eventDataList with
                                | EventStore.ConcurrencyConflict _ ->
                                    Error "Concurrency conflict while appending Series_refreshed"
                                | EventStore.Success _ ->
                                    for handler in projectionHandlers do
                                        Projection.runProjection conn handler
                                    Ok {
                                        NewEpisodeCount = result.NewEpisodeCount
                                        PreviousStatus = refreshData.PreviousStatus
                                        NewStatus = refreshData.NewStatus
                                    })
        }

    /// Return all series slugs that are candidates for nightly refresh.
    ///
    /// Always includes still-running series (status Returning / InProduction).
    /// Additionally, an Ended series re-enters the candidate set when it carries
    /// a *recency signal* — it is in_focus, or its most recent watch
    /// (MAX(series_episode_progress.watched_date)) falls within the last 180 days.
    /// This activity gate lets a TMDB-added season on a show the user is actually
    /// engaged with get auto-discovered, without re-fetching a whole library of
    /// genuinely-finished shows every night. Dates are ISO strings, so the lexical
    /// comparison against date('now', '-180 days') is correct; a NULL MAX (no watch
    /// history) simply fails the comparison and the show stays excluded.
    let getRefreshCandidates (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand
            """
            SELECT slug FROM series_detail sd
            WHERE status IN ('Returning', 'InProduction')
               OR (status = 'Ended' AND (
                     in_focus = 1
                     OR (SELECT MAX(watched_date) FROM series_episode_progress
                         WHERE series_slug = sd.slug) >= date('now', '-180 days')
                  ))
            ORDER BY slug
            """
        |> Db.query (fun (rd: IDataReader) -> rd.ReadString "slug")

    /// The counts `runNightlyJob` previously only `eprintfn`d (administration-yamm5
    /// / ADR-0026), now returned so Composition.fs's job-runs registry can map
    /// them into a `ScheduledJobs.JobRunOutcome` summary. `Skipped = true`
    /// means the whole run declined to act (TMDB API key not configured) —
    /// the other counts are meaningless (all zero) in that case.
    type SeriesRefreshSummary = {
        Refreshed: int
        Errors: int
        NewEpisodes: int
        StatusTransitions: int
        Skipped: bool
    }

    /// Run the nightly refresh: iterate every refresh candidate and refresh
    /// it. Throttled with a small delay between series so TMDB isn't hit
    /// with bursts for large libraries.
    let runNightlyJob
        (conn: SqliteConnection)
        (jobLock: SemaphoreSlim)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<SeriesRefreshSummary> =
        async {
            let tmdbConfig = getTmdbConfig()
            if String.IsNullOrWhiteSpace(tmdbConfig.ApiKey) then
                eprintfn "[SeriesRefresh] Skipping nightly refresh: TMDB API key not configured"
                return { Refreshed = 0; Errors = 0; NewEpisodes = 0; StatusTransitions = 0; Skipped = true }
            else
                let candidates = withLock jobLock (fun () -> getRefreshCandidates conn)
                eprintfn "[SeriesRefresh] Nightly refresh: %d series to check" candidates.Length
                let mutable refreshed = 0
                let mutable errors = 0
                let mutable totalNewEpisodes = 0
                let mutable statusTransitions = 0
                for slug in candidates do
                    try
                        let! result = refreshOneForJob jobLock conn httpClient tmdbConfig imageBasePath projectionHandlers slug
                        match result with
                        | Ok data ->
                            refreshed <- refreshed + 1
                            totalNewEpisodes <- totalNewEpisodes + data.NewEpisodeCount
                            if data.NewStatus.IsSome then
                                statusTransitions <- statusTransitions + 1
                                eprintfn "[SeriesRefresh] %s: status %s -> %s"
                                    slug (data.PreviousStatus |> Option.defaultValue "?") (data.NewStatus |> Option.defaultValue "?")
                            if data.NewEpisodeCount > 0 then
                                eprintfn "[SeriesRefresh] %s: %d new episode(s)" slug data.NewEpisodeCount
                        | Error e ->
                            errors <- errors + 1
                            eprintfn "[SeriesRefresh] %s: %s" slug e
                    with ex ->
                        errors <- errors + 1
                        eprintfn "[SeriesRefresh] %s: %s" slug ex.Message
                    // Throttle: TMDB allows 50 req/s but we fetch ~1+N
                    // requests per series (details + seasons). Wait 500ms
                    // between series to keep burst pressure modest.
                    do! Async.Sleep 500
                eprintfn "[SeriesRefresh] Nightly refresh complete: %d refreshed, %d errors, %d new episodes, %d status transitions"
                    refreshed errors totalNewEpisodes statusTransitions
                return { Refreshed = refreshed; Errors = errors; NewEpisodes = totalNewEpisodes; StatusTransitions = statusTransitions; Skipped = false }
        }
