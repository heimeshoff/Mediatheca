/// Shared server composition root. `Program.fs` (Docker/CLI entry point) and
/// `src/Desktop/Program.fs` (Photino desktop shell) both call `buildApp` so
/// there is exactly one place that wires up the database, projections,
/// scheduled jobs, and the Fable.Remoting API — no duplicated setup between
/// deployment targets.
module Mediatheca.Server.Composition

open System
open System.IO
open System.Net.Http
open System.Threading
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.ResponseCompression
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

/// A raw, uninitialized `SqliteConnection` factory (administration-mz6kp,
/// ADR-0033) — pragmas only (`EventStore.configureConnection`), never table/
/// FTS creation, which stays a one-time startup step on the bootstrap `conn`
/// (`createConnection` above). Pooling (Microsoft.Data.Sqlite pools physical
/// connections per connection string) makes `use conn = factory()` cheap: a
/// warm pooled handle, not a real file-open, on every call.
let createConnectionFactory (dbPath: string) : unit -> SqliteConnection =
    fun () ->
        let conn = new SqliteConnection($"Data Source={dbPath}")
        conn.Open()
        EventStore.configureConnection conn
        conn

/// Build (but do not run) the app. `urls` overrides Kestrel's bind address
/// (ASPNETCORE_URLS / default) — used by the desktop shell to force a
/// loopback-only, ephemeral-port bind (ADR-0007: no auth, so the desktop
/// server must never be network-reachable). Pass `None` to keep whatever
/// binding the hosting environment already configures (Docker sets
/// ASPNETCORE_URLS itself).
let buildApp (args: string[]) (urls: string option) : WebApplication =
    let builder = WebApplication.CreateBuilder(args)

    builder.Services.AddGiraffe() |> ignore

    // Response compression (Brotli + gzip, the defaults when no provider is
    // registered explicitly). The Fable.Remoting list endpoints dominate the
    // client's cold start — getGames alone is ~261 KB of JSON — and the whole
    // library snapshot the Ctrl+K search modal depends on is ~354 KB
    // uncompressed. JSON of that shape compresses several-fold.
    //
    // Deliberately left on the default MIME list, which covers application/
    // json, text/html, text/css and application/javascript but NOT
    // text/event-stream: the /api/stream/* SSE handlers must keep flushing
    // event-by-event, so they stay uncompressed.
    //
    // EnableForHttps is on because everything here is already carried over an
    // encrypted transport (Tailscale) or loopback (the desktop shell), and the
    // app is single-user with no authentication and no session cookies
    // (ADR-0007) — so the BREACH-style concern that motivates the default-off
    // setting has no secret to leak. Without this, compression would silently
    // stop the day the app is fronted by HTTPS.
    builder.Services.AddResponseCompression(fun (options: ResponseCompressionOptions) ->
        options.EnableForHttps <- true
    ) |> ignore

    urls |> Option.iter (fun u -> builder.WebHost.UseUrls(u) |> ignore)

    let app = builder.Build()

    // Data directory — configurable via DATA_DIR env var, per-platform default otherwise
    let dataDir = DataDir.resolveDefault ()

    if not (Directory.Exists(dataDir)) then
        Directory.CreateDirectory(dataDir) |> ignore

    // Initialize database
    let dbPath = Path.Combine(dataDir, "mediatheca.db")

    let conn = createConnection dbPath

    // administration-mz6kp (ADR-0033): every request/SSE-handler opens and
    // disposes its own `SqliteConnection` from this factory instead of
    // sharing the bootstrap `conn` above — removing the shared mutable
    // connection object by construction, so the ADR-0028-class object-level
    // command-creation/disposal race cannot arise on the request path at
    // all. Supersedes ADR-0030's process-wide `requestDbLock`, which guarded
    // that shared object and is retired along with it. `conn` itself is no
    // longer shared with request threads — it remains only for this
    // function's own single-threaded startup work (seeds, backfill,
    // migrations, projection catch-up).
    let connectionFactory = createConnectionFactory dbPath

    // Initialize CastStore tables
    CastStore.initialize conn

    // Initialize SettingsStore
    SettingsStore.initialize conn

    // Pre-cutover whole-database safety copy (StartupCutover): VACUUM INTO a
    // dated file under <data-dir>/backups/, taken BEFORE this release's
    // silent migrations (the cache-tier renames, the one-time seed, the
    // deprecated-column drops) first touch an existing store. No-op once the
    // cutover has completed and on fresh installs. A failed backup only
    // disables the automated cutover for this boot — the app still starts.
    let cutoverBackupOk =
        match StartupCutover.backupIfPending conn dbPath with
        | Ok _ -> true
        | Error reason ->
            eprintfn "[StartupCutover] pre-cutover backup FAILED (%s) — the automated cutover will NOT run this boot" reason
            false

    // Initialize JellyfinStore tables
    JellyfinStore.initialize conn

    // Migrate Jellyfin data from old projection tables (one-time, idempotent)
    JellyfinStore.migrateFromProjections conn

    // Initialize the metadata cache tier (administration-c3nvp) — schema
    // only; seeding happens below, after projection tables exist.
    MetadataCache.initialize conn

    // games-p6vkz: PlaytimeTracker no longer owns any tables — the play
    // session table is now PlaySessionProjection's (checkpoint-tracked,
    // rebuildable) and the old Steam-sync cursor table is deleted outright
    // (the two-fold aggregate design makes the sync cursor derivable).
    // Nothing to initialize here anymore.

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

    // Dynamic TMDB config provider (reads from DB, falls back to env var).
    // administration-mz6kp (ADR-0033): these providers are invoked from
    // request-serving record members (potentially concurrently), so each
    // call opens its own short-lived connection via `connectionFactory`
    // rather than closing over the single-threaded startup `conn` above —
    // the same reasoning that moved every other request-reachable DB touch
    // off that shared connection.
    let getTmdbConfig () : Tmdb.TmdbConfig =
        use conn = connectionFactory ()
        let apiKey =
            SettingsStore.getSetting conn "tmdb_api_key"
            |> Option.orElse envTmdbKey
            |> Option.defaultValue ""
        { ApiKey = apiKey
          ImageBaseUrl = "https://image.tmdb.org/t/p/" }

    // Dynamic RAWG config provider (reads from DB, falls back to env var)
    let getRawgConfig () : Rawg.RawgConfig =
        use conn = connectionFactory ()
        let apiKey =
            SettingsStore.getSetting conn "rawg_api_key"
            |> Option.orElse envRawgKey
            |> Option.defaultValue ""
        { ApiKey = apiKey }

    // Dynamic Jellyfin config provider (reads from DB)
    let getJellyfinConfig () : Jellyfin.JellyfinConfig =
        use conn = connectionFactory ()
        { ServerUrl = SettingsStore.getSetting conn "jellyfin_server_url" |> Option.defaultValue ""
          Username = SettingsStore.getSetting conn "jellyfin_username" |> Option.defaultValue ""
          Password = SettingsStore.getSetting conn "jellyfin_password" |> Option.defaultValue ""
          UserId = SettingsStore.getSetting conn "jellyfin_user_id" |> Option.defaultValue ""
          AccessToken = SettingsStore.getSetting conn "jellyfin_access_token" |> Option.defaultValue "" }

    // Dynamic Steam config provider (reads from DB, falls back to env var)
    let getSteamConfig () : Steam.SteamConfig =
        use conn = connectionFactory ()
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
        PlaySessionProjection.handler
    ]

    // Catch up all projections from their saved checkpoints. Projections are
    // disposable read models (ADR-0002); a full rebuild is now an explicit
    // operator command from the Projections admin tab
    // (Administration.projectionRebuildStreamHandler) rather than something
    // startup forces on every boot. Routed through StartupCutover so a boot
    // after a crash inside the cutover's migrate→rebuild window rebuilds
    // (drop + replay, always safe) instead of double-applying via catch-up.
    StartupCutover.ensureSafeCatchUp conn projectionHandlers

    // Seed the metadata cache tier from the now-guaranteed-to-exist
    // game_detail projection snapshot (administration-c3nvp) — gated on the
    // metadata_cache_seeded marker, so this is a no-op after the first run.
    MetadataCache.seedFromProjections conn

    // series-d5tpn: drop the externally-sourced series_list/series_detail
    // columns now that nothing writes or reads them. MUST run after the seed
    // above — the seed's SELECT reads these same columns off series_detail,
    // so dropping first would break it on any database not yet seeded.
    SeriesProjection.dropDeprecatedColumns conn

    // games-v4nqe: drops the description/short_description/website_url/hltb_*/
    // play_modes/steam_last_played columns the emission cutover makes dead.
    // `genres` is deliberately excluded (ADR-0055) — see
    // GameProjection.dropDeprecatedColumns's doc comment.
    GameProjection.dropDeprecatedColumns conn

    // The automated series + play-session cutover (plan.md Phases 4-5):
    // drift check → compensating events → SeriesProjection rebuild, then
    // dry-run gate → play-session migration → rebuild-all → final drift
    // check. One-time (completion marker), idempotent on retry, and runs in
    // this single-threaded startup window — before the web server serves a
    // request and before any scheduled job starts, so no guard interleaving
    // is possible. Skipped if the pre-cutover backup could not be taken.
    if cutoverBackupOk then
        StartupCutover.run conn dbPath projectionHandlers

    // Game journal (Notion-style blocks, plain storage) — table + one-time
    // migration of the old event-sourced game content blocks
    GameJournal.initialize conn
    GameJournal.migrateFromContentBlocks conn dataDir

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

    // Initialize JellyfinSync (restore last sync time from DB)
    JellyfinSync.initialize conn

    // Scheduled jobs registry (administration-yamm5 / ADR-0026): built here,
    // above both `Administration.create` and `ScheduledJobs.startAll`, so the
    // same list — and the same job-runs recorder — feed both the Jobs tab's
    // read API and the daily timer. Daily refresh at a configurable
    // local-time hour (defaults to 04:00 local).
    let playtimeSyncHour =
        SettingsStore.getSetting conn "playtime_sync_hour"
        |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
        |> Option.defaultValue 4

    let seriesRefreshHour =
        SettingsStore.getSetting conn "series_refresh_hour"
        |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
        |> Option.defaultValue 4

    // games-a7dqx (ADR-0053): resumable throttled play-facets backfill.
    // Defaults to 05:00 local, an hour clear of the Steam playtime sync
    // (04:00) so the two jobs' Steam Store API calls don't pile up on the
    // same catch-up window.
    let facetBackfillHour =
        SettingsStore.getSetting conn "facet_backfill_hour"
        |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
        |> Option.defaultValue 5

    // games-b8xnw (ADR-0043/ADR-0045): resumable throttled Deck-compat
    // backfill, reusing games-a7dqx's job shape (its own `depends_on`
    // reason). Defaults to 06:00 local, an hour clear of the play-facets
    // backfill (05:00) so the two jobs' Steam Store fetches don't pile up.
    let deckCompatBackfillHour =
        SettingsStore.getSetting conn "deck_compat_backfill_hour"
        |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
        |> Option.defaultValue 6

    // games-ev65k (ADR-0043/ADR-0045): resumable throttled release-date
    // backfill, reusing the same job shape as games-a7dqx/games-b8xnw.
    // Defaults to 07:00 local, an hour clear of the Deck-compat backfill
    // (06:00) so the three jobs' Steam Store fetches don't pile up.
    let releaseDateBackfillHour =
        SettingsStore.getSetting conn "release_date_backfill_hour"
        |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
        |> Option.defaultValue 7

    // administration-tj8n2 (ADR-0028): scheduled jobs get their OWN connection,
    // dedicated and never shared with request threads or `conn` — separate
    // from the request-serving `conn` above. Both jobs (and the job-runs
    // recorder) share this ONE job connection plus `jobDbLock`, a
    // SemaphoreSlim(1,1) acquired around each job's individual DB-touching
    // sections (never across an awaited HTTP call) so the two jobs' network
    // I/O still overlaps (ADR-0026's "two different jobs can run
    // concurrently") while their brief DB moments serialize. This corrects
    // ADR-0024/0026's premise that WAL + busy_timeout alone makes ONE shared
    // `SqliteConnection` object safe for concurrent multi-threaded command
    // creation/disposal — it doesn't; that reasoning only covers separate
    // connections to the same file, not one connection used by two threads.
    let jobConn = createConnection dbPath
    let jobDbLock = new SemaphoreSlim(1, 1)

    let scheduledJobs : ScheduledJobs.JobSpec list = [
        { Name = "Steam playtime sync"
          Hour = playtimeSyncHour
          Run = fun () ->
            async {
                // PlaytimeTracker computes the gaming-day internally (boundary at syncHour + 30 min),
                // so a 04:00 scheduled fire and an early-morning catch-up both attribute to yesterday.
                match! PlaytimeTracker.runSync jobConn jobDbLock httpClient getSteamConfig getRawgConfig imageBasePath projectionHandlers None with
                | Ok result ->
                    let summary =
                        sprintf "%d sessions, %d snapshots, %d games created, %d promoted to focus"
                            result.SessionsRecorded result.SnapshotsUpdated result.GamesCreated result.GamesPromotedToFocus
                    eprintfn "[PlaytimeTracker] Sync complete: %s" summary
                    return ({ Disposition = ScheduledJobs.JobDisposition.Ok; Summary = summary } : ScheduledJobs.JobRunOutcome)
                | Error err ->
                    eprintfn "[PlaytimeTracker] Sync skipped: %s" err
                    return ({ Disposition = ScheduledJobs.JobDisposition.Skipped; Summary = err } : ScheduledJobs.JobRunOutcome)
            } }
        { Name = "Series TMDB refresh"
          Hour = seriesRefreshHour
          Run = fun () ->
            async {
                let! summary = SeriesRefresh.runNightlyJob jobConn jobDbLock httpClient getTmdbConfig imageBasePath projectionHandlers
                if summary.Skipped then
                    return ({ Disposition = ScheduledJobs.JobDisposition.Skipped; Summary = "TMDB API key not configured" } : ScheduledJobs.JobRunOutcome)
                else
                    let text =
                        sprintf "%d refreshed, %d errors, %d new episodes, %d status transitions"
                            summary.Refreshed summary.Errors summary.NewEpisodes summary.StatusTransitions
                    return ({ Disposition = ScheduledJobs.JobDisposition.Ok; Summary = text } : ScheduledJobs.JobRunOutcome)
            } }
        { Name = "Game play-facets backfill"
          Hour = facetBackfillHour
          Run = fun () ->
            async {
                let! result = GameFacetBackfill.runBackfill jobConn jobDbLock httpClient
                let summary = sprintf "%d/%d games fetched, %d errors" result.Succeeded result.Processed result.Errors
                eprintfn "[GameFacetBackfill] %s" summary
                return ({ Disposition = ScheduledJobs.JobDisposition.Ok; Summary = summary } : ScheduledJobs.JobRunOutcome)
            } }
        { Name = "Game Deck-compat backfill"
          Hour = deckCompatBackfillHour
          Run = fun () ->
            async {
                let! result = GameDeckCompatBackfill.runBackfill jobConn jobDbLock httpClient
                let summary = sprintf "%d/%d games fetched, %d errors" result.Succeeded result.Processed result.Errors
                eprintfn "[GameDeckCompatBackfill] %s" summary
                return ({ Disposition = ScheduledJobs.JobDisposition.Ok; Summary = summary } : ScheduledJobs.JobRunOutcome)
            } }
        { Name = "Game release-date backfill"
          Hour = releaseDateBackfillHour
          Run = fun () ->
            async {
                let! result = GameReleaseDateBackfill.runBackfill jobConn jobDbLock httpClient
                let summary = sprintf "%d/%d games fetched, %d errors" result.Succeeded result.Processed result.Errors
                eprintfn "[GameReleaseDateBackfill] %s" summary
                return ({ Disposition = ScheduledJobs.JobDisposition.Ok; Summary = summary } : ScheduledJobs.JobRunOutcome)
            } }
    ]

    // job_runs table + startup crash reconciliation (ADR-0026) — table
    // creation/reconciliation runs once at startup, before any job-connection
    // concurrency exists, so it stays on the shared `conn`; the table itself
    // is file-level, not connection-level, so `jobConn`'s later reads/writes
    // to it are unaffected.
    Administration.initializeJobRuns conn
    let jobRunRecorder = Administration.makeJobRunRecorder jobConn jobDbLock

    // Per-instance projection guards (ADR-0035): built exactly once here and
    // passed to every consumer below, so "one guard per process" is a
    // property of this wiring rather than of Administration.fs.
    let adminGuards = Administration.makeGuards ()

    // Create API
    let api = Api.create connectionFactory httpClient getTmdbConfig getRawgConfig getSteamConfig getJellyfinConfig imageBasePath projectionHandlers
    let adminApi = Administration.create connectionFactory dbPath imageBasePath projectionHandlers scheduledJobs jobRunRecorder adminGuards

    let remotingHandler =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder Route.builder
        |> Remoting.fromValue api
        |> Remoting.withErrorHandler (fun ex _routeInfo ->
            eprintfn "Fable.Remoting error: %s\n%s" ex.Message ex.StackTrace
            Propagate ex.Message)
        |> Remoting.buildHttpHandler

    let adminRemotingHandler =
        Remoting.createApi ()
        |> Remoting.withRouteBuilder AdminRoute.builder
        |> Remoting.fromValue adminApi
        |> Remoting.withErrorHandler (fun ex _routeInfo ->
            eprintfn "Fable.Remoting error (admin): %s\n%s" ex.Message ex.StackTrace
            Propagate ex.Message)
        |> Remoting.buildHttpHandler

    let webApp =
        choose [
            route "/health" >=> text "ok"
            route "/api/stream/import-steam-family"
                >=> Api.steamFamilyImportHandler connectionFactory httpClient getRawgConfig getSteamConfig imageBasePath projectionHandlers
            route "/api/stream/steam-connect"
                >=> Api.steamConnectStreamHandler connectionFactory
            route "/api/stream/export-events"
                >=> Administration.exportEventsStreamHandler connectionFactory
            route "/api/stream/import-events"
                >=> Administration.importEventsStreamHandler connectionFactory
            route "/api/stream/wipe-import-events"
                >=> Administration.wipeImportEventsStreamHandler connectionFactory dbPath projectionHandlers adminGuards
            Administration.projectionRebuildStreamHandler connectionFactory projectionHandlers adminGuards
            route "/api/stream/drift-check"
                >=> Administration.driftCheckStreamHandler connectionFactory projectionHandlers adminGuards
            remotingHandler
            adminRemotingHandler
        ]

    // Must precede the static-file and Giraffe middleware so their responses
    // pass through the compression stream.
    app.UseResponseCompression() |> ignore

    // Serve static files from deploy/public in production
    let staticPath = Path.Combine(Directory.GetCurrentDirectory(), "deploy", "public")
    if Directory.Exists(staticPath) then
        let fileProvider = new PhysicalFileProvider(staticPath)
        app.UseDefaultFiles(DefaultFilesOptions(FileProvider = fileProvider)) |> ignore
        // Register .webmanifest so the PWA manifest is served as application/manifest+json.
        // (.js, .png are already mapped by the default provider.)
        let contentTypeProvider = FileExtensionContentTypeProvider()
        contentTypeProvider.Mappings[".webmanifest"] <- "application/manifest+json"
        app.UseStaticFiles(
            StaticFileOptions(
                FileProvider = fileProvider,
                ContentTypeProvider = contentTypeProvider
            )
        ) |> ignore

    // Serve images from /images path
    if Directory.Exists(imageBasePath) then
        app.UseStaticFiles(
            StaticFileOptions(
                FileProvider = new PhysicalFileProvider(imageBasePath),
                RequestPath = "/images"
            )
        ) |> ignore

    app.UseGiraffe webApp

    // Scheduled jobs: registry, hours, and the job-runs recorder are all
    // built above (before Administration.create) per ADR-0026 — start the
    // timers here, sharing the same recorder instance.
    //
    // MEDIATHECA_DISABLE_SCHEDULED_JOBS retired (administration-tj8n2 /
    // ADR-0028): it existed only to dodge the catch-up-timer connection race
    // for the Playwright e2e harness (administration-da908 / ADR-0027). That
    // race is fixed for real now — jobs run on their own dedicated connection
    // plus a per-command lock, not the shared `conn` — so the harness no
    // longer needs an escape hatch and jobs start unconditionally, exactly
    // like every other run.
    let _scheduledJobTimers = ScheduledJobs.startAll jobRunRecorder scheduledJobs

    app
