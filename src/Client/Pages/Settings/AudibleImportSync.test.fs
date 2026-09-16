/// integration-jjvg2 (ADR-0074/ADR-0076/ADR-0026): coverage for the Audible
/// Settings card's import/progress-sync reducer -- persisted status loading,
/// a successful/failed import stopping the spinner and recording the
/// session-fresh result, and a rejected auth file from "Sync progress now"
/// setting the standing notice. Same shape `GoodreadsCard.test.fs`/
/// `AudibleAuthFileTests.fs` use.
module Mediatheca.Client.Pages.Settings.AudibleImportSyncTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Pages.Settings.State

let private fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
let private fakeAdminApi : IAdminApi = Unchecked.defaultof<IAdminApi>

let audibleImportSyncTests =
    testList "integration-jjvg2: Settings.State Audible import/sync reducer" [

        testCase "Audible_sync_status_loaded records the persisted last-import/last-sync fields" <| fun () ->
            let model, _ = init ()
            let status : AudibleSyncStatus = { LastImportResult = Some "3 total, 3 created, 0 already known, 2 progress observed"; LastSync = Some "2026-09-16T05:00:00Z"; LastSyncResult = Some "2 observed, 0 unmatched" }
            let updated, _ = update fakeApi fakeAdminApi (Audible_sync_status_loaded status) model
            Expect.equal updated.AudibleLastImportResult status.LastImportResult "last import summary recorded"
            Expect.equal updated.AudibleLastSync status.LastSync "last sync time recorded"
            Expect.equal updated.AudibleLastSyncResult status.LastSyncResult "last sync result recorded"

        testCase "a successful import stops the spinner and records the session-fresh result" <| fun () ->
            let model, _ = init ()
            let result : AudibleImportResult = { Total = 3; Created = 3; AlreadyKnown = 0; ProgressObserved = 2; Errors = [] }
            let updated, _ = update fakeApi fakeAdminApi (Audible_import_completed (Ok result)) { model with IsImportingAudibleLibrary = true }
            Expect.isFalse updated.IsImportingAudibleLibrary "the spinner stops"
            match updated.AudibleImportResult with
            | Some (Ok r) -> Expect.equal r.Created 3 "the import result carries the counts for the alert"
            | other -> failwithf "Expected Some (Ok _), got %A" other

        testCase "a failed import stops the spinner and records the error" <| fun () ->
            let model, _ = init ()
            let updated, _ = update fakeApi fakeAdminApi (Audible_import_completed (Error "Audible is not configured — paste an auth file in Settings")) { model with IsImportingAudibleLibrary = true }
            Expect.isFalse updated.IsImportingAudibleLibrary "the spinner stops"
            match updated.AudibleImportResult with
            | Some (Error msg) -> Expect.stringContains msg "not configured" "the error names the problem"
            | other -> failwithf "Expected Some (Error _), got %A" other

        testCase "a rejected-auth-file progress sync sets the standing notice and stops the spinner" <| fun () ->
            let model, _ = init ()
            let updated, _ = update fakeApi fakeAdminApi (Audible_progress_sync_completed (Error "audible auth file rejected: the API rejected the minted access token twice in a row; paste a fresh auth file")) { model with IsSyncingAudibleProgress = true }
            Expect.isFalse updated.IsSyncingAudibleProgress "the spinner stops"
            match updated.AudibleLastError with
            | Some msg -> Expect.isTrue (msg.StartsWith("audible auth file rejected: ")) "the standing notice carries the fixed rejection prefix"
            | None -> failwith "Expected the rejection to set AudibleLastError"
            match updated.AudibleProgressSyncResult with
            | Some (Error _) -> ()
            | other -> failwithf "Expected Some (Error _), got %A" other

        testCase "a successful progress sync leaves any existing notice untouched -- the follow-up Load_audible_sync_status is the single source of truth" <| fun () ->
            let model, _ = init ()
            let result : AudibleProgressSyncResult = { Observed = 1; Unmatched = 0; Errors = [] }
            let updated, _ = update fakeApi fakeAdminApi (Audible_progress_sync_completed (Ok result)) { model with IsSyncingAudibleProgress = true; AudibleLastError = Some "stale notice" }
            Expect.isFalse updated.IsSyncingAudibleProgress "the spinner stops"
            Expect.equal updated.AudibleLastError (Some "stale notice") "the reducer itself never clears the notice out of band"
    ]

Mocha.runTests audibleImportSyncTests |> ignore
