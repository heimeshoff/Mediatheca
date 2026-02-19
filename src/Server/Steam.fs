namespace Mediatheca.Server

open System.Net.Http
open Thoth.Json.Net

module Steam =

    type SteamConfig = {
        ApiKey: string
        SteamId: string
    }

    // Internal Steam API response types

    type SteamOwnedGameResponse = {
        AppId: int
        Name: string
        PlaytimeForever: int
        ImgIconUrl: string
        RtimeLastPlayed: int
    }

    type SteamOwnedGamesResponse = {
        GameCount: int
        Games: SteamOwnedGameResponse list
    }

    type SteamVanityUrlResponse = {
        Success: int
        SteamId: string option
        Message: string option
    }

    type SteamFamilyMemberResponse = {
        Steamid: string
    }

    type SteamFamilyGroupResponse = {
        FamilyGroupid: string
        Members: SteamFamilyMemberResponse list
    }

    type SteamSharedLibraryApp = {
        Appid: int
        Name: string
        OwnerSteamids: string list
        RtTimeAcquired: int
    }

    // Decoders

    let private decodeOwnedGame: Decoder<SteamOwnedGameResponse> =
        Decode.object (fun get -> {
            AppId = get.Required.Field "appid" Decode.int
            Name = get.Optional.Field "name" Decode.string |> Option.defaultValue ""
            PlaytimeForever = get.Optional.Field "playtime_forever" Decode.int |> Option.defaultValue 0
            ImgIconUrl = get.Optional.Field "img_icon_url" Decode.string |> Option.defaultValue ""
            RtimeLastPlayed = get.Optional.Field "rtime_last_played" Decode.int |> Option.defaultValue 0
        })

    let private decodeOwnedGamesResponse: Decoder<SteamOwnedGamesResponse> =
        Decode.object (fun get ->
            let response = get.Required.Field "response" (Decode.object (fun get2 -> {
                GameCount = get2.Optional.Field "game_count" Decode.int |> Option.defaultValue 0
                Games = get2.Optional.Field "games" (Decode.list decodeOwnedGame) |> Option.defaultValue []
            }))
            response
        )

    let private decodeVanityUrlResponse: Decoder<SteamVanityUrlResponse> =
        Decode.object (fun get ->
            let response = get.Required.Field "response" (Decode.object (fun get2 -> {
                Success = get2.Required.Field "success" Decode.int
                SteamId = get2.Optional.Field "steamid" Decode.string
                Message = get2.Optional.Field "message" Decode.string
            }))
            response
        )

    let private decodeFamilyMember: Decoder<SteamFamilyMemberResponse> =
        Decode.object (fun get -> {
            Steamid = get.Required.Field "steamid" Decode.string
        })

    // Decoder for GetFamilyGroupForUser — family_groupid is directly under response
    let private decodeFamilyGroupForUserResponse: Decoder<SteamFamilyGroupResponse> =
        Decode.object (fun get ->
            let fg = get.Required.Field "response" (Decode.object (fun get2 -> {
                FamilyGroupid = get2.Required.Field "family_groupid" Decode.string
                Members = get2.Optional.Field "members" (Decode.list decodeFamilyMember) |> Option.defaultValue []
            }))
            fg
        )

    // Decoder for GetFamilyGroup — members directly under response, no family_groupid
    let private decodeFamilyGroupDetailResponse: Decoder<SteamFamilyGroupResponse> =
        Decode.object (fun get ->
            let fg = get.Required.Field "response" (Decode.object (fun get2 -> {
                FamilyGroupid = get2.Optional.Field "family_groupid" Decode.string |> Option.defaultValue ""
                Members = get2.Optional.Field "members" (Decode.list decodeFamilyMember) |> Option.defaultValue []
            }))
            fg
        )

    let private decodeSharedLibraryApp: Decoder<SteamSharedLibraryApp> =
        Decode.object (fun get -> {
            Appid = get.Required.Field "appid" Decode.int
            Name = get.Optional.Field "name" Decode.string |> Option.defaultValue ""
            OwnerSteamids = get.Optional.Field "owner_steamids" (Decode.list Decode.string) |> Option.defaultValue []
            RtTimeAcquired = get.Optional.Field "rt_time_acquired" Decode.int |> Option.defaultValue 0
        })

    let private decodeSharedLibraryApps: Decoder<SteamSharedLibraryApp list> =
        Decode.object (fun get ->
            let response = get.Required.Field "response" (Decode.object (fun get2 ->
                get2.Optional.Field "apps" (Decode.list decodeSharedLibraryApp) |> Option.defaultValue []
            ))
            response
        )

    type SteamStoreDetails = {
        ShortDescription: string
        DetailedDescription: string
        AboutTheGame: string
        WebsiteUrl: string option
        Categories: string list
        HeaderImageUrl: string option
    }

    type SteamPlayerSummary = {
        Steamid: string
        PersonaName: string
    }

    let private decodePlayerSummary: Decoder<SteamPlayerSummary> =
        Decode.object (fun get -> {
            Steamid = get.Required.Field "steamid" Decode.string
            PersonaName = get.Required.Field "personaname" Decode.string
        })

    let private decodePlayerSummaries: Decoder<SteamPlayerSummary list> =
        Decode.object (fun get ->
            let response = get.Required.Field "response" (Decode.object (fun get2 ->
                get2.Optional.Field "players" (Decode.list decodePlayerSummary) |> Option.defaultValue []
            ))
            response
        )

    let private decodeCategoryDescription: Decoder<string> =
        Decode.object (fun get ->
            get.Required.Field "description" Decode.string
        )

    let private decodeStoreData: Decoder<SteamStoreDetails> =
        Decode.object (fun get -> {
            ShortDescription = get.Optional.Field "short_description" Decode.string |> Option.defaultValue ""
            DetailedDescription = get.Optional.Field "detailed_description" Decode.string |> Option.defaultValue ""
            AboutTheGame = get.Optional.Field "about_the_game" Decode.string |> Option.defaultValue ""
            WebsiteUrl = get.Optional.Field "website" Decode.string
            Categories = get.Optional.Field "categories" (Decode.list decodeCategoryDescription) |> Option.defaultValue []
            HeaderImageUrl = get.Optional.Field "header_image" Decode.string
        })

    let private decodeStoreAppEntry: Decoder<Result<SteamStoreDetails, string>> =
        Decode.object (fun get ->
            let success = get.Required.Field "success" Decode.bool
            if success then
                let data = get.Required.Field "data" decodeStoreData
                Ok data
            else
                Error "Steam Store API returned success=false"
        )

    // Helpers

    let unixTimestampToDateString (timestamp: int) : string option =
        if timestamp = 0 then None
        else
            let dt = System.DateTimeOffset.UnixEpoch.AddSeconds(float timestamp).UtcDateTime
            Some (dt.ToString("yyyy-MM-dd"))

    // HTTP helper

    let private fetchJson (httpClient: HttpClient) (url: string) : Async<string> =
        async {
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return body
        }

    let private fetchJsonWithToken (httpClient: HttpClient) (url: string) (accessToken: string) : Async<string> =
        let separator = if url.Contains("?") then "&" else "?"
        let urlWithToken = sprintf "%s%saccess_token=%s" url separator accessToken
        fetchJson httpClient urlWithToken

    // Public API functions

    let resolveVanityUrl (httpClient: HttpClient) (apiKey: string) (vanityUrl: string) : Async<Result<string, string>> =
        async {
            try
                let url = sprintf "https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key=%s&vanityurl=%s" apiKey (System.Uri.EscapeDataString vanityUrl)
                let! json = fetchJson httpClient url
                match Decode.fromString decodeVanityUrlResponse json with
                | Ok response ->
                    if response.Success = 1 then
                        match response.SteamId with
                        | Some steamId -> return Ok steamId
                        | None -> return Error "Vanity URL resolved but no Steam ID returned"
                    else
                        return Error (response.Message |> Option.defaultValue "Failed to resolve vanity URL")
                | Error e -> return Error (sprintf "Failed to parse response: %s" e)
            with ex ->
                return Error (sprintf "Failed to resolve vanity URL: %s" ex.Message)
        }

    let getOwnedGames (httpClient: HttpClient) (config: SteamConfig) : Async<Mediatheca.Shared.SteamOwnedGame list> =
        async {
            if System.String.IsNullOrWhiteSpace(config.ApiKey) || System.String.IsNullOrWhiteSpace(config.SteamId) then
                return []
            else
                let url = sprintf "https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/?key=%s&steamid=%s&include_appinfo=true&include_played_free_games=true" config.ApiKey config.SteamId
                let! json = fetchJson httpClient url
                match Decode.fromString decodeOwnedGamesResponse json with
                | Ok response ->
                    return response.Games |> List.map (fun g ->
                        { Mediatheca.Shared.SteamOwnedGame.AppId = g.AppId
                          Name = g.Name
                          PlaytimeMinutes = g.PlaytimeForever
                          ImgIconUrl = g.ImgIconUrl
                          RtimeLastPlayed = g.RtimeLastPlayed })
                | Error _ -> return []
        }

    let getRecentlyPlayedGames (httpClient: HttpClient) (config: SteamConfig) : Async<Mediatheca.Shared.SteamOwnedGame list> =
        async {
            if System.String.IsNullOrWhiteSpace(config.ApiKey) || System.String.IsNullOrWhiteSpace(config.SteamId) then
                return []
            else
                let url = sprintf "https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v1/?key=%s&steamid=%s&include_appinfo=true" config.ApiKey config.SteamId
                let! json = fetchJson httpClient url
                match Decode.fromString decodeOwnedGamesResponse json with
                | Ok response ->
                    return response.Games |> List.map (fun g ->
                        { Mediatheca.Shared.SteamOwnedGame.AppId = g.AppId
                          Name = g.Name
                          PlaytimeMinutes = g.PlaytimeForever
                          ImgIconUrl = g.ImgIconUrl
                          RtimeLastPlayed = g.RtimeLastPlayed })
                | Error _ -> return []
        }

    let downloadSteamCover (httpClient: HttpClient) (appId: int) (slug: string) (imageBasePath: string) : Async<string option> =
        async {
            try
                let url = sprintf "https://steamcdn-a.akamaihd.net/steam/apps/%d/library_600x900.jpg" appId
                let! response = httpClient.GetAsync(url) |> Async.AwaitTask
                if response.IsSuccessStatusCode then
                    let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                    let ref = sprintf "posters/game-%s.jpg" slug
                    let destPath = System.IO.Path.Combine(imageBasePath, ref)
                    let dir = System.IO.Path.GetDirectoryName(destPath)
                    if not (System.IO.Directory.Exists(dir)) then
                        System.IO.Directory.CreateDirectory(dir) |> ignore
                    System.IO.File.WriteAllBytes(destPath, bytes)
                    return Some ref
                else
                    return None
            with _ ->
                return None
        }

    let downloadSteamBackdrop (httpClient: HttpClient) (appId: int) (slug: string) (imageBasePath: string) : Async<string option> =
        async {
            try
                let url = sprintf "https://steamcdn-a.akamaihd.net/steam/apps/%d/library_hero.jpg" appId
                let! response = httpClient.GetAsync(url) |> Async.AwaitTask
                if response.IsSuccessStatusCode then
                    let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                    let ref = sprintf "backdrops/game-%s.jpg" slug
                    let destPath = System.IO.Path.Combine(imageBasePath, ref)
                    let dir = System.IO.Path.GetDirectoryName(destPath)
                    if not (System.IO.Directory.Exists(dir)) then
                        System.IO.Directory.CreateDirectory(dir) |> ignore
                    System.IO.File.WriteAllBytes(destPath, bytes)
                    return Some ref
                else
                    return None
            with _ ->
                return None
        }

    let getFamilyGroupForUser (httpClient: HttpClient) (accessToken: string) : Async<Result<SteamFamilyGroupResponse, string>> =
        async {
            try
                let url = "https://api.steampowered.com/IFamilyGroupsService/GetFamilyGroupForUser/v1/"
                let! json = fetchJsonWithToken httpClient url accessToken
                match Decode.fromString decodeFamilyGroupForUserResponse json with
                | Ok response -> return Ok response
                | Error e -> return Error (sprintf "Failed to parse family group: %s" e)
            with ex ->
                return Error (sprintf "Failed to get family group: %s" ex.Message)
        }

    let getSharedLibraryApps (httpClient: HttpClient) (accessToken: string) (familyGroupId: string) : Async<Result<SteamSharedLibraryApp list, string>> =
        async {
            try
                let url = sprintf "https://api.steampowered.com/IFamilyGroupsService/GetSharedLibraryApps/v1/?family_groupid=%s" familyGroupId
                let! json = fetchJsonWithToken httpClient url accessToken
                match Decode.fromString decodeSharedLibraryApps json with
                | Ok apps -> return Ok apps
                | Error e -> return Error (sprintf "Failed to parse shared library: %s" e)
            with ex ->
                return Error (sprintf "Failed to get shared library: %s" ex.Message)
        }

    let getFamilyGroup (httpClient: HttpClient) (accessToken: string) (familyGroupId: string) : Async<Result<SteamFamilyGroupResponse, string>> =
        async {
            try
                let url = sprintf "https://api.steampowered.com/IFamilyGroupsService/GetFamilyGroup/v1/?family_groupid=%s" familyGroupId
                let! json = fetchJsonWithToken httpClient url accessToken
                printfn "[SteamFamily] GetFamilyGroup raw JSON (first 2000 chars): %s" (if json.Length > 2000 then json.Substring(0, 2000) + "..." else json)
                match Decode.fromString decodeFamilyGroupDetailResponse json with
                | Ok response -> return Ok response
                | Error e -> return Error (sprintf "Failed to parse family group details: %s" e)
            with ex ->
                return Error (sprintf "Failed to get family group details: %s" ex.Message)
        }

    let getPlayerSummaries (httpClient: HttpClient) (apiKey: string) (steamIds: string list) : Async<Result<SteamPlayerSummary list, string>> =
        async {
            if List.isEmpty steamIds then return Ok []
            else
                try
                    let csv = steamIds |> String.concat ","
                    let url = sprintf "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=%s&steamids=%s" apiKey csv
                    let! json = fetchJson httpClient url
                    match Decode.fromString decodePlayerSummaries json with
                    | Ok players -> return Ok players
                    | Error e -> return Error (sprintf "Failed to parse player summaries: %s" e)
                with ex ->
                    return Error (sprintf "Failed to get player summaries: %s" ex.Message)
        }

    let getSteamStoreDetails (httpClient: HttpClient) (appId: int) : Async<Result<SteamStoreDetails, string>> =
        async {
            try
                let url = sprintf "https://store.steampowered.com/api/appdetails?appids=%d" appId
                let! json = fetchJson httpClient url
                let appIdKey = string appId
                match Decode.fromString (Decode.dict decodeStoreAppEntry) json with
                | Ok dict ->
                    match dict.TryGetValue(appIdKey) with
                    | true, Ok details -> return Ok details
                    | true, Error e -> return Error e
                    | false, _ -> return Error (sprintf "No entry found for appId %d in response" appId)
                | Error e -> return Error (sprintf "Failed to parse Steam store response: %s" e)
            with ex ->
                return Error (sprintf "Failed to get Steam store details: %s" ex.Message)
        }
