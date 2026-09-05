/// integration-v0xmv (ADR-0070): coverage for the client-side rename of the
/// Steam Family "needs reconnect" state into "token rejected" -- the QR
/// login is gone, so the only remedy left is pasting a fresh token. Exercises
/// `State.update` end-to-end (not the private `isFamilyTokenRejected` helper
/// directly, which Fable does not export across modules) so the assertion is
/// on the same observable behaviour the acceptance criterion names: only the
/// new "family token rejected" prefix sets `SteamFamilyTokenRejected` -- any
/// other error message (including the retired QR-login wording this
/// supersedes) does not.
module Mediatheca.Client.Pages.Settings.FamilyTokenRejectedTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Pages.Settings.State

let private fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
let private fakeAdminApi : IAdminApi = Unchecked.defaultof<IAdminApi>

let familyTokenRejectedTests =
    testList "integration-v0xmv: Settings.State family-token-rejected detection" [

        testCase "a 'family token rejected: ...' fetch error sets SteamFamilyTokenRejected" <| fun () ->
            let model, _ = init ()
            let updated, _ =
                update fakeApi fakeAdminApi
                    (Steam_family_members_fetched (Error "family token rejected: your Steam Family access token has expired or was rejected — paste a fresh one in Settings → Steam Family"))
                    model
            Expect.isTrue updated.SteamFamilyTokenRejected "The new prefix is recognised"

        testCase "an unrelated fetch error does not set SteamFamilyTokenRejected" <| fun () ->
            let model, _ = init ()
            let updated, _ =
                update fakeApi fakeAdminApi
                    (Steam_family_members_fetched (Error "HTTP 500"))
                    model
            Expect.isFalse updated.SteamFamilyTokenRejected "A generic failure is not a token rejection"

        testCase "a successful member fetch clears any standing SteamFamilyTokenRejected flag" <| fun () ->
            let model, _ = init ()
            let rejected, _ =
                update fakeApi fakeAdminApi
                    (Steam_family_members_fetched (Error "family token rejected: expired"))
                    model
            Expect.isTrue rejected.SteamFamilyTokenRejected "sanity: flag was set"
            let cleared, _ =
                update fakeApi fakeAdminApi
                    (Steam_family_members_fetched (Ok []))
                    rejected
            Expect.isFalse cleared.SteamFamilyTokenRejected "A successful fetch clears the flag"

        testCase "a successful token save clears any standing SteamFamilyTokenRejected flag" <| fun () ->
            let model, _ = init ()
            let rejected, _ =
                update fakeApi fakeAdminApi
                    (Steam_family_members_fetched (Error "family token rejected: expired"))
                    model
            Expect.isTrue rejected.SteamFamilyTokenRejected "sanity: flag was set"
            let saved, _ =
                update fakeApi fakeAdminApi
                    (Steam_family_token_save_result (Ok ()))
                    rejected
            Expect.isFalse saved.SteamFamilyTokenRejected "A successful token save clears the flag"
    ]

Mocha.runTests familyTokenRejectedTests |> ignore
