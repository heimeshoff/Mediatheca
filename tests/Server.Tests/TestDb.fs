module Mediatheca.Tests.TestDb

// administration-mz6kp (ADR-0033): a per-test, file-backed SQLite fixture
// sharing the exact production factory shape (`unit -> SqliteConnection`,
// per-connection pragmas re-applied on every open via
// `EventStore.configureConnection`) rather than `:memory:`.
//
// Deliberately NOT shared-cache `:memory:`:
//   1. a shared-cache in-memory DB is destroyed the instant its last
//      connection closes, so a factory-per-call fixture would silently hand
//      out an empty DB between two operations of the same test;
//   2. in-memory DBs don't use WAL, so they wouldn't actually exercise the
//      file-level serialization this migration ships (WAL + busy_timeout
//      giving write serialization ACROSS separate connections to the same
//      file — the exact property `Api.create`/`Administration.create` now
//      lean on for every request).
//
// `bootstrap` runs once, on one connection, before the factory is handed
// back — schema/table creation only (each test file's own previous
// `createInMemoryConnection`-style setup), mirroring the one-time startup
// step `Composition.buildApp` does on its own bootstrap `conn`.

open System
open System.IO
open Microsoft.Data.Sqlite
open Mediatheca.Server

/// `Connection` is a single, long-lived connection opened at construction
/// time (after `bootstrap` has run on it) — tests use it directly for setup
/// and assertions, exactly like the `conn` a `createInMemoryConnection`
/// helper used to hand back. `Factory` is the same-shaped
/// `unit -> SqliteConnection` production code takes, opening a fresh
/// connection to the SAME underlying file on every call (visible to
/// `Connection`'s writes and vice versa — same-file WAL connections, not
/// isolated in-memory databases). Disposing deletes the backing `.db` file
/// and its `-wal`/`-shm` sidecars.
type TempDb(bootstrap: SqliteConnection -> unit) =
    let path = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-test-%s.db" (Guid.NewGuid().ToString("N")))

    let factory () : SqliteConnection =
        let conn = new SqliteConnection($"Data Source={path}")
        conn.Open()
        EventStore.configureConnection conn
        conn

    let connection = factory ()
    do bootstrap connection

    member _.Connection : SqliteConnection = connection
    member _.Factory : unit -> SqliteConnection = factory

    interface IDisposable with
        member _.Dispose() =
            connection.Dispose()
            for suffix in [ ""; "-wal"; "-shm" ] do
                let f = path + suffix
                if File.Exists(f) then
                    try File.Delete(f) with _ -> ()

/// `use db = TestDb.withTempDbFactory bootstrap` — disposal (including the
/// backing files) happens automatically at the end of the enclosing scope,
/// exception or not, exactly like the `use conn = createInMemoryConnection ()`
/// pattern it replaces.
let withTempDbFactory (bootstrap: SqliteConnection -> unit) : TempDb =
    new TempDb(bootstrap)
