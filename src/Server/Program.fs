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

    // Initialize database
    let dbPath =
        Path.Combine(
            AppContext.BaseDirectory,
            "mediatheca.db"
        )

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

    // Dynamic TMDB config provider (reads from DB, falls back to env var)
    let getTmdbConfig () : Tmdb.TmdbConfig =
        let apiKey =
            SettingsStore.getSetting conn "tmdb_api_key"
            |> Option.orElse envTmdbKey
            |> Option.defaultValue ""
        { ApiKey = apiKey
          ImageBaseUrl = "https://image.tmdb.org/t/p/" }

    let httpClient = new HttpClient()

    // Image storage
    let imageBasePath = Path.Combine(AppContext.BaseDirectory, "images")
    if not (Directory.Exists(imageBasePath)) then
        Directory.CreateDirectory(imageBasePath) |> ignore

    // Projection handlers
    let projectionHandlers = [
        MovieProjection.handler
        FriendProjection.handler
        ContentBlockProjection.handler
    ]

    // Start projections
    Projection.startAllProjections conn projectionHandlers

    // Create API
    let api = Api.create conn httpClient getTmdbConfig imageBasePath projectionHandlers

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
