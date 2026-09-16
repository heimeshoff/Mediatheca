/// integration-dhctm (ADR-0074): coverage for the Audible Settings card's
/// reducer -- the three states this task's acceptance criteria name: a
/// malformed-file save error, a valid-file save flipping the badge to
/// "Connected as {name} ({marketplace})", and a stubbed 401 from
/// `testAudibleConnection` driving the standing "paste a fresh auth file"
/// notice. Exercises `State.update` end-to-end, the same shape
/// `FamilyTokenRejected.test.fs` uses for Steam Family's rejected-token
/// detection.
module Mediatheca.Client.Pages.Settings.AudibleAuthFileTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Pages.Settings.State

let private fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
let private fakeAdminApi : IAdminApi = Unchecked.defaultof<IAdminApi>

let audibleAuthFileTests =
    testList "integration-dhctm: Settings.State Audible auth-file reducer" [

        testCase "a malformed-file save result shows the validation message and does not flip Configured" <| fun () ->
            let model, _ = init ()
            let updated, _ =
                update fakeApi fakeAdminApi
                    (Audible_save_result (Error "Invalid auth file: Error at: `$.refresh_token`\nExpecting an object with a field named `refresh_token`"))
                    model
            Expect.isFalse updated.AudibleConfigured "A failed save never flips Configured"
            match updated.AudibleSaveResult with
            | Some (Error msg) -> Expect.stringContains msg "refresh_token" "The validation message names the missing field"
            | other -> failwithf "Expected Some (Error _), got %A" other

        testCase "a valid-file save result flips the badge to Connected as {name} ({marketplace})" <| fun () ->
            let model, _ = init ()
            let status : AudibleStatus = { Configured = true; CustomerName = Some "Marco H"; Marketplace = "de"; LastError = None }
            let updated, _ =
                update fakeApi fakeAdminApi
                    (Audible_save_result (Ok status))
                    model
            Expect.isTrue updated.AudibleConfigured "Configured flips to true"
            Expect.equal updated.AudibleCustomerName (Some "Marco H") "Customer name recorded"
            Expect.equal updated.AudibleMarketplace "de" "Marketplace recorded"
            Expect.equal updated.AudibleAuthFileInput "" "The paste textarea is cleared -- the file is a secret and is never re-displayed"
            match updated.AudibleSaveResult with
            | Some (Ok msg) -> Expect.equal msg "Connected as Marco H (de)" "The badge-facing message names the customer and marketplace"
            | other -> failwithf "Expected Some (Ok _), got %A" other

        testCase "a stubbed 401 (\"audible auth file rejected: ...\") from Test connection sets the standing notice" <| fun () ->
            let model, _ = init ()
            let updated, _ =
                update fakeApi fakeAdminApi
                    (Audible_test_result (Error "audible auth file rejected: Amazon rejected the refresh token (HTTP 401)"))
                    model
            Expect.equal updated.AudibleLastError (Some "audible auth file rejected: Amazon rejected the refresh token (HTTP 401)") "The rejection prefix drives the standing notice"

        testCase "a successful Test connection clears any standing notice" <| fun () ->
            let model, _ = init ()
            let rejected, _ =
                update fakeApi fakeAdminApi
                    (Audible_test_result (Error "audible auth file rejected: expired"))
                    model
            Expect.isSome rejected.AudibleLastError "sanity: the notice was set"
            let cleared, _ =
                update fakeApi fakeAdminApi
                    (Audible_test_result (Ok "Connected as Marco H (de) — 7 titles"))
                    rejected
            Expect.isNone cleared.AudibleLastError "A successful test clears the standing notice"

        testCase "an unrelated Test connection failure leaves any standing notice untouched" <| fun () ->
            let model, _ = init ()
            let rejected, _ =
                update fakeApi fakeAdminApi
                    (Audible_test_result (Error "audible auth file rejected: expired"))
                    model
            let stillRejected, _ =
                update fakeApi fakeAdminApi
                    (Audible_test_result (Error "network timeout"))
                    rejected
            Expect.equal stillRejected.AudibleLastError (Some "audible auth file rejected: expired") "A generic failure does not overwrite the standing notice"

        testCase "the installer choice defaults to pipx and Audible_installer_changed swaps the shown install command" <| fun () ->
            let model, _ = init ()
            Expect.equal model.AudibleInstaller Pipx "pipx is the default installer"
            Expect.equal (audibleInstallCommand model.AudibleInstaller) "pipx install audible-cli" "pipx command"
            let switched, _ = update fakeApi fakeAdminApi (Audible_installer_changed Uv) model
            Expect.equal switched.AudibleInstaller Uv "uv selected"
            Expect.equal (audibleInstallCommand switched.AudibleInstaller) "uv tool install audible-cli" "uv command"
            let back, _ = update fakeApi fakeAdminApi (Audible_installer_changed Pipx) switched
            Expect.equal back.AudibleInstaller Pipx "switching back to pipx works"

        testCase "Audible_cleared resets Configured, CustomerName and the standing notice" <| fun () ->
            let model, _ = init ()
            let status : AudibleStatus = { Configured = true; CustomerName = Some "Marco H"; Marketplace = "de"; LastError = None }
            let configured, _ = update fakeApi fakeAdminApi (Audible_save_result (Ok status)) model
            Expect.isTrue configured.AudibleConfigured "sanity: configured"
            let cleared, _ = update fakeApi fakeAdminApi Audible_cleared configured
            Expect.isFalse cleared.AudibleConfigured "Configured resets to false"
            Expect.isNone cleared.AudibleCustomerName "Customer name resets"
            Expect.isNone cleared.AudibleLastError "Standing notice resets"
    ]

Mocha.runTests audibleAuthFileTests |> ignore
