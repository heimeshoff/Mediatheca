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

    let searchMovies (httpClient: HttpClient) (config: TmdbConfig) (query: string) : Async<Mediatheca.Shared.TmdbSearchResult list> =
        async {
            match SearchCache.tryGet query with
            | Some cached -> return cached
            | None ->
                let url = $"https://api.themoviedb.org/3/search/movie?api_key={config.ApiKey}&query={System.Uri.EscapeDataString(query)}"
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
                              PosterPath = r.PosterPath }
                        )
                    SearchCache.set query results
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
