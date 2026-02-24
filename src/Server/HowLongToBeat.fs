namespace Mediatheca.Server

open System
open System.Net.Http
open System.Text
open System.Text.RegularExpressions
open Thoth.Json.Net

module HowLongToBeat =

    // ─── Types ───────────────────────────────────────────────────────────

    type HltbResult = {
        GameId: int
        GameName: string
        CompMainSeconds: int
        CompPlusSeconds: int
        Comp100Seconds: int
        CompAllSeconds: int
        MainCount: int
        PlusCount: int
        Comp100Count: int
    }

    let toHours (seconds: int) : float =
        float seconds / 3600.0

    // ─── Constants ───────────────────────────────────────────────────────

    let private baseUrl = "https://howlongtobeat.com"

    let private userAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"

    let private requestTimeoutMs = 5000

    // ─── Cached API data ─────────────────────────────────────────────────

    module private ApiDataCache =
        open System.Collections.Concurrent

        type ApiData = {
            SearchEndpoint: string
            AuthToken: string
            CachedAt: DateTime
        }

        let mutable private cached: ApiData option = None
        let private lockObj = obj()

        let tryGet () =
            lock lockObj (fun () ->
                match cached with
                | Some data when (DateTime.UtcNow - data.CachedAt).TotalMinutes < 30.0 ->
                    Some data
                | _ ->
                    cached <- None
                    None
            )

        let set (endpoint: string) (token: string) =
            lock lockObj (fun () ->
                cached <- Some {
                    SearchEndpoint = endpoint
                    AuthToken = token
                    CachedAt = DateTime.UtcNow
                }
            )

        let invalidate () =
            lock lockObj (fun () ->
                cached <- None
            )

    // ─── HTTP helpers ────────────────────────────────────────────────────

    let private fetchString (httpClient: HttpClient) (url: string) : Async<Result<string, string>> =
        async {
            try
                use request = new HttpRequestMessage(HttpMethod.Get, url)
                request.Headers.Add("User-Agent", userAgent)
                let cts = new Threading.CancellationTokenSource(requestTimeoutMs)
                let! response = httpClient.SendAsync(request, cts.Token) |> Async.AwaitTask
                if response.IsSuccessStatusCode then
                    let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    return Ok body
                else
                    return Error (sprintf "HTTP %d from %s" (int response.StatusCode) url)
            with ex ->
                return Error (sprintf "Request to %s failed: %s" url ex.Message)
        }

    // ─── Step 1: Discover search endpoint ────────────────────────────────

    /// Regex to find Next.js script bundle URLs in the HLTB homepage HTML
    let private scriptTagPattern =
        Regex(@"<script\s+src=""(/_next/static/[^""]+\.js)""", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

    /// Regex to find the API endpoint path in JS bundles.
    /// Matches fetch("/api/...") POST calls in the minified JS.
    let private endpointPattern =
        Regex(@"fetch\s*\(\s*[""']/api/([a-zA-Z0-9_/]+)", RegexOptions.IgnoreCase ||| RegexOptions.Compiled)

    let private discoverSearchEndpoint (httpClient: HttpClient) : Async<Result<string, string>> =
        async {
            printfn "[HLTB] Discovering search endpoint..."
            match! fetchString httpClient baseUrl with
            | Error e ->
                return Error (sprintf "Failed to fetch HLTB homepage: %s" e)
            | Ok html ->
                let scriptMatches = scriptTagPattern.Matches(html)
                if scriptMatches.Count = 0 then
                    return Error "No Next.js script tags found in HLTB homepage"
                else
                    let scriptUrls =
                        [ for m in scriptMatches -> sprintf "%s%s" baseUrl (m.Groups.[1].Value) ]

                    let mutable foundEndpoint: string option = None

                    for scriptUrl in scriptUrls do
                        if foundEndpoint.IsNone then
                            match! fetchString httpClient scriptUrl with
                            | Ok jsContent ->
                                let endpointMatch = endpointPattern.Match(jsContent)
                                if endpointMatch.Success then
                                    let path = endpointMatch.Groups.[1].Value
                                    let endpoint = sprintf "/api/%s" path
                                    printfn "[HLTB] Discovered search endpoint: %s" endpoint
                                    foundEndpoint <- Some endpoint
                            | Error _ ->
                                () // Skip failed script fetches

                    match foundEndpoint with
                    | Some endpoint -> return Ok endpoint
                    | None -> return Error "Could not find search endpoint in any JS bundle"
        }

    // ─── Step 2: Obtain auth token ───────────────────────────────────────

    let private decodeToken: Decoder<string> =
        Decode.object (fun get ->
            get.Required.Field "token" Decode.string
        )

    let private fetchAuthToken (httpClient: HttpClient) (searchEndpoint: string) : Async<Result<string, string>> =
        async {
            let timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            let url = sprintf "%s%s/init?t=%d" baseUrl searchEndpoint timestamp
            printfn "[HLTB] Fetching auth token from %s" url
            match! fetchString httpClient url with
            | Error e ->
                return Error (sprintf "Failed to fetch auth token: %s" e)
            | Ok json ->
                match Decode.fromString decodeToken json with
                | Ok token ->
                    printfn "[HLTB] Auth token obtained successfully"
                    return Ok token
                | Error e ->
                    return Error (sprintf "Failed to parse auth token response: %s" e)
        }

    // ─── Step 3: Search ──────────────────────────────────────────────────

    let private buildSearchBody (gameName: string) : string =
        let terms =
            gameName.Split([| ' '; '\t' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun t -> sprintf "\"%s\"" (t.Replace("\"", "\\\"")))
            |> String.concat ", "

        let sb = StringBuilder()
        sb.Append("{") |> ignore
        sb.Append("\"searchType\":\"games\",") |> ignore
        sb.AppendFormat("\"searchTerms\":[{0}],", terms) |> ignore
        sb.Append("\"searchPage\":1,") |> ignore
        sb.Append("\"size\":20,") |> ignore
        sb.Append("\"searchOptions\":{") |> ignore
        sb.Append("\"games\":{") |> ignore
        sb.Append("\"userId\":0,") |> ignore
        sb.Append("\"platform\":\"\",") |> ignore
        sb.Append("\"sortCategory\":\"popular\",") |> ignore
        sb.Append("\"rangeCategory\":\"main\",") |> ignore
        sb.Append("\"rangeTime\":{\"min\":null,\"max\":null},") |> ignore
        sb.Append("\"gameplay\":{\"perspective\":\"\",\"flow\":\"\",\"genre\":\"\",\"difficulty\":\"\"},") |> ignore
        sb.Append("\"rangeYear\":{\"min\":\"\",\"max\":\"\"},") |> ignore
        sb.Append("\"modifier\":\"\"") |> ignore
        sb.Append("},") |> ignore
        sb.Append("\"users\":{\"sortCategory\":\"postcount\"},") |> ignore
        sb.Append("\"filter\":\"\",") |> ignore
        sb.Append("\"sort\":0,") |> ignore
        sb.Append("\"randomizer\":0") |> ignore
        sb.Append("},") |> ignore
        sb.Append("\"useCache\":true") |> ignore
        sb.Append("}") |> ignore
        sb.ToString()

    // ─── Response decoder ────────────────────────────────────────────────

    type private HltbSearchResult = {
        GameId: int
        GameName: string
        GameAlias: string
        CompMain: int
        CompPlus: int
        Comp100: int
        CompAll: int
        CompMainCount: int
        CompPlusCount: int
        Comp100Count: int
    }

    let private decodeSearchResult: Decoder<HltbSearchResult> =
        Decode.object (fun get -> {
            GameId = get.Required.Field "game_id" Decode.int
            GameName = get.Required.Field "game_name" Decode.string
            GameAlias = get.Optional.Field "game_alias" Decode.string |> Option.defaultValue ""
            CompMain = get.Optional.Field "comp_main" Decode.int |> Option.defaultValue 0
            CompPlus = get.Optional.Field "comp_plus" Decode.int |> Option.defaultValue 0
            Comp100 = get.Optional.Field "comp_100" Decode.int |> Option.defaultValue 0
            CompAll = get.Optional.Field "comp_all" Decode.int |> Option.defaultValue 0
            CompMainCount = get.Optional.Field "comp_main_count" Decode.int |> Option.defaultValue 0
            CompPlusCount = get.Optional.Field "comp_plus_count" Decode.int |> Option.defaultValue 0
            Comp100Count = get.Optional.Field "comp_100_count" Decode.int |> Option.defaultValue 0
        })

    let private decodeSearchResponse: Decoder<HltbSearchResult list> =
        Decode.object (fun get ->
            get.Required.Field "data" (Decode.list decodeSearchResult)
        )

    // ─── Similarity matching ─────────────────────────────────────────────

    /// Jaccard similarity between two strings, tokenized by whitespace.
    let private jaccardSimilarity (a: string) (b: string) : float =
        let tokenize (s: string) =
            s.ToLowerInvariant().Split([| ' '; '\t'; '-'; ':'; '\''; '(' ; ')' |], StringSplitOptions.RemoveEmptyEntries)
            |> Set.ofArray
        let setA = tokenize a
        let setB = tokenize b
        if Set.isEmpty setA && Set.isEmpty setB then 1.0
        elif Set.isEmpty setA || Set.isEmpty setB then 0.0
        else
            let intersection = Set.intersect setA setB |> Set.count |> float
            let union = Set.union setA setB |> Set.count |> float
            intersection / union

    let private selectBestMatch (query: string) (results: HltbSearchResult list) : HltbSearchResult option =
        if List.isEmpty results then
            None
        else
            results
            |> List.map (fun r ->
                let nameSim = jaccardSimilarity query r.GameName
                let aliasSim =
                    if String.IsNullOrWhiteSpace(r.GameAlias) then 0.0
                    else jaccardSimilarity query r.GameAlias
                let sim = max nameSim aliasSim
                (r, sim)
            )
            |> List.sortByDescending snd
            |> List.tryHead
            |> Option.bind (fun (r, sim) ->
                // Require at least some minimal similarity to avoid returning unrelated games
                if sim >= 0.2 then Some r
                else
                    printfn "[HLTB] Best match '%s' has low similarity %.2f to query '%s', discarding" r.GameName sim query
                    None
            )

    // ─── API data initialization ─────────────────────────────────────────

    let private getApiData (httpClient: HttpClient) : Async<Result<{| Endpoint: string; Token: string |}, string>> =
        async {
            match ApiDataCache.tryGet() with
            | Some cached ->
                return Ok {| Endpoint = cached.SearchEndpoint; Token = cached.AuthToken |}
            | None ->
                let! endpointResult = discoverSearchEndpoint httpClient
                let endpoint =
                    match endpointResult with
                    | Ok ep -> ep
                    | Error e ->
                        printfn "[HLTB] WARNING: Endpoint discovery failed: %s" e
                        printfn "[HLTB] Trying fallback endpoint /api/finder"
                        "/api/finder"
                match! fetchAuthToken httpClient endpoint with
                | Ok token ->
                    ApiDataCache.set endpoint token
                    return Ok {| Endpoint = endpoint; Token = token |}
                | Error tokenErr ->
                    return Error (sprintf "Auth token fetch failed: %s" tokenErr)
        }

    // ─── Execute search request ──────────────────────────────────────────

    let private executeSearch (httpClient: HttpClient) (endpoint: string) (token: string) (gameName: string) : Async<Result<HltbSearchResult list, int>> =
        async {
            try
                let url = sprintf "%s%s" baseUrl endpoint
                let body = buildSearchBody gameName
                use request = new HttpRequestMessage(HttpMethod.Post, url)
                request.Content <- new StringContent(body, Encoding.UTF8, "application/json")
                request.Headers.Add("User-Agent", userAgent)
                request.Headers.Add("Referer", sprintf "%s/" baseUrl)
                request.Headers.Add("Origin", baseUrl)
                request.Headers.Add("x-auth-token", token)
                let cts = new Threading.CancellationTokenSource(requestTimeoutMs)
                let! response = httpClient.SendAsync(request, cts.Token) |> Async.AwaitTask
                let statusCode = int response.StatusCode
                if response.IsSuccessStatusCode then
                    let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    match Decode.fromString decodeSearchResponse responseBody with
                    | Ok results -> return Ok results
                    | Error e ->
                        printfn "[HLTB] Failed to parse search response: %s" e
                        return Ok []
                else
                    return Error statusCode
            with ex ->
                printfn "[HLTB] Search request failed: %s" ex.Message
                return Ok []
        }

    // ─── Public API ──────────────────────────────────────────────────────

    /// Search for a game on HowLongToBeat and return the best matching result.
    /// Returns None on any error or if no sufficiently similar match is found.
    let searchGame (httpClient: HttpClient) (gameName: string) : Async<HltbResult option> =
        async {
            if String.IsNullOrWhiteSpace(gameName) then
                return None
            else
                try
                    match! getApiData httpClient with
                    | Error e ->
                        printfn "[HLTB] Failed to get API data: %s" e
                        return None
                    | Ok apiData ->
                        match! executeSearch httpClient apiData.Endpoint apiData.Token gameName with
                        | Ok results ->
                            let best = selectBestMatch gameName results
                            return best |> Option.map (fun r -> {
                                GameId = r.GameId
                                GameName = r.GameName
                                CompMainSeconds = r.CompMain
                                CompPlusSeconds = r.CompPlus
                                Comp100Seconds = r.Comp100
                                CompAllSeconds = r.CompAll
                                MainCount = r.CompMainCount
                                PlusCount = r.CompPlusCount
                                Comp100Count = r.Comp100Count
                            })
                        | Error statusCode when statusCode = 403 ->
                            // Auth token expired — invalidate cache and retry once
                            printfn "[HLTB] Got 403 — refreshing auth token and retrying..."
                            ApiDataCache.invalidate()
                            match! getApiData httpClient with
                            | Error e ->
                                printfn "[HLTB] Retry failed to get API data: %s" e
                                return None
                            | Ok retryData ->
                                match! executeSearch httpClient retryData.Endpoint retryData.Token gameName with
                                | Ok results ->
                                    let best = selectBestMatch gameName results
                                    return best |> Option.map (fun r -> {
                                        GameId = r.GameId
                                        GameName = r.GameName
                                        CompMainSeconds = r.CompMain
                                        CompPlusSeconds = r.CompPlus
                                        Comp100Seconds = r.Comp100
                                        CompAllSeconds = r.CompAll
                                        MainCount = r.CompMainCount
                                        PlusCount = r.CompPlusCount
                                        Comp100Count = r.Comp100Count
                                    })
                                | Error retryStatus ->
                                    printfn "[HLTB] Retry search also failed with HTTP %d" retryStatus
                                    return None
                        | Error statusCode ->
                            printfn "[HLTB] Search failed with HTTP %d" statusCode
                            return None
                with ex ->
                    printfn "[HLTB] Unexpected error searching for '%s': %s" gameName ex.Message
                    return None
        }
