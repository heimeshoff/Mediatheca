// Spike harness (integration-ygwsa) — UNEXECUTED, see README.md in this folder.
//
// Mints a fresh Steam family access token from the refresh token persisted
// by login.fsx, using a PLAIN HTTP POST to
// `IAuthenticationService/GenerateAccessTokenForApp/v1` — deliberately no
// SteamKit2 dependency or CM connection at this step, matching the
// HTTP-only style of the rest of `src/Server/Steam.fs`. This only works
// (per research) because login.fsx requested a MobileApp-platform,
// persistent-session refresh token; a SteamClient-platform token would need
// an authenticated CM connection to refresh as of an April 2025 Steam-side
// change.
//
// Then calls `IFamilyGroupsService/GetFamilyGroupForUser` with the minted
// token — THIS is the decision-critical, unverified call: does a token
// minted this way carry the audience/scope the family endpoints require?
// This harness could not be run to find out (see README.md).
//
// Run: dotnet fsi refresh-and-call.fsx

open System
open System.Net.Http

let refreshTokenPath =
    System.IO.Path.Combine(__SOURCE_DIRECTORY__, "refresh-token.local.txt")

let run () =
    async {
        if not (System.IO.File.Exists(refreshTokenPath)) then
            printfn "No refresh token found at %s — run login.fsx first." refreshTokenPath
        else
            let refreshToken = System.IO.File.ReadAllText(refreshTokenPath).Trim()
            use httpClient = new HttpClient()

            // The refresh token is a JWT; its `sub` claim is the steamid,
            // which GenerateAccessTokenForApp requires as a parameter.
            let steamId =
                let payload = refreshToken.Split('.').[1].Replace('-', '+').Replace('_', '/')
                let padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
                let json = Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded))
                let idx = json.IndexOf("\"sub\"")
                let start = json.IndexOf('"', json.IndexOf(':', idx)) + 1
                json.Substring(start, json.IndexOf('"', start) - start)

            printfn "steamid (from refresh-token JWT sub claim): %s" steamId

            // GenerateAccessTokenForApp/v1: refresh_token + steamid in,
            // access_token out. `renewal_type` = 1 requests token renewal
            // (rotates the refresh token too) — left at the default here since
            // this is a one-shot spike call, not the production
            // renew-and-persist policy (that's Steam.withTokenRefresh's job,
            // see src/Server/Steam.fs).
            let content =
                new FormUrlEncodedContent(
                    dict [ "refresh_token", refreshToken; "steamid", steamId ]
                    |> Seq.map (fun kv -> System.Collections.Generic.KeyValuePair(kv.Key, kv.Value))
                )

            let! response =
                httpClient.PostAsync(
                    "https://api.steampowered.com/IAuthenticationService/GenerateAccessTokenForApp/v1/",
                    content)
                |> Async.AwaitTask
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

            printfn "GenerateAccessTokenForApp -> HTTP %d" (int response.StatusCode)
            printfn "%s" body

            if response.IsSuccessStatusCode then
                // Minimal manual extraction (this is a throwaway harness, not
                // production decoding — see Thoth decoders in Steam.fs for
                // the real pattern).
                let marker = "\"access_token\":\""
                let idx = body.IndexOf(marker)
                if idx >= 0 then
                    let start = idx + marker.Length
                    let finish = body.IndexOf('"', start)
                    let accessToken = body.Substring(start, finish - start)

                    printfn ""
                    printfn "Minted access token (length %d). Calling IFamilyGroupsService/GetFamilyGroupForUser..." accessToken.Length

                    let familyUrl =
                        sprintf "https://api.steampowered.com/IFamilyGroupsService/GetFamilyGroupForUser/v1/?access_token=%s" accessToken
                    let! familyResponse = httpClient.GetAsync(familyUrl) |> Async.AwaitTask
                    let! familyBody = familyResponse.Content.ReadAsStringAsync() |> Async.AwaitTask

                    printfn "GetFamilyGroupForUser -> HTTP %d" (int familyResponse.StatusCode)
                    printfn "%s" familyBody
                    printfn ""
                    printfn "^ THIS is the decision-critical result: 200 with family data means the"
                    printfn "  refresh-token-minted approach works; 401/403 means it does not carry"
                    printfn "  the right audience/scope and the fallback (semi-automated browser"
                    printfn "  retrieval) is the way forward for integration-hebjs."
                else
                    printfn "Could not find access_token in response body."
    }

run () |> Async.RunSynchronously
