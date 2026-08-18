module Mediatheca.Tests.SteamFamilyIncrementalImportTests

/// integration-n3vqa: the one-click family import used to re-enrich *every*
/// shared app on every click -- a Steam Store `appdetails` request per
/// matched (already-known) game as well as per new one -- which is the
/// burst-enumerate-everything traffic shape Valve has twice flagged the
/// builder's account over. This suite pins the diff-don't-re-import fix:
///
/// 1. The steady-state import (a family library already fully imported,
///    zero new apps) issues a *fixed, enumerated* total of outbound Steam
///    requests -- the family/shared-library/owned-games calls only, never
///    an `appdetails` sweep across already-known games.
/// 2. Each genuinely new app still costs exactly one `appdetails` request,
///    so the total grows by exactly one such request per new app.
/// 3. Known games still get `Set_steam_library_date` / family-owner updates
///    on the default (diffing) path -- no regression of integration-hebjs.
/// 4. The result names each newly-acquired game (name, acquired date, which
///    family member added it when mapped) and the count since the previous
///    `steam_family_last_sync`.
/// 5. The last result persists (`steam_family_last_result`) and is readable
///    back via `getSteamFamilyLastResult` after a "reload".
/// 6. An explicit "full re-enrich" run reproduces today's behaviour
///    (`appdetails` for every app, known or new).
///
/// No test here makes a live Steam call -- every request goes through a
/// stub `HttpMessageHandler` that also counts every outbound request by
/// category (ADR-0066's throttle is a different, complementary concern:
/// request *spacing*, not *count* -- this suite never asserts spacing).

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

type private StubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, Text.Encoding.UTF8, "application/json")
    resp

let private notFoundResponse () =
    new HttpResponseMessage(HttpStatusCode.NotFound)

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ ContentBlockProjection.handler; GameProjection.handler; PlaySessionProjection.handler ]

/// Counts every outbound request the fake `HttpClient` sees, bucketed by
/// which Steam endpoint it hit -- the "exact total" the hardest acceptance
/// criterion pins is built from these buckets, not a single opaque counter,
/// so a mis-attributed request (e.g. hitting the wrong bucket) shows up
/// immediately rather than being masked by an aggregate that still adds up.
type private RequestCounts() =
    let counts = System.Collections.Concurrent.ConcurrentDictionary<string, int>()
    member _.Record(bucket: string) =
        counts.AddOrUpdate(bucket, 1, fun _ n -> n + 1) |> ignore
    member _.Get(bucket: string) =
        match counts.TryGetValue(bucket) with
        | true, n -> n
        | false, _ -> 0
    member _.Total = counts.Values |> Seq.sum

// A non-empty ApiKey: `Steam.tryGetOwnedGames` short-circuits to `Ok []` with
// NO HTTP call at all when the Web API key is blank (matching `getOwnedGames`'s
// existing degenerate-config behaviour) -- this suite needs the GetOwnedGames
// call to actually happen so it counts toward the enumerated baseline total.
let private steamConfig = ({ ApiKey = "test-web-api-key"; SteamId = "76561198000000001" } : Steam.SteamConfig)

let private createApi (factory: unit -> SqliteConnection) (httpClient: HttpClient) : IMediathecaApi =
    Api.create
        factory
        httpClient
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig)) // empty RAWG key: no RAWG calls, keeps the counted total deterministic
        (fun () -> steamConfig)
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        noImagesDir
        allProjectionHandlers

