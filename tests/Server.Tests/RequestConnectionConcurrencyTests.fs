module Mediatheca.Tests.RequestConnectionConcurrencyTests

// administration-cx92m (ADR-0030): regression coverage for the
// request×request connection race. `Microsoft.Data.Sqlite.SqliteConnection`
// is not thread-safe for concurrent command creation/disposal from multiple
// threads (ADR-0028's root-cause finding, corrected here from the dedicated
// job connection to the shared request connection) — Kestrel/Giraffe
// genuinely dispatch concurrent requests on different thread-pool threads,
// and administration-a4d9b's Playwright specs empirically proved that
// concurrent `addFriend` calls crash the shared `conn` with
// `SqliteConnection does not support nested transactions`.
//
// This test drives the real, unmodified production choke point
// (`Api.executeCommand`'s body, reached here via the real `IMediathecaApi`
// built by `Api.create`, through `addFriend` — a command entirely
// synchronous end to end, with no external HTTP dependency, making it the
// simplest real repro of the exact production crash) against a real
// temp-file SQLite connection (not `:memory:`), firing many concurrent
// `addFriend` calls sharing the SAME `requestDbLock` instance
// `Composition.fs` builds once per process. Asserts no exception (the exact
// crash this fix prevents) and that every friend was actually recorded (the
// lock didn't silently drop a write under load).

open System
open System.IO
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private createFileConn () : SqliteConnection * string =
    let path = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-request-lock-test-%s.db" (Guid.NewGuid().ToString("N")))
    let conn = new SqliteConnection($"Data Source={path}")
    conn.Open()
    EventStore.initialize conn
    conn, path

let private cleanup (conn: SqliteConnection) (path: string) =
    conn.Dispose()
    for suffix in [ ""; "-wal"; "-shm" ] do
        let f = path + suffix
        if File.Exists(f) then try File.Delete(f) with _ -> ()

/// A real `IMediathecaApi` built the same way `Composition.buildApp` builds
/// one per process — the only difference from production is the temp-file
/// `conn`/`dbPath` and dummy provider/config values, none of which
/// `addFriend` touches (it never calls TMDB/RAWG/Steam/Jellyfin).
let private createApi (conn: SqliteConnection) (dbLock: SemaphoreSlim) (imageBasePath: string) : IMediathecaApi =
    Api.create
        conn
        dbLock
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

        testCase "N concurrent addFriend calls on a real temp-file connection all complete with no SqliteConnection exception, and every friend is recorded" <| fun _ ->
            let conn, path = createFileConn ()
            try
                let dbLock = new SemaphoreSlim(1, 1)
                let imageBasePath = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-request-lock-images-%s" (Guid.NewGuid().ToString("N")))
                Directory.CreateDirectory(imageBasePath) |> ignore
                try
                    let api = createApi conn dbLock imageBasePath
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
                        "No concurrent addFriend call should throw — the shared-connection race must not crash any request (this is the exact SqliteConnection does not support nested transactions repro)"

                    let oks = results |> List.ofSeq |> List.choose (function Ok slug -> Some slug | Error _ -> None)
                    Expect.equal (List.length oks) friendCount
                        "every concurrent addFriend should have succeeded — the lock must not silently drop a write under load"
                    Expect.equal (oks |> List.distinct |> List.length) friendCount
                        "every friend should have a distinct slug — no lost or duplicated command"
                finally
                    try Directory.Delete(imageBasePath, true) with _ -> ()
            finally
                cleanup conn path

        testCase "repeating the concurrent burst several times keeps surfacing no race (probabilistic regression confidence)" <| fun _ ->
            for _ in 1 .. 5 do
                let conn, path = createFileConn ()
                try
                    let dbLock = new SemaphoreSlim(1, 1)
                    let imageBasePath = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-request-lock-images-%s" (Guid.NewGuid().ToString("N")))
                    Directory.CreateDirectory(imageBasePath) |> ignore
                    try
                        let api = createApi conn dbLock imageBasePath
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
                finally
                    cleanup conn path
    ]
