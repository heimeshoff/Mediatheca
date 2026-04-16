namespace Mediatheca.Server

open System
open System.Data
open System.Net.Http
open Microsoft.Data.Sqlite
open Donald

/// Refresh TMDB metadata for series already in the library. Used both by
/// the nightly scheduled job and by the manual "Refresh from TMDB" action
/// in the series detail page context menu.
module SeriesRefresh =

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
        |> Db.newCommand "SELECT season_number, episode_number FROM series_episodes WHERE series_slug = @slug"
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

    /// Apply a fetch result to the projection: update series_list/series_detail
    /// status-ish fields and upsert every season + episode row. Old episodes
    /// stay in place (so watch progress is preserved); newly-added TMDB
    /// episodes are inserted, and updated episodes get their metadata replaced.
    let applyToProjection (conn: SqliteConnection) (slug: string) (result: RefreshFetchResult) : unit =
        let genresJson =
            result.Genres
            |> List.map Thoth.Json.Net.Encode.string
            |> Thoth.Json.Net.Encode.list
            |> Thoth.Json.Net.Encode.toString 0

        // Update series_list
        conn
        |> Db.newCommand """
            UPDATE series_list SET
                name = @name,
                year = @year,
                poster_ref = @poster_ref,
                genres = @genres,
                tmdb_rating = @tmdb_rating,
                status = @status,
                season_count = @season_count,
                episode_count = @episode_count
            WHERE slug = @slug
        """
        |> Db.setParams [
            "slug", SqlType.String slug
            "name", SqlType.String result.Name
            "year", SqlType.Int32 result.Year
            "poster_ref", match result.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
            "genres", SqlType.String genresJson
            "tmdb_rating", match result.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
            "status", SqlType.String result.Status
            "season_count", SqlType.Int32 (List.length result.Seasons)
            "episode_count", SqlType.Int32 (result.Seasons |> List.sumBy (fun s -> s.Episodes.Length))
        ]
        |> Db.exec

        // Update series_detail
        conn
        |> Db.newCommand """
            UPDATE series_detail SET
                name = @name,
                year = @year,
                overview = @overview,
                genres = @genres,
                poster_ref = @poster_ref,
                backdrop_ref = @backdrop_ref,
                tmdb_rating = @tmdb_rating,
                episode_runtime = @episode_runtime,
                status = @status
            WHERE slug = @slug
        """
        |> Db.setParams [
            "slug", SqlType.String slug
            "name", SqlType.String result.Name
            "year", SqlType.Int32 result.Year
            "overview", SqlType.String result.Overview
            "genres", SqlType.String genresJson
            "poster_ref", match result.PosterRef with Some r -> SqlType.String r | None -> SqlType.Null
            "backdrop_ref", match result.BackdropRef with Some r -> SqlType.String r | None -> SqlType.Null
            "tmdb_rating", match result.TmdbRating with Some r -> SqlType.Double r | None -> SqlType.Null
            "episode_runtime", match result.EpisodeRuntime with Some r -> SqlType.Int32 r | None -> SqlType.Null
            "status", SqlType.String result.Status
        ]
        |> Db.exec

        // Upsert seasons + episodes (old rows for unchanged seasons/episodes
        // are overwritten; progress rows in series_episode_progress are
        // unaffected since they live in a separate table).
        for season in result.Seasons do
            conn
            |> Db.newCommand """
                INSERT OR REPLACE INTO series_seasons (series_slug, season_number, name, overview, poster_ref, air_date, episode_count)
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
                    INSERT OR REPLACE INTO series_episodes (series_slug, season_number, episode_number, name, overview, runtime, air_date, still_ref, tmdb_rating)
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

    /// Refresh a single series: fetches TMDB data, applies it to the
    /// projection, and appends a Series_refreshed event to the stream.
    let refreshOne
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (tmdbConfig: Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (slug: string)
        : Async<Result<Series.SeriesRefreshedData, string>> =
        async {
            // Look up tmdb_id and current status
            let row =
                conn
                |> Db.newCommand "SELECT tmdb_id, status FROM series_detail WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String slug ]
                |> Db.querySingle (fun (rd: IDataReader) ->
                    rd.ReadInt32 "tmdb_id", rd.ReadString "status")
            match row with
            | None -> return Error (sprintf "Series '%s' not found" slug)
            | Some (tmdbId, previousStatus) ->
                let existingKeys = existingEpisodeKeys conn slug
                let! fetchResult = fetchFromTmdb httpClient tmdbConfig imageBasePath slug tmdbId existingKeys
                match fetchResult with
                | Error e -> return Error e
                | Ok result ->
                    applyToProjection conn slug result

                    // Append Series_refreshed event to the stream
                    let statusTransitioned = previousStatus <> result.Status
                    let refreshData: Series.SeriesRefreshedData = {
                        RefreshedAt = DateTime.UtcNow.ToString("o")
                        NewEpisodeCount = result.NewEpisodeCount
                        PreviousStatus = if statusTransitioned then Some previousStatus else None
                        NewStatus = if statusTransitioned then Some result.Status else None
                    }
                    let streamId = Series.streamId slug
                    let storedEvents = EventStore.readStream conn streamId
                    let events = storedEvents |> List.choose Series.Serialization.fromStoredEvent
                    let state = Series.reconstitute events
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
                            return Ok refreshData
        }

    /// Return all series slugs that are candidates for nightly refresh
    /// (status = Returning or InProduction).
    let getRefreshCandidates (conn: SqliteConnection) : string list =
        conn
        |> Db.newCommand "SELECT slug FROM series_detail WHERE status IN ('Returning', 'InProduction') ORDER BY slug"
        |> Db.query (fun (rd: IDataReader) -> rd.ReadString "slug")

    /// Run the nightly refresh: iterate every refresh candidate and refresh
    /// it. Throttled with a small delay between series so TMDB isn't hit
    /// with bursts for large libraries.
    let runNightlyJob
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Async<unit> =
        async {
            let tmdbConfig = getTmdbConfig()
            if String.IsNullOrWhiteSpace(tmdbConfig.ApiKey) then
                eprintfn "[SeriesRefresh] Skipping nightly refresh: TMDB API key not configured"
            else
                let candidates = getRefreshCandidates conn
                eprintfn "[SeriesRefresh] Nightly refresh: %d series to check" candidates.Length
                let mutable refreshed = 0
                let mutable errors = 0
                let mutable totalNewEpisodes = 0
                let mutable statusTransitions = 0
                for slug in candidates do
                    try
                        let! result = refreshOne conn httpClient tmdbConfig imageBasePath projectionHandlers slug
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
        }
