module Mediatheca.Server.Program

open System
open System.IO
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.StaticFiles
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Data.Sqlite
open Giraffe
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Mediatheca.Shared

let createConnection (dbPath: string) =
    let conn = new SqliteConnection($"Data Source={dbPath}")
    conn.Open()
    EventStore.initialize conn
    conn

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)

    builder.Services.AddGiraffe() |> ignore

    let app = builder.Build()

    // Data directory — configurable via DATA_DIR env var (same pattern as Cinemarco)
    let dataDir =
        match Environment.GetEnvironmentVariable("DATA_DIR") |> Option.ofObj with
        | Some dir when dir <> "" -> dir
        | _ ->
            let home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            Path.Combine(home, "app", "mediatheca")

    if not (Directory.Exists(dataDir)) then
        Directory.CreateDirectory(dataDir) |> ignore

    // Initialize database
    let dbPath = Path.Combine(dataDir, "mediatheca.db")

    let conn = createConnection dbPath

    // Initialize CastStore tables
    CastStore.initialize conn

    // Initialize SettingsStore
    SettingsStore.initialize conn

    // Initialize JellyfinStore tables
    JellyfinStore.initialize conn

    // Migrate Jellyfin data from old projection tables (one-time, idempotent)
    JellyfinStore.migrateFromProjections conn

    // Initialize PlaytimeTracker tables
    PlaytimeTracker.initialize conn

    // Seed TMDB API key from env var if DB has no value yet
    let envTmdbKey =
        Environment.GetEnvironmentVariable("TMDB_API_KEY")
        |> Option.ofObj
    match envTmdbKey with
    | Some key when key <> "" ->
        match SettingsStore.getSetting conn "tmdb_api_key" with
        | None -> SettingsStore.setSetting conn "tmdb_api_key" key
        | Some _ -> ()
    | _ -> ()

    // Seed RAWG API key from env var if DB has no value yet
    let envRawgKey =
        Environment.GetEnvironmentVariable("RAWG_API_KEY")
        |> Option.ofObj
    match envRawgKey with
    | Some key when key <> "" ->
        match SettingsStore.getSetting conn "rawg_api_key" with
        | None -> SettingsStore.setSetting conn "rawg_api_key" key
        | Some _ -> ()
    | _ -> ()

    // Seed Steam API key from env var if DB has no value yet
    let envSteamKey =
        Environment.GetEnvironmentVariable("STEAM_API_KEY")
        |> Option.ofObj
    match envSteamKey with
    | Some key when key <> "" ->
        match SettingsStore.getSetting conn "steam_api_key" with
        | None -> SettingsStore.setSetting conn "steam_api_key" key
        | Some _ -> ()
    | _ -> ()

    // Seed Steam ID from env var if DB has no value yet
    let envSteamId =
        Environment.GetEnvironmentVariable("STEAM_ID")
        |> Option.ofObj
    match envSteamId with
    | Some id when id <> "" ->
        match SettingsStore.getSetting conn "steam_id" with
        | None -> SettingsStore.setSetting conn "steam_id" id
        | Some _ -> ()
    | _ -> ()

    // Dynamic TMDB config provider (reads from DB, falls back to env var)
    let getTmdbConfig () : Tmdb.TmdbConfig =
        let apiKey =
            SettingsStore.getSetting conn "tmdb_api_key"
            |> Option.orElse envTmdbKey
            |> Option.defaultValue ""
        { ApiKey = apiKey
          ImageBaseUrl = "https://image.tmdb.org/t/p/" }

    // Dynamic RAWG config provider (reads from DB, falls back to env var)
    let getRawgConfig () : Rawg.RawgConfig =
        let apiKey =
            SettingsStore.getSetting conn "rawg_api_key"
            |> Option.orElse envRawgKey
            |> Option.defaultValue ""
        { ApiKey = apiKey }

    // Dynamic Jellyfin config provider (reads from DB)
    let getJellyfinConfig () : Jellyfin.JellyfinConfig =
        { ServerUrl = SettingsStore.getSetting conn "jellyfin_server_url" |> Option.defaultValue ""
          Username = SettingsStore.getSetting conn "jellyfin_username" |> Option.defaultValue ""
          Password = SettingsStore.getSetting conn "jellyfin_password" |> Option.defaultValue ""
          UserId = SettingsStore.getSetting conn "jellyfin_user_id" |> Option.defaultValue ""
          AccessToken = SettingsStore.getSetting conn "jellyfin_access_token" |> Option.defaultValue "" }

    // Dynamic Steam config provider (reads from DB, falls back to env var)
    let getSteamConfig () : Steam.SteamConfig =
        let apiKey =
            SettingsStore.getSetting conn "steam_api_key"
            |> Option.orElse envSteamKey
            |> Option.defaultValue ""
        let steamId =
            SettingsStore.getSetting conn "steam_id"
            |> Option.orElse envSteamId
            |> Option.defaultValue ""
        { ApiKey = apiKey; SteamId = steamId }

    let httpClient = new HttpClient()

    // Image storage
    let imageBasePath = Path.Combine(dataDir, "images")
    if not (Directory.Exists(imageBasePath)) then
        Directory.CreateDirectory(imageBasePath) |> ignore

    // Projection handlers
    let projectionHandlers = [
        MovieProjection.handler
        FriendProjection.handler
        ContentBlockProjection.handler
        CatalogProjection.handler
        SeriesProjection.handler
        GameProjection.handler
    ]

    // Catch up all projections, rebuilding game projection for steam_app_id column
    Projection.rebuildProjection conn SeriesProjection.handler
    Projection.rebuildProjection conn GameProjection.handler
    Projection.startAllProjections conn projectionHandlers

    // Backfill director/crew data for existing movies
    let backfillDirectors () =
        try
            let moviesWithoutCrew = CastStore.getMoviesWithoutCrew conn
            if not (List.isEmpty moviesWithoutCrew) then
                printfn "Backfilling director data for %d movies..." moviesWithoutCrew.Length
                let tmdbConfig = getTmdbConfig()
                if tmdbConfig.ApiKey <> "" then
                    for (streamId, tmdbId) in moviesWithoutCrew do
                        try
                            let credits = Tmdb.getMovieCredits httpClient tmdbConfig tmdbId |> Async.RunSynchronously
                            let directors = credits.Crew |> List.filter (fun c -> c.Job = "Director")
                            for director in directors do
                                let dirImageRef =
                                    match director.ProfilePath with
                                    | Some p ->
                                        let ref = sprintf "cast/%d.jpg" director.Id
                                        let destPath = Path.Combine(imageBasePath, ref)
                                        if not (ImageStore.imageExists imageBasePath ref) then
                                            try
                                                Tmdb.downloadImage httpClient tmdbConfig p "w185" destPath
                                                |> Async.RunSynchronously
                                            with _ -> ()
                                        Some ref
                                    | None -> None
                                let cmId = CastStore.upsertCastMember conn director.Name director.Id dirImageRef
                                CastStore.addMovieCrew conn streamId cmId director.Job director.Department
                        with ex ->
                            eprintfn "  Failed to backfill directors for %s (tmdb=%d): %s" streamId tmdbId ex.Message
                    printfn "Director backfill complete."
        with ex ->
            eprintfn "Director backfill failed: %s" ex.Message

    backfillDirectors()

    // Create API
    let api = Api.create conn httpClient getTmdbConfig getRawgConfig getSteamConfig getJellyfinConfig imageBasePath projectionHandlers

    let remotingHandler =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue api
        |> Remoting.withErrorHandler (fun ex _routeInfo ->
            eprintfn "Fable.Remoting error: %s\n%s" ex.Message ex.StackTrace
            Propagate ex.Message)
        |> Remoting.buildHttpHandler

    let webApp =
        choose [
            route "/health" >=> text "ok"
            route "/api/stream/import-steam-family"
                >=> Api.steamFamilyImportHandler conn httpClient getRawgConfig getSteamConfig imageBasePath projectionHandlers
            remotingHandler
        ]

    // Serve static files from deploy/public in production
    let staticPath = Path.Combine(Directory.GetCurrentDirectory(), "deploy", "public")
    if Directory.Exists(staticPath) then
        let fileProvider = new PhysicalFileProvider(staticPath)
        app.UseDefaultFiles(DefaultFilesOptions(FileProvider = fileProvider)) |> ignore
        app.UseStaticFiles(StaticFileOptions(FileProvider = fileProvider)) |> ignore

    // Serve images from /images path
    if Directory.Exists(imageBasePath) then
        app.UseStaticFiles(
            StaticFileOptions(
                FileProvider = new PhysicalFileProvider(imageBasePath),
                RequestPath = "/images"
            )
        ) |> ignore

    app.UseGiraffe webApp

    // Start background playtime tracker
    let _playtimeTimer = PlaytimeTracker.startBackgroundTimer conn httpClient getSteamConfig projectionHandlers

    app.Run()
    0
