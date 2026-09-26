/// integration-jkbm1 (ADR-0088), iteration 2: coverage for the Nintendo
/// pre-check branch in `RomM_platforms_loaded` (verifier note, iteration 1) --
/// Nintendo platforms (matched by name OR slug) are pre-selected only when
/// nothing is selected yet, and a previously-saved non-empty selection is
/// never overridden. Exercises `State.update` end-to-end, the same shape
/// `FamilyTokenRejected.test.fs` uses for this module's private branches.
module Mediatheca.Client.Pages.Settings.RomMPlatformPrecheckTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.Settings.Types
open Mediatheca.Client.Pages.Settings.State

let private fakeApi : IMediathecaApi = Unchecked.defaultof<IMediathecaApi>
let private fakeAdminApi : IAdminApi = Unchecked.defaultof<IAdminApi>

let private nintendoSwitch : RomMPlatformDto = { Id = 1; Name = "Nintendo Switch"; Slug = "switch" }
let private nintendo3ds : RomMPlatformDto = { Id = 2; Name = "Nintendo 3DS"; Slug = "n3ds" }
let private ps4 : RomMPlatformDto = { Id = 3; Name = "Sony PlayStation 4"; Slug = "ps4" }

let rommPlatformPrecheckTests =
    testList "integration-jkbm1: Settings.State RomM Nintendo pre-check (RomM_platforms_loaded)" [

        testCase "On first load (nothing selected yet), every Nintendo platform is pre-checked and non-Nintendo platforms are not" <| fun () ->
            let model, _ = init ()
            Expect.isEmpty model.RomMSelectedPlatformIds "sanity: nothing selected yet"
            let updated, _ =
                update fakeApi fakeAdminApi (RomM_platforms_loaded (Ok [ nintendoSwitch; nintendo3ds; ps4 ])) model
            Expect.equal (List.sort updated.RomMSelectedPlatformIds) [ 1; 2 ] "Both Nintendo platforms pre-checked, PS4 is not"
            Expect.equal updated.RomMPlatforms [ nintendoSwitch; nintendo3ds; ps4 ] "The full platform list is recorded for the picker"
            Expect.isFalse updated.IsLoadingRomMPlatforms "The spinner stops"

        testCase "matches a platform whose slug (not name) carries 'nintendo'" <| fun () ->
            let model, _ = init ()
            let handheld : RomMPlatformDto = { Id = 9; Name = "Game Boy Advance"; Slug = "nintendo-gba" }
            let updated, _ =
                update fakeApi fakeAdminApi (RomM_platforms_loaded (Ok [ handheld; ps4 ])) model
            Expect.equal updated.RomMSelectedPlatformIds [ 9 ] "Slug match pre-checks it even though the name has no 'nintendo'"

        testCase "A previously-saved (non-empty) selection is never overridden by the Nintendo pre-check" <| fun () ->
            let model, _ = init ()
            let modelWithSaved = { model with RomMSelectedPlatformIds = [ 3 ] }
            let updated, _ =
                update fakeApi fakeAdminApi (RomM_platforms_loaded (Ok [ nintendoSwitch; nintendo3ds; ps4 ])) modelWithSaved
            Expect.equal updated.RomMSelectedPlatformIds [ 3 ] "The saved PS4-only selection survives -- Nintendo is not force-added"

        testCase "A failed platform fetch records the error, stops the spinner, and never touches the selection" <| fun () ->
            let model, _ = init ()
            let updated, _ =
                update fakeApi fakeAdminApi (RomM_platforms_loaded (Error "RomM token rejected: the Client API Token was refused")) { model with IsLoadingRomMPlatforms = true }
            Expect.isFalse updated.IsLoadingRomMPlatforms "The spinner stops"
            Expect.isEmpty updated.RomMSelectedPlatformIds "No selection is derived from a failed fetch"
            match updated.RomMPlatformsError with
            | Some msg -> Expect.isTrue (msg.StartsWith("RomM token rejected: ")) "The error is recorded for display"
            | None -> failwith "Expected RomMPlatformsError to be set"
    ]

Mocha.runTests rommPlatformPrecheckTests |> ignore
