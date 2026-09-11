module Mediatheca.Tests.QbittorrentTests

/// integration-qb7tk: the qBittorrent WebUI adapter. `login` maps both of
/// qBittorrent's auth contracts -- 4.x HTTP-200-with-a-text-body + `SID`
/// cookie, 5.x HTTP 204/401 + `QBT_SID_<port>` cookie (ADR-0072) -- onto a
/// typed `Result`, `withSession` is a login-once-use-once wrapper with no
/// persisted cookie (ADR-0071 point 7 -- deliberately not the ADR-0011
/// re-auth-and-retry shape), and the request-building functions never leak
/// vendor JSON or send an `Origin`/`Referer` header. All HTTP is faked via
/// a recording `HttpMessageHandler`, same pattern as
/// `PlaytimeSyncKeyRejectionTests.fs`.

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Expecto
open Mediatheca.Server
open Mediatheca.Server.Qbittorrent

/// Records every request the adapter sends. Bodies are read eagerly (the
/// production code disposes each `HttpRequestMessage`, and its `Content`,
/// once the call that sent it returns) so a test can still assert on a
/// captured request's body after the fact; `Requests` keeps the message
/// itself for header assertions, which survive disposal.
type private RecordingHandler(respond: HttpRequestMessage -> HttpResponseMessage) =
    inherit HttpMessageHandler()
    let requests = ResizeArray<HttpRequestMessage>()
    let bodies = ResizeArray<string>()
    member _.Requests : HttpRequestMessage list = requests |> List.ofSeq
    member _.RequestBodies : string list = bodies |> List.ofSeq
    override _.SendAsync(request: HttpRequestMessage, _cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        requests.Add(request)
        let body =
            match request.Content with
            | null -> ""
            | content -> content.ReadAsStringAsync() |> Async.AwaitTask |> Async.RunSynchronously
        bodies.Add(body)
        Task.FromResult<HttpResponseMessage>(respond request)

type private ThrowingHandler(exn: exn) =
    inherit HttpMessageHandler()
    override _.SendAsync(_request: HttpRequestMessage, _cancellationToken: CancellationToken) : Task<HttpResponseMessage> =
        raise exn

let private textResponse (status: HttpStatusCode) (body: string) =
    let resp = new HttpResponseMessage(status)
    resp.Content <- new StringContent(body, Encoding.UTF8, "text/plain")
    resp

let private jsonResponse (json: string) =
    let resp = new HttpResponseMessage(HttpStatusCode.OK)
    resp.Content <- new StringContent(json, Encoding.UTF8, "application/json")
    resp

/// qBittorrent 5.x login success (ADR-0072): HTTP 204, empty body, and the
/// session cookie named `QBT_SID_<webui-port>` -- verbatim the raw form
/// qBittorrent 5.2.3 emits (`QNetworkCookie::toRawForm`, `expires` with its
/// embedded commas, `SameSite=Lax` under CSRF protection).
let private okLoginResponse (sid: string) =
    let resp = textResponse HttpStatusCode.NoContent ""
    resp.Headers.Add("Set-Cookie", sprintf "QBT_SID_8080=%s; expires=Fri, 11-Sep-2026 10:33:20 GMT; HttpOnly; path=/; SameSite=Lax" sid)
    resp

/// qBittorrent 4.x login success: HTTP 200 + body "Ok." + cookie `SID`.
let private legacyOkLoginResponse (sid: string) =
    let resp = textResponse HttpStatusCode.OK "Ok."
    resp.Headers.Add("Set-Cookie", sprintf "SID=%s; HttpOnly; path=/" sid)
    resp

let private config: QbittorrentConfig =
    { Url = "http://qbt.local:8080"; Username = "admin"; Password = "secret" }

let private session: Session = { CookieName = "QBT_SID_8080"; Sid = "test-sid" }

[<Tests>]
let qbittorrentTests =
    testList "Qbittorrent adapter (integration-qb7tk)" [

        testList "login" [

            testCase "5.x: HTTP 204 + QBT_SID_<port> cookie -> a session carrying that cookie's name and value (ADR-0072)" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> okLoginResponse "abc123")
                use http = new HttpClient(handler)
                let result = login http config |> Async.RunSynchronously
                Expect.equal result (Ok { CookieName = "QBT_SID_8080"; Sid = "abc123" })
                    "An empty 204 with the 5.x session cookie is a successful login; the cookie NAME is kept for the round-trip"

            testCase "4.x: HTTP 200 + \"Ok.\" + SID cookie -> a session under the legacy name" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> legacyOkLoginResponse "abc123")
                use http = new HttpClient(handler)
                let result = login http config |> Async.RunSynchronously
                Expect.equal result (Ok { CookieName = "SID"; Sid = "abc123" }) "The 4.x contract still logs in"

            testCase "5.x: HTTP 401 -> AuthFailed (bad credentials)" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> textResponse HttpStatusCode.Unauthorized "Unauthorized")
                use http = new HttpClient(handler)
                let result = login http config |> Async.RunSynchronously
                Expect.equal result (Error AuthFailed) "5.x answers wrong credentials with 401; that is AuthFailed, not a generic HTTP error"

            testCase "4.x: HTTP 200 + \"Fails.\" -> AuthFailed (bad credentials)" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> textResponse HttpStatusCode.OK "Fails.")
                use http = new HttpClient(handler)
                let result = login http config |> Async.RunSynchronously
                Expect.equal result (Error AuthFailed) "Bad credentials map to AuthFailed, not a generic HTTP error"

            testCase "2xx without any session cookie -> OtherFailure naming the status (the 'already logged in' answer, never a success)" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> textResponse HttpStatusCode.NoContent "")
                use http = new HttpClient(handler)
                let result = login http config |> Async.RunSynchronously
                match result with
                | Error (OtherFailure msg) ->
                    Expect.stringContains msg "HTTP 204" "Names the status"
                    Expect.stringContains msg "session cookie" "Says what was missing"
                | other -> failtestf "Expected OtherFailure, got %A" other

            testCase "The adapter's own HttpClient handler has no cookie jar (UseCookies = false), so no session leaks into the next login" <| fun _ ->
                use handler = createHandler ()
                Expect.isFalse handler.UseCookies "A cookie jar would replay the previous session cookie on the next login"

            testCase "HTTP 403 -> AuthFailed (client banned)" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> textResponse HttpStatusCode.Forbidden "")
                use http = new HttpClient(handler)
                let result = login http config |> Async.RunSynchronously
                Expect.equal result (Error AuthFailed) "A banned client (403) maps to AuthFailed"

            testCase "Transport fault -> OtherFailure" <| fun _ ->
                use http = new HttpClient(new ThrowingHandler(HttpRequestException("connection refused")))
                let result = login http config |> Async.RunSynchronously
                match result with
                | Error (OtherFailure msg) -> Expect.stringContains msg "connection refused" "Carries the transport exception's message"
                | other -> failtestf "Expected OtherFailure, got %A" other
        ]

        testCase "torrents/info decoder: ratio as float, seeding_time seconds -> TimeSpan, save_path, content_path, state" <| fun _ ->
            let fixture =
                """
                [
                  {
                    "hash": "abc123def456",
                    "name": "Movie.Name.2020.1080p",
                    "save_path": "/downloads/movies/Movie.Name.2020",
                    "content_path": "/downloads/movies/Movie.Name.2020/movie.mkv",
                    "ratio": 1.523,
                    "seeding_time": 86400,
                    "state": "uploading"
                  }
                ]
                """
            let handler = new RecordingHandler(fun _ -> jsonResponse fixture)
            use http = new HttpClient(handler)
            let result = listTorrents http config session |> Async.RunSynchronously
            match result with
            | Ok [ torrent ] ->
                Expect.equal torrent.Hash "abc123def456" "hash"
                Expect.equal torrent.Name "Movie.Name.2020.1080p" "name"
                Expect.equal torrent.SavePath "/downloads/movies/Movie.Name.2020" "save_path"
                Expect.equal torrent.ContentPath "/downloads/movies/Movie.Name.2020/movie.mkv" "content_path"
                Expect.floatClose Accuracy.high torrent.Ratio 1.523 "ratio decoded as a float"
                Expect.equal torrent.SeedingTime (TimeSpan.FromSeconds 86400.0) "seeding_time (seconds) -> TimeSpan"
                Expect.equal torrent.State "uploading" "state"
            | other -> failtestf "Expected exactly one decoded torrent, got %A" other

        testCase "listFiles decodes a torrents/files fixture into relative file names" <| fun _ ->
            let fixture =
                """
                [
                  { "name": "Movie.Name.2020.mkv", "size": 123456, "progress": 1.0 },
                  { "name": "Subs/Movie.Name.2020.en.srt", "size": 4096, "progress": 1.0 }
                ]
                """
            let handler = new RecordingHandler(fun _ -> jsonResponse fixture)
            use http = new HttpClient(handler)
            let result = listFiles http config session "abc123def456" |> Async.RunSynchronously
            Expect.equal result (Ok [ "Movie.Name.2020.mkv"; "Subs/Movie.Name.2020.en.srt" ]) "Decodes each entry's relative file name"

        testCase "deleteTorrents sends hashes joined with | and deleteFiles as true/false in the request body" <| fun _ ->
            let handler = new RecordingHandler(fun _ -> textResponse HttpStatusCode.OK "")
            use http = new HttpClient(handler)
            let result = deleteTorrents http config session [ "hash1"; "hash2" ] true |> Async.RunSynchronously
            Expect.equal result (Ok ()) "Delete succeeded"
            match handler.RequestBodies with
            | [ body ] ->
                Expect.equal body "hashes=hash1|hash2&deleteFiles=true" "Body joins hashes with | and renders deleteFiles as true/false"
            | other -> failtestf "Expected exactly one captured request, got %d" (List.length other)

        testCase "withSession logs in exactly once per operation and sends the SID as a Cookie header; no cookie survives across operations" <| fun _ ->
            let mutable loginCount = 0
            let handler =
                new RecordingHandler(fun request ->
                    if request.RequestUri.AbsolutePath.EndsWith("/auth/login") then
                        loginCount <- loginCount + 1
                        okLoginResponse (sprintf "session-%d" loginCount)
                    else
                        textResponse HttpStatusCode.OK "v4.6.0")
            use http = new HttpClient(handler)
            let capturedSids = ResizeArray<string>()
            let operation (s: Session) =
                async {
                    capturedSids.Add(s.Sid)
                    let! version = getAppVersion http config s
                    return version |> Result.map ignore
                }

            let result1 = withSession http config operation |> Async.RunSynchronously
            let result2 = withSession http config operation |> Async.RunSynchronously

            Expect.equal result1 (Ok ()) "First operation succeeded"
            Expect.equal result2 (Ok ()) "Second operation succeeded"
            Expect.equal loginCount 2 "Each operation triggers exactly one login (login-once-use-once)"
            Expect.equal (List.ofSeq capturedSids) [ "session-1"; "session-2" ]
                "Each operation is handed its own fresh session -- no cookie survives from one operation into the next"

            let followUps = handler.Requests |> List.filter (fun r -> not (r.RequestUri.AbsolutePath.EndsWith("/auth/login")))
            let cookiesSent =
                followUps
                |> List.map (fun r -> r.Headers.GetValues("Cookie") |> Seq.head)
            Expect.equal cookiesSent [ "QBT_SID_8080=session-1"; "QBT_SID_8080=session-2" ]
                "The session is sent back as an explicit Cookie header under the exact name qBittorrent issued it, matching that operation's own session"

        testCase "No request built by the adapter carries an Origin or Referer header" <| fun _ ->
            let handler =
                new RecordingHandler(fun request ->
                    let path = request.RequestUri.AbsolutePath
                    if path.EndsWith("/auth/login") then okLoginResponse "abc123"
                    elif path.EndsWith("/torrents/info") then jsonResponse "[]"
                    elif path.EndsWith("/torrents/files") then jsonResponse "[]"
                    elif path.EndsWith("/torrents/delete") then textResponse HttpStatusCode.OK ""
                    else textResponse HttpStatusCode.OK "")
            use http = new HttpClient(handler)

            login http config |> Async.RunSynchronously |> ignore
            listTorrents http config session |> Async.RunSynchronously |> ignore
            listFiles http config session "abc123def456" |> Async.RunSynchronously |> ignore
            deleteTorrents http config session [ "abc123def456" ] true |> Async.RunSynchronously |> ignore

            Expect.isNonEmpty handler.Requests "The scenario above issued at least one request"
            for request in handler.Requests do
                Expect.isFalse (request.Headers.Contains("Origin")) (sprintf "%O carries no Origin header" request.RequestUri)
                Expect.isFalse (request.Headers.Contains("Referer")) (sprintf "%O carries no Referer header" request.RequestUri)

        testList "testConnection (the Settings 'Test connection' round-trip, acceptance criterion 2)" [

            testCase "Reachable qBittorrent -> Ok with the app version and torrent count" <| fun _ ->
                let handler =
                    new RecordingHandler(fun request ->
                        let path = request.RequestUri.AbsolutePath
                        if path.EndsWith("/auth/login") then okLoginResponse "abc123"
                        elif path.EndsWith("/app/version") then textResponse HttpStatusCode.OK "v4.6.0"
                        elif path.EndsWith("/torrents/info") then
                            jsonResponse
                                """
                                [
                                  { "hash": "h1", "name": "One", "save_path": "/downloads/movies/One", "content_path": "/downloads/movies/One/one.mkv", "ratio": 1.0, "seeding_time": 100, "state": "uploading" },
                                  { "hash": "h2", "name": "Two", "save_path": "/downloads/movies/Two", "content_path": "/downloads/movies/Two/two.mkv", "ratio": 2.0, "seeding_time": 200, "state": "uploading" }
                                ]
                                """
                        else textResponse HttpStatusCode.OK "")
                use http = new HttpClient(handler)
                let result = testConnection http config |> Async.RunSynchronously
                Expect.equal result (Ok ("v4.6.0", 2)) "Reports the app version and current torrent count"

            testCase "Wrong credentials (5.x, HTTP 401) -> Error AuthFailed (authentication, not a generic HTTP error)" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> textResponse HttpStatusCode.Unauthorized "Unauthorized")
                use http = new HttpClient(handler)
                let result = testConnection http config |> Async.RunSynchronously
                Expect.equal result (Error AuthFailed) "Bad credentials surface as AuthFailed, not a generic HTTP error"

            testCase "Wrong credentials (4.x, HTTP 200 \"Fails.\") -> Error AuthFailed" <| fun _ ->
                let handler = new RecordingHandler(fun _ -> textResponse HttpStatusCode.OK "Fails.")
                use http = new HttpClient(handler)
                let result = testConnection http config |> Async.RunSynchronously
                Expect.equal result (Error AuthFailed) "Bad credentials surface as AuthFailed, not a generic HTTP error"
        ]
    ]

let private bootstrap (conn: Microsoft.Data.Sqlite.SqliteConnection) =
    SettingsStore.initialize conn

[<Tests>]
let qbittorrentSettingsStoreTests =
    testList "SettingsStore holds the qbittorrent_* keys (integration-qb7tk acceptance criterion 1)" [

        testCase "qbittorrent_url, qbittorrent_username and qbittorrent_password round-trip through SettingsStore" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            SettingsStore.setSetting db.Connection "qbittorrent_url" "https://qbt.example.ts.net"
            SettingsStore.setSetting db.Connection "qbittorrent_username" "admin"
            SettingsStore.setSetting db.Connection "qbittorrent_password" "hunter2"

            Expect.equal (SettingsStore.getSetting db.Connection "qbittorrent_url") (Some "https://qbt.example.ts.net") "qbittorrent_url round-trips"
            Expect.equal (SettingsStore.getSetting db.Connection "qbittorrent_username") (Some "admin") "qbittorrent_username round-trips"
            Expect.equal (SettingsStore.getSetting db.Connection "qbittorrent_password") (Some "hunter2") "qbittorrent_password round-trips"
    ]
