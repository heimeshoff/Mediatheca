module Mediatheca.Tests.SteamFamilyTokenTests

open System
open System.Net.Http
open Expecto
open Mediatheca.Server.Steam

// Coverage for integration-ygwsa (spike): the pure mint-and-retry orchestration
// that a production Steam-family token-refresh seam would use, mirroring
// `Jellyfin.withReauthRetry` (ADR-0011). `withTokenRefresh` is unit-testable
// with plain lambdas — no HTTP, no SteamKit2, no SQLite — because the live
// mint implementation (SteamKit2 refresh token -> access token) is UNVERIFIED
// as of this spike (see ADR-0019) and out of scope here.

[<Tests>]
let steamFamilyTokenTests =
    testList "withTokenRefresh" [

        testCase "no rejection - fetch runs once with the original token, no mint" <| fun _ ->
            let mutable fetchCount = 0
            let mutable mintCount = 0
            let mutable persisted = []
            let result =
                withTokenRefresh
                    "stored-token"
                    (fun token -> async {
                        fetchCount <- fetchCount + 1
                        return Ok (sprintf "payload(%s)" token)
                    })
                    (fun () -> async { mintCount <- mintCount + 1; return Ok "new-token" })
                    (fun token -> persisted <- persisted @ [ token ])
                |> Async.RunSynchronously
            Expect.equal result (Ok "payload(stored-token)") "Returned the payload from the first fetch"
            Expect.equal fetchCount 1 "Fetched exactly once"
            Expect.equal mintCount 0 "Never minted a new token"
            Expect.isEmpty persisted "Persisted no new token"

        testCase "rejected then success - mints once, persists new token, retries once" <| fun _ ->
            let mutable fetchCount = 0
            let mutable mintCount = 0
            let mutable persisted = []
            let result =
                withTokenRefresh
                    "stale-token"
                    (fun token -> async {
                        fetchCount <- fetchCount + 1
                        if token = "stale-token" then return Error Rejected
                        else return Ok (sprintf "payload(%s)" token)
                    })
                    (fun () -> async { mintCount <- mintCount + 1; return Ok "fresh-token" })
                    (fun token -> persisted <- persisted @ [ token ])
                |> Async.RunSynchronously
            Expect.equal result (Ok "payload(fresh-token)") "Retried with the fresh token and succeeded"
            Expect.equal fetchCount 2 "Fetched twice (original + one retry)"
            Expect.equal mintCount 1 "Minted exactly once"
            Expect.equal persisted [ "fresh-token" ] "Persisted the fresh token exactly once"

        testCase "rejected twice - does not loop, reports a clear error" <| fun _ ->
            let mutable fetchCount = 0
            let mutable mintCount = 0
            let result =
                withTokenRefresh
                    "stale-token"
                    (fun _ -> async {
                        fetchCount <- fetchCount + 1
                        return Error Rejected
                    })
                    (fun () -> async { mintCount <- mintCount + 1; return Ok "fresh-token" })
                    (fun _ -> ())
                |> Async.RunSynchronously
            Expect.equal fetchCount 2 "Fetched twice, then stopped (no retry loop)"
            Expect.equal mintCount 1 "Minted exactly once, not on every rejection"
            match result with
            | Error msg -> Expect.stringContains msg "again after minting" "Error explains the second rejection"
            | Ok _ -> failtest "Expected an Error after a second rejection"

        testCase "mint failure - original fetch is not retried, clear error returned" <| fun _ ->
            let mutable fetchCount = 0
            let mutable persisted = []
            let result =
                withTokenRefresh
                    "stale-token"
                    (fun _ -> async {
                        fetchCount <- fetchCount + 1
                        return Error Rejected
                    })
                    (fun () -> async { return Error "no refresh token stored" })
                    (fun token -> persisted <- persisted @ [ token ])
                |> Async.RunSynchronously
            Expect.equal fetchCount 1 "Fetched exactly once before the failed mint"
            Expect.isEmpty persisted "Persisted nothing on a failed mint"
            match result with
            | Error msg -> Expect.stringContains msg "no refresh token stored" "Error passes through the mint failure reason"
            | Ok _ -> failtest "Expected an Error after a failed mint"

        testCase "non-rejection failure passes through unchanged, no mint" <| fun _ ->
            let mutable mintCount = 0
            let result =
                withTokenRefresh
                    "stored-token"
                    (fun _ -> async { return Error (FamilyOtherFailure "HTTP 500") })
                    (fun () -> async { mintCount <- mintCount + 1; return Ok "fresh-token" })
                    (fun _ -> ())
                |> Async.RunSynchronously
            Expect.equal result (Error "HTTP 500") "Non-auth failures pass straight through"
            Expect.equal mintCount 0 "Never minted for a non-auth failure"
    ]

// Coverage for integration-hebjs: the real `mint` half of the seam above —
// minting a fresh access token from a stored Steam refresh token. Only the
// pure/degenerate paths are unit-testable without a live Steam network (the
// QR-login flow that produces the refresh token in the first place is an
// interactive external dependency, out of scope for unit tests — see the
// task's TDD_SKIPPED note).

[<Tests>]
let steamIdFromRefreshTokenTests =
    testList "steamIdFromRefreshToken" [

        testCase "decodes the steamid from a JWT-shaped refresh token's sub claim" <| fun _ ->
            let payloadJson = """{"sub":"76561198000000000","exp":1234567890}"""
            let payloadB64 =
                payloadJson
                |> Text.Encoding.UTF8.GetBytes
                |> Convert.ToBase64String
                |> fun s -> s.TrimEnd('=').Replace('+', '-').Replace('/', '_')
            let token = sprintf "header.%s.signature" payloadB64
            match steamIdFromRefreshToken token with
            | Ok steamId -> Expect.equal steamId "76561198000000000" "Decoded the sub claim"
            | Error e -> failtest (sprintf "Expected Ok, got Error %s" e)

        testCase "a non-JWT-shaped token returns a clear Error" <| fun _ ->
            match steamIdFromRefreshToken "not-a-jwt-at-all" with
            | Error _ -> ()
            | Ok _ -> failtest "Expected an Error for a malformed token"
    ]

[<Tests>]
let mintFamilyAccessTokenTests =
    testList "mintFamilyAccessToken" [

        testCase "an empty refresh token short-circuits to a reconnect-required error, no HTTP call" <| fun _ ->
            use httpClient = new HttpClient()
            let result = mintFamilyAccessToken httpClient "" |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringContains msg "reconnect required" "Error names the missing-credential case, mirroring ADR-0011's Jellyfin reauthThunk"
            | Ok _ -> failtest "Expected an Error for an empty refresh token"

        testCase "a whitespace-only refresh token also short-circuits, no HTTP call" <| fun _ ->
            use httpClient = new HttpClient()
            let result = mintFamilyAccessToken httpClient "   " |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringContains msg "reconnect required" "Whitespace-only is treated the same as empty"
            | Ok _ -> failtest "Expected an Error for a whitespace-only refresh token"
    ]
