namespace Mediatheca.Server

open System.Net.Http
open System.Text
open Thoth.Json.Net

module Jellyfin =

    type JellyfinConfig = {
        ServerUrl: string
        Username: string
        Password: string
        UserId: string
        AccessToken: string
    }

    // Internal Jellyfin API response types

    type JellyfinUserData = {
        Played: bool
        PlayCount: int
        LastPlayedDate: string option
        PlaybackPositionTicks: int64
        IsFavorite: bool
    }

    type JellyfinProviderIds = {
        Tmdb: string option
        Imdb: string option
    }

    type JellyfinBaseItem = {
        Id: string
        Name: string
        Type: string
        ProductionYear: int option
        RunTimeTicks: int64 option
        Genres: string list
        Overview: string option
        ProviderIds: JellyfinProviderIds
        UserData: JellyfinUserData option
        // Episode-specific
        SeriesName: string option
        SeriesId: string option
        IndexNumber: int option
        ParentIndexNumber: int option
    }

    type JellyfinItemsResponse = {
        Items: JellyfinBaseItem list
        TotalRecordCount: int
    }

    type JellyfinAuthResult = {
        AccessToken: string
        UserId: string
        UserName: string
    }

    // Decoders

    let private decodeUserData: Decoder<JellyfinUserData> =
        Decode.object (fun get -> {
            Played = get.Optional.Field "Played" Decode.bool |> Option.defaultValue false
            PlayCount = get.Optional.Field "PlayCount" Decode.int |> Option.defaultValue 0
            LastPlayedDate = get.Optional.Field "LastPlayedDate" Decode.string
            PlaybackPositionTicks = get.Optional.Field "PlaybackPositionTicks" Decode.int64 |> Option.defaultValue 0L
            IsFavorite = get.Optional.Field "IsFavorite" Decode.bool |> Option.defaultValue false
        })

    let private decodeProviderIds: Decoder<JellyfinProviderIds> =
        Decode.object (fun get -> {
            Tmdb = get.Optional.Field "Tmdb" Decode.string
            Imdb = get.Optional.Field "Imdb" Decode.string
        })

    let private decodeBaseItem: Decoder<JellyfinBaseItem> =
        Decode.object (fun get -> {
            Id = get.Required.Field "Id" Decode.string
            Name = get.Required.Field "Name" Decode.string
            Type = get.Optional.Field "Type" Decode.string |> Option.defaultValue ""
            ProductionYear = get.Optional.Field "ProductionYear" Decode.int
            RunTimeTicks = get.Optional.Field "RunTimeTicks" Decode.int64
            Genres = get.Optional.Field "Genres" (Decode.list Decode.string) |> Option.defaultValue []
            Overview = get.Optional.Field "Overview" Decode.string
            ProviderIds = get.Optional.Field "ProviderIds" decodeProviderIds |> Option.defaultValue { Tmdb = None; Imdb = None }
            UserData = get.Optional.Field "UserData" decodeUserData
            SeriesName = get.Optional.Field "SeriesName" Decode.string
            SeriesId = get.Optional.Field "SeriesId" Decode.string
            IndexNumber = get.Optional.Field "IndexNumber" Decode.int
            ParentIndexNumber = get.Optional.Field "ParentIndexNumber" Decode.int
        })

    let private decodeItemsResponse: Decoder<JellyfinItemsResponse> =
        Decode.object (fun get -> {
            Items = get.Optional.Field "Items" (Decode.list decodeBaseItem) |> Option.defaultValue []
            TotalRecordCount = get.Optional.Field "TotalRecordCount" Decode.int |> Option.defaultValue 0
        })

    let private decodeAuthResult: Decoder<JellyfinAuthResult> =
        Decode.object (fun get -> {
            AccessToken = get.Required.Field "AccessToken" Decode.string
            UserId = get.Required.Field "User" (Decode.object (fun get2 -> get2.Required.Field "Id" Decode.string))
            UserName = get.Required.Field "User" (Decode.object (fun get2 -> get2.Required.Field "Name" Decode.string))
        })

    // HTTP helpers

    let private authHeader (token: string) =
        sprintf "MediaBrowser Client=\"Mediatheca\", Device=\"Server\", DeviceId=\"mediatheca-server\", Version=\"1.0\", Token=%s" token

    let private authHeaderNoToken =
        "MediaBrowser Client=\"Mediatheca\", Device=\"Server\", DeviceId=\"mediatheca-server\", Version=\"1.0\""

    let private fetchJsonWithAuth (httpClient: HttpClient) (url: string) (token: string) : Async<string> =
        async {
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            request.Headers.Add("Authorization", authHeader token)
            let! response = httpClient.SendAsync(request) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return body
        }

    // Public API functions

    let authenticate (httpClient: HttpClient) (serverUrl: string) (username: string) (password: string) : Async<Result<JellyfinAuthResult, string>> =
        async {
            try
                let url = sprintf "%s/Users/AuthenticateByName" (serverUrl.TrimEnd('/'))
                let body = sprintf """{"Username":"%s","Pw":"%s"}""" (username.Replace("\"", "\\\"")) (password.Replace("\"", "\\\""))
                use request = new HttpRequestMessage(HttpMethod.Post, url)
                request.Headers.Add("Authorization", authHeaderNoToken)
                request.Content <- new StringContent(body, Encoding.UTF8, "application/json")
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                if not response.IsSuccessStatusCode then
                    return Error (sprintf "Authentication failed (HTTP %d): %s" (int response.StatusCode) responseBody)
                else
                    match Decode.fromString decodeAuthResult responseBody with
                    | Ok result -> return Ok result
                    | Error e -> return Error (sprintf "Failed to parse auth response: %s" e)
            with ex ->
                return Error (sprintf "Failed to connect to Jellyfin: %s" ex.Message)
        }

    let getLibraryItems (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) (itemTypes: string) : Async<Result<JellyfinBaseItem list, string>> =
        async {
            try
                let url = sprintf "%s/Users/%s/Items?IncludeItemTypes=%s&Recursive=true&Fields=ProviderIds,Overview,Genres,PremiereDate&enableUserData=true&Limit=10000" (serverUrl.TrimEnd('/')) userId itemTypes
                let! json = fetchJsonWithAuth httpClient url token
                match Decode.fromString decodeItemsResponse json with
                | Ok response -> return Ok response.Items
                | Error e -> return Error (sprintf "Failed to parse library response: %s" e)
            with ex ->
                return Error (sprintf "Failed to fetch library: %s" ex.Message)
        }

    let getMovies (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) : Async<Result<JellyfinBaseItem list, string>> =
        getLibraryItems httpClient serverUrl userId token "Movie"

    let getSeries (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) : Async<Result<JellyfinBaseItem list, string>> =
        getLibraryItems httpClient serverUrl userId token "Series"

    let getEpisodes (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) (seriesId: string) : Async<Result<JellyfinBaseItem list, string>> =
        async {
            try
                let url = sprintf "%s/Shows/%s/Episodes?userId=%s&Fields=ProviderIds&enableUserData=true&Limit=10000" (serverUrl.TrimEnd('/')) seriesId userId
                let! json = fetchJsonWithAuth httpClient url token
                match Decode.fromString decodeItemsResponse json with
                | Ok response -> return Ok response.Items
                | Error e -> return Error (sprintf "Failed to parse episodes response: %s" e)
            with ex ->
                return Error (sprintf "Failed to fetch episodes: %s" ex.Message)
        }
