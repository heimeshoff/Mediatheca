module Mediatheca.Tests.JellyfinReauthTests

open Expecto
open Mediatheca.Server.Jellyfin

// Regression coverage for integration-002: a 401/403 during a Jellyfin fetch
// must trigger exactly one re-authentication with the stored credentials, persist
// the fresh token, and retry the original request once. A second failure (or a
// failed/impossible re-auth) is reported, not looped.
//
// `withReauthRetry` is the pure orchestration core — it takes a token-consuming
// fetch, a re-auth thunk, and a persist callback as lambdas, so the exactly-once
// policy is unit-testable without HTTP or SQLite (same pattern as
// JellyfinImport.syncSeriesWatchHistory).

let private authOk (token: string) (userId: string) : JellyfinAuthResult =
    { AccessToken = token; UserId = userId; UserName = "tester" }

[<Tests>]
let jellyfinReauthTests =
    testList "withReauthRetry" [

        testCase "no 401 - fetch runs once with the original token, no re-auth" <| fun _ ->
            let mutable fetchCount = 0
            let mutable reauthCount = 0
            let mutable persisted = []
            let result =
                withReauthRetry
                    "stored-token"
                    (fun token -> async {
                        fetchCount <- fetchCount + 1
                        return Ok (sprintf "payload(%s)" token)
                    })
                    (fun () -> async { reauthCount <- reauthCount + 1; return Ok (authOk "new" "uid") })
                    (fun auth -> persisted <- persisted @ [ auth.AccessToken ])
                |> Async.RunSynchronously
            Expect.equal result (Ok "payload(stored-token)") "Returned the payload from the first fetch"
            Expect.equal fetchCount 1 "Fetched exactly once"
            Expect.equal reauthCount 0 "Never re-authenticated"
            Expect.isEmpty persisted "Persisted no new token"

        testCase "401 then success - re-auths once, persists new token, retries once" <| fun _ ->
            let mutable fetchCount = 0
            let mutable reauthCount = 0
            let mutable persisted = []
            let result =
                withReauthRetry
                    "stale-token"
                    (fun token -> async {
                        fetchCount <- fetchCount + 1
                        if token = "stale-token" then return Error Unauthorized
                        else return Ok (sprintf "payload(%s)" token)
                    })
                    (fun () -> async { reauthCount <- reauthCount + 1; return Ok (authOk "fresh-token" "uid-9") })
                    (fun auth -> persisted <- persisted @ [ auth.AccessToken ])
                |> Async.RunSynchronously
            Expect.equal result (Ok "payload(fresh-token)") "Retried with the fresh token and succeeded"
            Expect.equal fetchCount 2 "Fetched twice (original + one retry)"
            Expect.equal reauthCount 1 "Re-authenticated exactly once"
            Expect.equal persisted [ "fresh-token" ] "Persisted the fresh token exactly once"

        testCase "401 twice - reports failure, does NOT loop" <| fun _ ->
            let mutable fetchCount = 0
            let mutable reauthCount = 0
            let result =
                withReauthRetry
                    "stale-token"
                    (fun _token -> async {
                        fetchCount <- fetchCount + 1
                        return Error Unauthorized
                    })
                    (fun () -> async { reauthCount <- reauthCount + 1; return Ok (authOk "fresh-token" "uid") })
                    (fun _ -> ())
                |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringContains msg "re-authentic" "Failure message mentions re-authentication"
            | Ok _ -> failtest "Expected failure on a second 401"
            Expect.equal fetchCount 2 "Fetched exactly twice (no further retries)"
            Expect.equal reauthCount 1 "Re-authenticated exactly once (no loop)"

        testCase "re-auth itself fails - clear failure, original request not retried" <| fun _ ->
            let mutable fetchCount = 0
            let result =
                withReauthRetry
                    "stale-token"
                    (fun _token -> async {
                        fetchCount <- fetchCount + 1
                        return Error Unauthorized
                    })
                    (fun () -> async { return Error "credentials rejected" })
                    (fun _ -> ())
                |> Async.RunSynchronously
            match result with
            | Error msg ->
                Expect.stringContains msg "re-authentic" "Mentions re-authentication"
                Expect.stringContains msg "credentials rejected" "Carries the underlying re-auth error"
            | Ok _ -> failtest "Expected failure when re-auth fails"
            Expect.equal fetchCount 1 "Original request fetched once; not retried after a failed re-auth"

        testCase "missing credentials - fails with a clear 're-authentication required' message" <| fun _ ->
            let mutable fetchCount = 0
            let result =
                withReauthRetry
                    "stale-token"
                    (fun _token -> async {
                        fetchCount <- fetchCount + 1
                        return Error Unauthorized
                    })
                    (fun () -> async { return Error "re-authentication required: Jellyfin username/password not configured" })
                    (fun _ -> ())
                |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringContains msg "re-authentication required" "Names the missing-credentials condition"
            | Ok _ -> failtest "Expected failure when credentials are missing"
            Expect.equal fetchCount 1 "Original request fetched once"

        testCase "non-auth fetch error - passed through, no re-auth" <| fun _ ->
            let mutable reauthCount = 0
            let result =
                withReauthRetry
                    "stored-token"
                    (fun _token -> async { return Error (OtherFailure "Failed to parse library response") })
                    (fun () -> async { reauthCount <- reauthCount + 1; return Ok (authOk "x" "y") })
                    (fun _ -> ())
                |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringContains msg "Failed to parse library response" "Passes the original error through"
            | Ok _ -> failtest "Expected the non-auth error to surface"
            Expect.equal reauthCount 0 "A non-401 error never triggers re-auth"
    ]
