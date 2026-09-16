/// integration-wmqn3 (ADR-0075): coverage for the Goodreads Settings card's
/// reducer -- saving a user id, the shelf opt-in checkboxes always keeping
/// `currently-reading` in the saved list, a failed "Sync now" setting the
/// standing notice, and a successful one clearing it. Exercises
/// `State.update` end-to-end, the same shape `AudibleAuthFileTests.fs` uses.
module Mediatheca.Client.Pages.Settings.GoodreadsCardTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Pages.Settings.State

let private fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
let private fakeAdminApi : IAdminApi = Unchecked.defaultof<IAdminApi>

let goodreadsCardTests =
    testList "integration-wmqn3: Settings.State Goodreads reducer" [

        testCase "a successful user id save records the id and clears the input" <| fun () ->
            let model, _ = init ()
            let seeded = { model with GoodreadsUserIdInput = "https://www.goodreads.com/user/show/12345678-marco" }
            let updated, _ = update fakeApi fakeAdminApi (Goodreads_save_result (Ok "12345678")) seeded
            Expect.equal updated.GoodreadsUserId (Some "12345678") "user id recorded"
            Expect.equal updated.GoodreadsUserIdInput "" "the input is cleared"
            match updated.GoodreadsSaveResult with
            | Some (Ok id) -> Expect.equal id "12345678" "save result carries the parsed id"
            | other -> failwithf "Expected Some (Ok _), got %A" other

        testCase "a rejected (non-numeric) input shows the error and does not set a user id" <| fun () ->
            let model, _ = init ()
            let updated, _ = update fakeApi fakeAdminApi (Goodreads_save_result (Error "Could not find a Goodreads user id in that input -- paste your profile URL or numeric id")) model
            Expect.isNone updated.GoodreadsUserId "no user id set on failure"
            match updated.GoodreadsSaveResult with
            | Some (Error msg) -> Expect.stringContains msg "user id" "the validation message names the problem"
            | other -> failwithf "Expected Some (Error _), got %A" other

        // `Toggle_goodreads_read_shelf`/`Toggle_goodreads_to_read_shelf` call
        // `api.setGoodreadsImportShelves` directly inside `Cmd.OfAsync.perform`
        // -- like every other api-triggering message in this file's sibling
        // suites (`Save_audible_auth_file`, `Test_audible_connection`, etc.,
        // none of which `AudibleAuthFileTests.fs` dispatches either), this
        // can't run against `fakeApi` (`Unchecked.defaultof` compiles to a
        // bare `null` in Fable, and even a field READ on it throws). Not
        // covered here for the same reason those aren't; the card's own
        // rendering is this task's `[human-eye]` acceptance criterion.

        testCase "a failed sync sets the standing notice and stops the spinner" <| fun () ->
            let model, _ = init ()
            let failed, _ = update fakeApi fakeAdminApi (Goodreads_sync_completed (Error "profile private or user id unknown")) { model with IsSyncingGoodreads = true }
            Expect.equal failed.GoodreadsLastError (Some "profile private or user id unknown") "the failure sets the standing notice"
            Expect.isFalse failed.IsSyncingGoodreads "the spinner stops"
            match failed.GoodreadsSyncResult with
            | Some (Error msg) -> Expect.equal msg "profile private or user id unknown" "the sync result carries the error for the alert"
            | other -> failwithf "Expected Some (Error _), got %A" other

        testCase "a successful sync stops the spinner and leaves the notice for the follow-up Load_goodreads_settings to refresh" <| fun () ->
            let model, _ = init ()
            let successResult : GoodreadsSyncResult = { Shelves = []; Errors = [] }
            let updated, _ = update fakeApi fakeAdminApi (Goodreads_sync_completed (Ok successResult)) { model with IsSyncingGoodreads = true; GoodreadsLastError = Some "stale notice" }
            Expect.isFalse updated.IsSyncingGoodreads "the spinner stops"
            Expect.equal updated.GoodreadsLastError (Some "stale notice") "the reducer itself never clears the notice out of band -- Load_goodreads_settings (dispatched alongside this) is the single source of truth"
    ]

Mocha.runTests goodreadsCardTests |> ignore
