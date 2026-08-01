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
        // Materialization (integration-m4k7p): air date + primary image tag so a
        // season/episode the TMDB-fed projection lacks can be built from Jellyfin.
        PremiereDate: string option
        PrimaryImageTag: string option
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
            PremiereDate = get.Optional.Field "PremiereDate" Decode.string
            PrimaryImageTag =
                get.Optional.Field "ImageTags" (Decode.object (fun g -> g.Optional.Field "Primary" Decode.string))
                |> Option.bind id
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

    /// A failed Jellyfin fetch, distinguishing a rejected token (401/403 — the
    /// re-auth trigger, integration-002) from any other failure. Keeping these
    /// apart lets the orchestration decide whether to re-authenticate or to
    /// surface the error unchanged.
    type FetchError =
        | Unauthorized
        | OtherFailure of string

    /// GET the URL with the given token. A 401/403 returns `Error Unauthorized`
    /// instead of throwing (the previous `EnsureSuccessStatusCode` threw on every
    /// non-success), so the caller can decide to re-authenticate and retry.
    let private fetchJsonWithAuth (httpClient: HttpClient) (url: string) (token: string) : Async<Result<string, FetchError>> =
        async {
            try
                use request = new HttpRequestMessage(HttpMethod.Get, url)
                request.Headers.Add("Authorization", authHeader token)
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 401 || status = 403 then
                    return Error Unauthorized
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (sprintf "HTTP %d" status))
                else
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Ok body
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    /// Run a token-consuming fetch with an exactly-once re-auth-and-retry policy
    /// (integration-002, ADR 0010 follow-up).
    ///
    /// - Runs `fetch` with `token`.
    /// - On `Error Unauthorized` (a rejected token), calls `reauthenticate` once.
    ///   On success it persists the fresh token via `persist` and retries `fetch`
    ///   exactly once with the new token; a *second* `Unauthorized` is reported as
    ///   a failure, never looped.
    /// - If `reauthenticate` fails (rejected credentials, or none stored — the
    ///   caller signals that via its `Error` message), the original request is NOT
    ///   retried and a clear "re-authentication" failure is returned.
    /// - Any non-auth fetch error is passed straight through.
    ///
    /// Pure orchestration over injected effects, so it is unit-testable with plain
    /// lambdas (same pattern as JellyfinImport.syncSeriesWatchHistory).
    let withReauthRetry
        (token: string)
        (fetch: string -> Async<Result<'a, FetchError>>)
        (reauthenticate: unit -> Async<Result<JellyfinAuthResult, string>>)
        (persist: JellyfinAuthResult -> unit)
        : Async<Result<'a, string>> =
        async {
            let! first = fetch token
            match first with
            | Ok value -> return Ok value
            | Error (OtherFailure msg) -> return Error msg
            | Error Unauthorized ->
                let! reauth = reauthenticate ()
                match reauth with
                | Error e -> return Error (sprintf "Jellyfin re-authentication failed: %s" e)
                | Ok auth ->
                    persist auth
                    let! retry = fetch auth.AccessToken
                    match retry with
                    | Ok value -> return Ok value
                    | Error (OtherFailure msg) -> return Error msg
                    | Error Unauthorized ->
                        return Error "Jellyfin rejected the token again after re-authentication; aborting (no retry loop)"
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

    /// Low-level fetch that preserves the `FetchError` (so a 401/403 stays
    /// distinguishable for re-auth). A decode failure is an `OtherFailure`.
    let private fetchLibraryItems (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) (itemTypes: string) : Async<Result<JellyfinBaseItem list, FetchError>> =
        async {
            let url = sprintf "%s/Users/%s/Items?IncludeItemTypes=%s&Recursive=true&Fields=ProviderIds,Overview,Genres,PremiereDate&enableUserData=true&Limit=10000" (serverUrl.TrimEnd('/')) userId itemTypes
            let! json = fetchJsonWithAuth httpClient url token
            match json with
            | Error e -> return Error e
            | Ok body ->
                match Decode.fromString decodeItemsResponse body with
                | Ok response -> return Ok response.Items
                | Error e -> return Error (OtherFailure (sprintf "Failed to parse library response: %s" e))
        }

    let private fetchEpisodeItems (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) (seriesId: string) : Async<Result<JellyfinBaseItem list, FetchError>> =
        async {
            let url = sprintf "%s/Shows/%s/Episodes?userId=%s&Fields=ProviderIds,Overview,RunTimeTicks,PremiereDate&enableUserData=true&Limit=10000" (serverUrl.TrimEnd('/')) seriesId userId
            let! json = fetchJsonWithAuth httpClient url token
            match json with
            | Error e -> return Error e
            | Ok body ->
                match Decode.fromString decodeItemsResponse body with
                | Ok response -> return Ok response.Items
                | Error e -> return Error (OtherFailure (sprintf "Failed to parse episodes response: %s" e))
        }

    let getLibraryItems (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) (itemTypes: string) : Async<Result<JellyfinBaseItem list, string>> =
        async {
            let! result = fetchLibraryItems httpClient serverUrl userId token itemTypes
            return result |> Result.mapError (function Unauthorized -> "Unauthorized (HTTP 401/403)" | OtherFailure m -> m)
        }

    let getMovies (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) : Async<Result<JellyfinBaseItem list, string>> =
        getLibraryItems httpClient serverUrl userId token "Movie"

    let getSeries (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) : Async<Result<JellyfinBaseItem list, string>> =
        getLibraryItems httpClient serverUrl userId token "Series"

    let getEpisodes (httpClient: HttpClient) (serverUrl: string) (userId: string) (token: string) (seriesId: string) : Async<Result<JellyfinBaseItem list, string>> =
        async {
            let! result = fetchEpisodeItems httpClient serverUrl userId token seriesId
            return result |> Result.mapError (function Unauthorized -> "Unauthorized (HTTP 401/403)" | OtherFailure m -> m)
        }

    /// Build the re-auth thunk used by `withReauthRetry`. Returns a clear
    /// "re-authentication required" error when no credentials are stored, so a
    /// rejected token surfaces a meaningful `SyncFailed` rather than an opaque
    /// HTTP error (integration-002 acceptance criterion 3).
    let private reauthThunk (httpClient: HttpClient) (config: JellyfinConfig) : unit -> Async<Result<JellyfinAuthResult, string>> =
        fun () -> async {
            if System.String.IsNullOrWhiteSpace(config.Username) || System.String.IsNullOrWhiteSpace(config.Password) then
                return Error "re-authentication required: Jellyfin username/password not configured"
            else
                return! authenticate httpClient config.ServerUrl config.Username config.Password
        }

    /// Self-healing library fetch: on a 401/403 it re-authenticates once with the
    /// stored credentials, persists the fresh token via `persistAuth`, and retries
    /// once (integration-002). `config.AccessToken` / `config.UserId` are the
    /// starting token/user.
    let getLibraryItemsWithReauth (httpClient: HttpClient) (config: JellyfinConfig) (persistAuth: JellyfinAuthResult -> unit) (itemTypes: string) : Async<Result<JellyfinBaseItem list, string>> =
        withReauthRetry
            config.AccessToken
            (fun token -> fetchLibraryItems httpClient config.ServerUrl config.UserId token itemTypes)
            (reauthThunk httpClient config)
            persistAuth

    let getMoviesWithReauth (httpClient: HttpClient) (config: JellyfinConfig) (persistAuth: JellyfinAuthResult -> unit) : Async<Result<JellyfinBaseItem list, string>> =
        getLibraryItemsWithReauth httpClient config persistAuth "Movie"

    let getSeriesWithReauth (httpClient: HttpClient) (config: JellyfinConfig) (persistAuth: JellyfinAuthResult -> unit) : Async<Result<JellyfinBaseItem list, string>> =
        getLibraryItemsWithReauth httpClient config persistAuth "Series"

    let getEpisodesWithReauth (httpClient: HttpClient) (config: JellyfinConfig) (persistAuth: JellyfinAuthResult -> unit) (seriesId: string) : Async<Result<JellyfinBaseItem list, string>> =
        withReauthRetry
            config.AccessToken
            (fun token -> fetchEpisodeItems httpClient config.ServerUrl config.UserId token seriesId)
            (reauthThunk httpClient config)
            persistAuth

    /// Binary sibling of `fetchJsonWithAuth` (integration-007): identical auth
    /// header and 401/403 -> `Unauthorized` mapping, but reads the response body
    /// as bytes for the image endpoint instead of as a string.
    let private fetchImageBytesWithAuth (httpClient: HttpClient) (url: string) (token: string) : Async<Result<byte[], FetchError>> =
        async {
            try
                use request = new HttpRequestMessage(HttpMethod.Get, url)
                request.Headers.Add("Authorization", authHeader token)
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 401 || status = 403 then
                    return Error Unauthorized
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (sprintf "HTTP %d" status))
                else
                    let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                    return Ok bytes
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    /// An episode/movie's primary image, sized near TMDB's `w300`-class stills
    /// (600 for retina) and forced to Jpg so the bytes match the `.jpg`
    /// extension materialized stills are stored under (integration-007). No
    /// `PrimaryImageTag` pre-check: materialization only runs for the handful of
    /// episodes missing from the projection per sync, so an unconditional
    /// attempt is cheap and robust against `ImageTags` not being populated on
    /// the `/Shows/{id}/Episodes` response. A missing image (404) surfaces as
    /// `OtherFailure "HTTP 404"`.
    let private fetchPrimaryImage (httpClient: HttpClient) (serverUrl: string) (token: string) (itemId: string) : Async<Result<byte[], FetchError>> =
        let url = sprintf "%s/Items/%s/Images/Primary?maxWidth=600&format=Jpg" (serverUrl.TrimEnd('/')) itemId
        fetchImageBytesWithAuth httpClient url token

    /// Self-healing episode-still fetch (integration-007): built on
    /// `withReauthRetry` exactly like `getEpisodesWithReauth`, so a 401/403 on
    /// the image endpoint re-authenticates once, persists the fresh token, and
    /// retries exactly once (ADR 0011 policy, unchanged — no new auth path).
    /// Strictly best-effort by design: callers (`JellyfinImport.fetchEpisodeStill`)
    /// degrade any `Error` — missing image, non-2xx, or a failed re-auth — to
    /// `None` rather than surfacing it as a sync error.
    let getPrimaryImageWithReauth (httpClient: HttpClient) (config: JellyfinConfig) (persistAuth: JellyfinAuthResult -> unit) (itemId: string) : Async<Result<byte[], string>> =
        withReauthRetry
            config.AccessToken
            (fun token -> fetchPrimaryImage httpClient config.ServerUrl token itemId)
            (reauthThunk httpClient config)
            persistAuth