/// Builds a fake `HttpClient` answering the family/owned-games calls with
/// `sharedAppsJson` verbatim, `appdetails` with a minimal valid payload for
/// whatever appid was requested, and everything else (CDN cover/backdrop
/// downloads) with 404 (already known to degrade gracefully per
/// `AddGameFromSteamTests`). Every request increments `counts` under its
/// bucket.
let private buildHttpClient (sharedAppsJson: string) (counts: RequestCounts) : HttpClient =
    let handler =
        new StubHandler(fun req ->
            let url = req.RequestUri.ToString()
            if url.Contains("GetOwnedGames") then
                counts.Record("ownedGames")
                jsonResponse """{"response":{"game_count":0,"games":[]}}"""
            elif url.Contains("GetFamilyGroupForUser") then
                counts.Record("familyGroupForUser")
                jsonResponse """{"response":{"family_groupid":"12345","members":[]}}"""
            elif url.Contains("GetSharedLibraryApps") then
                counts.Record("sharedLibraryApps")
                jsonResponse (sprintf """{"response":{"apps":[%s]}}""" sharedAppsJson)
            elif url.Contains("appdetails") then
                counts.Record("appdetails")
                let m = System.Text.RegularExpressions.Regex.Match(url, @"appids=(\d+)")
                let appid = if m.Success then m.Groups.[1].Value else "0"
                jsonResponse (
                    sprintf
                        """{"%s":{"success":true,"data":{"about_the_game":"","short_description":"","detailed_description":"","website":null,"categories":[],"release_date":{"coming_soon":false,"date":""}}}}"""
                        appid)
            elif url.Contains("steamcdn") then
                counts.Record("cdn")
                notFoundResponse ()
            else
                counts.Record("other")
                notFoundResponse ())
    new HttpClient(handler)

let private sharedApp (appid: int) (name: string) (rtTimeAcquired: int) (ownerSteamids: string list) =
    let owners = ownerSteamids |> List.map (sprintf "\"%s\"") |> String.concat ","
    sprintf """{"appid":%d,"name":"%s","owner_steamids":[%s],"rt_time_acquired":%d}""" appid name owners rtTimeAcquired

