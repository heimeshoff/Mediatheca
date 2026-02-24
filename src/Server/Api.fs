namespace Mediatheca.Server

open System.Data
open System.Net.Http
open System.Text.RegularExpressions
open Microsoft.Data.Sqlite
open Donald
open Giraffe
open Mediatheca.Shared

module Api =

    let private stripHtmlTags (html: string) =
        if System.String.IsNullOrEmpty(html) then ""
        else Regex.Replace(html, "<[^>]+>", "")

    let private executeCommand
        (conn: SqliteConnection)
        (streamId: string)
        (fromStoredEvent: EventStore.StoredEvent -> 'Event option)
        (reconstitute: 'Event list -> 'State)
        (decide: 'State -> 'Command -> Result<'Event list, string>)
        (toEventData: 'Event -> EventStore.EventData)
        (command: 'Command)
        (projectionHandlers: Projection.ProjectionHandler list)
        : Result<unit, string> =

        // 1. Read stream, deserialize, reconstitute
        let storedEvents = EventStore.readStream conn streamId
        let events = storedEvents |> List.choose fromStoredEvent
        let state = reconstitute events
        let currentPosition = EventStore.getStreamPosition conn streamId

        // 2. Decide
        match decide state command with
        | Error e -> Error e
        | Ok newEvents ->
            if List.isEmpty newEvents then
                Ok ()
            else
                // 3. Serialize and append
                let eventDataList = newEvents |> List.map toEventData
                match EventStore.appendToStream conn streamId currentPosition eventDataList with
                | EventStore.ConcurrencyConflict _ ->
                    Error "Concurrency conflict, please retry"
                | EventStore.Success _ ->
                    // 4. Catch-up projections
                    for handler in projectionHandlers do
                        Projection.runProjection conn handler
                    Ok ()

    let private generateUniqueSlug (conn: SqliteConnection) (streamIdFn: string -> string) (baseSlug: string) : string =
        let mutable slug = baseSlug
        let mutable suffix = 2
        while EventStore.getStreamPosition conn (streamIdFn slug) >= 0L do
            slug <- sprintf "%s-%d" baseSlug suffix
            suffix <- suffix + 1
        slug

    let private addMovieToLibrary
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (movieProjections: Projection.ProjectionHandler list)
        (tmdbId: int)
        : Async<Result<string, string>> = async {
            try
                let tmdbConfig = getTmdbConfig()
                let! details = Tmdb.getMovieDetails httpClient tmdbConfig tmdbId
                let! credits = Tmdb.getMovieCredits httpClient tmdbConfig tmdbId

                let year =
                    details.ReleaseDate
                    |> Option.bind (fun d ->
                        if d.Length >= 4 then
                            match System.Int32.TryParse(d.[0..3]) with
                            | true, y -> Some y
                            | _ -> None
                        else None)
                    |> Option.defaultValue 0

                let baseSlug = Slug.movieSlug details.Title year
                let slug = generateUniqueSlug conn Movies.streamId baseSlug
                let sid = Movies.streamId slug

                let posterRef =
                    match details.PosterPath with
                    | Some p ->
                        let ref = sprintf "posters/%s.jpg" slug
                        try
                            Tmdb.downloadImage httpClient tmdbConfig p "w500" (System.IO.Path.Combine(imageBasePath, ref))
                            |> Async.RunSynchronously
                            Some ref
                        with _ -> None
                    | None -> None

                let backdropRef =
                    match details.BackdropPath with
                    | Some p ->
                        let ref = sprintf "backdrops/%s.jpg" slug
                        try
                            Tmdb.downloadImage httpClient tmdbConfig p "w1280" (System.IO.Path.Combine(imageBasePath, ref))
                            |> Async.RunSynchronously
                            Some ref
                        with _ -> None
                    | None -> None

                let movieData: Movies.MovieAddedData = {
                    Name = details.Title
                    Year = year
                    Runtime = details.Runtime
                    Overview = details.Overview
                    Genres = details.Genres |> List.map (fun g -> g.Name)
                    PosterRef = posterRef
                    BackdropRef = backdropRef
                    TmdbId = tmdbId
                    TmdbRating = details.VoteAverage
                }

                let result =
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Add_movie_to_library movieData)
                        movieProjections

                match result with
                | Error e -> return Error e
                | Ok () ->
                    let topBilled = credits.Cast |> List.sortBy (fun c -> c.Order) |> List.truncate 10
                    for castMember in topBilled do
                        let castImageRef =
                            match castMember.ProfilePath with
                            | Some p ->
                                let ref = sprintf "cast/%d.jpg" castMember.Id
                                let destPath = System.IO.Path.Combine(imageBasePath, ref)
                                if not (ImageStore.imageExists imageBasePath ref) then
                                    try
                                        Tmdb.downloadImage httpClient tmdbConfig p "w185" destPath
                                        |> Async.RunSynchronously
                                    with _ -> ()
                                Some ref
                            | None -> None
                        let cmId = CastStore.upsertCastMember conn castMember.Name castMember.Id castImageRef
                        CastStore.addMovieCast conn sid cmId castMember.Character castMember.Order (castMember.Order < 10)

                    return Ok slug
            with ex ->
                return Error $"Failed to add movie: {ex.Message}"
        }

    let private addSeriesToLibrary
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (tmdbId: int)
        : Async<Result<string, string>> = async {
            try
                let tmdbConfig = getTmdbConfig()
                let! detailsResult = Tmdb.getTvSeriesDetails httpClient tmdbConfig tmdbId
                match detailsResult with
                | Error e -> return Error e
                | Ok details ->
                    let year =
                        details.FirstAirDate
                        |> Option.bind (fun d ->
                            if d.Length >= 4 then
                                match System.Int32.TryParse(d.[0..3]) with
                                | true, y -> Some y
                                | _ -> None
                            else None)
                        |> Option.defaultValue 0

                    let baseSlug = Slug.seriesSlug details.Name year
                    let slug = generateUniqueSlug conn Series.streamId baseSlug
                    let sid = Series.streamId slug

                    let! posterRef, backdropRef =
                        Tmdb.downloadSeriesImages httpClient tmdbConfig slug details.PosterPath details.BackdropPath imageBasePath

                    let! seasons =
                        details.Seasons
                        |> List.filter (fun s -> s.SeasonNumber > 0)
                        |> List.map (fun seasonSummary -> async {
                            let! seasonResult = Tmdb.getTvSeasonDetails httpClient tmdbConfig tmdbId seasonSummary.SeasonNumber
                            match seasonResult with
                            | Ok seasonDetails ->
                                let! episodes =
                                    seasonDetails.Episodes
                                    |> List.map (fun ep -> async {
                                        let stillRef =
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

                    let validSeasons = seasons |> Array.toList |> List.choose id

                    let episodeRuntime =
                        match details.EpisodeRunTime with
                        | first :: _ -> Some first
                        | [] -> None

                    let seriesData: Series.SeriesAddedData = {
                        Name = details.Name
                        Year = year
                        Overview = details.Overview
                        Genres = details.Genres |> List.map (fun g -> g.Name)
                        Status = Tmdb.mapSeriesStatus details.Status
                        PosterRef = posterRef
                        BackdropRef = backdropRef
                        TmdbId = tmdbId
                        TmdbRating = if details.VoteAverage > 0.0 then Some details.VoteAverage else None
                        EpisodeRuntime = episodeRuntime
                        Seasons = validSeasons
                    }

                    let result =
                        executeCommand
                            conn sid
                            Series.Serialization.fromStoredEvent
                            Series.reconstitute
                            Series.decide
                            Series.Serialization.toEventData
                            (Series.Add_series_to_library seriesData)
                            projectionHandlers

                    match result with
                    | Error e -> return Error e
                    | Ok () ->
                        let! creditsResult = Tmdb.getTvSeriesCredits httpClient tmdbConfig tmdbId
                        match creditsResult with
                        | Ok credits ->
                            let topBilled = credits.Cast |> List.sortBy (fun c -> c.Order) |> List.truncate 10
                            for castMember in topBilled do
                                let castImageRef =
                                    match castMember.ProfilePath with
                                    | Some p ->
                                        let ref = sprintf "cast/%d.jpg" castMember.Id
                                        let destPath = System.IO.Path.Combine(imageBasePath, ref)
                                        if not (ImageStore.imageExists imageBasePath ref) then
                                            try
                                                Tmdb.downloadImage httpClient tmdbConfig p "w185" destPath
                                                |> Async.RunSynchronously
                                            with _ -> ()
                                        Some ref
                                    | None -> None
                                let cmId = CastStore.upsertCastMember conn castMember.Name castMember.Id castImageRef
                                CastStore.addSeriesCast conn sid cmId castMember.Character castMember.Order (castMember.Order < 10)
                        | Error _ -> ()

                        return Ok slug
            with ex ->
                return Error $"Failed to add series: {ex.Message}"
        }

    let runSteamFamilyImport
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        (emit: SteamFamilyImportProgress -> unit)
        : Async<Result<SteamFamilyImportResult, string>> = async {
            try
                let token =
                    SettingsStore.getSetting conn "steam_family_token"
                    |> Option.defaultValue ""
                if System.String.IsNullOrWhiteSpace(token) then
                    return Error "Steam Family access token not configured"
                else
                    let! familyResult = Steam.getFamilyGroupForUser httpClient token
                    match familyResult with
                    | Error e -> return Error e
                    | Ok familyGroupBasic ->
                        let existingMembersJson =
                            SettingsStore.getSetting conn "steam_family_members"
                            |> Option.defaultValue "[]"
                        let memberDecoder =
                            Thoth.Json.Net.Decode.list (
                                Thoth.Json.Net.Decode.object (fun get -> {
                                    Mediatheca.Shared.SteamFamilyMember.SteamId = get.Required.Field "steamId" Thoth.Json.Net.Decode.string
                                    DisplayName = get.Required.Field "displayName" Thoth.Json.Net.Decode.string
                                    FriendSlug = get.Optional.Field "friendSlug" Thoth.Json.Net.Decode.string
                                    IsMe = get.Optional.Field "isMe" Thoth.Json.Net.Decode.bool |> Option.defaultValue false
                                })
                            )
                        let memberMappings =
                            match Thoth.Json.Net.Decode.fromString memberDecoder existingMembersJson with
                            | Ok m -> m
                            | Error _ -> []

                        let steamIdToFriendSlug =
                            memberMappings
                            |> List.choose (fun m ->
                                match m.FriendSlug with
                                | Some slug -> Some (m.SteamId, slug)
                                | None -> None)
                            |> Map.ofList

                        let! sharedResult = Steam.getSharedLibraryApps httpClient token familyGroupBasic.FamilyGroupid
                        match sharedResult with
                        | Error e -> return Error e
                        | Ok sharedApps ->
                            // Steam's GetSharedLibraryApps may omit the authenticated user's
                            // own Steam ID from owner_steamids. Supplement with their owned games.
                            let steamConfig = getSteamConfig()
                            let! ownedGames = Steam.getOwnedGames httpClient steamConfig
                            let userOwnedAppIds = ownedGames |> List.map (fun g -> g.AppId) |> Set.ofList
                            let userSteamId = steamConfig.SteamId

                            let enrichedApps =
                                if System.String.IsNullOrWhiteSpace(userSteamId) then sharedApps
                                else
                                    sharedApps
                                    |> List.map (fun app ->
                                        if userOwnedAppIds |> Set.contains app.Appid
                                           && not (app.OwnerSteamids |> List.contains userSteamId) then
                                            { app with OwnerSteamids = userSteamId :: app.OwnerSteamids }
                                        else app)

                            let mutable gamesProcessed = 0
                            let mutable gamesCreated = 0
                            let mutable familyOwnersSet = 0
                            let mutable errors: string list = []
                            let total = enrichedApps.Length

                            let setFamilyOwners (sid: string) (app: Steam.SteamSharedLibraryApp) =
                                for ownerSteamId in app.OwnerSteamids do
                                    if ownerSteamId = userSteamId && not (System.String.IsNullOrWhiteSpace(userSteamId)) then
                                        let result =
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                Games.Mark_as_owned
                                                projectionHandlers
                                        match result with
                                        | Ok () -> familyOwnersSet <- familyOwnersSet + 1
                                        | Error _ -> ()
                                    else
                                        match steamIdToFriendSlug |> Map.tryFind ownerSteamId with
                                        | Some friendSlug ->
                                            let result =
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Add_family_owner friendSlug)
                                                    projectionHandlers
                                            match result with
                                            | Ok () -> familyOwnersSet <- familyOwnersSet + 1
                                            | Error _ -> ()
                                        | None -> ()

                            for app in enrichedApps do
                                try
                                    gamesProcessed <- gamesProcessed + 1

                                    let existingByAppId = GameProjection.findBySteamAppId conn app.Appid
                                    match existingByAppId with
                                    | Some slug ->
                                        let sid = Games.streamId slug
                                        setFamilyOwners sid app
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_steam_library_date (Steam.unixTimestampToDateString app.RtTimeAcquired))
                                            projectionHandlers |> ignore
                                        // Fetch Steam Store details for description, website, and play modes
                                        let! storeDetails = Steam.getSteamStoreDetails httpClient app.Appid
                                        match storeDetails with
                                        | Ok details ->
                                            if details.AboutTheGame <> "" then
                                                let desc =
                                                    if details.AboutTheGame <> "" then stripHtmlTags details.AboutTheGame
                                                    elif details.DetailedDescription <> "" then stripHtmlTags details.DetailedDescription
                                                    else ""
                                                if desc <> "" then
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_short_description details.ShortDescription)
                                                        projectionHandlers |> ignore
                                            if details.WebsiteUrl.IsSome then
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Set_website_url details.WebsiteUrl)
                                                    projectionHandlers |> ignore
                                            for category in details.Categories do
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Add_play_mode category)
                                                    projectionHandlers |> ignore
                                        | Error _ -> ()
                                        emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Matched" }
                                    | None ->
                                        let existingByName =
                                            if app.Name <> "" then GameProjection.findByName conn app.Name
                                            else []
                                        match existingByName with
                                        | (slug, _) :: _ ->
                                            let sid = Games.streamId slug
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_steam_app_id app.Appid)
                                                projectionHandlers |> ignore
                                            setFamilyOwners sid app
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_steam_library_date (Steam.unixTimestampToDateString app.RtTimeAcquired))
                                                projectionHandlers |> ignore
                                            // Fetch Steam Store details for description, website, and play modes
                                            let! storeDetails = Steam.getSteamStoreDetails httpClient app.Appid
                                            match storeDetails with
                                            | Ok details ->
                                                if details.AboutTheGame <> "" then
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_short_description details.ShortDescription)
                                                        projectionHandlers |> ignore
                                                if details.WebsiteUrl.IsSome then
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_website_url details.WebsiteUrl)
                                                        projectionHandlers |> ignore
                                                for category in details.Categories do
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Add_play_mode category)
                                                        projectionHandlers |> ignore
                                            | Error _ -> ()
                                            emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Matched by name" }
                                        | [] ->
                                            if app.Name = "" then
                                                errors <- errors @ [ sprintf "App %d has no name, skipping" app.Appid ]
                                                emit { Current = gamesProcessed; Total = total; GameName = (sprintf "App %d" app.Appid); Action = "Skipped" }
                                            else
                                                let rawgConfig = getRawgConfig()
                                                let! rawgResults =
                                                    if not (System.String.IsNullOrWhiteSpace(rawgConfig.ApiKey)) then
                                                        Rawg.searchGames httpClient rawgConfig app.Name
                                                    else
                                                        async { return [] }

                                                let rawgMatch = rawgResults |> List.tryHead

                                                let rawgDescription, genres, rawgId, rawgRating, year =
                                                    match rawgMatch with
                                                    | Some r ->
                                                        let rawgYear = r.Year |> Option.defaultValue 0
                                                        "", r.Genres, Some r.RawgId, r.Rating, rawgYear
                                                    | None ->
                                                        "", [], None, None, 0

                                                // Fetch Steam Store details for description, website, and play modes
                                                let! storeDetails = Steam.getSteamStoreDetails httpClient app.Appid
                                                let steamDescription, steamShortDescription, steamWebsiteUrl, steamCategories =
                                                    match storeDetails with
                                                    | Ok details ->
                                                        let desc =
                                                            if details.AboutTheGame <> "" then stripHtmlTags details.AboutTheGame
                                                            elif details.DetailedDescription <> "" then stripHtmlTags details.DetailedDescription
                                                            else ""
                                                        desc, details.ShortDescription, details.WebsiteUrl, details.Categories
                                                    | Error _ -> "", "", None, []

                                                let description =
                                                    if steamDescription <> "" then steamDescription
                                                    elif rawgDescription <> "" then rawgDescription
                                                    else ""

                                                let baseSlug = Slug.gameSlug app.Name (if year > 0 then year else 2000)
                                                let slug = generateUniqueSlug conn Games.streamId baseSlug
                                                let! coverRef = Steam.downloadSteamCover httpClient app.Appid slug imageBasePath
                                                let! backdropRef = Steam.downloadSteamBackdrop httpClient app.Appid slug imageBasePath

                                                let gameData: Games.GameAddedData = {
                                                    Name = app.Name
                                                    Year = if year > 0 then year else 0
                                                    Genres = genres
                                                    Description = description
                                                    ShortDescription = steamShortDescription
                                                    WebsiteUrl = steamWebsiteUrl
                                                    CoverRef = coverRef
                                                    BackdropRef = backdropRef
                                                    RawgId = rawgId
                                                    RawgRating = rawgRating
                                                }

                                                let sid = Games.streamId slug
                                                let result =
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Add_game gameData)
                                                        projectionHandlers

                                                match result with
                                                | Ok () ->
                                                    gamesCreated <- gamesCreated + 1
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_steam_app_id app.Appid)
                                                        projectionHandlers |> ignore
                                                    setFamilyOwners sid app
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_steam_library_date (Steam.unixTimestampToDateString app.RtTimeAcquired))
                                                        projectionHandlers |> ignore
                                                    for category in steamCategories do
                                                        executeCommand conn sid
                                                            Games.Serialization.fromStoredEvent
                                                            Games.reconstitute
                                                            Games.decide
                                                            Games.Serialization.toEventData
                                                            (Games.Add_play_mode category)
                                                            projectionHandlers |> ignore
                                                    emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Created" }
                                                | Error e ->
                                                    errors <- errors @ [ sprintf "Failed to create '%s': %s" app.Name e ]
                                                    emit { Current = gamesProcessed; Total = total; GameName = app.Name; Action = "Error" }
                                with ex ->
                                    errors <- errors @ [ sprintf "Error processing app %d: %s" app.Appid ex.Message ]
                                    emit { Current = gamesProcessed; Total = total; GameName = (sprintf "App %d" app.Appid); Action = "Error" }

                            return Ok {
                                Mediatheca.Shared.SteamFamilyImportResult.FamilyMembers = memberMappings.Length
                                GamesProcessed = gamesProcessed
                                GamesCreated = gamesCreated
                                FamilyOwnersSet = familyOwnersSet
                                Errors = errors
                            }
            with ex ->
                return Error $"Steam Family import failed: {ex.Message}"
        }

    let steamFamilyImportHandler
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : HttpHandler =
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

                let emit (progress: SteamFamilyImportProgress) =
                    let json = sprintf "\"current\":%d,\"total\":%d,\"gameName\":\"%s\",\"action\":\"%s\""
                                    progress.Current progress.Total
                                    (progress.GameName.Replace("\\", "\\\\").Replace("\"", "\\\""))
                                    progress.Action
                    writeEvent "progress" (sprintf "{%s}" json)
                    |> Async.AwaitTask |> Async.RunSynchronously

                let! result =
                    runSteamFamilyImport conn httpClient getRawgConfig getSteamConfig imageBasePath projectionHandlers emit
                    |> Async.StartAsTask

                match result with
                | Ok r ->
                    let errorsJson =
                        r.Errors
                        |> List.map (fun e -> sprintf "\"%s\"" (e.Replace("\\", "\\\\").Replace("\"", "\\\"")))
                        |> String.concat ","
                    let json = sprintf "\"familyMembers\":%d,\"gamesProcessed\":%d,\"gamesCreated\":%d,\"familyOwnersSet\":%d,\"errors\":[%s]"
                                    r.FamilyMembers r.GamesProcessed r.GamesCreated r.FamilyOwnersSet errorsJson
                    do! writeEvent "complete" (sprintf "{%s}" json)
                | Error e ->
                    let escaped = e.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    do! writeEvent "error" (sprintf "{\"message\":\"%s\"}" escaped)

                return! earlyReturn ctx
            }

    let create
        (conn: SqliteConnection)
        (httpClient: HttpClient)
        (getTmdbConfig: unit -> Tmdb.TmdbConfig)
        (getRawgConfig: unit -> Rawg.RawgConfig)
        (getSteamConfig: unit -> Steam.SteamConfig)
        (getJellyfinConfig: unit -> Jellyfin.JellyfinConfig)
        (imageBasePath: string)
        (projectionHandlers: Projection.ProjectionHandler list)
        : IMediathecaApi =

        let movieProjections = projectionHandlers
        let friendProjections = projectionHandlers

        {
            healthCheck = fun () -> async { return "Mediatheca is running" }

            searchLibrary = fun query -> async {
                let movieResults = MovieProjection.search conn query
                let seriesResults = SeriesProjection.search conn query
                let gameResults = GameProjection.search conn query
                return movieResults @ seriesResults @ gameResults
            }

            searchTmdb = fun query -> async {
                return! Tmdb.searchMovies httpClient (getTmdbConfig()) query
            }

            addMovie = fun tmdbId ->
                addMovieToLibrary conn httpClient getTmdbConfig imageBasePath movieProjections tmdbId

            removeMovie = fun slug -> async {
                let sid = Movies.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        Movies.Remove_movie_from_library
                        movieProjections
                match result with
                | Ok () ->
                    // Remove catalog entries referencing this movie
                    let catalogEntries = CatalogProjection.getEntriesByMovieSlug conn slug
                    for (catalogSlug, entryId) in catalogEntries do
                        let catalogSid = Catalogs.streamId catalogSlug
                        executeCommand
                            conn catalogSid
                            Catalogs.Serialization.fromStoredEvent
                            Catalogs.reconstitute
                            Catalogs.decide
                            Catalogs.Serialization.toEventData
                            (Catalogs.Remove_entry entryId)
                            projectionHandlers
                        |> ignore
                    // Clean up cast and images
                    CastStore.removeMovieCastAndCleanup conn imageBasePath sid
                    ImageStore.deleteImage imageBasePath (sprintf "posters/%s.jpg" slug)
                    ImageStore.deleteImage imageBasePath (sprintf "backdrops/%s.jpg" slug)
                    return Ok ()
                | Error e -> return Error e
            }

            getMovie = fun slug -> async {
                return MovieProjection.getBySlug conn slug
            }

            getMovies = fun () -> async {
                return MovieProjection.getAll conn
            }

            categorizeMovie = fun slug genres -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Categorize_movie genres)
                        movieProjections
            }

            replacePoster = fun slug posterRef -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Replace_poster posterRef)
                        movieProjections
            }

            replaceBackdrop = fun slug backdropRef -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Replace_backdrop backdropRef)
                        movieProjections
            }

            recommendMovie = fun slug friendSlug -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Recommend_by friendSlug)
                        movieProjections
            }

            removeRecommendation = fun slug friendSlug -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_recommendation friendSlug)
                        movieProjections
            }

            wantToWatchWith = fun slug friendSlug -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Add_want_to_watch_with friendSlug)
                        movieProjections
            }

            removeWantToWatchWith = fun slug friendSlug -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_from_want_to_watch_with friendSlug)
                        movieProjections
            }

            setPersonalRating = fun slug rating -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Set_personal_rating rating)
                        movieProjections
            }

            setMovieInFocus = fun slug inFocus -> async {
                let sid = Movies.streamId slug
                let command = if inFocus then Movies.Set_movie_in_focus else Movies.Clear_movie_in_focus
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        command
                        movieProjections
            }

            // Watch Sessions
            recordWatchSession = fun slug request -> async {
                let sid = Movies.streamId slug
                let runtime =
                    conn
                    |> Db.newCommand "SELECT runtime FROM movie_detail WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.querySingle (fun (rd: IDataReader) ->
                        if rd.IsDBNull(rd.GetOrdinal("runtime")) then None
                        else Some (rd.ReadInt32 "runtime"))
                    |> Option.flatten
                let sessionId = System.Guid.NewGuid().ToString("N")
                let sessionData: Movies.WatchSessionRecordedData = {
                    SessionId = sessionId
                    Date = request.Date
                    Duration = runtime
                    FriendSlugs = request.FriendSlugs
                }
                let result =
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Record_watch_session sessionData)
                        movieProjections
                match result with
                | Ok () -> return Ok sessionId
                | Error e -> return Error e
            }

            updateWatchSessionDate = fun slug sessionId date -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Change_watch_session_date (sessionId, date))
                        movieProjections
            }

            addFriendToWatchSession = fun slug sessionId friendSlug -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Add_friend_to_watch_session (sessionId, friendSlug))
                        movieProjections
            }

            removeFriendFromWatchSession = fun slug sessionId friendSlug -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_friend_from_watch_session (sessionId, friendSlug))
                        movieProjections
            }

            removeWatchSession = fun slug sessionId -> async {
                let sid = Movies.streamId slug
                return
                    executeCommand
                        conn sid
                        Movies.Serialization.fromStoredEvent
                        Movies.reconstitute
                        Movies.decide
                        Movies.Serialization.toEventData
                        (Movies.Remove_watch_session sessionId)
                        movieProjections
            }

            getWatchSessions = fun slug -> async {
                return MovieProjection.getWatchSessions conn slug
            }

            // Content Blocks
            addContentBlock = fun slug sessionId request -> async {
                let sid = ContentBlocks.streamId slug
                let blockId = System.Guid.NewGuid().ToString("N")
                let blockData: ContentBlocks.ContentBlockData = {
                    BlockId = blockId
                    BlockType = request.BlockType
                    Content = request.Content
                    ImageRef = request.ImageRef
                    Url = request.Url
                    Caption = request.Caption
                }
                let result =
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Add_content_block (blockData, sessionId))
                        projectionHandlers
                match result with
                | Ok () -> return Ok blockId
                | Error e -> return Error e
            }

            updateContentBlock = fun slug blockId request -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Update_content_block (blockId, request.Content, request.ImageRef, request.Url, request.Caption))
                        projectionHandlers
            }

            removeContentBlock = fun slug blockId -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Remove_content_block blockId)
                        projectionHandlers
            }

            changeContentBlockType = fun slug blockId blockType -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Change_content_block_type (blockId, blockType))
                        projectionHandlers
            }

            reorderContentBlocks = fun slug sessionId blockIds -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Reorder_content_blocks (blockIds, sessionId))
                        projectionHandlers
            }

            getContentBlocks = fun slug sessionId -> async {
                match sessionId with
                | Some sid -> return ContentBlockProjection.getBySession conn slug sid
                | None -> return ContentBlockProjection.getForMovieDetail conn slug
            }

            groupContentBlocksInRow = fun slug leftId rightId rowGroup -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Group_content_blocks_in_row (leftId, rightId, rowGroup))
                        projectionHandlers
            }

            ungroupContentBlock = fun slug blockId -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Ungroup_content_block blockId)
                        projectionHandlers
            }

            uploadContentImage = fun data filename -> async {
                try
                    let ext = System.IO.Path.GetExtension(filename).ToLowerInvariant()
                    let imageId = System.Guid.NewGuid().ToString("N")
                    let ref = sprintf "content/%s%s" imageId ext
                    let destPath = System.IO.Path.Combine(imageBasePath, ref)
                    let dir = System.IO.Path.GetDirectoryName(destPath)
                    if not (System.IO.Directory.Exists(dir)) then
                        System.IO.Directory.CreateDirectory(dir) |> ignore
                    System.IO.File.WriteAllBytes(destPath, data)
                    return Ok ref
                with ex ->
                    return Error $"Failed to upload image: {ex.Message}"
            }

            // Catalogs
            createCatalog = fun request -> async {
                let baseSlug = Slug.catalogSlug request.Name
                let slug = generateUniqueSlug conn Catalogs.streamId baseSlug
                let sid = Catalogs.streamId slug
                let data: Catalogs.CatalogCreatedData = {
                    Name = request.Name
                    Description = request.Description
                    IsSorted = request.IsSorted
                }
                let result =
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Create_catalog data)
                        projectionHandlers
                match result with
                | Ok () -> return Ok slug
                | Error e -> return Error e
            }

            updateCatalog = fun slug request -> async {
                let sid = Catalogs.streamId slug
                let data: Catalogs.CatalogUpdatedData = {
                    Name = request.Name
                    Description = request.Description
                }
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Update_catalog data)
                        projectionHandlers
            }

            removeCatalog = fun slug -> async {
                let sid = Catalogs.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        Catalogs.Remove_catalog
                        projectionHandlers
            }

            getCatalog = fun slug -> async {
                return CatalogProjection.getBySlug conn slug
            }

            getCatalogs = fun () -> async {
                return CatalogProjection.getAll conn
            }

            addCatalogEntry = fun slug request -> async {
                let sid = Catalogs.streamId slug
                let entryId = System.Guid.NewGuid().ToString("N")
                let data: Catalogs.EntryAddedData = {
                    EntryId = entryId
                    MovieSlug = request.MovieSlug
                    Note = request.Note
                }
                let result =
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Add_entry data)
                        projectionHandlers
                match result with
                | Ok () -> return Ok entryId
                | Error e -> return Error e
            }

            updateCatalogEntry = fun slug entryId request -> async {
                let sid = Catalogs.streamId slug
                let data: Catalogs.EntryUpdatedData = {
                    EntryId = entryId
                    Note = request.Note
                }
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Update_entry data)
                        projectionHandlers
            }

            removeCatalogEntry = fun slug entryId -> async {
                let sid = Catalogs.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Remove_entry entryId)
                        projectionHandlers
            }

            reorderCatalogEntries = fun slug entryIds -> async {
                let sid = Catalogs.streamId slug
                return
                    executeCommand
                        conn sid
                        Catalogs.Serialization.fromStoredEvent
                        Catalogs.reconstitute
                        Catalogs.decide
                        Catalogs.Serialization.toEventData
                        (Catalogs.Reorder_entries entryIds)
                        projectionHandlers
            }

            getCatalogsForMovie = fun movieSlug -> async {
                return CatalogProjection.getCatalogsForMovie conn movieSlug
            }

            // Dashboard
            getDashboardStats = fun () -> async {
                let movieCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM movie_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let seriesCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let gameCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let friendCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM friend_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let catalogCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM catalog_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let watchSessionCount =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(md.runtime), 0) as total FROM watch_sessions ws JOIN movie_detail md ON ws.movie_slug = md.slug"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let seriesWatchTime =
                    conn
                    |> Db.newCommand """
                        SELECT COALESCE(SUM(e.runtime), 0) as total
                        FROM (SELECT DISTINCT series_slug, season_number, episode_number FROM series_episode_progress) p
                        JOIN series_episodes e ON e.series_slug = p.series_slug AND e.season_number = p.season_number AND e.episode_number = p.episode_number
                    """
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let totalPlayTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(total_play_time), 0) as total FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                return {
                    Mediatheca.Shared.DashboardStats.MovieCount = movieCount
                    SeriesCount = seriesCount
                    GameCount = gameCount
                    FriendCount = friendCount
                    CatalogCount = catalogCount
                    WatchSessionCount = watchSessionCount
                    TotalWatchTimeMinutes = totalWatchTime
                    SeriesWatchTimeMinutes = seriesWatchTime
                    TotalPlayTimeMinutes = totalPlayTime
                }
            }

            getRecentSeries = fun count -> async {
                return SeriesProjection.getRecentSeries conn count
            }

            getRecentActivity = fun count -> async {
                let events = EventStore.getRecentEvents conn count
                return events |> List.map (fun e ->
                    let description =
                        match e.EventType with
                        | "Movie_added_to_library" -> "Movie added to library"
                        | "Movie_removed_from_library" -> "Movie removed from library"
                        | "Watch_session_recorded" -> "Watch session recorded"
                        | "Watch_session_removed" -> "Watch session removed"
                        | "Friend_added" -> "Friend added"
                        | "Friend_removed" -> "Friend removed"
                        | "Catalog_created" -> "Catalog created"
                        | "Catalog_removed" -> "Catalog removed"
                        | "Entry_added" -> "Entry added to catalog"
                        | "Content_block_added" -> "Content block added"
                        | "Series_added_to_library" -> "Series added to library"
                        | "Series_removed_from_library" -> "Series removed from library"
                        | "Episode_watched" -> "Episode watched"
                        | "Episode_unwatched" -> "Episode marked unwatched"
                        | "Season_marked_watched" -> "Season marked as watched"
                        | "Episodes_watched_up_to" -> "Episodes watched up to"
                        | "Season_marked_unwatched" -> "Season marked unwatched"
                        | "Episode_watched_date_changed" -> "Episode watched date changed"
                        | "Rewatch_session_created" -> "Rewatch session created"
                        | "Rewatch_session_removed" -> "Rewatch session removed"
                        | "Series_personal_rating_set" -> "Series personal rating updated"
                        | "Series_recommended_by" -> "Series recommendation added"
                        | "Series_recommendation_removed" -> "Series recommendation removed"
                        | "Series_want_to_watch_with" -> "Want to watch series with friend"
                        | "Series_removed_want_to_watch_with" -> "Removed want to watch series with friend"
                        | "Game_added_to_library" -> "Game added to library"
                        | "Game_removed_from_library" -> "Game removed from library"
                        | "Game_status_changed" -> "Game status changed"
                        | "Game_personal_rating_set" -> "Game personal rating updated"
                        | "Game_played_with" -> "Played game with friend"
                        | "Game_played_with_removed" -> "Removed played game with friend"
                        | "Game_recommended_by" -> "Game recommendation added"
                        | "Game_recommendation_removed" -> "Game recommendation removed"
                        | "Want_to_play_with" -> "Want to play game with friend"
                        | "Removed_want_to_play_with" -> "Removed want to play game with friend"
                        | "Game_marked_as_owned" -> "Game marked as owned"
                        | "Game_ownership_removed" -> "Game ownership removed"
                        | "Game_family_owner_added" -> "Game family owner added"
                        | "Game_family_owner_removed" -> "Game family owner removed"
                        | "Game_steam_app_id_set" -> "Game linked to Steam"
                        | "Game_play_time_set" -> "Game play time updated"
                        | other -> other.Replace("_", " ")
                    { Mediatheca.Shared.RecentActivityItem.Timestamp = e.Timestamp.ToString("o")
                      StreamId = e.StreamId
                      EventType = e.EventType
                      Description = description }
                )
            }

            // Dashboard Tabs
            getDashboardAllTab = fun () -> async {
                let seriesNextUp = SeriesProjection.getDashboardSeriesNextUp conn (Some 6)
                let moviesInFocus = MovieProjection.getMoviesInFocus conn 6
                let gamesInFocus = GameProjection.getGamesInFocus conn
                let gamesRecentlyPlayed = GameProjection.getGamesRecentlyPlayed conn 6
                let playSessions = PlaytimeTracker.getDashboardPlaySessions conn 14
                let newGames = GameProjection.getDashboardNewGames conn 10
                let jellyfinServerUrl = SettingsStore.getSetting conn "jellyfin_server_url"
                return {
                    Mediatheca.Shared.DashboardAllTab.SeriesNextUp = seriesNextUp
                    MoviesInFocus = moviesInFocus
                    GamesInFocus = gamesInFocus
                    GamesRecentlyPlayed = gamesRecentlyPlayed
                    PlaySessions = playSessions
                    NewGames = newGames
                    JellyfinServerUrl =
                        jellyfinServerUrl
                        |> Option.bind (fun s -> if System.String.IsNullOrWhiteSpace(s) then None else Some s)
                }
            }

            getDashboardMoviesTab = fun () -> async {
                let recentlyAdded = MovieProjection.getRecentlyAddedMovies conn 10
                let totalMovies =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM movie_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchSessions =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(md.runtime), 0) as total FROM watch_sessions ws JOIN movie_detail md ON ws.movie_slug = md.slug"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                return {
                    Mediatheca.Shared.DashboardMoviesTab.RecentlyAdded = recentlyAdded
                    Stats = {
                        Mediatheca.Shared.DashboardMovieStats.TotalMovies = totalMovies
                        TotalWatchSessions = totalWatchSessions
                        TotalWatchTimeMinutes = totalWatchTime
                    }
                }
            }

            getDashboardSeriesTab = fun () -> async {
                let nextUp = SeriesProjection.getDashboardSeriesNextUp conn None
                let recentlyFinished = SeriesProjection.getRecentlyFinished conn
                let recentlyAbandoned = SeriesProjection.getRecentlyAbandoned conn
                let totalSeries =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalEpisodesWatched =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM (SELECT DISTINCT series_slug, season_number, episode_number FROM series_episode_progress)"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalWatchTime =
                    conn
                    |> Db.newCommand """
                        SELECT COALESCE(SUM(e.runtime), 0) as total
                        FROM (SELECT DISTINCT series_slug, season_number, episode_number FROM series_episode_progress) p
                        JOIN series_episodes e ON e.series_slug = p.series_slug AND e.season_number = p.season_number AND e.episode_number = p.episode_number
                    """
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                return {
                    Mediatheca.Shared.DashboardSeriesTab.NextUp = nextUp
                    RecentlyFinished = recentlyFinished
                    RecentlyAbandoned = recentlyAbandoned
                    Stats = {
                        Mediatheca.Shared.DashboardSeriesStats.TotalSeries = totalSeries
                        TotalEpisodesWatched = totalEpisodesWatched
                        TotalWatchTimeMinutes = totalWatchTime
                    }
                }
            }

            getDashboardGamesTab = fun () -> async {
                let recentlyAdded = GameProjection.getRecentlyAddedGames conn 10
                let recentlyPlayed = GameProjection.getGamesRecentlyPlayed conn 10
                let totalGames =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let totalPlayTime =
                    conn
                    |> Db.newCommand "SELECT COALESCE(SUM(total_play_time), 0) as total FROM game_list"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                    |> Option.defaultValue 0
                let gamesCompleted =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'Completed'"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                let gamesInProgress =
                    conn
                    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_list WHERE status = 'Playing'"
                    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
                    |> Option.defaultValue 0
                return {
                    Mediatheca.Shared.DashboardGamesTab.RecentlyAdded = recentlyAdded
                    RecentlyPlayed = recentlyPlayed
                    Stats = {
                        Mediatheca.Shared.DashboardGameStats.TotalGames = totalGames
                        TotalPlayTimeMinutes = totalPlayTime
                        GamesCompleted = gamesCompleted
                        GamesInProgress = gamesInProgress
                    }
                }
            }

            // Event Store Browser
            getEvents = fun query -> async {
                let events = EventStore.queryEvents conn query.StreamFilter query.EventTypeFilter query.Limit query.Offset
                return events |> List.map (fun e ->
                    { Mediatheca.Shared.EventDto.GlobalPosition = e.GlobalPosition
                      StreamId = e.StreamId
                      StreamPosition = e.StreamPosition
                      EventType = e.EventType
                      Data = e.Data
                      Timestamp = e.Timestamp.ToString("o") }
                )
            }

            getEventStreams = fun () -> async {
                return EventStore.getDistinctStreams conn
            }

            getEventTypes = fun () -> async {
                return EventStore.getDistinctEventTypes conn
            }

            addFriend = fun name -> async {
                let slug = Slug.friendSlug name
                let sid = Friends.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        (Friends.Add_friend (name, None))
                        friendProjections
                match result with
                | Ok () -> return Ok slug
                | Error e -> return Error e
            }

            updateFriend = fun slug name imageRef -> async {
                let sid = Friends.streamId slug
                return
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        (Friends.Update_friend (name, imageRef))
                        friendProjections
            }

            removeFriend = fun slug -> async {
                let sid = Friends.streamId slug
                let imageRef = FriendProjection.getBySlug conn slug |> Option.bind (fun f -> f.ImageRef)
                let result =
                    executeCommand
                        conn sid
                        Friends.Serialization.fromStoredEvent
                        Friends.reconstitute
                        Friends.decide
                        Friends.Serialization.toEventData
                        Friends.Remove_friend
                        friendProjections
                match result with
                | Ok () ->
                    imageRef |> Option.iter (fun ref -> ImageStore.deleteImage imageBasePath ref)
                    return Ok ()
                | Error e -> return Error e
            }

            getFriend = fun slug -> async {
                return FriendProjection.getBySlug conn slug
            }

            getFriendMedia = fun friendSlug -> async {
                let movieRec = MovieProjection.getMoviesRecommendedByFriend conn friendSlug
                let seriesRec = SeriesProjection.getSeriesRecommendedByFriend conn friendSlug
                let gameRec = GameProjection.getGamesRecommendedByFriend conn friendSlug
                let movieWant = MovieProjection.getMoviesWantToWatchWithFriend conn friendSlug
                let seriesWant = SeriesProjection.getSeriesWantToWatchWithFriend conn friendSlug
                let gameWant = GameProjection.getGamesWantToPlayWithFriend conn friendSlug
                let gamePlayed = GameProjection.getGamesPlayedWithFriend conn friendSlug
                let movieWatched = MovieProjection.getMoviesWatchedWithFriend conn friendSlug
                let seriesWatched = SeriesProjection.getSeriesWatchedWithFriend conn friendSlug
                let gamePlayedAsWatched = gamePlayed |> List.map (fun g -> { Slug = g.Slug; Name = g.Name; Year = g.Year; PosterRef = g.PosterRef; Dates = []; MediaType = g.MediaType })
                return {
                    Mediatheca.Shared.FriendMedia.Recommended = (movieRec @ seriesRec @ gameRec) |> List.sortBy (fun i -> i.Name)
                    WantToWatch = (movieWant @ seriesWant @ gameWant) |> List.sortBy (fun i -> i.Name)
                    Watched = (movieWatched @ seriesWatched @ gamePlayedAsWatched) |> List.sortBy (fun i -> i.Name)
                }
            }

            getFriends = fun () -> async {
                return FriendProjection.getAll conn
            }

            uploadFriendImage = fun slug data filename -> async {
                let ext = System.IO.Path.GetExtension(filename).ToLowerInvariant()
                let ref = sprintf "friends/%s%s" slug ext
                ImageStore.saveImage imageBasePath ref data
                let sid = Friends.streamId slug
                let friend = FriendProjection.getBySlug conn slug
                match friend with
                | Some f ->
                    let result =
                        executeCommand
                            conn sid
                            Friends.Serialization.fromStoredEvent
                            Friends.reconstitute
                            Friends.decide
                            Friends.Serialization.toEventData
                            (Friends.Update_friend (f.Name, Some ref))
                            friendProjections
                    match result with
                    | Ok () -> return Ok ref
                    | Error e -> return Error e
                | None -> return Error "Friend not found"
            }

            getTmdbApiKey = fun () -> async {
                let key =
                    SettingsStore.getSetting conn "tmdb_api_key"
                    |> Option.defaultValue ""
                if key.Length > 4 then
                    return sprintf "****%s" (key.Substring(key.Length - 4))
                elif key.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setTmdbApiKey = fun key -> async {
                try
                    SettingsStore.setSetting conn "tmdb_api_key" key
                    return Ok ()
                with ex ->
                    return Error $"Failed to save API key: {ex.Message}"
            }

            testTmdbApiKey = fun key -> async {
                try
                    let testConfig: Tmdb.TmdbConfig = {
                        ApiKey = key
                        ImageBaseUrl = "https://image.tmdb.org/t/p/"
                    }
                    let! results = Tmdb.searchMovies httpClient testConfig "test"
                    return Ok ()
                with ex ->
                    return Error $"TMDB API key validation failed: {ex.Message}"
            }

            getMovieTrailer = fun tmdbId -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    return! Tmdb.getMovieTrailer httpClient tmdbConfig tmdbId
                with _ ->
                    return None
            }

            getSeriesTrailer = fun tmdbId -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    return! Tmdb.getSeriesTrailer httpClient tmdbConfig tmdbId
                with _ ->
                    return None
            }

            getSeasonTrailer = fun tmdbId seasonNumber -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    return! Tmdb.getSeasonTrailer httpClient tmdbConfig tmdbId seasonNumber
                with _ ->
                    return None
            }

            getFullCredits = fun tmdbId -> async {
                try
                    let tmdbConfig = getTmdbConfig()
                    let! credits = Tmdb.getMovieCredits httpClient tmdbConfig tmdbId
                    let imageUrl (profilePath: string option) =
                        match profilePath with
                        | Some p -> Some $"{tmdbConfig.ImageBaseUrl}w185{p}"
                        | None -> None
                    let cast =
                        credits.Cast
                        |> List.sortBy (fun c -> c.Order)
                        |> List.map (fun c ->
                            { CastMemberDto.Name = c.Name
                              Role = c.Character
                              TmdbId = c.Id
                              ImageRef = imageUrl c.ProfilePath })
                    let crew =
                        credits.Crew
                        |> List.map (fun c ->
                            { CrewMemberDto.Name = c.Name
                              Job = c.Job
                              Department = c.Department
                              TmdbId = c.Id
                              ImageRef = imageUrl c.ProfilePath })
                    return Ok { FullCreditsDto.Cast = cast; Crew = crew }
                with ex ->
                    return Error $"Failed to load full credits: {ex.Message}"
            }

            // TV Series
            searchTvSeries = fun query -> async {
                return! Tmdb.searchTvSeries httpClient (getTmdbConfig()) query
            }

            addSeries = fun tmdbId ->
                addSeriesToLibrary conn httpClient getTmdbConfig imageBasePath projectionHandlers tmdbId

            removeSeries = fun slug -> async {
                let sid = Series.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        Series.Remove_series
                        projectionHandlers
                match result with
                | Ok () ->
                    // Remove catalog entries referencing this series
                    let catalogEntries = CatalogProjection.getEntriesByMovieSlug conn slug
                    for (catalogSlug, entryId) in catalogEntries do
                        let catalogSid = Catalogs.streamId catalogSlug
                        executeCommand
                            conn catalogSid
                            Catalogs.Serialization.fromStoredEvent
                            Catalogs.reconstitute
                            Catalogs.decide
                            Catalogs.Serialization.toEventData
                            (Catalogs.Remove_entry entryId)
                            projectionHandlers
                        |> ignore
                    // Clean up cast and images
                    CastStore.removeSeriesCastAndCleanup conn imageBasePath sid
                    ImageStore.deleteImage imageBasePath (sprintf "posters/series-%s.jpg" slug)
                    ImageStore.deleteImage imageBasePath (sprintf "backdrops/series-%s.jpg" slug)
                    // Clean up episode stills
                    let stillsDir = System.IO.Path.Combine(imageBasePath, "stills")
                    if System.IO.Directory.Exists(stillsDir) then
                        let stillFiles = System.IO.Directory.GetFiles(stillsDir, sprintf "%s-s*.jpg" slug)
                        for f in stillFiles do
                            try System.IO.File.Delete(f) with _ -> ()
                    return Ok ()
                | Error e -> return Error e
            }

            abandonSeries = fun slug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        Series.Abandon_series
                        projectionHandlers
            }

            unabandonSeries = fun slug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        Series.Unabandon_series
                        projectionHandlers
            }

            getSeries = fun () -> async {
                return SeriesProjection.getAll conn
            }

            getSeriesDetail = fun slug rewatchId -> async {
                return SeriesProjection.getBySlug conn slug rewatchId
            }

            setSeriesPersonalRating = fun slug rating -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Set_series_personal_rating rating)
                        projectionHandlers
            }

            setSeriesInFocus = fun slug inFocus -> async {
                let sid = Series.streamId slug
                let command = if inFocus then Series.Set_series_in_focus else Series.Clear_series_in_focus
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        command
                        projectionHandlers
            }

            addSeriesRecommendation = fun slug friendSlug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Recommend_series friendSlug)
                        projectionHandlers
            }

            removeSeriesRecommendation = fun slug friendSlug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_series_recommendation friendSlug)
                        projectionHandlers
            }

            addSeriesWantToWatchWith = fun slug friendSlug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Want_to_watch_series_with friendSlug)
                        projectionHandlers
            }

            removeSeriesWantToWatchWith = fun slug friendSlug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_want_to_watch_series_with friendSlug)
                        projectionHandlers
            }

            // Series Rewatch Sessions
            createRewatchSession = fun slug request -> async {
                let sid = Series.streamId slug
                let rewatchId = System.Guid.NewGuid().ToString("N")
                let data: Series.RewatchSessionCreatedData = {
                    RewatchId = rewatchId
                    Name = request.Name
                    FriendSlugs = request.FriendSlugs
                }
                let result =
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Create_rewatch_session data)
                        projectionHandlers
                match result with
                | Ok () -> return Ok rewatchId
                | Error e -> return Error e
            }

            removeRewatchSession = fun slug rewatchId -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_rewatch_session rewatchId)
                        projectionHandlers
            }

            setDefaultRewatchSession = fun slug rewatchId -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Set_default_rewatch_session rewatchId)
                        projectionHandlers
            }

            addFriendToRewatchSession = fun slug rewatchId friendSlug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Add_friend_to_rewatch_session { RewatchId = rewatchId; FriendSlug = friendSlug })
                        projectionHandlers
            }

            removeFriendFromRewatchSession = fun slug rewatchId friendSlug -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Remove_friend_from_rewatch_session { RewatchId = rewatchId; FriendSlug = friendSlug })
                        projectionHandlers
            }

            // Series Episode Progress
            markEpisodeWatched = fun slug request -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_episode_watched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            markEpisodeUnwatched = fun slug request -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_episode_unwatched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                        })
                        projectionHandlers
            }

            markSeasonWatched = fun slug request -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_season_watched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            markEpisodesWatchedUpTo = fun slug request -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_episodes_watched_up_to {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            markSeasonUnwatched = fun slug request -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Mark_season_unwatched {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                        })
                        projectionHandlers
            }

            updateEpisodeWatchedDate = fun slug request -> async {
                let sid = Series.streamId slug
                return
                    executeCommand
                        conn sid
                        Series.Serialization.fromStoredEvent
                        Series.reconstitute
                        Series.decide
                        Series.Serialization.toEventData
                        (Series.Change_episode_watched_date {
                            RewatchId = request.RewatchId
                            SeasonNumber = request.SeasonNumber
                            EpisodeNumber = request.EpisodeNumber
                            Date = request.Date
                        })
                        projectionHandlers
            }

            // Series Content Blocks + Catalogs
            getSeriesContentBlocks = fun slug -> async {
                return ContentBlockProjection.getForMovieDetail conn slug
            }

            addSeriesContentBlock = fun slug request -> async {
                let sid = ContentBlocks.streamId slug
                let blockId = System.Guid.NewGuid().ToString("N")
                let blockData: ContentBlocks.ContentBlockData = {
                    BlockId = blockId
                    BlockType = request.BlockType
                    Content = request.Content
                    ImageRef = request.ImageRef
                    Url = request.Url
                    Caption = request.Caption
                }
                let result =
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Add_content_block (blockData, None))
                        projectionHandlers
                match result with
                | Ok () -> return Ok blockId
                | Error e -> return Error e
            }

            updateSeriesContentBlock = fun slug blockId request -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Update_content_block (blockId, request.Content, request.ImageRef, request.Url, request.Caption))
                        projectionHandlers
            }

            removeSeriesContentBlock = fun slug blockId -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Remove_content_block blockId)
                        projectionHandlers
            }

            getCatalogsForSeries = fun slug -> async {
                return CatalogProjection.getCatalogsForSeriesWithChildren conn slug
            }

            // Games
            searchRawgGames = fun query -> async {
                return! Rawg.searchGames httpClient (getRawgConfig()) query
            }

            addGame = fun request -> async {
                try
                    let year = request.Year
                    let baseSlug = Slug.gameSlug request.Name year
                    let slug = generateUniqueSlug conn Games.streamId baseSlug
                    let sid = Games.streamId slug

                    // If we have a RAWG ID, fetch full details for description + download images
                    let! description, coverRef, backdropRef =
                        match request.RawgId with
                        | Some rawgId ->
                            async {
                                let rawgConfig = getRawgConfig()
                                // Fetch full game details (includes description)
                                let! details =
                                    async {
                                        try
                                            let! d = Rawg.getGameDetails httpClient rawgConfig rawgId
                                            return Some d
                                        with _ -> return None
                                    }

                                let desc =
                                    match details with
                                    | Some d when d.DescriptionRaw <> "" -> d.DescriptionRaw
                                    | _ -> request.Description

                                // Download images locally
                                let bgImage =
                                    match details with
                                    | Some d -> d.BackgroundImage |> Option.orElse request.CoverRef
                                    | None -> request.CoverRef

                                let bgImageAdditional =
                                    match details with
                                    | Some d -> d.BackgroundImageAdditional
                                    | None -> None

                                let! coverRef, backdropRef = Rawg.downloadGameImages httpClient slug bgImage bgImageAdditional imageBasePath
                                return desc, coverRef, backdropRef
                            }
                        | None ->
                            async { return request.Description, request.CoverRef, request.BackdropRef }

                    let gameData: Games.GameAddedData = {
                        Name = request.Name
                        Year = year
                        Genres = request.Genres
                        Description = description
                        ShortDescription = ""
                        WebsiteUrl = None
                        CoverRef = coverRef
                        BackdropRef = backdropRef
                        RawgId = request.RawgId
                        RawgRating = request.RawgRating
                    }

                    let result =
                        executeCommand
                            conn sid
                            Games.Serialization.fromStoredEvent
                            Games.reconstitute
                            Games.decide
                            Games.Serialization.toEventData
                            (Games.Add_game gameData)
                            projectionHandlers

                    match result with
                    | Error e -> return Error e
                    | Ok () -> return Ok slug
                with ex ->
                    return Error $"Failed to add game: {ex.Message}"
            }

            removeGame = fun slug -> async {
                let sid = Games.streamId slug
                let result =
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        Games.Remove_game
                        projectionHandlers
                match result with
                | Ok () ->
                    // Remove catalog entries referencing this game
                    let catalogEntries = CatalogProjection.getEntriesByMovieSlug conn slug
                    for (catalogSlug, entryId) in catalogEntries do
                        let catalogSid = Catalogs.streamId catalogSlug
                        executeCommand
                            conn catalogSid
                            Catalogs.Serialization.fromStoredEvent
                            Catalogs.reconstitute
                            Catalogs.decide
                            Catalogs.Serialization.toEventData
                            (Catalogs.Remove_entry entryId)
                            projectionHandlers
                        |> ignore
                    // Clean up images
                    ImageStore.deleteImage imageBasePath (sprintf "posters/game-%s.jpg" slug)
                    ImageStore.deleteImage imageBasePath (sprintf "backdrops/game-%s.jpg" slug)
                    return Ok ()
                | Error e -> return Error e
            }

            getGames = fun () -> async {
                return GameProjection.getAll conn
            }

            getGameDetail = fun slug -> async {
                return GameProjection.getBySlug conn slug
            }

            setGameStatus = fun slug status -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Change_status status)
                        projectionHandlers
            }

            setGamePersonalRating = fun slug rating -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Set_personal_rating rating)
                        projectionHandlers
            }

            setGameHltbHours = fun slug hours -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Set_hltb_hours hours)
                        projectionHandlers
            }

            addGameRecommendation = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Recommend_game friendSlug)
                        projectionHandlers
            }

            removeGameRecommendation = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_recommendation friendSlug)
                        projectionHandlers
            }

            addGameWantToPlayWith = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Add_want_to_play_with friendSlug)
                        projectionHandlers
            }

            removeGameWantToPlayWith = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_from_want_to_play_with friendSlug)
                        projectionHandlers
            }

            addGamePlayMode = fun slug playMode -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Add_play_mode playMode)
                        projectionHandlers
            }

            removeGamePlayMode = fun slug playMode -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_play_mode playMode)
                        projectionHandlers
            }

            getAllPlayModes = fun () -> async {
                return GameProjection.getAllPlayModes conn
            }

            markGameAsOwned = fun slug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        Games.Mark_as_owned
                        projectionHandlers
            }

            removeGameOwnership = fun slug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        Games.Remove_ownership
                        projectionHandlers
            }

            addGameFamilyOwner = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Add_family_owner friendSlug)
                        projectionHandlers
            }

            removeGameFamilyOwner = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_family_owner friendSlug)
                        projectionHandlers
            }

            addGamePlayedWith = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Add_played_with friendSlug)
                        projectionHandlers
            }

            removeGamePlayedWith = fun slug friendSlug -> async {
                let sid = Games.streamId slug
                return
                    executeCommand
                        conn sid
                        Games.Serialization.fromStoredEvent
                        Games.reconstitute
                        Games.decide
                        Games.Serialization.toEventData
                        (Games.Remove_played_with friendSlug)
                        projectionHandlers
            }

            // Game Content Blocks + Catalogs
            getGameContentBlocks = fun slug -> async {
                return ContentBlockProjection.getForMovieDetail conn slug
            }

            addGameContentBlock = fun slug request -> async {
                let sid = ContentBlocks.streamId slug
                let blockId = System.Guid.NewGuid().ToString("N")
                let blockData: ContentBlocks.ContentBlockData = {
                    BlockId = blockId
                    BlockType = request.BlockType
                    Content = request.Content
                    ImageRef = request.ImageRef
                    Url = request.Url
                    Caption = request.Caption
                }
                let result =
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Add_content_block (blockData, None))
                        projectionHandlers
                match result with
                | Ok () -> return Ok blockId
                | Error e -> return Error e
            }

            updateGameContentBlock = fun slug blockId request -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Update_content_block (blockId, request.Content, request.ImageRef, request.Url, request.Caption))
                        projectionHandlers
            }

            removeGameContentBlock = fun slug blockId -> async {
                let sid = ContentBlocks.streamId slug
                return
                    executeCommand
                        conn sid
                        ContentBlocks.Serialization.fromStoredEvent
                        ContentBlocks.reconstitute
                        ContentBlocks.decide
                        ContentBlocks.Serialization.toEventData
                        (ContentBlocks.Remove_content_block blockId)
                        projectionHandlers
            }

            getCatalogsForGame = fun slug -> async {
                return CatalogProjection.getCatalogsForMovie conn slug
            }

            getGameImageCandidates = fun slug -> async {
                match GameProjection.getBySlug conn slug with
                | None -> return []
                | Some game ->
                    let currentCandidates =
                        [ match game.CoverRef with
                          | Some ref ->
                              { GameImageCandidate.Url = $"/images/{ref}"
                                Source = "Current"; Label = "Current Cover"; IsCover = true; IsCurrent = true }
                          | None -> ()
                          match game.BackdropRef with
                          | Some ref ->
                              { GameImageCandidate.Url = $"/images/{ref}"
                                Source = "Current"; Label = "Current Backdrop"; IsCover = false; IsCurrent = true }
                          | None -> () ]

                    let steamCandidates =
                        match game.SteamAppId with
                        | Some appId ->
                            [ { GameImageCandidate.Url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_600x900_2x.jpg"
                                Source = "Steam"; Label = "Steam Library Cover"; IsCover = true; IsCurrent = false }
                              { Url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/library_hero.jpg"
                                Source = "Steam"; Label = "Steam Library Hero"; IsCover = false; IsCurrent = false }
                              { Url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg"
                                Source = "Steam"; Label = "Steam Header"; IsCover = false; IsCurrent = false } ]
                        | None -> []

                    let! rawgCandidates = async {
                        match game.RawgId with
                        | Some rawgId ->
                            let rawgConfig = getRawgConfig()
                            let! details = async {
                                try
                                    let! d = Rawg.getGameDetails httpClient rawgConfig rawgId
                                    return Some d
                                with _ -> return None
                            }
                            let detailCandidates =
                                match details with
                                | Some (d: Rawg.RawgGameDetailsResponse) ->
                                    [ match d.BackgroundImage with
                                      | Some url ->
                                          { GameImageCandidate.Url = url; Source = "RAWG"; Label = "RAWG Background"; IsCover = false; IsCurrent = false }
                                      | None -> ()
                                      match d.BackgroundImageAdditional with
                                      | Some url ->
                                          { Url = url; Source = "RAWG"; Label = "RAWG Background 2"; IsCover = false; IsCurrent = false }
                                      | None -> () ]
                                | None -> []
                            let! screenshots = Rawg.getGameScreenshots httpClient rawgConfig rawgId
                            return detailCandidates @ screenshots
                        | None -> return []
                    }

                    return currentCandidates @ steamCandidates @ rawgCandidates
            }

            selectGameImage = fun slug sourceUrl imageKind -> async {
                try
                    let! response = httpClient.GetAsync(sourceUrl) |> Async.AwaitTask
                    response.EnsureSuccessStatusCode() |> ignore
                    let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                    let ref =
                        if imageKind = "cover" then $"posters/game-{slug}.jpg"
                        else $"backdrops/game-{slug}.jpg"
                    ImageStore.saveImage imageBasePath ref bytes
                    let sid = Games.streamId slug
                    let command =
                        if imageKind = "cover" then Games.Replace_cover ref
                        else Games.Replace_backdrop ref
                    return
                        executeCommand
                            conn sid
                            Games.Serialization.fromStoredEvent
                            Games.reconstitute
                            Games.decide
                            Games.Serialization.toEventData
                            command
                            projectionHandlers
                with ex ->
                    return Error $"Failed to download image: {ex.Message}"
            }

            // Games Settings
            getRawgApiKey = fun () -> async {
                let key =
                    SettingsStore.getSetting conn "rawg_api_key"
                    |> Option.defaultValue ""
                if key.Length > 4 then
                    return sprintf "****%s" (key.Substring(key.Length - 4))
                elif key.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setRawgApiKey = fun key -> async {
                try
                    SettingsStore.setSetting conn "rawg_api_key" key
                    return Ok ()
                with ex ->
                    return Error $"Failed to save API key: {ex.Message}"
            }

            testRawgApiKey = fun key -> async {
                try
                    let testConfig: Rawg.RawgConfig = {
                        ApiKey = key
                    }
                    let! _ = Rawg.searchGames httpClient testConfig "test"
                    return Ok ()
                with ex ->
                    return Error $"RAWG API key validation failed: {ex.Message}"
            }

            // Steam Integration
            getSteamApiKey = fun () -> async {
                let key =
                    SettingsStore.getSetting conn "steam_api_key"
                    |> Option.defaultValue ""
                if key.Length > 4 then
                    return sprintf "****%s" (key.Substring(key.Length - 4))
                elif key.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setSteamApiKey = fun key -> async {
                try
                    SettingsStore.setSetting conn "steam_api_key" key
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Steam API key: {ex.Message}"
            }

            testSteamApiKey = fun key -> async {
                try
                    let testConfig: Steam.SteamConfig = {
                        ApiKey = key
                        SteamId = "76561197960435530" // Robin Walker (Valve employee, public profile)
                    }
                    let! games = Steam.getOwnedGames httpClient testConfig
                    if List.isEmpty games then
                        return Error "API key accepted but returned no results (may be invalid)"
                    else
                        return Ok ()
                with ex ->
                    return Error $"Steam API key validation failed: {ex.Message}"
            }

            getSteamId = fun () -> async {
                return
                    SettingsStore.getSetting conn "steam_id"
                    |> Option.defaultValue ""
            }

            setSteamId = fun steamId -> async {
                try
                    SettingsStore.setSetting conn "steam_id" steamId
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Steam ID: {ex.Message}"
            }

            resolveSteamVanityUrl = fun vanityUrl -> async {
                let steamConfig = getSteamConfig()
                if System.String.IsNullOrWhiteSpace(steamConfig.ApiKey) then
                    return Error "Steam API key not configured"
                else
                    return! Steam.resolveVanityUrl httpClient steamConfig.ApiKey vanityUrl
            }

            importSteamLibrary = fun () -> async {
                try
                    let steamConfig = getSteamConfig()
                    if System.String.IsNullOrWhiteSpace(steamConfig.ApiKey) || System.String.IsNullOrWhiteSpace(steamConfig.SteamId) then
                        return Error "Steam API key and Steam ID must be configured"
                    else
                        let! steamGames = Steam.getOwnedGames httpClient steamConfig
                        let mutable gamesMatched = 0
                        let mutable gamesCreated = 0
                        let mutable playTimeUpdated = 0
                        let mutable errors: string list = []

                        for steamGame in steamGames do
                            try
                                // Try to match by steam_app_id first
                                let existingByAppId = GameProjection.findBySteamAppId conn steamGame.AppId
                                match existingByAppId with
                                | Some slug ->
                                    // Matched by steam_app_id — update play time
                                    gamesMatched <- gamesMatched + 1
                                    let sid = Games.streamId slug
                                    if steamGame.PlaytimeMinutes > 0 then
                                        let result =
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_play_time steamGame.PlaytimeMinutes)
                                                projectionHandlers
                                        match result with
                                        | Ok () -> playTimeUpdated <- playTimeUpdated + 1
                                        | Error _ -> ()
                                    executeCommand conn sid
                                        Games.Serialization.fromStoredEvent
                                        Games.reconstitute
                                        Games.decide
                                        Games.Serialization.toEventData
                                        (Games.Set_steam_last_played (Steam.unixTimestampToDateString steamGame.RtimeLastPlayed))
                                        projectionHandlers |> ignore
                                    executeCommand conn sid
                                        Games.Serialization.fromStoredEvent
                                        Games.reconstitute
                                        Games.decide
                                        Games.Serialization.toEventData
                                        Games.Mark_as_owned
                                        projectionHandlers |> ignore
                                | None ->
                                    // Try to match by name
                                    let existingByName = GameProjection.findByName conn steamGame.Name
                                    match existingByName with
                                    | (slug, _) :: _ ->
                                        // Matched by name — set steam_app_id, add store, update play time
                                        gamesMatched <- gamesMatched + 1
                                        let sid = Games.streamId slug
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_steam_app_id steamGame.AppId)
                                            projectionHandlers |> ignore
                                        if steamGame.PlaytimeMinutes > 0 then
                                            let result =
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Set_play_time steamGame.PlaytimeMinutes)
                                                    projectionHandlers
                                            match result with
                                            | Ok () -> playTimeUpdated <- playTimeUpdated + 1
                                            | Error _ -> ()
                                        // Fetch Steam Store details for description, website, and play modes
                                        let! storeDetails = Steam.getSteamStoreDetails httpClient steamGame.AppId
                                        match storeDetails with
                                        | Ok details ->
                                            if details.AboutTheGame <> "" then
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Set_short_description details.ShortDescription)
                                                    projectionHandlers |> ignore
                                            if details.WebsiteUrl.IsSome then
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Set_website_url details.WebsiteUrl)
                                                    projectionHandlers |> ignore
                                            for category in details.Categories do
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Add_play_mode category)
                                                    projectionHandlers |> ignore
                                        | Error _ -> ()
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_steam_last_played (Steam.unixTimestampToDateString steamGame.RtimeLastPlayed))
                                            projectionHandlers |> ignore
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            Games.Mark_as_owned
                                            projectionHandlers |> ignore
                                    | [] ->
                                        // No match — create new game
                                        // Try RAWG enrichment
                                        let rawgConfig = getRawgConfig()
                                        let! rawgResults =
                                            if not (System.String.IsNullOrWhiteSpace(rawgConfig.ApiKey)) then
                                                Rawg.searchGames httpClient rawgConfig steamGame.Name
                                            else
                                                async { return [] }

                                        let rawgMatch = rawgResults |> List.tryHead

                                        let rawgDescription, genres, rawgId, rawgRating, year =
                                            match rawgMatch with
                                            | Some r ->
                                                let rawgYear = r.Year |> Option.defaultValue 0
                                                "", r.Genres, Some r.RawgId, r.Rating, rawgYear
                                            | None ->
                                                "", [], None, None, 0

                                        // Fetch Steam Store details for description, website, and play modes
                                        let! storeDetails = Steam.getSteamStoreDetails httpClient steamGame.AppId
                                        let steamDescription, steamShortDescription, steamWebsiteUrl, steamCategories =
                                            match storeDetails with
                                            | Ok details ->
                                                let desc =
                                                    if details.AboutTheGame <> "" then stripHtmlTags details.AboutTheGame
                                                    elif details.DetailedDescription <> "" then stripHtmlTags details.DetailedDescription
                                                    else ""
                                                desc, details.ShortDescription, details.WebsiteUrl, details.Categories
                                            | Error _ -> "", "", None, []

                                        // Use Steam description if available, then RAWG, then empty
                                        let description =
                                            if steamDescription <> "" then steamDescription
                                            elif rawgDescription <> "" then rawgDescription
                                            else ""

                                        // Download cover and backdrop from Steam CDN
                                        let baseSlug = Slug.gameSlug steamGame.Name (if year > 0 then year else 2000)
                                        let slug = generateUniqueSlug conn Games.streamId baseSlug
                                        let! coverRef = Steam.downloadSteamCover httpClient steamGame.AppId slug imageBasePath
                                        let! backdropRef = Steam.downloadSteamBackdrop httpClient steamGame.AppId slug imageBasePath

                                        let gameData: Games.GameAddedData = {
                                            Name = steamGame.Name
                                            Year = if year > 0 then year else 0
                                            Genres = genres
                                            Description = description
                                            ShortDescription = steamShortDescription
                                            WebsiteUrl = steamWebsiteUrl
                                            CoverRef = coverRef
                                            BackdropRef = backdropRef
                                            RawgId = rawgId
                                            RawgRating = rawgRating
                                        }

                                        let sid = Games.streamId slug
                                        let result =
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Add_game gameData)
                                                projectionHandlers

                                        match result with
                                        | Ok () ->
                                            gamesCreated <- gamesCreated + 1
                                            // Set steam_app_id and store
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_steam_app_id steamGame.AppId)
                                                projectionHandlers |> ignore
                                            if steamGame.PlaytimeMinutes > 0 then
                                                let ptResult =
                                                    executeCommand conn sid
                                                        Games.Serialization.fromStoredEvent
                                                        Games.reconstitute
                                                        Games.decide
                                                        Games.Serialization.toEventData
                                                        (Games.Set_play_time steamGame.PlaytimeMinutes)
                                                        projectionHandlers
                                                match ptResult with
                                                | Ok () -> playTimeUpdated <- playTimeUpdated + 1
                                                | Error _ -> ()
                                            // Add play modes from Steam categories
                                            for category in steamCategories do
                                                executeCommand conn sid
                                                    Games.Serialization.fromStoredEvent
                                                    Games.reconstitute
                                                    Games.decide
                                                    Games.Serialization.toEventData
                                                    (Games.Add_play_mode category)
                                                    projectionHandlers |> ignore
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                (Games.Set_steam_last_played (Steam.unixTimestampToDateString steamGame.RtimeLastPlayed))
                                                projectionHandlers |> ignore
                                            executeCommand conn sid
                                                Games.Serialization.fromStoredEvent
                                                Games.reconstitute
                                                Games.decide
                                                Games.Serialization.toEventData
                                                Games.Mark_as_owned
                                                projectionHandlers |> ignore
                                        | Error e ->
                                            errors <- errors @ [ sprintf "Failed to create '%s': %s" steamGame.Name e ]
                            with ex ->
                                errors <- errors @ [ sprintf "Error processing '%s': %s" steamGame.Name ex.Message ]

                        // Backfill descriptions for games matched by steam_app_id with empty descriptions
                        let mutable descriptionsEnriched = 0
                        let gamesToEnrich = GameProjection.findGamesWithEmptyDescriptionAndSteamAppId conn
                        for (slug, steamAppId) in gamesToEnrich do
                            try
                                do! Async.Sleep 300 // Rate limit Steam Store API calls
                                let! storeDetails = Steam.getSteamStoreDetails httpClient steamAppId
                                match storeDetails with
                                | Ok details ->
                                    let sid = Games.streamId slug
                                    let desc =
                                        if details.AboutTheGame <> "" then stripHtmlTags details.AboutTheGame
                                        elif details.DetailedDescription <> "" then stripHtmlTags details.DetailedDescription
                                        else ""
                                    if desc <> "" then
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_description desc)
                                            projectionHandlers |> ignore
                                    if details.ShortDescription <> "" then
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_short_description details.ShortDescription)
                                            projectionHandlers |> ignore
                                    if details.WebsiteUrl.IsSome then
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Set_website_url details.WebsiteUrl)
                                            projectionHandlers |> ignore
                                    for category in details.Categories do
                                        executeCommand conn sid
                                            Games.Serialization.fromStoredEvent
                                            Games.reconstitute
                                            Games.decide
                                            Games.Serialization.toEventData
                                            (Games.Add_play_mode category)
                                            projectionHandlers |> ignore
                                    if desc <> "" || details.ShortDescription <> "" then
                                        descriptionsEnriched <- descriptionsEnriched + 1
                                | Error _ -> ()
                            with ex ->
                                errors <- errors @ [ sprintf "Failed to enrich '%s': %s" slug ex.Message ]

                        if descriptionsEnriched > 0 then
                            printfn "Steam import: enriched %d games with missing descriptions" descriptionsEnriched

                        return Ok {
                            Mediatheca.Shared.SteamImportResult.GamesMatched = gamesMatched
                            GamesCreated = gamesCreated
                            PlayTimeUpdated = playTimeUpdated
                            Errors = errors
                        }
                with ex ->
                    return Error $"Steam import failed: {ex.Message}"
            }

            getSteamFamilyToken = fun () -> async {
                let token =
                    SettingsStore.getSetting conn "steam_family_token"
                    |> Option.defaultValue ""
                if token.Length > 4 then
                    return sprintf "****%s" (token.Substring(token.Length - 4))
                elif token.Length > 0 then
                    return "****"
                else
                    return ""
            }

            setSteamFamilyToken = fun token -> async {
                try
                    SettingsStore.setSetting conn "steam_family_token" token
                    return Ok ()
                with ex ->
                    return Error $"Failed to save family token: {ex.Message}"
            }

            getSteamFamilyMembers = fun () -> async {
                let json =
                    SettingsStore.getSetting conn "steam_family_members"
                    |> Option.defaultValue "[]"
                let steamConfig = getSteamConfig()
                let userSteamId = steamConfig.SteamId
                let decoder =
                    Thoth.Json.Net.Decode.list (
                        Thoth.Json.Net.Decode.object (fun get -> {
                            Mediatheca.Shared.SteamFamilyMember.SteamId = get.Required.Field "steamId" Thoth.Json.Net.Decode.string
                            DisplayName = get.Required.Field "displayName" Thoth.Json.Net.Decode.string
                            FriendSlug = get.Optional.Field "friendSlug" Thoth.Json.Net.Decode.string
                            IsMe = get.Optional.Field "isMe" Thoth.Json.Net.Decode.bool |> Option.defaultValue false
                        })
                    )
                match Thoth.Json.Net.Decode.fromString decoder json with
                | Ok members ->
                    return members |> List.map (fun m ->
                        { m with IsMe = not (System.String.IsNullOrWhiteSpace(userSteamId)) && m.SteamId = userSteamId })
                | Error _ -> return []
            }

            setSteamFamilyMembers = fun members -> async {
                try
                    let json =
                        members
                        |> List.map (fun m ->
                            Thoth.Json.Net.Encode.object [
                                "steamId", Thoth.Json.Net.Encode.string m.SteamId
                                "displayName", Thoth.Json.Net.Encode.string m.DisplayName
                                "friendSlug", Thoth.Json.Net.Encode.option Thoth.Json.Net.Encode.string m.FriendSlug
                                "isMe", Thoth.Json.Net.Encode.bool m.IsMe
                            ])
                        |> Thoth.Json.Net.Encode.list
                        |> Thoth.Json.Net.Encode.toString 0
                    SettingsStore.setSetting conn "steam_family_members" json
                    return Ok ()
                with ex ->
                    return Error $"Failed to save family members: {ex.Message}"
            }

            fetchSteamFamilyMembers = fun () -> async {
                try
                    let token =
                        SettingsStore.getSetting conn "steam_family_token"
                        |> Option.defaultValue ""
                    if System.String.IsNullOrWhiteSpace(token) then
                        return Error "Steam Family access token not configured"
                    else
                        let steamConfig = getSteamConfig()
                        // Get family group ID
                        printfn "[SteamFamily] Step 1: Calling getFamilyGroupForUser..."
                        let! familyResult = Steam.getFamilyGroupForUser httpClient token
                        match familyResult with
                        | Error e ->
                            printfn "[SteamFamily] getFamilyGroupForUser FAILED: %s" e
                            return Error e
                        | Ok familyGroupBasic ->
                            printfn "[SteamFamily] Got family group ID: %s, basic members: %d" familyGroupBasic.FamilyGroupid familyGroupBasic.Members.Length
                            // Fetch actual family group details (with members)
                            printfn "[SteamFamily] Step 2: Calling getFamilyGroup..."
                            let! familyDetailResult = Steam.getFamilyGroup httpClient token familyGroupBasic.FamilyGroupid
                            let familyMembers =
                                match familyDetailResult with
                                | Ok fg ->
                                    printfn "[SteamFamily] getFamilyGroup OK — members: %d (steamids: %s)" fg.Members.Length (fg.Members |> List.map (fun m -> m.Steamid) |> String.concat ", ")
                                    fg.Members
                                | Error e ->
                                    printfn "[SteamFamily] getFamilyGroup FAILED: %s — falling back to basic members (%d)" e familyGroupBasic.Members.Length
                                    familyGroupBasic.Members // fallback

                            printfn "[SteamFamily] Total family members: %d" familyMembers.Length

                            // Resolve display names via Steam Web API
                            let! playerNames =
                                if not (System.String.IsNullOrWhiteSpace(steamConfig.ApiKey)) && not (List.isEmpty familyMembers) then
                                    Steam.getPlayerSummaries httpClient steamConfig.ApiKey (familyMembers |> List.map (fun m -> m.Steamid))
                                else
                                    async { return Ok [] }

                            let nameMap =
                                match playerNames with
                                | Ok players -> players |> List.map (fun p -> p.Steamid, p.PersonaName) |> Map.ofList
                                | Error _ -> Map.empty

                            // Read existing mappings to preserve FriendSlug
                            let existingMembersJson =
                                SettingsStore.getSetting conn "steam_family_members"
                                |> Option.defaultValue "[]"
                            let memberDecoder =
                                Thoth.Json.Net.Decode.list (
                                    Thoth.Json.Net.Decode.object (fun get -> {
                                        Mediatheca.Shared.SteamFamilyMember.SteamId = get.Required.Field "steamId" Thoth.Json.Net.Decode.string
                                        DisplayName = get.Required.Field "displayName" Thoth.Json.Net.Decode.string
                                        FriendSlug = get.Optional.Field "friendSlug" Thoth.Json.Net.Decode.string
                                        IsMe = get.Optional.Field "isMe" Thoth.Json.Net.Decode.bool |> Option.defaultValue false
                                    })
                                )
                            let existingMappings =
                                match Thoth.Json.Net.Decode.fromString memberDecoder existingMembersJson with
                                | Ok m -> m |> List.map (fun m -> m.SteamId, m.FriendSlug) |> Map.ofList
                                | Error _ -> Map.empty

                            let userSteamId = steamConfig.SteamId
                            let members =
                                familyMembers
                                |> List.map (fun m ->
                                    { Mediatheca.Shared.SteamFamilyMember.SteamId = m.Steamid
                                      DisplayName = nameMap |> Map.tryFind m.Steamid |> Option.defaultValue m.Steamid
                                      FriendSlug = existingMappings |> Map.tryFind m.Steamid |> Option.flatten
                                      IsMe = not (System.String.IsNullOrWhiteSpace(userSteamId)) && m.Steamid = userSteamId })

                            // Persist
                            let json =
                                members
                                |> List.map (fun m ->
                                    Thoth.Json.Net.Encode.object [
                                        "steamId", Thoth.Json.Net.Encode.string m.SteamId
                                        "displayName", Thoth.Json.Net.Encode.string m.DisplayName
                                        "friendSlug", Thoth.Json.Net.Encode.option Thoth.Json.Net.Encode.string m.FriendSlug
                                        "isMe", Thoth.Json.Net.Encode.bool m.IsMe
                                    ])
                                |> Thoth.Json.Net.Encode.list
                                |> Thoth.Json.Net.Encode.toString 0
                            SettingsStore.setSetting conn "steam_family_members" json

                            return Ok members
                with ex ->
                    return Error $"Failed to fetch family members: {ex.Message}"
            }

            importSteamFamily = fun () -> async {
                return! runSteamFamilyImport conn httpClient getRawgConfig getSteamConfig imageBasePath projectionHandlers (fun _ -> ())
            }

            // Jellyfin Integration
            getJellyfinServerUrl = fun () -> async {
                return SettingsStore.getSetting conn "jellyfin_server_url" |> Option.defaultValue ""
            }

            setJellyfinServerUrl = fun url -> async {
                try
                    SettingsStore.setSetting conn "jellyfin_server_url" url
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Jellyfin server URL: {ex.Message}"
            }

            getJellyfinUsername = fun () -> async {
                return SettingsStore.getSetting conn "jellyfin_username" |> Option.defaultValue ""
            }

            setJellyfinCredentials = fun (username, password) -> async {
                try
                    SettingsStore.setSetting conn "jellyfin_username" username
                    SettingsStore.setSetting conn "jellyfin_password" password
                    return Ok ()
                with ex ->
                    return Error $"Failed to save Jellyfin credentials: {ex.Message}"
            }

            scanJellyfinLibrary = fun () -> async {
                try
                    let config = getJellyfinConfig ()
                    if System.String.IsNullOrWhiteSpace(config.AccessToken) || System.String.IsNullOrWhiteSpace(config.UserId) then
                        return Error "Jellyfin not configured. Please test the connection first."
                    else
                        // Fetch movies and series from Jellyfin
                        let! moviesResult = Jellyfin.getMovies httpClient config.ServerUrl config.UserId config.AccessToken
                        let! seriesResult = Jellyfin.getSeries httpClient config.ServerUrl config.UserId config.AccessToken
                        match moviesResult, seriesResult with
                        | Error e, _ -> return Error (sprintf "Failed to fetch movies: %s" e)
                        | _, Error e -> return Error (sprintf "Failed to fetch series: %s" e)
                        | Ok jellyfinMovies, Ok jellyfinSeries ->
                            // Build lookup of existing Mediatheca items by tmdb_id
                            let moviesByTmdbId =
                                conn
                                |> Db.newCommand "SELECT slug, name, tmdb_id FROM movie_detail"
                                |> Db.query (fun (rd: IDataReader) ->
                                    let tmdbId = rd.ReadInt32 "tmdb_id"
                                    let slug = rd.ReadString "slug"
                                    let name = rd.ReadString "name"
                                    (tmdbId, (slug, name)))
                                |> Map.ofList

                            let seriesByTmdbId =
                                conn
                                |> Db.newCommand "SELECT slug, name, tmdb_id FROM series_detail"
                                |> Db.query (fun (rd: IDataReader) ->
                                    let tmdbId = rd.ReadInt32 "tmdb_id"
                                    let slug = rd.ReadString "slug"
                                    let name = rd.ReadString "name"
                                    (tmdbId, (slug, name)))
                                |> Map.ofList

                            // Helper to check if movie has watch sessions
                            let movieHasWatchData (slug: string) =
                                conn
                                |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions WHERE movie_slug = @slug"
                                |> Db.setParams [ "slug", SqlType.String slug ]
                                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
                                |> Option.defaultValue 0
                                |> fun c -> c > 0

                            // Helper to check if series has any watched episodes
                            let seriesHasWatchData (slug: string) =
                                conn
                                |> Db.newCommand "SELECT COUNT(*) as cnt FROM series_episode_progress WHERE series_slug = @slug"
                                |> Db.setParams [ "slug", SqlType.String slug ]
                                |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
                                |> Option.defaultValue 0
                                |> fun c -> c > 0

                            let toJellyfinItem (item: Jellyfin.JellyfinBaseItem) (itemType: JellyfinItemType) : JellyfinItem =
                                let tmdbId =
                                    item.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                { JellyfinId = item.Id
                                  Name = item.Name
                                  Year = item.ProductionYear
                                  ItemType = itemType
                                  TmdbId = tmdbId
                                  Played = item.UserData |> Option.map (fun ud -> ud.Played) |> Option.defaultValue false
                                  PlayCount = item.UserData |> Option.map (fun ud -> ud.PlayCount) |> Option.defaultValue 0
                                  LastPlayedDate = item.UserData |> Option.bind (fun ud -> ud.LastPlayedDate) }

                            // Match movies
                            let matchedMovies, unmatchedMovies =
                                jellyfinMovies
                                |> List.fold (fun (matched, unmatched) item ->
                                    let jItem = toJellyfinItem item JellyfinMovie
                                    match jItem.TmdbId with
                                    | Some tmdbId ->
                                        match Map.tryFind tmdbId moviesByTmdbId with
                                        | Some (slug, name) ->
                                            let m: JellyfinMatchedItem = {
                                                JellyfinItem = jItem
                                                MediathecaSlug = slug
                                                MediathecaName = name
                                                HasExistingWatchData = movieHasWatchData slug
                                            }
                                            (m :: matched, unmatched)
                                        | None -> (matched, jItem :: unmatched)
                                    | None -> (matched, jItem :: unmatched)
                                ) ([], [])

                            // Match series
                            let matchedSeries, unmatchedSeries =
                                jellyfinSeries
                                |> List.fold (fun (matched, unmatched) item ->
                                    let jItem = toJellyfinItem item JellyfinSeries
                                    match jItem.TmdbId with
                                    | Some tmdbId ->
                                        match Map.tryFind tmdbId seriesByTmdbId with
                                        | Some (slug, name) ->
                                            let m: JellyfinMatchedItem = {
                                                JellyfinItem = jItem
                                                MediathecaSlug = slug
                                                MediathecaName = name
                                                HasExistingWatchData = seriesHasWatchData slug
                                            }
                                            (m :: matched, unmatched)
                                        | None -> (matched, jItem :: unmatched)
                                    | None -> (matched, jItem :: unmatched)
                                ) ([], [])

                            // Persist Jellyfin IDs for matched movies
                            for m in matchedMovies do
                                conn
                                |> Db.newCommand "UPDATE movie_detail SET jellyfin_id = @jellyfin_id WHERE slug = @slug"
                                |> Db.setParams [
                                    "slug", SqlType.String m.MediathecaSlug
                                    "jellyfin_id", SqlType.String m.JellyfinItem.JellyfinId
                                ]
                                |> Db.exec

                            // Persist Jellyfin IDs for matched series + fetch episode IDs
                            for m in matchedSeries do
                                conn
                                |> Db.newCommand "UPDATE series_detail SET jellyfin_id = @jellyfin_id WHERE slug = @slug"
                                |> Db.setParams [
                                    "slug", SqlType.String m.MediathecaSlug
                                    "jellyfin_id", SqlType.String m.JellyfinItem.JellyfinId
                                ]
                                |> Db.exec

                                // Fetch episodes from Jellyfin for this series
                                let! episodesResult = Jellyfin.getEpisodes httpClient config.ServerUrl config.UserId config.AccessToken m.JellyfinItem.JellyfinId
                                match episodesResult with
                                | Ok episodes ->
                                    for ep in episodes do
                                        match ep.ParentIndexNumber, ep.IndexNumber with
                                        | Some seasonNum, Some episodeNum ->
                                            conn
                                            |> Db.newCommand """
                                                INSERT OR REPLACE INTO series_episode_jellyfin (series_slug, season_number, episode_number, jellyfin_id)
                                                VALUES (@slug, @season, @episode, @jellyfin_id)
                                            """
                                            |> Db.setParams [
                                                "slug", SqlType.String m.MediathecaSlug
                                                "season", SqlType.Int32 seasonNum
                                                "episode", SqlType.Int32 episodeNum
                                                "jellyfin_id", SqlType.String ep.Id
                                            ]
                                            |> Db.exec
                                        | _ -> () // Skip episodes without season/episode numbers
                                | Error _ -> () // Skip if episode fetch fails

                            let result: JellyfinScanResult = {
                                MatchedMovies = List.rev matchedMovies
                                MatchedSeries = List.rev matchedSeries
                                UnmatchedMovies = List.rev unmatchedMovies
                                UnmatchedSeries = List.rev unmatchedSeries
                            }
                            return Ok result
                with ex ->
                    return Error $"Jellyfin scan failed: {ex.Message}"
            }

            importJellyfinWatchHistory = fun () -> async {
                try
                    let config = getJellyfinConfig ()
                    if System.String.IsNullOrWhiteSpace(config.AccessToken) || System.String.IsNullOrWhiteSpace(config.UserId) then
                        return Error "Jellyfin not configured. Please test the connection first."
                    else
                        let mutable moviesAdded = 0
                        let mutable episodesAdded = 0
                        let mutable moviesAutoAdded = 0
                        let mutable seriesAutoAdded = 0
                        let mutable itemsSkipped = 0
                        let mutable errors: string list = []

                        // --- Movie watch sync (REQ-304) ---
                        let! moviesResult = Jellyfin.getMovies httpClient config.ServerUrl config.UserId config.AccessToken
                        match moviesResult with
                        | Error e -> errors <- errors @ [sprintf "Failed to fetch Jellyfin movies: %s" e]
                        | Ok jellyfinMovies ->
                            // Build TMDB ID -> (slug, name) lookup
                            let mutable moviesByTmdbId =
                                conn
                                |> Db.newCommand "SELECT slug, name, tmdb_id FROM movie_detail"
                                |> Db.query (fun (rd: IDataReader) ->
                                    let tmdbId = rd.ReadInt32 "tmdb_id"
                                    let slug = rd.ReadString "slug"
                                    let name = rd.ReadString "name"
                                    (tmdbId, (slug, name)))
                                |> Map.ofList

                            // Phase 1: Auto-add unmatched movies with TMDB IDs
                            for item in jellyfinMovies do
                                let tmdbId =
                                    item.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                match tmdbId with
                                | Some tid when not (Map.containsKey tid moviesByTmdbId) ->
                                    try
                                        let! addResult = addMovieToLibrary conn httpClient getTmdbConfig imageBasePath movieProjections tid
                                        match addResult with
                                        | Ok slug ->
                                            moviesByTmdbId <- Map.add tid (slug, item.Name) moviesByTmdbId
                                            moviesAutoAdded <- moviesAutoAdded + 1
                                        | Error e ->
                                            errors <- errors @ [sprintf "Auto-add movie '%s' (TMDB %d): %s" item.Name tid e]
                                    with ex ->
                                        errors <- errors @ [sprintf "Auto-add movie '%s' (TMDB %d): %s" item.Name tid ex.Message]
                                | _ -> ()

                            // Phase 1b: Persist Jellyfin IDs for all matched movies
                            for item in jellyfinMovies do
                                let tmdbId =
                                    item.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                match tmdbId with
                                | Some tid ->
                                    match Map.tryFind tid moviesByTmdbId with
                                    | Some (slug, _) ->
                                        conn
                                        |> Db.newCommand "UPDATE movie_detail SET jellyfin_id = @jellyfin_id WHERE slug = @slug"
                                        |> Db.setParams [
                                            "slug", SqlType.String slug
                                            "jellyfin_id", SqlType.String item.Id
                                        ]
                                        |> Db.exec
                                    | None -> ()
                                | None -> ()

                            // Phase 2: Sync watch history
                            for item in jellyfinMovies do
                                let played = item.UserData |> Option.map (fun ud -> ud.Played) |> Option.defaultValue false
                                let tmdbId =
                                    item.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                match played, tmdbId with
                                | true, Some tid ->
                                    match Map.tryFind tid moviesByTmdbId with
                                    | Some (slug, _name) ->
                                        let lastPlayedDate =
                                            item.UserData
                                            |> Option.bind (fun ud -> ud.LastPlayedDate)
                                            |> Option.map (fun d -> d.Substring(0, min 10 d.Length))
                                            |> Option.defaultValue (System.DateTime.UtcNow.ToString("yyyy-MM-dd"))
                                        // Check if a watch session already exists on this date (substr to compare date part only)
                                        let existsOnDate =
                                            conn
                                            |> Db.newCommand "SELECT COUNT(*) as cnt FROM watch_sessions WHERE movie_slug = @slug AND SUBSTR(date, 1, 10) = @date"
                                            |> Db.setParams [ "slug", SqlType.String slug; "date", SqlType.String lastPlayedDate ]
                                            |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
                                            |> Option.defaultValue 0
                                            |> fun c -> c > 0
                                        if existsOnDate then
                                            itemsSkipped <- itemsSkipped + 1
                                        else
                                            let runtime =
                                                conn
                                                |> Db.newCommand "SELECT runtime FROM movie_detail WHERE slug = @slug"
                                                |> Db.setParams [ "slug", SqlType.String slug ]
                                                |> Db.querySingle (fun (rd: IDataReader) ->
                                                    if rd.IsDBNull(rd.GetOrdinal("runtime")) then None
                                                    else Some (rd.ReadInt32 "runtime"))
                                                |> Option.flatten
                                            let sessionId = System.Guid.NewGuid().ToString("N")
                                            let sessionData: Movies.WatchSessionRecordedData = {
                                                SessionId = sessionId
                                                Date = lastPlayedDate
                                                Duration = runtime
                                                FriendSlugs = []
                                            }
                                            let sid = Movies.streamId slug
                                            let result =
                                                executeCommand
                                                    conn sid
                                                    Movies.Serialization.fromStoredEvent
                                                    Movies.reconstitute
                                                    Movies.decide
                                                    Movies.Serialization.toEventData
                                                    (Movies.Record_watch_session sessionData)
                                                    movieProjections
                                            match result with
                                            | Ok () -> moviesAdded <- moviesAdded + 1
                                            | Error e -> errors <- errors @ [sprintf "Movie '%s': %s" slug e]
                                    | None -> itemsSkipped <- itemsSkipped + 1
                                | _ -> itemsSkipped <- itemsSkipped + 1

                        // --- Series episode watch sync (REQ-305) ---
                        let! seriesResult = Jellyfin.getSeries httpClient config.ServerUrl config.UserId config.AccessToken
                        match seriesResult with
                        | Error e -> errors <- errors @ [sprintf "Failed to fetch Jellyfin series: %s" e]
                        | Ok jellyfinSeries ->
                            let mutable seriesByTmdbId =
                                conn
                                |> Db.newCommand "SELECT slug, name, tmdb_id FROM series_detail"
                                |> Db.query (fun (rd: IDataReader) ->
                                    let tmdbId = rd.ReadInt32 "tmdb_id"
                                    let slug = rd.ReadString "slug"
                                    let name = rd.ReadString "name"
                                    (tmdbId, (slug, name)))
                                |> Map.ofList

                            // Phase 1: Auto-add unmatched series with TMDB IDs
                            for seriesItem in jellyfinSeries do
                                let tmdbId =
                                    seriesItem.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                match tmdbId with
                                | Some tid when not (Map.containsKey tid seriesByTmdbId) ->
                                    try
                                        let! addResult = addSeriesToLibrary conn httpClient getTmdbConfig imageBasePath projectionHandlers tid
                                        match addResult with
                                        | Ok slug ->
                                            seriesByTmdbId <- Map.add tid (slug, seriesItem.Name) seriesByTmdbId
                                            seriesAutoAdded <- seriesAutoAdded + 1
                                        | Error e ->
                                            errors <- errors @ [sprintf "Auto-add series '%s' (TMDB %d): %s" seriesItem.Name tid e]
                                    with ex ->
                                        errors <- errors @ [sprintf "Auto-add series '%s' (TMDB %d): %s" seriesItem.Name tid ex.Message]
                                | _ -> ()

                            // Phase 1b: Persist Jellyfin IDs for all matched series + episodes
                            for seriesItem in jellyfinSeries do
                                let tmdbId =
                                    seriesItem.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                match tmdbId with
                                | Some tid ->
                                    match Map.tryFind tid seriesByTmdbId with
                                    | Some (slug, _) ->
                                        // Persist series Jellyfin ID
                                        conn
                                        |> Db.newCommand "UPDATE series_detail SET jellyfin_id = @jellyfin_id WHERE slug = @slug"
                                        |> Db.setParams [
                                            "slug", SqlType.String slug
                                            "jellyfin_id", SqlType.String seriesItem.Id
                                        ]
                                        |> Db.exec
                                        // Fetch and persist episode Jellyfin IDs
                                        let! episodesForIds = Jellyfin.getEpisodes httpClient config.ServerUrl config.UserId config.AccessToken seriesItem.Id
                                        match episodesForIds with
                                        | Ok eps ->
                                            for ep in eps do
                                                match ep.ParentIndexNumber, ep.IndexNumber with
                                                | Some seasonNum, Some episodeNum ->
                                                    conn
                                                    |> Db.newCommand """
                                                        INSERT OR REPLACE INTO series_episode_jellyfin (series_slug, season_number, episode_number, jellyfin_id)
                                                        VALUES (@slug, @season, @episode, @jellyfin_id)
                                                    """
                                                    |> Db.setParams [
                                                        "slug", SqlType.String slug
                                                        "season", SqlType.Int32 seasonNum
                                                        "episode", SqlType.Int32 episodeNum
                                                        "jellyfin_id", SqlType.String ep.Id
                                                    ]
                                                    |> Db.exec
                                                | _ -> ()
                                        | Error _ -> ()
                                    | None -> ()
                                | None -> ()

                            // Phase 2: Sync watch history
                            for seriesItem in jellyfinSeries do
                                let tmdbId =
                                    seriesItem.ProviderIds.Tmdb
                                    |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)
                                match tmdbId with
                                | Some tid ->
                                    match Map.tryFind tid seriesByTmdbId with
                                    | Some (slug, _name) ->
                                        // Fetch episodes from Jellyfin for this series
                                        let! episodesResult = Jellyfin.getEpisodes httpClient config.ServerUrl config.UserId config.AccessToken seriesItem.Id
                                        match episodesResult with
                                        | Error e -> errors <- errors @ [sprintf "Series '%s' episodes: %s" slug e]
                                        | Ok episodes ->
                                            // Get the actual default rewatch session (may have been changed by user)
                                            let defaultRewatchId = SeriesProjection.getDefaultRewatchId conn slug
                                            let alreadyWatched = SeriesProjection.getWatchedEpisodesForSession conn slug defaultRewatchId
                                            for ep in episodes do
                                                let epPlayed = ep.UserData |> Option.map (fun ud -> ud.Played) |> Option.defaultValue false
                                                match epPlayed, ep.ParentIndexNumber, ep.IndexNumber with
                                                | true, Some seasonNum, Some epNum ->
                                                    if alreadyWatched |> Set.contains (seasonNum, epNum) then
                                                        itemsSkipped <- itemsSkipped + 1
                                                    else
                                                        let watchDate =
                                                            ep.UserData
                                                            |> Option.bind (fun ud -> ud.LastPlayedDate)
                                                            |> Option.map (fun d -> d.Substring(0, min 10 d.Length))
                                                            |> Option.defaultValue (System.DateTime.UtcNow.ToString("yyyy-MM-dd"))
                                                        let sid = Series.streamId slug
                                                        let result =
                                                            executeCommand
                                                                conn sid
                                                                Series.Serialization.fromStoredEvent
                                                                Series.reconstitute
                                                                Series.decide
                                                                Series.Serialization.toEventData
                                                                (Series.Mark_episode_watched {
                                                                    RewatchId = defaultRewatchId
                                                                    SeasonNumber = seasonNum
                                                                    EpisodeNumber = epNum
                                                                    Date = watchDate
                                                                })
                                                                projectionHandlers
                                                        match result with
                                                        | Ok () -> episodesAdded <- episodesAdded + 1
                                                        | Error e -> errors <- errors @ [sprintf "Series '%s' S%02dE%02d: %s" slug seasonNum epNum e]
                                                | _ -> itemsSkipped <- itemsSkipped + 1
                                    | None -> ()
                                | None -> ()

                        let importResult: JellyfinImportResult = {
                            MoviesAdded = moviesAdded
                            EpisodesAdded = episodesAdded
                            MoviesAutoAdded = moviesAutoAdded
                            SeriesAutoAdded = seriesAutoAdded
                            ItemsSkipped = itemsSkipped
                            Errors = errors
                        }
                        return Ok importResult
                with ex ->
                    return Error $"Jellyfin import failed: {ex.Message}"
            }

            testJellyfinConnection = fun (serverUrl, username, password) -> async {
                try
                    let! authResult = Jellyfin.authenticate httpClient serverUrl username password
                    match authResult with
                    | Ok result ->
                        // Save the token and userId for future use
                        SettingsStore.setSetting conn "jellyfin_server_url" serverUrl
                        SettingsStore.setSetting conn "jellyfin_username" username
                        SettingsStore.setSetting conn "jellyfin_password" password
                        SettingsStore.setSetting conn "jellyfin_user_id" result.UserId
                        SettingsStore.setSetting conn "jellyfin_access_token" result.AccessToken
                        return Ok (sprintf "Connected as %s" result.UserName)
                    | Error e ->
                        return Error e
                with ex ->
                    return Error $"Jellyfin connection test failed: {ex.Message}"
            }

            importFromCinemarco = fun request -> async {
                return CinemarcoImport.runImport conn imageBasePath projectionHandlers httpClient getTmdbConfig request
            }

            getViewSettings = fun key -> async {
                match SettingsStore.getSetting conn ("view:" + key) with
                | Some json ->
                    try
                        let settings = Newtonsoft.Json.JsonConvert.DeserializeObject<ViewSettings>(json, Fable.Remoting.Json.FableJsonConverter())
                        return Some settings
                    with _ -> return None
                | None -> return None
            }

            saveViewSettings = fun key settings -> async {
                let json = Newtonsoft.Json.JsonConvert.SerializeObject(settings, Fable.Remoting.Json.FableJsonConverter())
                SettingsStore.setSetting conn ("view:" + key) json
            }

            getCollapsedSections = fun key -> async {
                match SettingsStore.getSetting conn ("collapsed:" + key) with
                | Some csv when csv <> "" -> return csv.Split(',') |> Array.toList
                | _ -> return []
            }

            saveCollapsedSections = fun key sections -> async {
                let csv = sections |> String.concat ","
                SettingsStore.setSetting conn ("collapsed:" + key) csv
            }

            // Playtime Tracking
            getGamePlaySessions = fun slug -> async {
                return PlaytimeTracker.getPlaySessionsForGame conn slug
            }

            getPlaytimeSummary = fun fromDate toDate -> async {
                return PlaytimeTracker.getPlaytimeSummary conn fromDate toDate
            }

            getPlaytimeSyncStatus = fun () -> async {
                return PlaytimeTracker.getSyncStatus conn
            }

            triggerPlaytimeSync = fun () ->
                PlaytimeTracker.runSync conn httpClient getSteamConfig projectionHandlers

            // Steam Achievements
            getSteamRecentAchievements = fun () -> async {
                try
                    let steamConfig = getSteamConfig()
                    return! Steam.getRecentAchievements httpClient steamConfig
                with ex ->
                    return Error (sprintf "Failed to fetch achievements: %s" ex.Message)
            }

            // HowLongToBeat
            fetchHltbData = fun gameSlug -> async {
                try
                    // Look up the game name from the projection
                    match GameProjection.getBySlug conn gameSlug with
                    | None -> return Error "Game not found"
                    | Some game ->
                        match! HowLongToBeat.searchGame httpClient game.Name with
                        | None -> return Ok None
                        | Some hltbResult ->
                            let hours = HowLongToBeat.toHours hltbResult.CompMainSeconds
                            // Store the HLTB hours via the existing event
                            let sid = Games.streamId gameSlug
                            let result =
                                executeCommand
                                    conn sid
                                    Games.Serialization.fromStoredEvent
                                    Games.reconstitute
                                    Games.decide
                                    Games.Serialization.toEventData
                                    (Games.Set_hltb_hours (Some hours))
                                    projectionHandlers
                            match result with
                            | Ok () -> return Ok (Some hours)
                            | Error e -> return Error e
                with ex ->
                    return Error $"Failed to fetch HLTB data: {ex.Message}"
            }

            // Event History
            getStreamEvents = fun streamPrefix -> async {
                // Determine which streams to read based on the prefix
                let mainStreamId = streamPrefix
                let contentBlocksStreamId =
                    // For Movie-X and Game-X, also read ContentBlocks-X
                    if streamPrefix.StartsWith("Movie-") then
                        let slug = streamPrefix.Substring(6)
                        Some (ContentBlocks.streamId slug)
                    elif streamPrefix.StartsWith("Game-") then
                        let slug = streamPrefix.Substring(5)
                        Some (ContentBlocks.streamId slug)
                    else
                        None
                let streamIds =
                    mainStreamId :: (contentBlocksStreamId |> Option.toList)
                return EventFormatting.getStreamEvents conn streamIds
            }
        }
