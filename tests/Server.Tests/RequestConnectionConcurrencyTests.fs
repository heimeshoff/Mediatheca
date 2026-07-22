module Mediatheca.Tests.RequestConnectionConcurrencyTests

// administration-mz6kp (ADR-0033): regression coverage for the
// request×request connection race, updated for the per-request connection
// factory that retires ADR-0030's `requestDbLock`.
// `Microsoft.Data.Sqlite.SqliteConnection` is not thread-safe for concurrent
// command creation/disposal from multiple threads (ADR-0028's root-cause
// finding) — Kestrel/Giraffe genuinely dispatch concurrent requests on
// different thread-pool threads, and administration-a4d9b's Playwright specs
// empirically proved that concurrent `addFriend` calls crashed a SHARED
// `conn` with `SqliteConnection does not support nested transactions`.
// ADR-0030's process-wide `requestDbLock` closed that crash by serializing
// every request-reachable transaction-opening call site on the one shared
// connection; this migration removes the shared connection object itself —
// each request now opens and disposes its own `SqliteConnection` from a
// factory (`Api.create`'s new `factory` parameter), so there is no shared
// object left for concurrent command creation/disposal to race on at all.
//
// This test drives the real, unmodified production choke point
// (`Api.executeCommand`'s body, reached here via the real `IMediathecaApi`
// built by `Api.create`, through `addFriend` — a command entirely
// synchronous end to end, with no external HTTP dependency, making it the
// simplest real repro of the exact production crash) against a real
// temp-file SQLite database (not `:memory:` — WAL + busy_timeout only
// serialize writes at the *file* level across separate connections, the
// exact property this test needs to exercise), firing many concurrent
// `addFriend` calls that each open their OWN connection via the shared
// factory. Asserts no exception (the exact crash this fix prevents) and
// that every friend was actually recorded (per-request connections didn't
// silently drop or duplicate a write under load).

open System
open System.Net.Http
open System.IO
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private bootstrapRequest (conn: SqliteConnection) =
    EventStore.initialize conn

/// A real `IMediathecaApi` built the same way `Composition.buildApp` builds
/// one per process — the only difference from production is the temp-file
/// factory and dummy provider/config values, none of which `addFriend`
/// touches (it never calls TMDB/RAWG/Steam/Jellyfin).
let private createApi (factory: unit -> SqliteConnection) (imageBasePath: string) : IMediathecaApi =
    Api.create
        factory
        (new HttpClient())
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        imageBasePath
        [] // no projection handlers needed — addFriend only touches the event store

[<Tests>]
let requestConnectionConcurrencyTests =
    testList "RequestConnectionConcurrency" [

        testCase "N concurrent addFriend calls, each opening its own connection via the factory, all complete with no SqliteConnection exception, and every friend is recorded" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapRequest
            let imageBasePath = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-request-lock-images-%s" (Guid.NewGuid().ToString("N")))
            Directory.CreateDirectory(imageBasePath) |> ignore
            try
                let api = createApi db.Factory imageBasePath
                let friendCount = 25

                let exceptions = System.Collections.Concurrent.ConcurrentBag<exn>()
                let results = System.Collections.Concurrent.ConcurrentBag<Result<string, string>>()

                let tasks =
                    [ 1 .. friendCount ]
                    |> List.map (fun i ->
                        Task.Run(fun () ->
                            try
                                let result = api.addFriend (sprintf "Concurrent Friend %d" i) |> Async.RunSynchronously
                                results.Add(result)
                            with ex ->
                                exceptions.Add(ex)))
                    |> List.toArray

                Task.WaitAll(tasks, TimeSpan.FromSeconds(30.0)) |> ignore

                Expect.isEmpty (exceptions |> List.ofSeq)
                    "No concurrent addFriend call should throw — per-request connections must not crash any request (this is the exact SqliteConnection does not support nested transactions repro)"

                let oks = results |> List.ofSeq |> List.choose (function Ok slug -> Some slug | Error _ -> None)
                Expect.equal (List.length oks) friendCount
                    "every concurrent addFriend should have succeeded — a per-request connection must not silently drop a write under load"
                Expect.equal (oks |> List.distinct |> List.length) friendCount
                    "every friend should have a distinct slug — no lost or duplicated command"
            finally
                try Directory.Delete(imageBasePath, true) with _ -> ()

        testCase "repeating the concurrent burst several times keeps surfacing no race (probabilistic regression confidence)" <| fun _ ->
            for _ in 1 .. 5 do
                use db = TestDb.withTempDbFactory bootstrapRequest
                let imageBasePath = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-request-lock-images-%s" (Guid.NewGuid().ToString("N")))
                Directory.CreateDirectory(imageBasePath) |> ignore
                try
                    let api = createApi db.Factory imageBasePath
                    let exceptions = System.Collections.Concurrent.ConcurrentBag<exn>()

                    let tasks =
                        [ 1 .. 10 ]
                        |> List.map (fun i ->
                            Task.Run(fun () ->
                                try
                                    api.addFriend (sprintf "Burst Friend %d" i) |> Async.RunSynchronously |> ignore
                                with ex ->
                                    exceptions.Add(ex)))
                        |> List.toArray

                    Task.WaitAll(tasks, TimeSpan.FromSeconds(30.0)) |> ignore

                    Expect.isEmpty (exceptions |> List.ofSeq) "No burst should ever throw"
                finally
                    try Directory.Delete(imageBasePath, true) with _ -> ()
    ]