[<Tests>]
let steamFamilyIncrementalImportTests =
    testList "Steam Family incremental import -- diff, don't re-import (integration-n3vqa)" [

        testCase "A default import with zero new apps issues exactly the enumerated baseline total -- no appdetails sweep across known games" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"

            // First import: one shared app, not yet in the library -- creates
            // it (the one legitimate appdetails call this setup phase costs).
            let seedCounts = RequestCounts()
            let seedApps = sharedApp 620 "Portal 2" 1_700_000_000 [ "76561198000000001" ]
            let seedHttp = buildHttpClient seedApps seedCounts
            let seedApi = createApi db.Factory seedHttp
            match seedApi.importSteamFamily () |> Async.RunSynchronously with
            | Ok _ -> ()
            | Error e -> failtestf "Seed import failed: %s" e

            // Second import: same one shared app, now already known by
            // SteamAppId -- the run this criterion is actually about.
            let counts = RequestCounts()
            let http = buildHttpClient seedApps counts
            let api = createApi db.Factory http

            let result = api.importSteamFamily () |> Async.RunSynchronously

            match result with
            | Ok r -> Expect.equal r.GamesProcessed 1 "The one known app still goes through the per-app loop"
            | Error e -> failtestf "Expected Ok, got Error %s" e

            Expect.equal (counts.Get "appdetails") 0 "Zero appdetails calls for a known app on the default path"
            Expect.equal (counts.Get "familyGroupForUser") 1 "Exactly one GetFamilyGroupForUser call"
            Expect.equal (counts.Get "sharedLibraryApps") 1 "Exactly one GetSharedLibraryApps call"
            Expect.equal (counts.Get "ownedGames") 1 "Exactly one GetOwnedGames call"
            Expect.equal (counts.Get "cdn") 0 "No cover/backdrop downloads for a known app"
            // The exact total, enumerated -- not merely the per-app figure
            // above: an unnoticed extra sweep would slip through a per-app-only
            // assertion but not this one.
            Expect.equal counts.Total 3 "Total outbound Steam requests for a zero-new-apps default import is exactly 3 (family group + shared library + owned games)"

        testCase "One genuinely new app grows the total by exactly one appdetails call, on top of the known-app baseline" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"

            let seedCounts = RequestCounts()
            let knownApp = sharedApp 620 "Portal 2" 1_700_000_000 [ "76561198000000001" ]
            let seedHttp = buildHttpClient knownApp seedCounts
            let seedApi = createApi db.Factory seedHttp
            seedApi.importSteamFamily () |> Async.RunSynchronously |> ignore

            let counts = RequestCounts()
            let newApp = sharedApp 730 "Counter-Strike 2" 1_800_000_000 [ "76561198000000001" ]
            let apps = knownApp + "," + newApp
            let http = buildHttpClient apps counts
            let api = createApi db.Factory http

            let result = api.importSteamFamily () |> Async.RunSynchronously

            match result with
            | Ok r ->
                Expect.equal r.GamesProcessed 2 "Both the known and the new app go through the per-app loop"
                Expect.equal r.GamesCreated 1 "Exactly the new app is created"
            | Error e -> failtestf "Expected Ok, got Error %s" e

            Expect.equal (counts.Get "appdetails") 1 "Exactly one appdetails call -- for the new app only"

        testCase "Known games still get Set_steam_library_date and family-owner updates on the default (diffing) path -- no regression of integration-hebjs" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"

            let counts0 = RequestCounts()
            let app = sharedApp 620 "Portal 2" 1_700_000_000 [ "76561198000000001" ]
            let http0 = buildHttpClient app counts0
            let api0 = createApi db.Factory http0
            api0.importSteamFamily () |> Async.RunSynchronously |> ignore

            let slug =
                match GameProjection.findBySteamAppId db.Connection 620 with
                | Some s -> s
                | None -> failtest "Expected the seed import to have created a game linked to appid 620"
            Expect.isTrue (GameProjection.getBySlug db.Connection slug).Value.IsOwnedByMe "Seed import marked the caller's own copy as owned"

            // Re-import with a later acquired-date -- the default (known) path
            // must still push the updated library date and re-affirm family
            // ownership, even though it skips appdetails entirely.
            let counts1 = RequestCounts()
            let updatedApp = sharedApp 620 "Portal 2" 1_750_000_000 [ "76561198000000001" ]
            let http1 = buildHttpClient updatedApp counts1
            let api1 = createApi db.Factory http1
            match api1.importSteamFamily () |> Async.RunSynchronously with
            | Ok r -> Expect.equal r.FamilyOwnersSet 1 "The known app's ownership is still (re-)affirmed on the default path"
            | Error e -> failtestf "Expected Ok, got Error %s" e

            let detail = (GameProjection.getBySlug db.Connection slug).Value
            Expect.equal detail.SteamLibraryDate (Steam.unixTimestampToDateString 1_750_000_000) "SteamLibraryDate reflects the updated rt_time_acquired despite skipping appdetails"
            Expect.isTrue detail.IsOwnedByMe "Ownership is still set on the default path"

        testCase "Arrivals: a brand-new app is named with acquired date and the mapped family member who added it, and the count is against the previous last-sync" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"
            let previousSync = DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
            SettingsStore.setSetting db.Connection "steam_family_last_sync" (previousSync.ToString("o"))
            let membersJson =
                """[{"steamId":"76561198000000002","displayName":"Sam","friendSlug":"sam","isMe":false}]"""
            SettingsStore.setSetting db.Connection "steam_family_members" membersJson

            let counts = RequestCounts()
            // rt_time_acquired well after the previous sync.
            let arrivedApp = sharedApp 730 "Counter-Strike 2" 1_800_000_000 [ "76561198000000002" ]
            let http = buildHttpClient arrivedApp counts
            let api = createApi db.Factory http

            let result = api.importSteamFamily () |> Async.RunSynchronously

            match result with
            | Ok r ->
                Expect.equal r.Arrivals.Length 1 "Exactly one arrival -- the new app"
                let arrival = r.Arrivals.Head
                Expect.equal arrival.Name "Counter-Strike 2" "Arrival names the game"
                Expect.equal arrival.AcquiredDate (Steam.unixTimestampToDateString 1_800_000_000) "Arrival carries the acquired date"
                Expect.equal arrival.AddedBy (Some "sam") "Arrival names the mapped family member who added it"
                Expect.equal r.SinceLastSync (Some (previousSync.ToString("o"))) "Result reports what it diffed against"
            | Error e -> failtestf "Expected Ok, got Error %s" e

        testCase "A known app whose rt_time_acquired postdates the previous sync is still reported as an arrival (a family member bought a game you already own)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"

            let seedCounts = RequestCounts()
            let app = sharedApp 620 "Portal 2" 1_700_000_000 [ "76561198000000001" ]
            let seedHttp = buildHttpClient app seedCounts
            let seedApi = createApi db.Factory seedHttp
            seedApi.importSteamFamily () |> Async.RunSynchronously |> ignore
            // seed import just set steam_family_last_sync to "now" -- push the
            // cursor back into the past so the second run's rt_time_acquired
            // (fixed at 1_700_000_000, i.e. 2023) reads as "after last sync".
            SettingsStore.setSetting db.Connection "steam_family_last_sync" (DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o"))

            let counts = RequestCounts()
            let http = buildHttpClient app counts
            let api = createApi db.Factory http
            match api.importSteamFamily () |> Async.RunSynchronously with
            | Ok r ->
                Expect.equal r.Arrivals.Length 1 "The already-known app is still reported as an arrival"
                Expect.equal (counts.Get "appdetails") 0 "...without costing an appdetails call"
            | Error e -> failtestf "Expected Ok, got Error %s" e

        testCase "The last import result persists and is readable back via getSteamFamilyLastResult after a reload" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"

            let counts = RequestCounts()
            let app = sharedApp 620 "Portal 2" 1_700_000_000 [ "76561198000000001" ]
            let http = buildHttpClient app counts
            let api = createApi db.Factory http

            match api.importSteamFamily () |> Async.RunSynchronously with
            | Ok _ -> ()
            | Error e -> failtestf "Expected Ok, got Error %s" e

            match api.getSteamFamilyLastResult () |> Async.RunSynchronously with
            | Some persisted ->
                Expect.equal persisted.GamesCreated 1 "The persisted result reflects the completed import"
                Expect.equal persisted.Arrivals.Length 1 "The persisted result carries the arrivals list too"
            | None -> failtest "Expected the last result to be persisted and readable back"

        testCase "A full re-enrich run fetches appdetails for every app, known or new -- reproducing today's (pre-n3vqa) behaviour" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "steam_family_token" "valid-family-token"

            let seedCounts = RequestCounts()
            let app = sharedApp 620 "Portal 2" 1_700_000_000 [ "76561198000000001" ]
            let seedHttp = buildHttpClient app seedCounts
            use seedConn = db.Factory ()
            let seedResult =
                Api.runSteamFamilyImport seedConn seedHttp
                    (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
                    (fun () -> steamConfig)
                    noImagesDir allProjectionHandlers (fun _ -> ()) Api.Incremental
                |> Async.RunSynchronously
            match seedResult with
            | Ok _ -> ()
            | Error e -> failtestf "Seed import failed: %s" e

            let counts = RequestCounts()
            let http = buildHttpClient app counts
            use conn = db.Factory ()
            let result =
                Api.runSteamFamilyImport conn http
                    (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
                    (fun () -> steamConfig)
                    noImagesDir allProjectionHandlers (fun _ -> ()) Api.FullReenrich
                |> Async.RunSynchronously

            match result with
            | Ok r -> Expect.equal r.GamesProcessed 1 "The known app still goes through the per-app loop"
            | Error e -> failtestf "Expected Ok, got Error %s" e

            Expect.equal (counts.Get "appdetails") 1 "Full re-enrich still fetches appdetails for the known app"
    ]
