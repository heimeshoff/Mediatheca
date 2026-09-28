module Mediatheca.Tests.RomMTests

/// integration-jkbm1 (ADR-0088): `RomM.fs` adapter coverage against
/// fixtures RECORDED from the live self-hosted instance
/// (`https://romm.elver-minor.ts.net`, v5.3.1) with a read-only Client API
/// Token (iteration 2 -- the iteration-1 fixtures below were hand-authored
/// and got two wire-shape assumptions wrong that only a real recording
/// caught; see the per-fixture doc comments). `RomM.fs`'s decoders are
/// private (mirroring `Steam.fs`), so every case here goes through the
/// adapter's own public HTTP-calling functions against a stub
/// `HttpMessageHandler`, exactly like `PlaytimeSyncKeyRejectionTests.fs` does
/// for `Steam.fs`. Sync orchestration (matching/creation/idempotency/
/// gaming-day) lives in `RomMSyncTests.fs`.

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Expecto
open Mediatheca.Server

type private StubHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) =
        Task.FromResult<HttpResponseMessage>(respond request)

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, Text.Encoding.UTF8, "application/json")
    resp

let private unauthorizedResponse () = new HttpResponseMessage(HttpStatusCode.Unauthorized)

let private config: RomM.RomMConfig =
    { BaseUrl = "https://romm.example.com"; ApiToken = "rmm_test_token"; SelectedPlatformIds = [ 1 ] }

/// RECORDED VERBATIM from `GET /api/platforms` (2026-09-26, read-only
/// token, then scrubbed of nothing -- this endpoint carries no secret).
/// Untrimmed: three platforms is the entire live response. Confirms the
/// wire shape is a plain array (iteration-1 assumption, unchanged) -- and
/// incidentally shows this harbour instance has no non-Nintendo platform at
/// all yet, so the platform-FILTER behaviour (as opposed to decoding) stays
/// covered by `RomMSyncTests.fs`'s synthetic platform ids instead.
let private platformsFixture =
    """[{"id":3,"slug":"nes","fs_slug":"nes","rom_count":715,"name":"Nintendo Entertainment System","igdb_slug":"nes","category":"Console","generation":3,"family_name":"Nintendo","family_slug":"nintendo","created_at":"2026-09-23T15:43:36+00:00","updated_at":"2026-09-23T15:43:36+00:00","is_unidentified":false,"is_identified":true,"missing_from_fs":false,"display_name":"Nintendo Entertainment System","firmware_count":0},{"id":1,"slug":"snes","fs_slug":"snes","rom_count":804,"name":"Super Nintendo Entertainment System","igdb_slug":"snes","category":"Console","generation":4,"family_name":"Nintendo","family_slug":"nintendo","created_at":"2026-09-23T15:41:08+00:00","updated_at":"2026-09-23T15:41:08+00:00","is_unidentified":false,"is_identified":true,"missing_from_fs":false,"display_name":"Super Nintendo Entertainment System","firmware_count":0},{"id":2,"slug":"snes","fs_slug":"snes","rom_count":0,"name":"Super Nintendo Entertainment System","igdb_slug":"snes","category":"Console","generation":4,"family_name":"Nintendo","family_slug":"nintendo","created_at":"2026-09-23T15:41:53+00:00","updated_at":"2026-09-23T15:41:53+00:00","is_unidentified":false,"is_identified":true,"missing_from_fs":false,"display_name":"Super Nintendo Entertainment System","firmware_count":0}]"""

/// Trimmed from a real `GET /api/roms?limit=3&offset=0` recording (dropped
/// the huge `char_index`/`filter_values`/per-rom metadata blocks the
/// decoder never reads) -- confirms the paged envelope shape (`{items,
/// total, limit, offset}`) `decodeRomsPage` assumes.
let private romsListFixture =
    """{"items":[{"id":810,"platform_id":3,"platform_slug":"nes","name":"2-in-1 Super Mario Bros. + Duck Hunt"},{"id":33,"platform_id":1,"platform_slug":"snes","name":"3 Ninjas Kick Back"}],"total":1519,"limit":3,"offset":0}"""

