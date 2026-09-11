namespace Mediatheca.Server

open System
open System.Net.Http
open System.Text
open Thoth.Json.Net

/// qBittorrent WebUI adapter (integration-qb7tk). Read + credentials + a
/// "Test connection" round-trip only -- `deleteTorrents` exists here as a
/// typed primitive, but integration-r4vzm (the server-side removal flow) is
/// its first and only caller. See ADR-0071 point 7 for why this module gets
/// no ADR-0011-shaped persisted re-auth-and-retry: a qBittorrent `SID` is
/// seconds old and one cheap POST away, unlike a Jellyfin token that goes
/// stale over weeks.
module Qbittorrent =

    type QbittorrentConfig = {
        Url: string
        Username: string
        Password: string
    }

    /// The sibling of `Jellyfin.FetchError`. qBittorrent's login contract
    /// differs by version (ADR-0072): 4.x answers a bad login with HTTP 200
    /// + body "Fails." (success: HTTP 200 + "Ok."), while 5.x answers a bad
    /// login with HTTP 401 (success: HTTP 204, empty body -- the session
    /// cookie is the only success signal). A banned client gets HTTP 403 on
    /// either. All of those, plus every 403 on an authenticated call, map
    /// to `AuthFailed`.
    type QbittorrentError =
        | AuthFailed
        | OtherFailure of string

    /// The session cookie qBittorrent handed back on login -- its NAME as
    /// well as its value, because 5.x names it `QBT_SID_<webui-port>` (e.g.
    /// `QBT_SID_8080`) while 4.x named it `SID`, and qBittorrent only
    /// recognises a session that comes back under the same name. Live only
    /// for the duration of one `withSession` operation (login-once-use-once,
    /// never persisted).
    type Session = { CookieName: string; Sid: string }

    type TorrentInfo = {
        Hash: string
        Name: string
        SavePath: string
        ContentPath: string
        /// Decoded as-is; qBittorrent's sentinels (-1 for infinite, the 9999
        /// cap) are passed through untouched -- interpreting them is the
        /// caller's job.
        Ratio: float
        SeedingTime: TimeSpan
        State: string
    }

    // Decoders

    let private decodeTorrentInfo: Decoder<TorrentInfo> =
        Decode.object (fun get -> {
            Hash = get.Required.Field "hash" Decode.string
            Name = get.Required.Field "name" Decode.string
            SavePath = get.Required.Field "save_path" Decode.string
            ContentPath = get.Required.Field "content_path" Decode.string
            Ratio = get.Required.Field "ratio" Decode.float
            SeedingTime = TimeSpan.FromSeconds(float (get.Required.Field "seeding_time" Decode.int))
            State = get.Required.Field "state" Decode.string
        })

    let private decodeFileName: Decoder<string> =
        Decode.field "name" Decode.string

    // HTTP helpers

    let private baseUrl (config: QbittorrentConfig) = config.Url.TrimEnd('/')

    /// The handler behind this adapter's `HttpClient` has NO cookie jar
    /// (ADR-0072). .NET's default handler remembers every `Set-Cookie` and
    /// replays it on later requests to the same host -- so a second `login`
    /// would carry the previous operation's session cookie, qBittorrent
    /// would answer "already logged in" (2xx, no new `Set-Cookie`), and
    /// login-once-use-once would silently become one shared session. The
    /// explicit `withCookie` header is the only cookie this adapter sends.
    let createHandler () : HttpClientHandler = new HttpClientHandler(UseCookies = false)

    /// The `HttpClient` every qBittorrent call must go through -- see
    /// `createHandler`. Never the app-wide shared client.
    let createHttpClient () : HttpClient = new HttpClient(createHandler ())

    /// Adds the session cookie as an explicit request header, under the
    /// exact name qBittorrent issued it (`SID` or `QBT_SID_<port>`). No
    /// `Origin` or `Referer` header is ever added -- qBittorrent's CSRF
    /// check rejects cross-origin browsers, not server-to-server calls
    /// without an `Origin`.
    let private withCookie (session: Session) (request: HttpRequestMessage) =
        request.Headers.Add("Cookie", sprintf "%s=%s" session.CookieName session.Sid)
        request

    let private isSessionCookieName (name: string) =
        name = "SID" || name.StartsWith("QBT_SID_")

    /// Picks the session cookie out of the login response's `Set-Cookie`
    /// header(s): the `name=value` pair is always the first `;`-separated
    /// segment; the name is `SID` (4.x) or `QBT_SID_<port>` (5.x).
    let private extractSession (response: HttpResponseMessage) : Session option =
        match response.Headers.TryGetValues("Set-Cookie") with
        | true, values ->
            values
            |> Seq.tryPick (fun cookie ->
                let nameValue = cookie.Split(';').[0].Trim()
                match nameValue.IndexOf('=') with
                | -1 -> None
                | idx ->
                    let name = nameValue.Substring(0, idx).Trim()
                    let value = nameValue.Substring(idx + 1).Trim()
                    if isSessionCookieName name && value <> "" then Some { CookieName = name; Sid = value }
                    else None)
        | false, _ -> None

    /// `POST /api/v2/auth/login` (form `username`/`password`). Success is
    /// any 2xx that carries a session cookie -- 5.x: HTTP 204 + `QBT_SID_<port>`;
    /// 4.x: HTTP 200 "Ok." + `SID`. Bad credentials are HTTP 401 (5.x) or
    /// HTTP 200 "Fails." (4.x); a banned client is HTTP 403 -- all three are
    /// `AuthFailed`. A 2xx WITHOUT a session cookie is not a success: that is
    /// qBittorrent's answer to a request that already carried a valid
    /// session, which this adapter never sends on purpose (`createHandler`).
    let login (httpClient: HttpClient) (config: QbittorrentConfig) : Async<Result<Session, QbittorrentError>> =
        async {
            try
                let url = sprintf "%s/api/v2/auth/login" (baseUrl config)
                use request = new HttpRequestMessage(HttpMethod.Post, url)
                request.Content <- new FormUrlEncodedContent(dict [ "username", config.Username; "password", config.Password ])
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 401 || status = 403 then
                    return Error AuthFailed
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (sprintf "HTTP %d" status))
                else
                    match extractSession response with
                    | Some session -> return Ok session
                    | None ->
                        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        if body.Trim() = "Fails." then
                            return Error AuthFailed
                        else
                            return Error (OtherFailure (sprintf "qBittorrent login answered HTTP %d without a session cookie (SID / QBT_SID_<port>)" status))
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    /// Login-once-use-once: logs in exactly once and hands the resulting
    /// `Session` to `operation`. The `SID` lives only for the duration of
    /// this call -- there is no persisted cookie and no re-auth-and-retry
    /// seam (ADR-0071 point 7; deliberately not the ADR-0011 shape).
    let withSession
        (httpClient: HttpClient)
        (config: QbittorrentConfig)
        (operation: Session -> Async<Result<'a, QbittorrentError>>)
        : Async<Result<'a, QbittorrentError>> =
        async {
            let! loginResult = login httpClient config
            match loginResult with
            | Error e -> return Error e
            | Ok session -> return! operation session
        }

    /// `GET /api/v2/app/version`.
    let getAppVersion (httpClient: HttpClient) (config: QbittorrentConfig) (session: Session) : Async<Result<string, QbittorrentError>> =
        async {
            try
                let url = sprintf "%s/api/v2/app/version" (baseUrl config)
                use request = withCookie session (new HttpRequestMessage(HttpMethod.Get, url))
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 403 then
                    return Error AuthFailed
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (sprintf "HTTP %d" status))
                else
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Ok (body.Trim())
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    /// `GET /api/v2/torrents/info`.
    let listTorrents (httpClient: HttpClient) (config: QbittorrentConfig) (session: Session) : Async<Result<TorrentInfo list, QbittorrentError>> =
        async {
            try
                let url = sprintf "%s/api/v2/torrents/info" (baseUrl config)
                use request = withCookie session (new HttpRequestMessage(HttpMethod.Get, url))
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 403 then
                    return Error AuthFailed
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (sprintf "HTTP %d" status))
                else
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    match Decode.fromString (Decode.list decodeTorrentInfo) body with
                    | Ok torrents -> return Ok torrents
                    | Error e -> return Error (OtherFailure (sprintf "Failed to parse torrents/info response: %s" e))
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    /// `GET /api/v2/torrents/files?hash=…` -> file names relative to the
    /// save path (integration-r4vzm counts a pack torrent's extra files
    /// with it).
    let listFiles (httpClient: HttpClient) (config: QbittorrentConfig) (session: Session) (hash: string) : Async<Result<string list, QbittorrentError>> =
        async {
            try
                let url = sprintf "%s/api/v2/torrents/files?hash=%s" (baseUrl config) hash
                use request = withCookie session (new HttpRequestMessage(HttpMethod.Get, url))
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 403 then
                    return Error AuthFailed
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (sprintf "HTTP %d" status))
                else
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    match Decode.fromString (Decode.list decodeFileName) body with
                    | Ok names -> return Ok names
                    | Error e -> return Error (OtherFailure (sprintf "Failed to parse torrents/files response: %s" e))
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    /// `POST /api/v2/torrents/delete` with `hashes=h1|h2|…&deleteFiles=true|false`.
    /// Out of scope for this task -- integration-r4vzm is the first and
    /// only caller.
    let deleteTorrents (httpClient: HttpClient) (config: QbittorrentConfig) (session: Session) (hashes: string list) (deleteFiles: bool) : Async<Result<unit, QbittorrentError>> =
        async {
            try
                let url = sprintf "%s/api/v2/torrents/delete" (baseUrl config)
                use request = withCookie session (new HttpRequestMessage(HttpMethod.Post, url))
                let body = sprintf "hashes=%s&deleteFiles=%s" (String.concat "|" hashes) (if deleteFiles then "true" else "false")
                request.Content <- new StringContent(body, Encoding.UTF8, "application/x-www-form-urlencoded")
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 403 then
                    return Error AuthFailed
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (sprintf "HTTP %d" status))
                else
                    return Ok ()
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    /// The "Test connection" round-trip Settings calls: log in once, report
    /// the app version and current torrent count.
    let testConnection (httpClient: HttpClient) (config: QbittorrentConfig) : Async<Result<string * int, QbittorrentError>> =
        withSession httpClient config (fun session ->
            async {
                let! versionResult = getAppVersion httpClient config session
                match versionResult with
                | Error e -> return Error e
                | Ok version ->
                    let! torrentsResult = listTorrents httpClient config session
                    match torrentsResult with
                    | Error e -> return Error e
                    | Ok torrents -> return Ok (version, List.length torrents)
            })
