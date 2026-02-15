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

    // Catch up all projections, rebuilding series to fix out-of-sync rewatch session data
    Projection.rebuildProjection conn SeriesProjection.handler
    Projection.startAllProjections conn projectionHandlers

    // Create API
    let api = Api.create conn httpClient getTmdbConfig getRawgConfig imageBasePath projectionHandlers

    let webApp =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue api
        |> Remoting.buildHttpHandler

    // Serve static files from deploy/public in production
    let staticPath = Path.Combine(Directory.GetCurrentDirectory(), "deploy", "public")
    if Directory.Exists(staticPath) then
        app.UseDefaultFiles() |> ignore
        app.UseStaticFiles() |> ignore

    // Serve images from /images path
    if Directory.Exists(imageBasePath) then
        app.UseStaticFiles(
            StaticFileOptions(
                FileProvider = new PhysicalFileProvider(imageBasePath),
                RequestPath = "/images"
            )
        ) |> ignore

    app.UseGiraffe webApp

    app.Run()
    0
