namespace Mediatheca.Server

open System.Net.Http
open Thoth.Json.Net

module Rawg =

    type RawgConfig = {
        ApiKey: string
    }

    // Internal RAWG response types

    type RawgGenre = {
        Id: int
        Name: string
    }

    type RawgGameResult = {
        Id: int
        Name: string
        Released: string option
        BackgroundImage: string option
        Rating: float option
        Genres: RawgGenre list
    }

    type RawgSearchResponse = {
        Results: RawgGameResult list
    }

    type RawgGameDetailsResponse = {
        Id: int
        Name: string
        Released: string option
        Description: string
        DescriptionRaw: string
        BackgroundImage: string option
        BackgroundImageAdditional: string option
        Rating: float option
        Genres: RawgGenre list
    }

    // Decoders

    let private decodeGenre: Decoder<RawgGenre> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
        })

    let private decodeGameResult: Decoder<RawgGameResult> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
            Released = get.Optional.Field "released" Decode.string
            BackgroundImage = get.Optional.Field "background_image" Decode.string
            Rating = get.Optional.Field "rating" Decode.float
            Genres = get.Optional.Field "genres" (Decode.list decodeGenre) |> Option.defaultValue []
        })

    let private decodeSearchResponse: Decoder<RawgSearchResponse> =
        Decode.object (fun get -> {
            Results = get.Required.Field "results" (Decode.list decodeGameResult)
        })

    let private decodeGameDetails: Decoder<RawgGameDetailsResponse> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Required.Field "name" Decode.string
            Released = get.Optional.Field "released" Decode.string
            Description = get.Optional.Field "description" Decode.string |> Option.defaultValue ""
            DescriptionRaw = get.Optional.Field "description_raw" Decode.string |> Option.defaultValue ""
            BackgroundImage = get.Optional.Field "background_image" Decode.string
            BackgroundImageAdditional = get.Optional.Field "background_image_additional" Decode.string
            Rating = get.Optional.Field "rating" Decode.float
            Genres = get.Optional.Field "genres" (Decode.list decodeGenre) |> Option.defaultValue []
        })

    type RawgScreenshot = {
        Id: int
        Image: string
    }

    type RawgScreenshotsResponse = {
        Results: RawgScreenshot list
    }

    let private decodeScreenshot: Decoder<RawgScreenshot> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Image = get.Required.Field "image" Decode.string
        })

    let private decodeScreenshotsResponse: Decoder<RawgScreenshotsResponse> =
        Decode.object (fun get -> {
            Results = get.Required.Field "results" (Decode.list decodeScreenshot)
        })

    // Search cache

    module private SearchCache =
        open System
        open System.Collections.Concurrent

        type CacheEntry = {
            Results: Mediatheca.Shared.RawgSearchResult list
            ExpiresAt: DateTime
        }

        let private cache = ConcurrentDictionary<string, CacheEntry>()

        let tryGet (query: string) : Mediatheca.Shared.RawgSearchResult list option =
            let key = query.ToLowerInvariant().Trim()
            match cache.TryGetValue(key) with
            | true, entry ->
                if entry.ExpiresAt > DateTime.UtcNow then Some entry.Results
                else
                    cache.TryRemove(key) |> ignore
                    None
            | _ -> None

        let set (query: string) (results: Mediatheca.Shared.RawgSearchResult list) =
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

    let private parseYear (released: string option) : int option =
        released
        |> Option.bind (fun d ->
            if d.Length >= 4 then
                match System.Int32.TryParse(d.[0..3]) with
                | true, year -> Some year
                | _ -> None
            else None
        )

    let private downloadImage (httpClient: HttpClient) (imageUrl: string) (destPath: string) : Async<unit> =
        async {
            let! response = httpClient.GetAsync(imageUrl) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
            let dir = System.IO.Path.GetDirectoryName(destPath)
            if not (System.IO.Directory.Exists(dir)) then
                System.IO.Directory.CreateDirectory(dir) |> ignore
            System.IO.File.WriteAllBytes(destPath, bytes)
        }

    let searchGames (httpClient: HttpClient) (config: RawgConfig) (query: string) : Async<Mediatheca.Shared.RawgSearchResult list> =
        async {
            if System.String.IsNullOrWhiteSpace(config.ApiKey) then return []
            else
            match SearchCache.tryGet query with
            | Some cached -> return cached
            | None ->
                let url = $"https://api.rawg.io/api/games?key={config.ApiKey}&search={System.Uri.EscapeDataString(query)}&page_size=10"
                let! json = fetchJson httpClient url
                match Decode.fromString decodeSearchResponse json with
                | Ok response ->
                    let results : Mediatheca.Shared.RawgSearchResult list =
                        response.Results
                        |> List.map (fun r ->
                            { Mediatheca.Shared.RawgSearchResult.RawgId = r.Id
                              Name = r.Name
                              Year = parseYear r.Released
                              BackgroundImage = r.BackgroundImage
                              Rating = r.Rating
                              Genres = r.Genres |> List.map (fun g -> g.Name) }
                        )
                    SearchCache.set query results
                    return results
                | Error _ -> return []
        }

    let getGameDetails (httpClient: HttpClient) (config: RawgConfig) (rawgId: int) : Async<RawgGameDetailsResponse> =
        async {
            if System.String.IsNullOrWhiteSpace(config.ApiKey) then
                return failwith "RAWG API key is not configured"
            let url = $"https://api.rawg.io/api/games/{rawgId}?key={config.ApiKey}"
            let! json = fetchJson httpClient url
            match Decode.fromString decodeGameDetails json with
            | Ok details -> return details
            | Error e -> return failwith $"Failed to parse RAWG game details: {e}"
        }

    let getGameScreenshots (httpClient: HttpClient) (config: RawgConfig) (rawgId: int) : Async<Mediatheca.Shared.GameImageCandidate list> =
        async {
            if System.String.IsNullOrWhiteSpace(config.ApiKey) then return []
            else
                try
                    let url = $"https://api.rawg.io/api/games/{rawgId}/screenshots?key={config.ApiKey}"
                    let! json = fetchJson httpClient url
                    match Decode.fromString decodeScreenshotsResponse json with
                    | Ok response ->
                        return
                            response.Results
                            |> List.mapi (fun i s ->
                                { Mediatheca.Shared.GameImageCandidate.Url = s.Image
                                  Source = "RAWG"
                                  Label = $"RAWG Screenshot {i + 1}"
                                  IsCover = false })
                    | Error _ -> return []
                with _ -> return []
        }

    let downloadGameImages (httpClient: HttpClient) (slug: string) (backgroundImage: string option) (imageBasePath: string) : Async<string option * string option> =
        async {
            let coverRef =
                match backgroundImage with
                | Some url ->
                    let ref = $"posters/game-{slug}.jpg"
                    try
                        downloadImage httpClient url (System.IO.Path.Combine(imageBasePath, ref))
                        |> Async.RunSynchronously
                        Some ref
                    with _ -> None
                | None -> None
            let backdropRef =
                match backgroundImage with
                | Some url ->
                    let ref = $"backdrops/game-{slug}.jpg"
                    try
                        downloadImage httpClient url (System.IO.Path.Combine(imageBasePath, ref))
                        |> Async.RunSynchronously
                        Some ref
                    with _ -> None
                | None -> None
            return (coverRef, backdropRef)
        }
