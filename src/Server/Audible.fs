namespace Mediatheca.Server

open System
open System.Net.Http
open Thoth.Json.Net

/// Audible — the anticorruption layer for an imported `audible-cli` auth
/// file, unauthenticated catalog search/detail, and access-token minting
/// (ADR-0074). **No code path in this module registers a device or performs
/// an Amazon/Audible login** — the auth file's `refresh_token` is minted by
/// the user's own `audible-cli quickstart`, run on their own machine;
/// `refreshAccessToken` below only ever exchanges that refresh token for a
/// short-lived access token, the same shape as a Jellyfin re-auth (ADR-0011),
/// never a login. Mirrors `OpenLibrary.fs`'s structure (config record,
/// `Decoder<_>`s, a `SearchCache`) and `Jellyfin.fs`'s `withReauthRetry`
/// shape for the token orchestration.
module Audible =

    // ── Auth file (ADR-0074 point 2) ────────────────────────────────────

    /// The shape `audible-cli quickstart` writes to `~/.audible/<profile>.json`,
    /// narrowed to the fields this adapter actually uses. The file carries
    /// several other fields (`website_cookies`, `store_authentication_cookie`,
    /// `device_info`, a starting `access_token`/`expires`) that only matter
    /// for the signed-request variant this task does not implement (bearer
    /// auth suffices for `/1.0/library` — see the module doc above and this
    /// task's own Notes); `validateAuthFile` below ignores them rather than
    /// failing on their absence or shape.
    type AudibleAuthFile = {
        RefreshToken: string
        AdpToken: string
        DevicePrivateKey: string
        LocaleCode: string
        CustomerName: string
        CustomerUserId: string option
    }

    let private decodeAuthFile : Decoder<AudibleAuthFile> =
        Decode.object (fun get ->
            { RefreshToken = get.Required.Field "refresh_token" Decode.string
              AdpToken = get.Required.Field "adp_token" Decode.string
              DevicePrivateKey = get.Required.Field "device_private_key" Decode.string
              LocaleCode = get.Required.Field "locale_code" Decode.string
              CustomerName = get.Required.Field "customer_info" (Decode.field "name" Decode.string)
              CustomerUserId =
                get.Optional.Field "customer_info" (Decode.object (fun inner -> inner.Optional.Field "user_id" Decode.string))
                |> Option.flatten })

    /// Validates the shape of a pasted auth file (ADR-0074 point 2). Rejects
    /// non-JSON and any missing required field — Thoth's own decode-error
    /// message already names the failing field path, so no extra formatting
    /// is done here.
    let validateAuthFile (json: string) : Result<AudibleAuthFile, string> =
        Decode.fromString decodeAuthFile json

    // ── Marketplace / Amazon token host (mkb79 `localization.py`'s
    // `LOCALE_TEMPLATES`, copied verbatim as this task's Notes instruct) ──

    let private localeDomains : Map<string, string> =
        Map.ofList [
            "us", "com"
            "ca", "ca"
            "uk", "co.uk"
            "au", "com.au"
            "fr", "fr"
            "de", "de"
            "jp", "co.jp"
            "it", "it"
            "in", "in"
            "es", "es"
        ]

    let private localeDomain (locale: string) : string =
        localeDomains |> Map.tryFind (locale.ToLowerInvariant()) |> Option.defaultValue "com"

    /// `us -> api.audible.com`, `uk -> api.audible.co.uk`, `de -> api.audible.de`, …
    let marketplaceHost (locale: string) : string =
        sprintf "api.audible.%s" (localeDomain locale)

    /// `us -> api.amazon.com`, `de -> api.amazon.de`, … — the OAuth token
    /// endpoint's host, one per marketplace (mkb79 `LOCALE_TEMPLATES`).
    let amazonTokenHost (locale: string) : string =
        sprintf "api.amazon.%s" (localeDomain locale)

    // ── Shared error shapes ──────────────────────────────────────────────

    /// Every rejection of the imported auth file (a bad refresh token, or an
    /// access token the API rejects twice in a row) carries this fixed
    /// prefix (ADR-0074 point 4, the `steam_api_key_last_error` /
    /// ADR-0065 wording-distinct convention) so a caller can drive the
    /// standing Settings notice off one string check, never a login retry.
    let authFileRejectedPrefix = "audible auth file rejected: "

    /// A failed authenticated fetch, distinguishing a rejected access token
    /// (401/403 — the retry-once trigger) from any other failure. Mirrors
    /// `Jellyfin.FetchError`.
    type FetchError =
        | Unauthorized
        | OtherFailure of string

    // ── Token refresh (ADR-0074 point 3) ─────────────────────────────────

    type AudibleAccessToken = {
        AccessToken: string
        ExpiresAt: DateTime
    }

    let private decodeTokenResponse : Decoder<string * int> =
        Decode.object (fun get ->
            get.Required.Field "access_token" Decode.string,
            get.Required.Field "expires_in" Decode.int)

    /// `POST /auth/token` — mints a fresh access token from the imported
    /// refresh token (mkb79 `auth.py`'s `_refresh_token_request_body`
    /// shape). This is a token refresh on a device the USER registered via
    /// `audible-cli quickstart`, never a login and never the device-
    /// registration endpoint (ADR-0074 point 3). A 400/401/403 means Amazon rejected the refresh
    /// token itself — every message that case returns begins with
    /// `authFileRejectedPrefix`.
    let refreshAccessToken (httpClient: HttpClient) (authFile: AudibleAuthFile) : Async<Result<AudibleAccessToken, string>> =
        async {
            try
                let host = amazonTokenHost authFile.LocaleCode
                let url = sprintf "https://%s/auth/token" host
                use request = new HttpRequestMessage(HttpMethod.Post, url)
                let formValues =
                    dict [
                        "app_name", "Audible"
                        "app_version", "3.56.2"
                        "source_token", authFile.RefreshToken
                        "requested_token_type", "access_token"
                        "source_token_type", "refresh_token"
                    ]
                request.Content <- new FormUrlEncodedContent(formValues)
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                if status = 400 || status = 401 || status = 403 then
                    return Error (authFileRejectedPrefix + sprintf "Amazon rejected the refresh token (HTTP %d)" status)
                elif not response.IsSuccessStatusCode then
                    return Error (sprintf "Audible token refresh failed: HTTP %d" status)
                else
                    match Decode.fromString decodeTokenResponse body with
                    | Ok (accessToken, expiresIn) ->
                        return Ok { AccessToken = accessToken; ExpiresAt = DateTime.UtcNow.AddSeconds(float expiresIn) }
                    | Error e -> return Error (sprintf "Failed to parse Audible token response: %s" e)
            with ex ->
                return Error (sprintf "Failed to reach the Amazon token endpoint: %s" ex.Message)
        }

    /// Cached-with-proactive-refresh, reactive-retry-once orchestration
    /// (ADR-0074 point 3): reuses `cachedToken` while more than 5 minutes
    /// remain; otherwise (or on a 401 from `fetch`) calls `refresh` and
    /// retries `fetch` exactly once. A second `Unauthorized` — the access
    /// token rejected twice in a row — surfaces `authFileRejectedPrefix`,
    /// never a second refresh. Pure over injected effects (the
    /// `Jellyfin.withReauthRetry` shape, generalized with a proactive expiry
    /// check since Audible's access token is short-lived (~60 min) and worth
    /// avoiding a wasted round trip for), so it is unit-testable with plain
    /// lambdas.
    let withAccessToken
        (cachedToken: (string * DateTime) option)
        (refresh: unit -> Async<Result<AudibleAccessToken, string>>)
        (persist: AudibleAccessToken -> unit)
        (fetch: string -> Async<Result<'a, FetchError>>)
        : Async<Result<'a, string>> =
        async {
            let freshEnough (expiresAt: DateTime) = expiresAt - DateTime.UtcNow > TimeSpan.FromMinutes(5.0)
            let! tokenResult =
                match cachedToken with
                | Some (token, expiresAt) when freshEnough expiresAt -> async { return Ok token }
                | _ ->
                    async {
                        let! r = refresh ()
                        match r with
                        | Ok fresh ->
                            persist fresh
                            return Ok fresh.AccessToken
                        | Error e -> return Error e
                    }
            match tokenResult with
            | Error e -> return Error e
            | Ok token ->
                let! first = fetch token
                match first with
                | Ok value -> return Ok value
                | Error (OtherFailure msg) -> return Error msg
                | Error Unauthorized ->
                    let! r = refresh ()
                    match r with
                    | Error e -> return Error e
                    | Ok fresh ->
                        persist fresh
                        let! retry = fetch fresh.AccessToken
                        match retry with
                        | Ok value -> return Ok value
                        | Error (OtherFailure msg) -> return Error msg
                        | Error Unauthorized ->
                            return Error (authFileRejectedPrefix + "the API rejected the minted access token twice in a row; paste a fresh auth file")
        }

    // ── Authenticated fetch (bearer only, ADR-0074's "What" section) ──
    //
    // Bearer WITHOUT a `client-id` header. The adapter originally sent
    // `client-id: 0` alongside the bearer token (mkb79's bearer mode), but
    // Audible answers that with HTTP 400 "The specified authorization token
    // does not correspond to the specified Client-ID in the request headers"
    // for a token minted from an `audible-cli quickstart` device -- verified
    // 2026-09-16 against api.audible.de: the identical request succeeds the
    // moment the header is dropped, and fails with it on every host/token
    // combination tried. So no client-id at all.

    /// Audible's error bodies are `{"message": "..."}`; surface that text so
    /// a 400 explains itself instead of reading as a bare status code.
    let private describeFailure (status: int) (body: string) : string =
        match Decode.fromString (Decode.field "message" Decode.string) body with
        | Ok message when not (String.IsNullOrWhiteSpace message) -> sprintf "HTTP %d: %s" status message
        | _ -> sprintf "HTTP %d" status

    let private sendAuthenticated (httpClient: HttpClient) (url: string) (token: string) : Async<Result<string, FetchError>> =
        async {
            try
                use request = new HttpRequestMessage(HttpMethod.Get, url)
                request.Headers.Add("Authorization", sprintf "Bearer %s" token)
                let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                let status = int response.StatusCode
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                if status = 401 || status = 403 then
                    return Error Unauthorized
                elif not response.IsSuccessStatusCode then
                    return Error (OtherFailure (describeFailure status body))
                else
                    return Ok body
            with ex ->
                return Error (OtherFailure ex.Message)
        }

    let private decodeLibraryTotal : Decoder<int option> =
        Decode.oneOf [
            Decode.field "total_results" Decode.int |> Decode.map Some
            Decode.succeed None
        ]

    /// The "Test connection" probe (ADR-0074): `GET /1.0/library?num_results=1`,
    /// authenticated. Returns the total count when the response carries one,
    /// else "reachable".
    let getCustomerSummary (httpClient: HttpClient) (host: string) (token: string) : Async<Result<string, FetchError>> =
        async {
            let url = sprintf "https://%s/1.0/library?num_results=1&response_groups=product_desc" host
            let! result = sendAuthenticated httpClient url token
            match result with
            | Error e -> return Error e
            | Ok body ->
                match Decode.fromString decodeLibraryTotal body with
                | Ok (Some count) -> return Ok (sprintf "%d titles" count)
                | Ok None | Error _ -> return Ok "reachable"
        }

    // ── Unauthenticated catalog search / product detail (ADR-0074 point 5) ──

    let private fetchJsonUnauthenticated (httpClient: HttpClient) (url: string) : Async<string> =
        async {
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            let! response = httpClient.SendAsync(request) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return body
        }

    let private decodeName : Decoder<string> = Decode.field "name" Decode.string

    let private decodeYearFromDate (dateOpt: string option) : int option =
        dateOpt
        |> Option.bind (fun d ->
            if d.Length >= 4 then
                match Int32.TryParse(d.Substring(0, 4)) with
                | true, y -> Some y
                | _ -> None
            else None)

    let private decodeSearchProduct : Decoder<Mediatheca.Shared.AudibleSearchResult> =
        Decode.object (fun get ->
            { Mediatheca.Shared.AudibleSearchResult.Asin = get.Required.Field "asin" Decode.string
              Title = get.Required.Field "title" Decode.string
              Authors = get.Optional.Field "authors" (Decode.list decodeName) |> Option.defaultValue []
              Narrators = get.Optional.Field "narrators" (Decode.list decodeName) |> Option.defaultValue []
              RuntimeMinutes = get.Optional.Field "runtime_length_min" Decode.int
              ReleaseYear = get.Optional.Field "release_date" Decode.string |> decodeYearFromDate
              CoverUrl = get.Optional.Field "product_images" (Decode.field "500" Decode.string)
              SeriesName =
                get.Optional.Field "series" (Decode.list (Decode.field "title" Decode.string))
                |> Option.defaultValue []
                |> List.tryHead })

    let private decodeSearchResponse : Decoder<Mediatheca.Shared.AudibleSearchResult list> =
        Decode.object (fun get -> get.Required.Field "products" (Decode.list decodeSearchProduct))

    // 1h in-process cache keyed by (host, keywords) — the `OpenLibrary.SearchCache` shape.
    module private SearchCache =
        open System.Collections.Concurrent

        type CacheEntry = {
            Results: Mediatheca.Shared.AudibleSearchResult list
            ExpiresAt: DateTime
        }

        let private cache = ConcurrentDictionary<string, CacheEntry>()

        let tryGet (key: string) : Mediatheca.Shared.AudibleSearchResult list option =
            match cache.TryGetValue(key) with
            | true, entry ->
                if entry.ExpiresAt > DateTime.UtcNow then Some entry.Results
                else
                    cache.TryRemove(key) |> ignore
                    None
            | _ -> None

        let set (key: string) (results: Mediatheca.Shared.AudibleSearchResult list) =
            cache.[key] <- { Results = results; ExpiresAt = DateTime.UtcNow.AddHours(1.0) }

    /// Unauthenticated `GET /1.0/catalog/products?keywords=…` — works with no
    /// auth file stored at all (ADR-0074 point 5).
    let searchCatalog (httpClient: HttpClient) (host: string) (keywords: string) : Async<Mediatheca.Shared.AudibleSearchResult list> =
        async {
            let cacheKey = sprintf "%s|%s" host (keywords.ToLowerInvariant().Trim())
            match SearchCache.tryGet cacheKey with
            | Some cached -> return cached
            | None ->
                let url =
                    sprintf
                        "https://%s/1.0/catalog/products?keywords=%s&num_results=20&response_groups=product_desc,product_attrs,media,contributors,series&image_sizes=500"
                        host (Uri.EscapeDataString keywords)
                try
                    let! json = fetchJsonUnauthenticated httpClient url
                    match Decode.fromString decodeSearchResponse json with
                    | Ok results ->
                        SearchCache.set cacheKey results
                        return results
                    | Error _ -> return []
                with _ -> return []
        }

    type AudibleProduct = {
        Asin: string
        Title: string
        Subtitle: string option
        Authors: string list
        Narrators: string list
        Publisher: string option
        ReleaseDate: string option
        RuntimeMinutes: int option
        Description: string option
        Language: string option
        Rating: float option
        SeriesName: string option
        SeriesPosition: int option
        Categories: string list
        CoverUrl: string option
    }

    /// Tags kept verbatim (attributes always dropped); everything else is
    /// unwrapped down to its own text content -- never deleted, so a
    /// disallowed element's text (including `<script>`/`<style>` bodies)
    /// survives as plain text rather than vanishing. This is the ONE shared
    /// sanitizer for every Audible-sourced description (`decodeProduct`,
    /// `decodeLibraryItem`, `Audnexus.decodeAudnexusBook`) -- see this
    /// task's Notes for why a sanitized HTML subset, not Markdown or a
    /// custom rich-text DU, is the stored (cache-tier, ADR-0043/ADR-0045)
    /// shape. Entities are left alone; the client-side renderer decodes them.
    let private allowedDescriptionTags =
        set [ "p"; "br"; "b"; "strong"; "i"; "em"; "ul"; "ol"; "li" ]

    let private tagPattern =
        System.Text.RegularExpressions.Regex(@"<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)\b[^>]*?(/?)\s*>")

    let sanitizeDescription (s: string) : string =
        tagPattern.Replace(
            s,
            System.Text.RegularExpressions.MatchEvaluator(fun m ->
                let closing = m.Groups.[1].Value = "/"
                let name = m.Groups.[2].Value.ToLowerInvariant()
                if Set.contains name allowedDescriptionTags then
                    if name = "br" then "<br>"
                    elif closing then sprintf "</%s>" name
                    else sprintf "<%s>" name
                else ""))
        |> fun s -> s.Trim()

    let private decodeProduct : Decoder<AudibleProduct> =
        Decode.object (fun get ->
            { Asin = get.Required.Field "asin" Decode.string
              Title = get.Required.Field "title" Decode.string
              Subtitle = get.Optional.Field "subtitle" Decode.string
              Authors = get.Optional.Field "authors" (Decode.list decodeName) |> Option.defaultValue []
              Narrators = get.Optional.Field "narrators" (Decode.list decodeName) |> Option.defaultValue []
              Publisher = get.Optional.Field "publisher_name" Decode.string
              ReleaseDate = get.Optional.Field "release_date" Decode.string
              RuntimeMinutes = get.Optional.Field "runtime_length_min" Decode.int
              Description = get.Optional.Field "publisher_summary" Decode.string |> Option.map sanitizeDescription
              Language = get.Optional.Field "language" Decode.string
              Rating =
                get.Optional.Field "rating" (Decode.field "overall_distribution" (Decode.field "average_rating" Decode.string))
                |> Option.bind (fun s -> match Double.TryParse(s) with true, v -> Some v | _ -> None)
              SeriesName =
                get.Optional.Field "series" (Decode.list (Decode.field "title" Decode.string))
                |> Option.defaultValue []
                |> List.tryHead
              SeriesPosition =
                get.Optional.Field "series" (Decode.list (Decode.field "sequence" Decode.string))
                |> Option.defaultValue []
                |> List.tryHead
                |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
              Categories =
                get.Optional.Field "category_ladders" (Decode.list (Decode.field "ladder" (Decode.list (Decode.field "name" Decode.string))))
                |> Option.defaultValue []
                |> List.collect id
                |> List.truncate 6
              CoverUrl = get.Optional.Field "product_images" (Decode.field "900" Decode.string) })

    /// Unauthenticated `GET /1.0/catalog/products/{asin}` (ADR-0074 point 5).
    /// `None` on a 404/5xx/decode failure — never an exception. The real API
    /// nests the single product under a `"product"` key; a bare product
    /// object (as tests fixture directly) also decodes, for convenience.
    let getProduct (httpClient: HttpClient) (host: string) (asin: string) : Async<AudibleProduct option> =
        async {
            let url =
                sprintf
                    "https://%s/1.0/catalog/products/%s?response_groups=product_desc,product_extended_attrs,product_attrs,media,contributors,series,rating,category_ladders&image_sizes=900"
                    host asin
            try
                let! json = fetchJsonUnauthenticated httpClient url
                match Decode.fromString (Decode.field "product" decodeProduct) json with
                | Ok product -> return Some product
                | Error _ ->
                    match Decode.fromString decodeProduct json with
                    | Ok product -> return Some product
                    | Error _ -> return None
            with _ -> return None
        }

    // ── Authenticated library fetch (integration-jjvg2, ADR-0074/ADR-0076) ──

    /// One `/1.0/library` item, narrowed to the fields the import/progress
    /// sync need (this task's own "What" section). `PercentComplete` is the
    /// raw float Audible reports (0-100) -- floor-not-round happens at the
    /// CALLER (`AudibleSync.percentOf`), never here, so this decoder stays a
    /// faithful, lossless read of the wire shape.
    type AudibleLibraryItem = {
        Asin: string
        Title: string
        Authors: string list
        Narrators: string list
        RuntimeMinutes: int option
        PercentComplete: float option
        IsFinished: bool
        PurchaseDate: string option
        CoverUrl: string option
        SeriesName: string option
        SeriesPosition: int option
        ReleaseDate: string option
        Description: string option
    }

    let private decodeLibraryItem : Decoder<AudibleLibraryItem> =
        Decode.object (fun get ->
            { Asin = get.Required.Field "asin" Decode.string
              Title = get.Required.Field "title" Decode.string
              Authors = get.Optional.Field "authors" (Decode.list decodeName) |> Option.defaultValue []
              Narrators = get.Optional.Field "narrators" (Decode.list decodeName) |> Option.defaultValue []
              RuntimeMinutes = get.Optional.Field "runtime_length_min" Decode.int
              // `Decode.float` accepts both `0.0` (what Audible actually
              // sends) and a bare `42`. An earlier `Option.orElse` fallback
              // to `Decode.int` ran eagerly inside the getter and failed the
              // whole item on `0.0` -- the very first title of a real
              // library -- so no fallback here.
              PercentComplete = get.Optional.Field "percent_complete" Decode.float
              IsFinished = get.Optional.Field "is_finished" Decode.bool |> Option.defaultValue false
              PurchaseDate = get.Optional.Field "purchase_date" Decode.string
              CoverUrl = get.Optional.Field "product_images" (Decode.field "500" Decode.string)
              SeriesName =
                get.Optional.Field "series" (Decode.list (Decode.field "title" Decode.string))
                |> Option.defaultValue []
                |> List.tryHead
              SeriesPosition =
                get.Optional.Field "series" (Decode.list (Decode.field "sequence" Decode.string))
                |> Option.defaultValue []
                |> List.tryHead
                |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None)
              ReleaseDate = get.Optional.Field "release_date" Decode.string
              Description = get.Optional.Field "publisher_summary" Decode.string |> Option.map sanitizeDescription })

    let private decodeLibraryResponse : Decoder<AudibleLibraryItem list> =
        Decode.object (fun get -> get.Required.Field "items" (Decode.list decodeLibraryItem))

    /// `num_results` capped at Audible's own documented max for `/1.0/library`.
    let libraryPageSize = 1000

    /// Authenticated `GET /1.0/library`, paged until a page returns fewer
    /// than `libraryPageSize` items (this task's "What" section). ONE logical
    /// fetch for `withAccessToken`'s retry-once orchestration: if any page
    /// 401s, the WHOLE paged fetch reports `Unauthorized` so a retry (with a
    /// freshly minted token) starts over from page 1.
    let getLibrary (httpClient: HttpClient) (host: string) (token: string) : Async<Result<AudibleLibraryItem list, FetchError>> =
        let rec loop (page: int) (acc: AudibleLibraryItem list) : Async<Result<AudibleLibraryItem list, FetchError>> =
            async {
                let url =
                    sprintf
                        "https://%s/1.0/library?num_results=%d&page=%d&response_groups=product_desc,product_attrs,media,contributors,series,percent_complete,is_finished,listening_status,order_details&image_sizes=500"
                        host libraryPageSize page
                let! result = sendAuthenticated httpClient url token
                match result with
                | Error e -> return Error e
                | Ok body ->
                    match Decode.fromString decodeLibraryResponse body with
                    | Error e -> return Error (OtherFailure (sprintf "Failed to parse Audible library response: %s" e))
                    | Ok items ->
                        let combined = acc @ items
                        if List.length items < libraryPageSize then
                            return Ok combined
                        else
                            return! loop (page + 1) combined
            }
        loop 1 []

    // ── Runtime config (Composition.fs's `getAudibleConfig`, ADR-0074) ──────

    /// `AuthFile` is `None` until Settings saves one; `Marketplace` is the
    /// locale used for unauthenticated search when no auth file is present
    /// (default `"de"`, or the auth file's own `LocaleCode` once one
    /// exists). `CachedAccessToken`/`CachedAccessTokenExpiresAt` mirror
    /// `JellyfinConfig.AccessToken`'s shape — the last-minted token,
    /// re-read from `SettingsStore` on every config fetch.
    type AudibleConfig = {
        AuthFile: AudibleAuthFile option
        Marketplace: string
        CachedAccessToken: string option
        CachedAccessTokenExpiresAt: DateTime option
    }