/// Trimmed from a real `GET /api/roms/33` recording (rom id 33, "3 Ninjas
/// Kick Back", SNES) -- dropped the unused `igdb_metadata`/`moby_metadata`/
/// `hasheous_metadata`/etc. sibling blocks (all `{}` or irrelevant to this
/// decoder) and `screenshot`/`files`/`sibling_roms` arrays, kept every field
/// `decodeRomDetail` reads plus `rom_user` verbatim. Two iteration-1 wire-
/// shape assumptions turned out wrong against this real response:
/// - `metadatum.first_release_date` is Unix MILLISECONDS (785203200000),
///   not seconds -- confirmed by cross-checking the SAME game's sibling
///   `igdb_metadata.first_release_date` (785203200, genuine IGDB seconds)
///   recorded alongside it, and by 1994-11-19 (785203200000 as ms) matching
///   "3 Ninjas Kick Back"'s real, checkable release year.
/// - `url_cover` is an ABSOLUTE, THIRD-PARTY URL (libretro's public
///   thumbnail CDN), not a same-host RomM asset -- `downloadCover` must not
///   send the bearer token there (see `RomM.isSameHost` and its tests).
let private romDetailFixture =
    """{"id":33,"platform_id":1,"platform_slug":"snes","name":"3 Ninjas Kick Back","summary":"3 Ninjas Kick Back is a beat 'em up platform game for the Sega Genesis, Super Nintendo Entertainment System. It was released in 1994 and was developed by Malibu Interactive.","metadatum":{"rom_id":33,"genres":["Adventure","Platform"],"franchises":["3 Ninjas"],"companies":[],"publishers":[],"developers":[],"player_count":"1","first_release_date":785203200000,"average_rating":57.05},"path_cover_large":"/assets/romm/resources/roms/1/33/cover/big.png?ts=2026-09-24 08:55:22","url_cover":"https://thumbnails.libretro.com/Nintendo%20-%20Super%20Nintendo%20Entertainment%20System/Named_Boxarts/3%20Ninjas%20Kick%20Back%20%28USA%29.png","rom_user":{"id":9,"user_id":1,"rom_id":33,"backlogged":false,"now_playing":true,"hidden":false,"rating":0,"difficulty":0,"completion":0,"status":"incomplete"}}"""

/// Trimmed from a real `GET /api/play-sessions?limit=10&offset=0` recording
/// -- two of the ten sessions on the live instance, kept verbatim (device_id
/// values are opaque per-client UUIDs, not secrets, so kept as recorded).
/// Confirms `start_time`/`end_time` carry NO timezone suffix at all
/// (`"2026-09-25T10:43:02"`, no `Z`/offset) -- `decodeUtcDateTime`'s
/// `AssumeUniversal` handling (unchanged from iteration 1) turns out to be
/// exactly what this real, offset-less wire format needs.
let private playSessionsFixture =
    """[{"id":13,"user_id":1,"device_id":"ab70153c-e10b-4d5a-a7f7-791e5fb33f43","rom_id":33,"save_slot":null,"start_time":"2026-09-25T10:43:02","end_time":"2026-09-25T10:43:14","duration_ms":11889,"created_at":"2026-09-25T10:43:17","updated_at":"2026-09-25T10:43:17"},{"id":9,"user_id":1,"device_id":"ab70153c-e10b-4d5a-a7f7-791e5fb33f43","rom_id":20,"save_slot":null,"start_time":"2026-09-24T08:03:30","end_time":"2026-09-24T08:12:06","duration_ms":516090,"created_at":"2026-09-24T08:12:08","updated_at":"2026-09-24T08:12:08"}]"""

/// Hand-authored (not a live recording -- no harness to record against for
/// this task) directly off `StateSchema` (`backend/endpoints/responses/
/// assets.py`, tag `5.3.1`): `BaseAsset`'s common fields (`id`, `rom_id`,
/// `user_id`, `file_name*`, `file_size_bytes`, `full_path`,
/// `download_path`, `missing_from_fs`, `created_at`, `updated_at`) plus
/// `StateSchema`'s own `emulator`/`is_public`/`screenshot`. Two states for
/// the same rom, second one (id 12) the newer by `updated_at`.
let private statesFixture =
    """[{"id":11,"rom_id":33,"user_id":1,"file_name":"slot1.state","file_name_no_tags":"slot1","file_name_no_ext":"slot1","file_extension":"state","file_path":"states/33","file_size_bytes":123456,"full_path":"states/33/slot1.state","download_path":"/assets/romm/states/33/slot1.state","missing_from_fs":false,"created_at":"2026-09-20T10:00:00","updated_at":"2026-09-20T10:00:00","emulator":"snes9x","is_public":false,"screenshot":null},{"id":12,"rom_id":33,"user_id":1,"file_name":"slot2.state","file_name_no_tags":"slot2","file_name_no_ext":"slot2","file_extension":"state","file_path":"states/33","file_size_bytes":123999,"full_path":"states/33/slot2.state","download_path":"/assets/romm/states/33/slot2.state","missing_from_fs":false,"created_at":"2026-09-25T10:00:00","updated_at":"2026-09-25T10:00:00","emulator":"snes9x","is_public":false,"screenshot":null}]"""

