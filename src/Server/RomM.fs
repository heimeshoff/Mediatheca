namespace Mediatheca.Server

open System
open System.Net.Http
open System.Net.Http.Headers
open Thoth.Json.Net

/// integration-jkbm1 (ADR-0088): the RomM HTTP adapter -- config, wire
/// decoders, and typed HTTP calls against a self-hosted RomM instance
/// (v5.3.1). Pure adapter shape mirroring `Steam.fs`/`Audible.fs`: no
/// database access, no `SettingsStore` reads (those live in
/// `Composition.fs`'s `getRomMConfig`), and no `RomMSync.fs`-level
/// matching/creation logic. Compiled before `GameProjection.fs` (see
/// `Server.fsproj`), same as `Steam.fs`.
module RomM =

    type RomMConfig = {
        BaseUrl: string
        ApiToken: string
        /// The platform ids selected in Settings (the picker populated from
        /// `GET /api/platforms`, Nintendo pre-checked on first load) --
        /// carried on the config record the same way `Steam.SteamConfig`
        /// carries `SteamId`, so `RomMSync.runSync` takes one config
        /// provider rather than two.
        SelectedPlatformIds: int list
    }

    /// A failed RomM call, distinguishing a rejected/expired Client API
    /// Token (401/403) from any other failure -- mirrors `Steam.
    /// SteamWebApiError` (ADR-0065).
    type RomMError =
        | TokenRejected
        | RomMOtherFailure of string

    /// Fixed prefix an acceptance criterion pins on: persisted to
    /// `romm_last_error` verbatim, mirroring `Steam.
    /// webApiKeyRejectedMessage`/`steam_api_key_last_error` (ADR-0065).
    let tokenRejectedMessage =
        "RomM token rejected: the Client API Token was refused (401/403) -- " +
        "generate a fresh one in RomM -> Settings -> API Keys and paste it " +
        "into Settings -> RomM"

    // ── Wire types ──

    type RomMPlatform = {
        Id: int
        Name: string
        Slug: string
    }

    /// `PlaySessionSchema` -- only the fields the sync needs (`device_id`/
    /// `save_slot`/`created_at`/`updated_at` are never read, ADR-0088's
    /// session-id cursor only needs identity + timing + duration).
    type RomMPlaySession = {
        Id: int
        RomId: int
        StartTime: DateTime
        EndTime: DateTime option
        DurationMs: int64 option
    }

    /// `DetailedRomSchema`, trimmed to what the matching/creation step
    /// needs. `rom_user` is deliberately NOT modeled here at all -- an
    /// acceptance criterion requires it never reach a command, and the
    /// surest way to guarantee that is to never decode it in the first
    /// place.
    type RomMRomDetail = {
        Id: int
        Name: string
        Summary: string option
        Genres: string list
        PlatformId: int
        /// games-q748k (ADR-0088 concept extended): the platform's short
        /// slug (e.g. "snes"), decoded straight off `platform_slug` on the
        /// SAME rom-detail response the sync already fetches for every rom
        /// with closed sessions -- no second HTTP call needed. Defaults to
        /// "" when absent (an old/unexpected wire shape), which
        /// `playerRouteFor` below always maps to `None`.
        PlatformSlug: string
        CoverUrl: string option
        /// Derived from `metadatum.first_release_date` (a Unix-seconds
        /// timestamp, IGDB-shaped) -- `None` when RomM has no release date
        /// for this rom.
        ReleaseYear: int option
    }

    /// One entry of `GET /api/roms?platform_ids=...` -- the paged rom list
    /// named in the task's own "RomM API surface" section. `RomMSync`'s
    /// sync flow deliberately starts from `/api/play-sessions` instead (one
    /// paged call per run rather than one call per rom, and never-played
    /// roms must never surface a Game at all), so this list is not on that
    /// flow's call path -- it is still part of the adapter's documented
    /// surface, with its own decoder and fixture coverage.
    type RomMRomSummary = {
        Id: int
        Name: string
        PlatformId: int
    }

    // ── Decoders ──

    let private decodePlatform: Decoder<RomMPlatform> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Optional.Field "name" Decode.string |> Option.defaultValue ""
            Slug = get.Optional.Field "slug" Decode.string |> Option.defaultValue ""
        })

    let private decodePlatforms: Decoder<RomMPlatform list> =
        Decode.list decodePlatform

    let private decodeRomSummary: Decoder<RomMRomSummary> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            Name = get.Optional.Field "name" Decode.string |> Option.defaultValue ""
            PlatformId = get.Required.Field "platform_id" Decode.int
        })

    /// The roms list is a paged envelope (`{ items, total, limit, offset }`),
    /// unlike the plain arrays `/api/platforms` and `/api/play-sessions`
    /// return.
    let private decodeRomsPage: Decoder<RomMRomSummary list> =
        Decode.object (fun get ->
            get.Optional.Field "items" (Decode.list decodeRomSummary) |> Option.defaultValue [])

    let private tryParseUtc (s: string) : DateTime option =
        match DateTime.TryParse(
                s,
                Globalization.CultureInfo.InvariantCulture,
                Globalization.DateTimeStyles.AdjustToUniversal ||| Globalization.DateTimeStyles.AssumeUniversal) with
        | true, dt -> Some (DateTime.SpecifyKind(dt, DateTimeKind.Utc))
        | _ -> None

    let private decodeUtcDateTime: Decoder<DateTime> =
        Decode.string
        |> Decode.andThen (fun s ->
            match tryParseUtc s with
            | Some dt -> Decode.succeed dt
            | None -> Decode.fail (sprintf "Invalid date/time: %s" s))

    let private decodePlaySession: Decoder<RomMPlaySession> =
        Decode.object (fun get -> {
            Id = get.Required.Field "id" Decode.int
            RomId = get.Required.Field "rom_id" Decode.int
            StartTime = get.Required.Field "start_time" decodeUtcDateTime
            EndTime = get.Optional.Field "end_time" decodeUtcDateTime
            DurationMs = get.Optional.Field "duration_ms" Decode.int64
        })

    let private decodePlaySessions: Decoder<RomMPlaySession list> =
        Decode.list decodePlaySession

    type private RomMetadatum = {
        Genres: string list
        Companies: string list
        FirstReleaseDate: int64 option
    }

    let private emptyMetadatum: RomMetadatum = { Genres = []; Companies = []; FirstReleaseDate = None }

    let private decodeMetadatum: Decoder<RomMetadatum> =
        Decode.object (fun get -> {
            Genres = get.Optional.Field "genres" (Decode.list Decode.string) |> Option.defaultValue []
            Companies = get.Optional.Field "companies" (Decode.list Decode.string) |> Option.defaultValue []
            FirstReleaseDate = get.Optional.Field "first_release_date" Decode.int64
        })

    let private decodeRomDetail: Decoder<RomMRomDetail> =
        Decode.object (fun get ->
            let metadatum =
                get.Optional.Field "metadatum" decodeMetadatum
                |> Option.defaultValue emptyMetadatum
            let releaseYear =
                // `first_release_date` is Unix MILLISECONDS, not seconds --
                // confirmed against the live instance's recorded fixtures
                // (iteration 2): "3 Ninjas Kick Back" (SNES) carries
                // 785203200000, which is 1994-11-19 as milliseconds (matches
                // its real release year) but overflows
                // `DateTimeOffset.FromUnixTimeSeconds`'s representable range
                // entirely if treated as seconds. IGDB itself (the schema
                // this field is shaped after) uses seconds, but RomM's own
                // API answers milliseconds -- do not "fix" this back to
                // `FromUnixTimeSeconds` without re-checking a fresh recording.
                metadatum.FirstReleaseDate
                |> Option.map (fun unixMillis -> (DateTimeOffset.FromUnixTimeMilliseconds(unixMillis)).UtcDateTime.Year)
            {
                Id = get.Required.Field "id" Decode.int
                Name = get.Optional.Field "name" Decode.string |> Option.defaultValue ""
                Summary = get.Optional.Field "summary" Decode.string
                Genres = metadatum.Genres
                PlatformId = get.Required.Field "platform_id" Decode.int
                PlatformSlug = get.Optional.Field "platform_slug" Decode.string |> Option.defaultValue ""
                CoverUrl = get.Optional.Field "url_cover" Decode.string
                ReleaseYear = releaseYear
            })

    // ── HTTP ──

    let private buildUrl (baseUrl: string) (path: string) : string =
        (baseUrl.TrimEnd('/')) + path

    /// GETs `url` with `Authorization: Bearer <token>`, returning `Error
    /// TokenRejected` on 401/403 instead of throwing -- mirrors `Steam.
    /// fetchJsonRejectable`. Never logs `token` or the `Authorization`
    /// header value (an acceptance criterion: the token must never appear
    /// in a log line) -- this function has no logging call at all.
    let private fetchJsonRejectable (httpClient: HttpClient) (url: string) (token: string) : Async<Result<string, RomMError>> =
        async {
            try
                use request = new HttpRequestMessage(HttpMethod.Get, url)
                request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token)
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                if status = 401 || status = 403 then
                    return Error TokenRejected
                elif not response.IsSuccessStatusCode then
                    return Error (RomMOtherFailure (sprintf "HTTP %d" status))
                else
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Ok body
            with ex ->
                return Error (RomMOtherFailure ex.Message)
        }

    let getPlatforms (httpClient: HttpClient) (config: RomMConfig) : Async<Result<RomMPlatform list, RomMError>> =
        async {
            let url = buildUrl config.BaseUrl "/api/platforms"
            let! result = fetchJsonRejectable httpClient url config.ApiToken
            match result with
            | Error e -> return Error e
            | Ok json ->
                match Decode.fromString decodePlatforms json with
                | Ok platforms -> return Ok platforms
                | Error e -> return Error (RomMOtherFailure (sprintf "Failed to parse RomM platforms: %s" e))
        }

    /// `GET /api/roms?platform_ids=...&limit=...&offset=...` -- part of the
    /// documented adapter surface (see `RomMRomSummary`'s doc comment for
    /// why the sync flow itself never calls this).
    let getRoms (httpClient: HttpClient) (config: RomMConfig) (platformIds: int list) (limit: int) (offset: int) : Async<Result<RomMRomSummary list, RomMError>> =
        async {
            let platformFilter =
                platformIds |> List.map (sprintf "platform_ids=%d") |> String.concat "&"
            let url = buildUrl config.BaseUrl (sprintf "/api/roms?%s&limit=%d&offset=%d" platformFilter limit offset)
            let! result = fetchJsonRejectable httpClient url config.ApiToken
            match result with
            | Error e -> return Error e
            | Ok json ->
                match Decode.fromString decodeRomsPage json with
                | Ok roms -> return Ok roms
                | Error e -> return Error (RomMOtherFailure (sprintf "Failed to parse RomM roms list: %s" e))
        }

    /// One page of `GET /api/play-sessions` -- no `rom_id` filter (the
    /// live schema allows this per the task's own research), so this pages
    /// through every session on the instance.
    let private getPlaySessionsPage (httpClient: HttpClient) (config: RomMConfig) (limit: int) (offset: int) : Async<Result<RomMPlaySession list, RomMError>> =
        async {
            let url = buildUrl config.BaseUrl (sprintf "/api/play-sessions?limit=%d&offset=%d" limit offset)
            let! result = fetchJsonRejectable httpClient url config.ApiToken
            match result with
            | Error e -> return Error e
            | Ok json ->
                match Decode.fromString decodePlaySessions json with
                | Ok sessions -> return Ok sessions
                | Error e -> return Error (RomMOtherFailure (sprintf "Failed to parse RomM play sessions: %s" e))
        }

    [<Literal>]
    let PlaySessionsPageSize = 100

    /// Pages through `GET /api/play-sessions` until a page returns fewer
    /// than `PlaySessionsPageSize` items (or an empty page) -- the one
    /// paged call per run the task's Notes call for, rather than one call
    /// per rom.
    let getAllPlaySessions (httpClient: HttpClient) (config: RomMConfig) : Async<Result<RomMPlaySession list, RomMError>> =
        let rec loop (offset: int) (acc: RomMPlaySession list) : Async<Result<RomMPlaySession list, RomMError>> =
            async {
                let! pageResult = getPlaySessionsPage httpClient config PlaySessionsPageSize offset
                match pageResult with
                | Error e -> return Error e
                | Ok page ->
                    let acc = acc @ page
                    if List.length page < PlaySessionsPageSize then
                        return Ok acc
                    else
                        return! loop (offset + PlaySessionsPageSize) acc
            }
        loop 0 []

    let getRomDetail (httpClient: HttpClient) (config: RomMConfig) (romId: int) : Async<Result<RomMRomDetail, RomMError>> =
        async {
            let url = buildUrl config.BaseUrl (sprintf "/api/roms/%d" romId)
            let! result = fetchJsonRejectable httpClient url config.ApiToken
            match result with
            | Error e -> return Error e
            | Ok json ->
                match Decode.fromString decodeRomDetail json with
                | Ok rom -> return Ok rom
                | Error e -> return Error (RomMOtherFailure (sprintf "Failed to parse RomM rom detail: %s" e))
        }

    /// True when `url`'s host matches `baseUrl`'s host -- used by
    /// `downloadCover` to decide whether the RomM bearer token belongs on a
    /// cover request at all. Public (unlike the decoders above) so it can be
    /// pinned directly in a fixture test.
    let isSameHost (baseUrl: string) (url: string) : bool =
        try
            let baseUri = Uri(baseUrl, UriKind.Absolute)
            let targetUri = Uri(url, UriKind.Absolute)
            String.Equals(targetUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
        with _ -> false

    /// Best-effort cover download, mirroring `Steam.downloadSteamCover`'s
    /// shape (never throws, `None` on any failure). `coverUrl` is the rom
    /// detail's own `url_cover` -- confirmed against a live recording
    /// (iteration 2) to typically be an ABSOLUTE, THIRD-PARTY URL (e.g.
    /// libretro's public thumbnail CDN), not a same-host RomM asset. The
    /// bearer token is attached ONLY when `coverUrl`'s host matches
    /// `config.BaseUrl`'s host -- sending the Client API Token to an
    /// unrelated third-party host would be exactly the kind of token leak
    /// the "never logs the token" acceptance criterion guards against, just
    /// over HTTP instead of a log line.
    let downloadCover (httpClient: HttpClient) (config: RomMConfig) (coverUrl: string) (slug: string) (imageBasePath: string) : Async<string option> =
        async {
            try
                use request = new HttpRequestMessage(HttpMethod.Get, coverUrl)
                if isSameHost config.BaseUrl coverUrl then
                    request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", config.ApiToken)
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                if response.IsSuccessStatusCode then
                    let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                    let ref = sprintf "posters/game-%s.jpg" slug
                    let destPath = IO.Path.Combine(imageBasePath, ref)
                    let dir = IO.Path.GetDirectoryName(destPath)
                    if not (IO.Directory.Exists(dir)) then
                        IO.Directory.CreateDirectory(dir) |> ignore
                    IO.File.WriteAllBytes(destPath, bytes)
                    return Some ref
                else
                    return None
            with _ ->
                return None
        }

    /// Rounds `duration_ms` to the nearest minute, with a minimum of 1 for
    /// any positive duration -- a real short session must still consume
    /// its RomM session id instead of rounding to 0 and being retried
    /// forever (the cursor is per-id, not per-minute).
    let minutesFromDurationMs (durationMs: int64) : int =
        if durationMs <= 0L then 0
        else max 1 (int (Math.Round(float durationMs / 60000.0, MidpointRounding.AwayFromZero)))

    // ── Play button (integration-q748k, ADR-0088) ──

    /// Platform slugs EmulatorJS supports -- the BASE `_EJS_CORES_MAP` keys
    /// only (source below). Deliberately excludes `_EJS_NIGHTLY_CORES_MAP`'s
    /// extra slugs ("3ds", "new-nintendo-3ds", "intellivision"), which
    /// RomM's frontend only turns on when a server-side heartbeat flag
    /// (`config.EJS_NETPLAY_ENABLED`) is set -- this adapter has no way to
    /// read that flag, so treating those platforms as unplayable is the
    /// safe default rather than guessing the flag's state. Several slugs
    /// below are literal typos in RomM's own source ("commmodore-128",
    /// "game-televisison", "game-boy-adavance-sp") -- kept verbatim, since
    /// the whole point is matching RomM's real, as-shipped slugs.
    let private ejsPlatformSlugs =
        set [
            "3do"; "acpc"; "amiga"; "amiga-cd32"; "arcade"; "neogeoaes"; "neogeomvs"
            "atari2600"; "atari-2600-plus"; "atari5200"; "atari7800"; "c-plus-4"; "c64"
            "cpet"; "commodore-64c"; "c128"; "commmodore-128"; "colecovision"; "doom"
            "dos"; "jaguar"; "lynx"; "atari-lynx-mkii"; "neo-geo-pocket"
            "neo-geo-pocket-color"; "nes"; "famicom"; "fds"; "game-televisison"
            "new-style-nes"; "n64"; "ique-player"; "nds"; "nintendo-ds-lite"
            "nintendo-dsi"; "nintendo-dsi-xl"; "gb"; "game-boy-pocket"; "game-boy-light"
            "gba"; "game-boy-adavance-sp"; "game-boy-micro"; "gbc"; "pc-fx"; "psx"
            "philips-cd-i"; "psp"; "segacd"; "sega32"; "gamegear"; "sms"
            "sega-mark-iii"; "sega-game-box-9"; "sega-master-system-ii"
            "master-system-super-compact"; "master-system-girl"; "genesis"
            "sega-mega-drive-2-slash-genesis"; "sega-mega-jet"; "mega-pc"
            "tera-drive"; "sega-nomad"; "saturn"; "snes"; "sfam"
            "super-nintendo-original-european-version"; "super-famicom-shvc-001"
            "super-famicom-jr-model-shvc-101"; "new-style-super-nes-model-sns-101"
            "tg16"; "turbografx-cd"; "supergrafx"; "vic-20"; "virtualboy"
            "wonderswan"; "swancrystal"; "wonderswan-color"; "zxs"
        ]

    /// `isRuffleEmulationSupported`'s exact check: `["flash"; "browser"]`.
    let private rufflePlatformSlugs = set [ "flash"; "browser" ]

    /// `isJsDosEmulationSupported`'s exact check: `["win3x"; "win9x"]` --
    /// RomM's js-dos player targets Windows 3.x/9x DOS-era environments,
    /// not a bare "dos" slug (that one is an EmulatorJS core instead, via
    /// `dosbox_pure` -- see `ejsPlatformSlugs` above).
    let private jsdosPlatformSlugs = set [ "win3x"; "win9x" ]

    /// `isPico8EmulationSupported`'s exact check -- singular "pico" (the
    /// PLATFORM slug), not "pico8" (that is only the PLAYER ROUTE name).
    let private pico8PlatformSlugs = set [ "pico" ]

    /// Maps a RomM platform SLUG to the player-route segment RomM's own
    /// frontend (`frontend/src/plugins/router.ts`'s `ROUTES.EMULATORJS` /
    /// `ROUTES.RUFFLE` / `ROUTES.JSDOS` / `ROUTES.PICO8`) would open for it,
    /// or `None` when RomM's frontend shows no Play button at all for that
    /// platform -- confirmed absent from every `isXEmulationSupported`
    /// check for "switch", "ngc" (GameCube) and "wii". Source: RomM tag
    /// `5.3.1` (`rommapp/romm`, commit
    /// `95599dadbe93c8f7b8a8647148fafbe293df3167`),
    /// `frontend/src/utils/index.ts`'s `isEJSEmulationSupported` /
    /// `isRuffleEmulationSupported` / `isJsDosEmulationSupported` /
    /// `isPico8EmulationSupported`. Comparison is case-insensitive, mirroring
    /// those functions' own `slug.toLowerCase()` checks.
    let playerRouteFor (platformSlug: string) : string option =
        let slug =
            if String.IsNullOrEmpty platformSlug then ""
            else platformSlug.ToLowerInvariant()
        if ejsPlatformSlugs.Contains slug then Some "ejs"
        elif rufflePlatformSlugs.Contains slug then Some "ruffle"
        elif jsdosPlatformSlugs.Contains slug then Some "jsdos"
        elif pico8PlatformSlugs.Contains slug then Some "pico8"
        else None