/// Audnexus (`api.audnex.us`) — the best-effort metadata fallback when
/// `Audible.getProduct` lacks description/narrators/series (ADR-0074 point
/// 5). Never a hard dependency: a 404/5xx is `None`, never a failure. 100
/// req/min limit is irrelevant at this project's volumes, but one gate at
/// 700ms is applied anyway (this task's own instructions).
module Audnexus =

    type AudnexusBook = {
        Description: string option
        Narrators: string list
        SeriesName: string option
        SeriesPosition: int option
    }

    let mutable throttleInterval = TimeSpan.FromMilliseconds(700.0)

    let private gate = new System.Threading.SemaphoreSlim(1, 1)
    let mutable private lastCallStartedAt : DateTime option = None

    let private throttled (fetch: unit -> Async<'a>) : Async<'a> =
        async {
            do! gate.WaitAsync() |> Async.AwaitTask
            try
                let now = DateTime.UtcNow
                match lastCallStartedAt with
                | Some last ->
                    let remaining = throttleInterval - (now - last)
                    if remaining > TimeSpan.Zero then
                        do! Async.Sleep remaining
                | None -> ()
                lastCallStartedAt <- Some DateTime.UtcNow
                return! fetch ()
            finally
                gate.Release() |> ignore
        }

    let private decodeAudnexusBook : Decoder<AudnexusBook> =
        Decode.object (fun get ->
            { Description =
                get.Optional.Field "summary" Decode.string
                |> Option.orElse (get.Optional.Field "description" Decode.string)
                |> Option.map Audible.sanitizeDescription
              Narrators =
                get.Optional.Field "narrators" (Decode.list (Decode.field "name" Decode.string))
                |> Option.defaultValue []
              SeriesName = get.Optional.Field "seriesPrimary" (Decode.field "name" Decode.string)
              SeriesPosition =
                get.Optional.Field "seriesPrimary" (Decode.field "position" Decode.string)
                |> Option.bind (fun s -> match Int32.TryParse(s) with true, v -> Some v | _ -> None) })

    let getBook (httpClient: HttpClient) (asin: string) (region: string) : Async<AudnexusBook option> =
        throttled (fun () ->
            async {
                try
                    let url = sprintf "https://api.audnex.us/books/%s?region=%s" asin region
                    use request = new HttpRequestMessage(HttpMethod.Get, url)
                    let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                    if not response.IsSuccessStatusCode then
                        return None
                    else
                        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        match Decode.fromString decodeAudnexusBook body with
                        | Ok book -> return Some book
                        | Error _ -> return None
                with _ -> return None
            })