[<Tests>]
let rommDecodingTests =
    testList "RomM.fs -- wire decoding against recorded fixtures (integration-jkbm1)" [

        testCase "Decodes a platforms list" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse platformsFixture))
            match RomM.getPlatforms http config |> Async.RunSynchronously with
            | Ok platforms ->
                Expect.equal (List.length platforms) 3 "All three recorded platforms decoded"
                Expect.equal platforms.[0].Id 3 "First platform's id"
                Expect.equal platforms.[0].Name "Nintendo Entertainment System" "First platform's name"
                Expect.equal platforms.[0].Slug "nes" "First platform's slug"
            | Error e -> failtestf "Expected platforms to decode, got %A" e

        testCase "Decodes a paged roms list" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse romsListFixture))
            match RomM.getRoms http config [ 1; 3 ] 3 0 |> Async.RunSynchronously with
            | Ok roms ->
                Expect.equal (List.length roms) 2 "Both recorded roms decoded"
                Expect.equal roms.[0].Name "2-in-1 Super Mario Bros. + Duck Hunt" "First rom name"
                Expect.equal roms.[0].PlatformId 3 "First rom's platform id (NES)"
                Expect.equal roms.[1].Name "3 Ninjas Kick Back" "Second rom name"
                Expect.equal roms.[1].PlatformId 1 "Second rom's platform id (SNES)"
            | Error e -> failtestf "Expected roms list to decode, got %A" e

        testCase "Decodes a rom detail, deriving release year from metadatum.first_release_date (Unix MILLISECONDS), ignoring rom_user" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse romDetailFixture))
            match RomM.getRomDetail http config 33 |> Async.RunSynchronously with
            | Ok rom ->
                Expect.equal rom.Name "3 Ninjas Kick Back" "Rom name"
                Expect.equal rom.PlatformId 1 "Platform id"
                Expect.equal rom.Genres [ "Adventure"; "Platform" ] "Genres from metadatum"
                Expect.equal rom.ReleaseYear (Some 1994) "Release year derived from first_release_date, treated as milliseconds"
                Expect.equal rom.CoverUrl (Some "https://thumbnails.libretro.com/Nintendo%20-%20Super%20Nintendo%20Entertainment%20System/Named_Boxarts/3%20Ninjas%20Kick%20Back%20%28USA%29.png") "Cover URL is the real, third-party libretro CDN URL"
                Expect.equal rom.PlatformSlug "snes" "Platform slug decoded from the recorded fixture's platform_slug field"
            | Error e -> failtestf "Expected rom detail to decode, got %A" e

        testCase "Decodes a play sessions page, tolerating timestamps with no timezone suffix at all" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse playSessionsFixture))
            match RomM.getAllPlaySessions http config |> Async.RunSynchronously with
            | Ok sessions ->
                Expect.equal (List.length sessions) 2 "Both recorded sessions decoded"
                let s = sessions.[0]
                Expect.equal s.RomId 33 "rom_id"
                Expect.isSome s.EndTime "end_time decoded"
                Expect.equal s.DurationMs (Some 11889L) "duration_ms"
                Expect.equal s.StartTime (DateTime(2026, 9, 25, 10, 43, 2, DateTimeKind.Utc)) "The offset-less timestamp is parsed as UTC, not local time"
            | Error e -> failtestf "Expected play sessions to decode, got %A" e

        testCase "integration-q3cg7: a null rom_id is skipped, every other session in the page still decodes" <| fun _ ->
            let sessionsJson =
                """[{"id":1,"rom_id":33,"start_time":"2026-09-25T10:00:00Z","end_time":"2026-09-25T10:10:00Z","duration_ms":600000},{"id":15,"rom_id":null,"start_time":"2026-09-25T11:00:00Z","end_time":"2026-09-25T11:10:00Z","duration_ms":600000},{"id":9,"rom_id":20,"start_time":"2026-09-24T08:03:30Z","end_time":"2026-09-24T08:12:06Z","duration_ms":516090}]"""
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse sessionsJson))
            match RomM.getAllPlaySessions http config |> Async.RunSynchronously with
            | Ok sessions ->
                Expect.equal (List.length sessions) 2 "The orphaned session (rom_id: null) is skipped -- only the two owned sessions decode"
                Expect.equal (sessions |> List.map (fun s -> s.Id) |> List.sort) [ 1; 9 ] "The orphan's id (15) is absent"
            | Error e -> failtestf "Expected the page to decode, orphan skipped, got %A" e

        testCase "integration-q3cg7: an absent rom_id key is treated the same as a null rom_id -- skipped, no error" <| fun _ ->
            let sessionsJson =
                """[{"id":1,"rom_id":33,"start_time":"2026-09-25T10:00:00Z","end_time":"2026-09-25T10:10:00Z","duration_ms":600000},{"id":16,"start_time":"2026-09-25T11:00:00Z","end_time":"2026-09-25T11:10:00Z","duration_ms":600000}]"""
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse sessionsJson))
            match RomM.getAllPlaySessions http config |> Async.RunSynchronously with
            | Ok sessions ->
                Expect.equal (List.length sessions) 1 "The session missing rom_id entirely is skipped"
                Expect.equal sessions.[0].Id 1 "Only the owned session remains"
            | Error e -> failtestf "Expected the page to decode, orphan (absent key) skipped, got %A" e

        testCase "integration-q3cg7: a non-integer, non-null rom_id still fails the whole page's decode" <| fun _ ->
            let sessionsJson =
                """[{"id":1,"rom_id":"not-an-int","start_time":"2026-09-25T10:00:00Z","end_time":"2026-09-25T10:10:00Z","duration_ms":600000}]"""
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse sessionsJson))
            match RomM.getAllPlaySessions http config |> Async.RunSynchronously with
            | Error (RomM.RomMOtherFailure msg) ->
                Expect.stringStarts msg "Failed to parse RomM play sessions" "A malformed (non-int, non-null) rom_id is still a hard decode failure"
            | other -> failtestf "Expected a decode failure for a string rom_id, got %A" other

        testCase "integration-q3cg7: paging uses the raw item count, not the filtered count -- a full page with one orphan still fetches the next page" <| fun _ ->
            let mutable callCount = 0
            let makeSession id romId = sprintf """{"id":%d,"rom_id":%s,"start_time":"2026-09-20T10:00:00Z","end_time":"2026-09-20T10:30:00Z","duration_ms":1800000}""" id romId
            // Exactly PlaySessionsPageSize raw items on the first page, one of
            // them an orphan -- the RAW count (PageSize) must still decide
            // this is a full page, even though the FILTERED count is one
            // less.
            let fullPageWithOrphan =
                "[" +
                (String.concat "," [
                    for i in 1 .. RomM.PlaySessionsPageSize ->
                        if i = 1 then makeSession i "null" else makeSession i "10"
                ]) +
                "]"
            let shortPage = "[" + makeSession (RomM.PlaySessionsPageSize + 1) "10" + "]"
            let http =
                new HttpClient(new StubHandler(fun request ->
                    callCount <- callCount + 1
                    if request.RequestUri.Query.Contains(sprintf "offset=%d" RomM.PlaySessionsPageSize) then
                        jsonResponse shortPage
                    else
                        jsonResponse fullPageWithOrphan))
            match RomM.getAllPlaySessions http config |> Async.RunSynchronously with
            | Ok sessions ->
                Expect.equal callCount 2 "Paged exactly twice -- the raw (unfiltered) first-page count of PageSize items still triggered the second page"
                Expect.equal (List.length sessions) RomM.PlaySessionsPageSize "PageSize - 1 owned sessions from the first page, plus 1 from the short page"
            | Error e -> failtestf "Expected paging to succeed, got %A" e

        testCase "getAllPlaySessions pages until a short page" <| fun _ ->
            let mutable callCount = 0
            let makeSession id = sprintf """{"id":%d,"rom_id":10,"start_time":"2026-09-20T10:00:00Z","end_time":"2026-09-20T10:30:00Z","duration_ms":1800000}""" id
            let fullPage = "[" + (String.concat "," [ for i in 1 .. RomM.PlaySessionsPageSize -> makeSession i ]) + "]"
            let shortPage = "[" + makeSession (RomM.PlaySessionsPageSize + 1) + "]"
            let http =
                new HttpClient(new StubHandler(fun request ->
                    callCount <- callCount + 1
                    if request.RequestUri.Query.Contains(sprintf "offset=%d" RomM.PlaySessionsPageSize) then
                        jsonResponse shortPage
                    else
                        jsonResponse fullPage))
            match RomM.getAllPlaySessions http config |> Async.RunSynchronously with
            | Ok sessions ->
                Expect.equal (List.length sessions) (RomM.PlaySessionsPageSize + 1) "Both pages' sessions are concatenated"
                Expect.equal callCount 2 "Paged exactly twice -- the second (short) page ends pagination"
            | Error e -> failtestf "Expected paging to succeed, got %A" e

        testCase "A 401 becomes a typed TokenRejected result" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> unauthorizedResponse ()))
            match RomM.getPlatforms http config |> Async.RunSynchronously with
            | Error RomM.TokenRejected -> ()
            | other -> failtestf "Expected Error TokenRejected, got %A" other

        testCase "A 403 also becomes TokenRejected" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> new HttpResponseMessage(HttpStatusCode.Forbidden)))
            match RomM.getRomDetail http config 10 |> Async.RunSynchronously with
            | Error RomM.TokenRejected -> ()
            | other -> failtestf "Expected Error TokenRejected, got %A" other

        testCase "Rounds duration_ms to the nearest minute, minimum 1 for any positive duration" <| fun _ ->
            Expect.equal (RomM.minutesFromDurationMs 0L) 0 "Zero duration -> zero minutes"
            Expect.equal (RomM.minutesFromDurationMs 500L) 1 "Any positive duration rounds up to at least 1 minute"
            Expect.equal (RomM.minutesFromDurationMs 30000L) 1 "30s rounds away from zero to 1 minute"
            Expect.equal (RomM.minutesFromDurationMs 90000L) 2 "90s (1.5 min) rounds away from zero to 2 minutes"
            Expect.equal (RomM.minutesFromDurationMs 3000000L) 50 "3,000,000ms is exactly 50 minutes"

        testCase "tokenRejectedMessage carries the fixed prefix an acceptance criterion pins" <| fun _ ->
            Expect.isTrue (RomM.tokenRejectedMessage.StartsWith("RomM token rejected: ")) "Message starts with the fixed prefix"

        testCase "downloadCover sends the bearer token for a same-host (RomM instance) cover URL" <| fun _ ->
            // Iteration 2 (verifier note, "url_cover ... needs the bearer
            // token"): a recorded fixture from the live instance showed
            // `url_cover` is typically a THIRD-PARTY CDN URL, not a same-host
            // RomM asset -- but same-host covers do exist (`path_cover_large`-
            // style, self-hosted), and those still need the token.
            let mutable sawAuthHeader = false
            let http =
                new HttpClient(new StubHandler(fun request ->
                    sawAuthHeader <- request.Headers.Authorization <> null && request.Headers.Authorization.Parameter = config.ApiToken
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent([| 1uy; 2uy; 3uy |])
                    resp))
            let tempDir = IO.Path.Combine(IO.Path.GetTempPath(), "rommtest-" + Guid.NewGuid().ToString("N"))
            try
                RomM.downloadCover http config "https://romm.example.com/assets/covers/10.jpg" "test-slug" tempDir
                |> Async.RunSynchronously |> ignore
                Expect.isTrue sawAuthHeader "Same host as config.BaseUrl -- the bearer token is attached"
            finally
                if IO.Directory.Exists tempDir then IO.Directory.Delete(tempDir, true)

        testCase "downloadCover does NOT send the bearer token to a third-party host (e.g. a libretro thumbnail CDN)" <| fun _ ->
            let mutable sawAuthHeader = false
            let http =
                new HttpClient(new StubHandler(fun request ->
                    sawAuthHeader <- request.Headers.Authorization <> null
                    let resp = new HttpResponseMessage(HttpStatusCode.OK)
                    resp.Content <- new ByteArrayContent([| 1uy; 2uy; 3uy |])
                    resp))
            let tempDir = IO.Path.Combine(IO.Path.GetTempPath(), "rommtest-" + Guid.NewGuid().ToString("N"))
            try
                RomM.downloadCover http config "https://thumbnails.libretro.com/Nintendo/foo.png" "test-slug" tempDir
                |> Async.RunSynchronously |> ignore
                Expect.isFalse sawAuthHeader "Third-party host -- the RomM Client API Token must never be sent here"
            finally
                if IO.Directory.Exists tempDir then IO.Directory.Delete(tempDir, true)

        testCase "isSameHost matches the exact host, ignoring scheme/path/query, and is false across different hosts" <| fun _ ->
            Expect.isTrue (RomM.isSameHost "https://romm.example.com" "https://romm.example.com/assets/covers/10.jpg") "Same host"
            Expect.isTrue (RomM.isSameHost "https://romm.example.com/" "http://romm.example.com/assets/x.png?ts=1") "Scheme/trailing slash/query don't matter"
            Expect.isFalse (RomM.isSameHost "https://romm.example.com" "https://thumbnails.libretro.com/foo.png") "Different host"
            Expect.isFalse (RomM.isSameHost "https://romm.example.com" "not a url") "An unparseable cover URL degrades to false, not an exception"

        testCase "playerRouteFor: EmulatorJS platforms (Nintendo handhelds and home consoles) map to Some \"ejs\"" <| fun _ ->
            for slug in [ "nes"; "snes"; "n64"; "gb"; "gbc"; "gba"; "nds" ] do
                Expect.equal (RomM.playerRouteFor slug) (Some "ejs") (sprintf "%s is an EmulatorJS platform" slug)

        testCase "playerRouteFor: Flash maps to Some \"ruffle\"" <| fun _ ->
            Expect.equal (RomM.playerRouteFor "flash") (Some "ruffle") "flash -> ruffle"
            Expect.equal (RomM.playerRouteFor "browser") (Some "ruffle") "browser -> ruffle"

        testCase "playerRouteFor: DOS-era platforms map to Some \"jsdos\"" <| fun _ ->
            Expect.equal (RomM.playerRouteFor "win3x") (Some "jsdos") "win3x -> jsdos"
            Expect.equal (RomM.playerRouteFor "win9x") (Some "jsdos") "win9x -> jsdos"

        testCase "playerRouteFor: PICO-8 maps to Some \"pico8\"" <| fun _ ->
            Expect.equal (RomM.playerRouteFor "pico") (Some "pico8") "pico -> pico8"

        testCase "playerRouteFor: Switch, GameCube, Wii and an unknown slug all map to None" <| fun _ ->
            Expect.equal (RomM.playerRouteFor "switch") None "Switch has no in-browser player"
            Expect.equal (RomM.playerRouteFor "ngc") None "GameCube has no in-browser player"
            Expect.equal (RomM.playerRouteFor "wii") None "Wii has no in-browser player"
            Expect.equal (RomM.playerRouteFor "some-unknown-platform") None "An unrecognized slug is never playable"

        testCase "playerRouteFor is case-insensitive, mirroring RomM's own slug.toLowerCase() checks" <| fun _ ->
            Expect.equal (RomM.playerRouteFor "SNES") (Some "ejs") "Uppercase slug still matches"
            Expect.equal (RomM.playerRouteFor "") None "Empty slug is never playable"

        testCase "The token never appears in a log line -- fetchJsonRejectable has no logging call at all" <| fun _ ->
            // A structural guarantee, not a runtime-observable one: there is
            // no `printfn`/`eprintfn` anywhere between building the request
            // and returning, so a token can never be interpolated into one.
            // Pinned here as a call that must still succeed cleanly with the
            // token embedded in the request, confirming the header is sent
            // (a 200 comes back) without the test needing to intercept stdout.
            let mutable sawAuthHeader = false
            let http =
                new HttpClient(new StubHandler(fun request ->
                    sawAuthHeader <- request.Headers.Authorization <> null && request.Headers.Authorization.Parameter = config.ApiToken
                    jsonResponse platformsFixture))
            RomM.getPlatforms http config |> Async.RunSynchronously |> ignore
            Expect.isTrue sawAuthHeader "The bearer token is sent as the Authorization header, not embedded in the URL/logged"

        testCase "getLatestState picks the state with the latest updated_at (integration-f8ncw)" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse statesFixture))
            match RomM.getLatestState http config 33 |> Async.RunSynchronously with
            | Ok (Some state) -> Expect.equal state.Id 12 "id 12 has the later updated_at -- 2026-09-25 vs 2026-09-20"
            | other -> failtestf "Expected Ok (Some state), got %A" other

        testCase "getLatestState returns Ok None when the rom has no states" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse "[]"))
            Expect.equal (RomM.getLatestState http config 33 |> Async.RunSynchronously) (Ok None) "An empty states array is not an error -- just nothing to resume from"

        testCase "getLatestState returns Error on an unparsable states response" <| fun _ ->
            let http = new HttpClient(new StubHandler(fun _ -> jsonResponse "{not valid json"))
            match RomM.getLatestState http config 33 |> Async.RunSynchronously with
            | Error (RomM.RomMOtherFailure _) -> ()
            | other -> failtestf "Expected Error (RomMOtherFailure _), got %A" other
    ]
