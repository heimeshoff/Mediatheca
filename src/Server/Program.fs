module Mediatheca.Server.Program

open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
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

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.fromValue Api.mediathecaApi
    |> Remoting.buildHttpHandler

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

    // Start projections (none yet, but engine is ready)
    Projection.startAllProjections conn []

    // Serve static files from deploy/public in production
    let staticPath = Path.Combine(Directory.GetCurrentDirectory(), "deploy", "public")
    if Directory.Exists(staticPath) then
        app.UseDefaultFiles() |> ignore
        app.UseStaticFiles() |> ignore

    app.UseGiraffe webApp

    app.Run()
    0
