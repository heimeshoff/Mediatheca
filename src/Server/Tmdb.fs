namespace Mediatheca.Server

open System.Net.Http
open Thoth.Json.Net

module Tmdb =

    type TmdbConfig = {
        ApiKey: string
        ImageBaseUrl: string
    }

    // Internal TMDB response types

    type TmdbMovieResult = {
        Id: int
        Title: string
        ReleaseDate: string option
        Overview: string
        PosterPath: string option
    }

    type TmdbSearchResponse = {
        Results: TmdbMovieResult list
    }

    type TmdbGenre = {
        Id: int
        Name: string
    }

    type TmdbMovieDetailsResponse = {
        Id: int
        Title: string
        ReleaseDate: string option
        Overview: string
        Runtime: int option
        Genres: TmdbGenre list
        PosterPath: string option
        BackdropPath: string option
        VoteAverage: float option
    }

    type TmdbCastMember = {
        Id: int
        Name: string
        Character: string
        ProfilePath: string option
        Order: int
    }

    type TmdbCrewMember = {
        Id: int
        Name: string
        Job: string
        Department: string
        ProfilePath: string option
    }

    type TmdbCreditsResponse = {
        Cast: TmdbCastMember list
        Crew: TmdbCrewMember list
    }

    // Decoders

    let private decodeMovieResult: Decoder<TmdbMovieResult> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Title = get.Required.Field "title" Decode.string
            ReleaseDate = get.Optional.Field "release_date" Decode.string
            Overview = get.Required.Field "overview" Decode.string
            PosterPath = get.Optional.Field "poster_path" Decode.string
        })

    let private decodeSearchResponse: Decoder<TmdbSearchResponse> =
        Decode.object (fun get -> {
            Results = get.Required.Field "results" (Decode.list decodeMovieResult)
        })

    let private decodeGenre: Decoder<TmdbGenre> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
        })

    let private decodeMovieDetails: Decoder<TmdbMovieDetailsResponse> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Title = get.Required.Field "title" Decode.string
            ReleaseDate = get.Optional.Field "release_date" Decode.string
            Overview = get.Required.Field "overview" Decode.string
            Runtime = get.Optional.Field "runtime" Decode.int
            Genres = get.Required.Field "genres" (Decode.list decodeGenre)
            PosterPath = get.Optional.Field "poster_path" Decode.string
            BackdropPath = get.Optional.Field "backdrop_path" Decode.string
            VoteAverage = get.Optional.Field "vote_average" Decode.float
        })

    let private decodeCastMember: Decoder<TmdbCastMember> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
            Character = get.Required.Field "character" Decode.string
            ProfilePath = get.Optional.Field "profile_path" Decode.string
            Order = get.Required.Field "order" Decode.int
        })

    let private decodeCrewMember: Decoder<TmdbCrewMember> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
            Job = get.Required.Field "job" Decode.string
            Department = get.Required.Field "department" Decode.string
            ProfilePath = get.Optional.Field "profile_path" Decode.string
        })

    let private decodeCredits: Decoder<TmdbCreditsResponse> =
        Decode.object (fun get -> {
            Cast = get.Required.Field "cast" (Decode.list decodeCastMember)
            Crew = get.Required.Field "crew" (Decode.list decodeCrewMember)
        })

    type TmdbVideo = {
        Key: string
        Site: string
        Type: string
        Official: bool
    }

    type TmdbVideosResponse = {
        Results: TmdbVideo list
    }

    let private decodeVideo: Decoder<TmdbVideo> =
        Decode.object (fun get -> {
            Key = get.Required.Field "key" Decode.string
            Site = get.Required.Field "site" Decode.string
            Type = get.Required.Field "type" Decode.string
            Official = get.Optional.Field "official" Decode.bool |> Option.defaultValue false
        })

    let private decodeVideosResponse: Decoder<TmdbVideosResponse> =
        Decode.object (fun get -> {
            Results = get.Required.Field "results" (Decode.list decodeVideo)
        })

    // Search cache

    module private SearchCache =
        open System
        open System.Collections.Concurrent

        type CacheEntry = {
            Results: Mediatheca.Shared.TmdbSearchResult list
            ExpiresAt: DateTime
        }

        let private cache = ConcurrentDictionary<string, CacheEntry>()

        let tryGet (query: string) : Mediatheca.Shared.TmdbSearchResult list option =
            let key = query.ToLowerInvariant().Trim()
            match cache.TryGetValue(key) with
            | true, entry ->
                if entry.ExpiresAt > DateTime.UtcNow then Some entry.Results
                else
                    cache.TryRemove(key) |> ignore
                    None
            | _ -> None

        let set (query: string) (results: Mediatheca.Shared.TmdbSearchResult list) =
            let key = query.ToLowerInvariant().Trim()
            cache.[key] <- { Results = results; ExpiresAt = DateTime.UtcNow.AddHours(1.0) }

    // API functions

    let private fetchJson (httpClient: HttpClient) (url: string) : Async<string> =
        async {
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return body
        }

    let private parseYear (releaseDate: string option) : int option =
        releaseDate
        |> Option.bind (fun d ->
            if d.Length >= 4 then
                match System.Int32.TryParse(d.[0..3]) with
                | true, year -> Some year
                | _ -> None
            else None
        )

    let searchMovies (httpClient: HttpClient) (config: TmdbConfig) (query: string) (year: int option) : Async<Mediatheca.Shared.TmdbSearchResult list> =
        async {
            let cacheKey = match year with Some y -> $"{query}:{y}" | None -> query
            match SearchCache.tryGet cacheKey with
            | Some cached -> return cached
            | None ->
                let yearParam = match year with Some y -> $"&year={y}" | None -> ""
                let url = $"https://api.themoviedb.org/3/search/movie?api_key={config.ApiKey}&query={System.Uri.EscapeDataString(query)}{yearParam}"
                let! json = fetchJson httpClient url
                match Decode.fromString decodeSearchResponse json with
                | Ok response ->
                    let results : Mediatheca.Shared.TmdbSearchResult list =
                        response.Results
                        |> List.map (fun r ->
                            { TmdbId = r.Id
                              Title = r.Title
                              Year = parseYear r.ReleaseDate
                              Overview = r.Overview
                              PosterPath = r.PosterPath
                              MediaType = Mediatheca.Shared.Movie }
                        )
                    SearchCache.set cacheKey results
                    return results
                | Error _ -> return []
        }

    let getMovieDetails (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) : Async<TmdbMovieDetailsResponse> =
        async {
            let url = $"https://api.themoviedb.org/3/movie/{tmdbId}?api_key={config.ApiKey}"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeMovieDetails json with
            | Ok details -> return details
            | Error e -> return failwith $"Failed to parse TMDB movie details: {e}"
        }

    let getMovieCredits (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) : Async<TmdbCreditsResponse> =
        async {
            let url = $"https://api.themoviedb.org/3/movie/{tmdbId}/credits?api_key={config.ApiKey}"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeCredits json with
            | Ok credits -> return credits
            | Error e -> return failwith $"Failed to parse TMDB credits: {e}"
        }

    let getMovieTrailer (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) : Async<string option> =
        async {
            let url = $"https://api.themoviedb.org/3/movie/{tmdbId}/videos?api_key={config.ApiKey}"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeVideosResponse json with
            | Ok response ->
                let youtubeTrailers =
                    response.Results
                    |> List.filter (fun v -> v.Site = "YouTube" && v.Type = "Trailer")
                // Prefer official trailers, then fall back to any trailer
                let best =
                    youtubeTrailers |> List.tryFind (fun v -> v.Official)
                    |> Option.orElseWith (fun () -> youtubeTrailers |> List.tryHead)
                return best |> Option.map (fun v -> v.Key)
            | Error _ -> return None
        }

    let getSeriesTrailer (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) : Async<string option> =
        async {
            let url = $"https://api.themoviedb.org/3/tv/{tmdbId}/videos?api_key={config.ApiKey}"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeVideosResponse json with
            | Ok response ->
                let youtubeTrailers =
                    response.Results
                    |> List.filter (fun v -> v.Site = "YouTube" && v.Type = "Trailer")
                let best =
                    youtubeTrailers |> List.tryFind (fun v -> v.Official)
                    |> Option.orElseWith (fun () -> youtubeTrailers |> List.tryHead)
                return best |> Option.map (fun v -> v.Key)
            | Error _ -> return None
        }

    let getSeasonTrailer (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) (seasonNumber: int) : Async<string option> =
        async {
            let url = $"https://api.themoviedb.org/3/tv/{tmdbId}/season/{seasonNumber}/videos?api_key={config.ApiKey}"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeVideosResponse json with
            | Ok response ->
                let youtubeTrailers =
                    response.Results
                    |> List.filter (fun v -> v.Site = "YouTube" && v.Type = "Trailer")
                let best =
                    youtubeTrailers |> List.tryFind (fun v -> v.Official)
                    |> Option.orElseWith (fun () -> youtubeTrailers |> List.tryHead)
                return best |> Option.map (fun v -> v.Key)
            | Error _ -> return None
        }

    let downloadImage (httpClient: HttpClient) (config: TmdbConfig) (tmdbPath: string) (size: string) (destPath: string) : Async<unit> =
        async {
            let url = $"{config.ImageBaseUrl}{size}{tmdbPath}"
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
            let dir = System.IO.Path.GetDirectoryName(destPath)
            if not (System.IO.Directory.Exists(dir)) then
                System.IO.Directory.CreateDirectory(dir) |> ignore
            System.IO.File.WriteAllBytes(destPath, bytes)
        }

    // ─── TV Series types ────────────────────────────────────────────────

    type TmdbTvEpisode = {
        EpisodeNumber: int
        Name: string
        Overview: string
        AirDate: string option
        Runtime: int option
        StillPath: string option
        VoteAverage: float
    }

    type TmdbTvSeasonSummary = {
        SeasonNumber: int
        Name: string
        Overview: string
        PosterPath: string option
        AirDate: string option
        EpisodeCount: int
    }

    type TmdbTvSearchResult = {
        Id: int
        Name: string
        FirstAirDate: string option
        Overview: string
        PosterPath: string option
        VoteAverage: float
    }

    type TmdbTvDetailsResponse = {
        Id: int
        Name: string
        FirstAirDate: string option
        Overview: string
        Genres: TmdbGenre list
        PosterPath: string option
        BackdropPath: string option
        VoteAverage: float
        Status: string
        NumberOfSeasons: int
        NumberOfEpisodes: int
        EpisodeRunTime: int list
        Seasons: TmdbTvSeasonSummary list
    }

    type TmdbTvSeasonResponse = {
        SeasonNumber: int
        Name: string
        Overview: string
        PosterPath: string option
        AirDate: string option
        Episodes: TmdbTvEpisode list
    }

    // ─── TV Series decoders ─────────────────────────────────────────────

    let private decodeTvSearchResult: Decoder<TmdbTvSearchResult> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
            FirstAirDate = get.Optional.Field "first_air_date" Decode.string
            Overview = get.Required.Field "overview" Decode.string
            PosterPath = get.Optional.Field "poster_path" Decode.string
            VoteAverage = get.Optional.Field "vote_average" Decode.float |> Option.defaultValue 0.0
        })

    let private decodeTvSearchResponse: Decoder<{| Results: TmdbTvSearchResult list |}> =
        Decode.object (fun get -> {| Results = get.Required.Field "results" (Decode.list decodeTvSearchResult) |})

    let private decodeTvSeasonSummary: Decoder<TmdbTvSeasonSummary> =
        Decode.object (fun get -> {
            SeasonNumber = get.Required.Field "season_number" Decode.int
            Name = get.Required.Field "name" Decode.string
            Overview = get.Required.Field "overview" Decode.string
            PosterPath = get.Optional.Field "poster_path" Decode.string
            AirDate = get.Optional.Field "air_date" Decode.string
            EpisodeCount = get.Required.Field "episode_count" Decode.int
        })

    let private decodeTvDetails: Decoder<TmdbTvDetailsResponse> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
            FirstAirDate = get.Optional.Field "first_air_date" Decode.string
            Overview = get.Required.Field "overview" Decode.string
            Genres = get.Required.Field "genres" (Decode.list decodeGenre)
            PosterPath = get.Optional.Field "poster_path" Decode.string
            BackdropPath = get.Optional.Field "backdrop_path" Decode.string
            VoteAverage = get.Optional.Field "vote_average" Decode.float |> Option.defaultValue 0.0
            Status = get.Required.Field "status" Decode.string
            NumberOfSeasons = get.Required.Field "number_of_seasons" Decode.int
            NumberOfEpisodes = get.Required.Field "number_of_episodes" Decode.int
            EpisodeRunTime = get.Optional.Field "episode_run_time" (Decode.list Decode.int) |> Option.defaultValue []
            Seasons = get.Required.Field "seasons" (Decode.list decodeTvSeasonSummary)
        })

    let private decodeTvEpisode: Decoder<TmdbTvEpisode> =
        Decode.object (fun get -> {
            EpisodeNumber = get.Required.Field "episode_number" Decode.int
            Name = get.Required.Field "name" Decode.string
            Overview = get.Required.Field "overview" Decode.string
            AirDate = get.Optional.Field "air_date" Decode.string
            Runtime = get.Optional.Field "runtime" Decode.int
            StillPath = get.Optional.Field "still_path" Decode.string
            VoteAverage = get.Optional.Field "vote_average" Decode.float |> Option.defaultValue 0.0
        })

    let private decodeTvSeasonResponse: Decoder<TmdbTvSeasonResponse> =
        Decode.object (fun get -> {
            SeasonNumber = get.Required.Field "season_number" Decode.int
            Name = get.Required.Field "name" Decode.string
            Overview = get.Required.Field "overview" Decode.string
            PosterPath = get.Optional.Field "poster_path" Decode.string
            AirDate = get.Optional.Field "air_date" Decode.string
            Episodes = get.Required.Field "episodes" (Decode.list decodeTvEpisode)
        })

    // ─── TV Series status mapping ───────────────────────────────────────

    let mapSeriesStatus (tmdbStatus: string) : string =
        match tmdbStatus with
        | "Returning Series" -> "Returning"
        | "Ended" -> "Ended"
        | "Canceled" -> "Canceled"
        | "In Production" -> "InProduction"
        | "Planned" -> "Planned"
        | _ -> "Unknown"

    // ─── TV Series API functions ────────────────────────────────────────

    let searchTvSeries (httpClient: HttpClient) (config: TmdbConfig) (query: string) (year: int option) : Async<Mediatheca.Shared.TmdbSearchResult list> =
        async {
            let cacheKey = match year with Some y -> $"tv:{query}:{y}" | None -> $"tv:{query}"
            match SearchCache.tryGet cacheKey with
            | Some cached -> return cached
            | None ->
                let yearParam = match year with Some y -> $"&first_air_date_year={y}" | None -> ""
                let url = $"https://api.themoviedb.org/3/search/tv?api_key={config.ApiKey}&query={System.Uri.EscapeDataString(query)}&language=en-US&page=1{yearParam}"
                let! json = fetchJson httpClient url
                match Decode.fromString decodeTvSearchResponse json with
                | Ok response ->
                    let results : Mediatheca.Shared.TmdbSearchResult list =
                        response.Results
                        |> List.map (fun r ->
                            { TmdbId = r.Id
                              Title = r.Name
                              Year = parseYear r.FirstAirDate
                              Overview = r.Overview
                              PosterPath = r.PosterPath
                              MediaType = Mediatheca.Shared.Series }
                        )
                    SearchCache.set cacheKey results
                    return results
                | Error _ -> return []
        }

    let getTvSeriesDetails (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) : Async<Result<TmdbTvDetailsResponse, string>> =
        async {
            let url = $"https://api.themoviedb.org/3/tv/{tmdbId}?api_key={config.ApiKey}&language=en-US"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeTvDetails json with
            | Ok details -> return Ok details
            | Error e -> return Error $"Failed to parse TMDB TV series details: {e}"
        }

    let getTvSeasonDetails (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) (seasonNumber: int) : Async<Result<TmdbTvSeasonResponse, string>> =
        async {
            let url = $"https://api.themoviedb.org/3/tv/{tmdbId}/season/{seasonNumber}?api_key={config.ApiKey}&language=en-US"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeTvSeasonResponse json with
            | Ok season -> return Ok season
            | Error e -> return Error $"Failed to parse TMDB TV season details: {e}"
        }

    let getTvSeriesCredits (httpClient: HttpClient) (config: TmdbConfig) (tmdbId: int) : Async<Result<TmdbCreditsResponse, string>> =
        async {
            let url = $"https://api.themoviedb.org/3/tv/{tmdbId}/credits?api_key={config.ApiKey}&language=en-US"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeCredits json with
            | Ok credits -> return Ok credits
            | Error e -> return Error $"Failed to parse TMDB TV series credits: {e}"
        }

    let downloadSeriesImages (httpClient: HttpClient) (config: TmdbConfig) (slug: string) (posterPath: string option) (backdropPath: string option) (imageBasePath: string) : Async<string option * string option> =
        async {
            let posterRef =
                match posterPath with
                | Some p ->
                    let ref = $"posters/series-{slug}.jpg"
                    try
                        downloadImage httpClient config p "w500" (System.IO.Path.Combine(imageBasePath, ref))
                        |> Async.RunSynchronously
                        Some ref
                    with _ -> None
                | None -> None
            let backdropRef =
                match backdropPath with
                | Some p ->
                    let ref = $"backdrops/series-{slug}.jpg"
                    try
                        downloadImage httpClient config p "w1280" (System.IO.Path.Combine(imageBasePath, ref))
                        |> Async.RunSynchronously
                        Some ref
                    with _ -> None
                | None -> None
            return (posterRef, backdropRef)
        }

    let downloadEpisodeStill (httpClient: HttpClient) (config: TmdbConfig) (slug: string) (seasonNumber: int) (episodeNumber: int) (stillPath: string) (imageBasePath: string) : Async<string option> =
        async {
            let ref = $"stills/{slug}-s%02d{seasonNumber}e%02d{episodeNumber}.jpg"
            try
                downloadImage httpClient config stillPath "w300" (System.IO.Path.Combine(imageBasePath, ref))
                |> Async.RunSynchronously
                return Some ref
            with _ -> return None
        }
