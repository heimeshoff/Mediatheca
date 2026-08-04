module Mediatheca.Tests.PlayFacetsOverrideTests

open Expecto
open Mediatheca.Shared

/// games-j6wkr (ADR-0053 trap guard): the GameDetail segmented controls must
/// flip exactly one facet on `PlayFacetsOverride` and leave the other six
/// byte-identical — never round-trip the merged `PlayFacets` as if it were
/// the override. `Shared.PlayFacetsOverride`'s `withX` functions are the
/// pure, testable seam the client calls exclusively; this is the
/// machine-checkable half of the trap the task's refinement called for.

let private fullyOverridden : PlayFacetsOverride = {
    Solo = Some true
    CoopCouch = Some false
    CoopOnline = Some true
    VersusCouch = Some false
    VersusOnline = Some true
    RemotePlayTogether = Some false
    Vr = Some VrSupported
}

let private allNone : PlayFacetsOverride = {
    Solo = None
    CoopCouch = None
    CoopOnline = None
    VersusCouch = None
    VersusOnline = None
    RemotePlayTogether = None
    Vr = None
}

[<Tests>]
let playFacetsOverrideTests =
    testList "PlayFacetsOverride.withX (ADR-0053 one-field-override trap guard)" [

        testCase "withSolo flips only Solo, leaving the other six fields byte-identical" <| fun _ ->
            let result = PlayFacetsOverride.withSolo (Some false) fullyOverridden
            Expect.equal result.Solo (Some false) "Solo changed to the new value"
            Expect.equal
                { result with Solo = fullyOverridden.Solo }
                fullyOverridden
                "every other field is untouched"

        testCase "withCoopCouch flips only CoopCouch" <| fun _ ->
            let result = PlayFacetsOverride.withCoopCouch (Some true) fullyOverridden
            Expect.equal result.CoopCouch (Some true) "CoopCouch changed to the new value"
            Expect.equal
                { result with CoopCouch = fullyOverridden.CoopCouch }
                fullyOverridden
                "every other field is untouched"

        testCase "withCoopOnline flips only CoopOnline" <| fun _ ->
            let result = PlayFacetsOverride.withCoopOnline (Some false) fullyOverridden
            Expect.equal result.CoopOnline (Some false) "CoopOnline changed to the new value"
            Expect.equal
                { result with CoopOnline = fullyOverridden.CoopOnline }
                fullyOverridden
                "every other field is untouched"

        testCase "withVersusCouch flips only VersusCouch" <| fun _ ->
            let result = PlayFacetsOverride.withVersusCouch (Some true) fullyOverridden
            Expect.equal result.VersusCouch (Some true) "VersusCouch changed to the new value"
            Expect.equal
                { result with VersusCouch = fullyOverridden.VersusCouch }
                fullyOverridden
                "every other field is untouched"

        testCase "withVersusOnline flips only VersusOnline" <| fun _ ->
            let result = PlayFacetsOverride.withVersusOnline (Some false) fullyOverridden
            Expect.equal result.VersusOnline (Some false) "VersusOnline changed to the new value"
            Expect.equal
                { result with VersusOnline = fullyOverridden.VersusOnline }
                fullyOverridden
                "every other field is untouched"

        testCase "withRemotePlayTogether flips only RemotePlayTogether" <| fun _ ->
            let result = PlayFacetsOverride.withRemotePlayTogether (Some true) fullyOverridden
            Expect.equal result.RemotePlayTogether (Some true) "RemotePlayTogether changed to the new value"
            Expect.equal
                { result with RemotePlayTogether = fullyOverridden.RemotePlayTogether }
                fullyOverridden
                "every other field is untouched"

        testCase "withVr flips only Vr" <| fun _ ->
            let result = PlayFacetsOverride.withVr (Some VrOnly) fullyOverridden
            Expect.equal result.Vr (Some VrOnly) "Vr changed to the new value"
            Expect.equal
                { result with Vr = fullyOverridden.Vr }
                fullyOverridden
                "every other field is untouched"

        testCase "un-overriding one facet (Some -> None) leaves the rest untouched, starting from all-None" <| fun _ ->
            let withOneSet = PlayFacetsOverride.withSolo (Some true) allNone
            let backToNone = PlayFacetsOverride.withSolo None withOneSet
            Expect.equal backToNone allNone "clearing the only set field returns to the all-None override"
    ]
