module Mediatheca.Tests.FacetDerivationTests

open Expecto
open Mediatheca.Server
open Mediatheca.Shared

/// games-a7dqx (ADR-0053): `FacetDerivation.deriveFacets`/`merge`, verified
/// against live Steam `appdetails?l=english` fetches during implementation
/// (see `FacetDerivation.fs`'s module doc comment for the full id table and
/// the appIds/category-id lists these fixtures are transcribed from).

let private allFalseNoVr : PlayFacets = {
    Solo = false; CoopCouch = false; CoopOnline = false
    VersusCouch = false; VersusOnline = false; RemotePlayTogether = false; Vr = NoVr
}

[<Tests>]
let deriveFacetsTests =
    testList "FacetDerivation.deriveFacets (live-verified id table)" [

        testCase "Cyberpunk 2077 (1091500): solo only, no multiplayer signal at all" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 22; 28; 29; 64; 67; 66; 68; 78; 74; 55; 57; 58; 79; 69; 65; 70; 23; 61; 62 ]
            Expect.equal facets { allFalseNoVr with Solo = true } "Solo only — every accessibility/platform-feature id is discarded"

        testCase "It Takes Two (1426210): co-op only (couch+online), remote play together, no solo mode" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 1; 9; 38; 39; 24; 22; 28; 29; 55; 57; 69; 70; 23; 44 ]
            Expect.equal facets
                { allFalseNoVr with CoopCouch = true; CoopOnline = true; RemotePlayTogether = true }
                "No id 2 present — no solo mode, matching the real game"

        testCase "Portal 2 (620): solo + co-op (couch+online) + remote play together" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 9; 38; 39; 24; 22; 28; 29; 13; 51; 30; 67; 68; 74; 55; 56; 57; 58; 79; 59; 69; 70; 23; 15; 17; 14; 41; 42; 43; 44; 62 ]
            Expect.equal facets
                { allFalseNoVr with Solo = true; CoopCouch = true; CoopOnline = true; RemotePlayTogether = true }
                "Remote Play on Phone/Tablet/TV (41/42/43) are discarded — only 44 (Together) is kept"

        testCase "Left 4 Dead 2 (550): solo + co-op online + versus online + remote play together, no couch on PC" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 49; 36; 9; 38; 22; 28; 29; 13; 30; 67; 66; 68; 78; 74; 55; 56; 57; 58; 59; 69; 70; 23; 8; 15; 16; 14; 41; 42; 43; 44; 62; 63 ]
            Expect.equal facets
                { allFalseNoVr with Solo = true; CoopOnline = true; VersusOnline = true; RemotePlayTogether = true }
                "No id 24/37/39 present — couch facets stay false, matching the PC release"

        testCase "Half-Life: Alyx (546560): VR-only, solo, no multiplayer" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 22; 52; 54; 13; 31; 30; 68; 78; 79; 69; 65; 70; 17; 62 ]
            Expect.equal facets { allFalseNoVr with Solo = true; Vr = VrOnly } "id 54 (VR Only) wins even though id 31 (a broader VR tag) is also present"

        testCase "Rocket League (252950): solo + co-op/versus both couch+online, cross-platform, remote play together" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 49; 36; 37; 9; 38; 39; 24; 27; 22; 29; 30; 60; 55; 57; 59; 18; 23; 15; 41; 42; 43; 44; 62 ]
            Expect.equal facets
                { Solo = true; CoopCouch = true; CoopOnline = true; VersusCouch = true; VersusOnline = true; RemotePlayTogether = true; Vr = NoVr }
                "Every facet lights up — the richest real-world fixture"

        testCase "Terraria (105600): solo + co-op online + versus online, no couch, no remote play together" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 49; 36; 9; 38; 22; 28; 29; 23; 41; 42; 43; 62 ]
            Expect.equal facets
                { allFalseNoVr with Solo = true; CoopOnline = true; VersusOnline = true }
                "Explicit Online Co-op(38)/Online PvP(36) ids present — no couch, no id 44"

        testCase "No Man's Sky (275850): solo + co-op online + versus online + VR optional" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 49; 36; 9; 38; 27; 22; 28; 52; 53; 29; 64; 67; 72; 66; 68; 78; 74; 55; 56; 57; 58; 79; 59; 69; 65; 70; 23; 42; 43; 61; 62 ]
            Expect.equal facets
                { allFalseNoVr with Solo = true; CoopOnline = true; VersusOnline = true; Vr = VrSupported }
                "id 53 (VR Supported, distinct from Alyx's 31/54) resolves to VrSupported, not VrOnly"

        testCase "Beat Saber (620980): VR-only, solo + versus online, no VR-tag co-occurrence needed" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 49; 36; 52; 54; 25; 62 ]
            Expect.equal facets { allFalseNoVr with Solo = true; VersusOnline = true; Vr = VrOnly } "id 54 alone (no 31/53) is sufficient for VrOnly"

        testCase "Elite Dangerous (359320): bare Co-op (id 9, no 38/24) resolves online; MMO (id 20) resolves VersusOnline; VR optional" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 20; 9; 53; 31; 35; 18; 42; 62 ]
            Expect.equal facets
                { allFalseNoVr with Solo = true; CoopOnline = true; VersusOnline = true; Vr = VrSupported }
                "Decision 2's umbrella-resolves-to-online rule: bare Co-op with no locality qualifier -> CoopOnline"

        testCase "Left 4 Dead (500): bare Co-op (id 9, no 38/24) resolves online, same case as Elite Dangerous" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 9; 22; 28; 13; 67; 66; 68; 78; 55; 56; 57; 58; 69; 70; 23; 8; 15; 25; 16; 14; 43; 62 ]
            Expect.equal facets { allFalseNoVr with Solo = true; CoopOnline = true } "No id 44 (Together), no id 24/39 (couch)"

        testCase "Valheim (892970): solo + co-op online only, no versus, no remote play together" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 2; 1; 9; 38; 28; 67; 68; 78; 79; 69; 23; 42; 62 ]
            Expect.equal facets { allFalseNoVr with Solo = true; CoopOnline = true } "Explicit Online Co-op(38), no PvP-related id at all"

        testCase "Counter-Strike 2 (730): versus online via Cross-Platform Multiplayer (id 27) alone — no id 49/9 at all" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 1; 27; 29; 51; 30; 35; 64; 67; 66; 68; 74; 69; 70; 8; 15; 41; 42; 43; 63 ]
            Expect.equal facets
                { allFalseNoVr with VersusOnline = true }
                "id 27 is its own VersusOnline signal — bare Multi-player(1) must NOT also set CoopOnline once id 27 already resolves versusOnlineExplicit"

        testCase "Empty category list (a non-Steam game, or a fetch that returned nothing) derives all-false/NoVr" <| fun _ ->
            let facets = FacetDerivation.deriveFacets []
            Expect.equal facets allFalseNoVr "No ids at all — every facet false, Vr = NoVr"

        testCase "Bare Multi-player alone (no Co-op/PvP/cross-platform/MMO signal at all) resolves to CoopOnline" <| fun _ ->
            // The genuinely ambiguous residual case decision 2 estimated at
            // a handful of the "44 games affected" total — see
            // FacetDerivation.fs's doc comment for the reasoning.
            let facets = FacetDerivation.deriveFacets [ 1 ]
            Expect.equal facets { allFalseNoVr with CoopOnline = true } "Bare Multi-player with no other structure id resolves to the online co-op facet"

        testCase "Umbrella PvP (id 49, no 37/24/36/47/27/20) resolves to VersusOnline" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 49 ]
            Expect.equal facets { allFalseNoVr with VersusOnline = true } "Bare PvP with no locality qualifier resolves online"

        testCase "Co-op + Shared/Split Screen (ids 9 and 24, no explicit 39) still resolves CoopCouch" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 9; 24 ]
            Expect.equal facets { allFalseNoVr with CoopCouch = true } "'also Co-op AND Shared/Split Screen' clause from decision's type sketch"

        testCase "PvP + Shared/Split Screen (ids 49 and 24, no explicit 37) still resolves VersusCouch" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 49; 24 ]
            Expect.equal facets { allFalseNoVr with VersusCouch = true } "'also PvP AND Shared/Split Screen' clause from decision's type sketch"

        testCase "LAN Co-op (id 48) folds into CoopOnline" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 48 ]
            Expect.equal facets { allFalseNoVr with CoopOnline = true } "LAN Co-op folds into CoopOnline per decision's field comment"

        testCase "LAN PvP (id 47) folds into VersusOnline" <| fun _ ->
            let facets = FacetDerivation.deriveFacets [ 47 ]
            Expect.equal facets { allFalseNoVr with VersusOnline = true } "LAN PvP folds into VersusOnline per decision's field comment"
    ]

[<Tests>]
let mergeTests =
    testList "FacetDerivation.merge (ADR-0053)" [

        testCase "An all-None override leaves the cache default entirely untouched" <| fun _ ->
            let cached = { allFalseNoVr with Solo = true; VersusOnline = true; Vr = VrSupported }
            let ovr : PlayFacetsOverride = {
                Solo = None; CoopCouch = None; CoopOnline = None; VersusCouch = None
                VersusOnline = None; RemotePlayTogether = None; Vr = None
            }
            Expect.equal (FacetDerivation.merge cached ovr) cached "None on every field defers entirely to the cache"

        testCase "Some v overrules the cache on that field only, everything else stays cached" <| fun _ ->
            let cached = { allFalseNoVr with Solo = true; CoopOnline = true }
            let ovr : PlayFacetsOverride = {
                Solo = None; CoopCouch = Some true; CoopOnline = None; VersusCouch = None
                VersusOnline = None; RemotePlayTogether = None; Vr = None
            }
            let result = FacetDerivation.merge cached ovr
            Expect.isTrue result.CoopCouch "CoopCouch overridden to true"
            Expect.isTrue result.Solo "Solo untouched, still true from cache"
            Expect.isTrue result.CoopOnline "CoopOnline untouched, still true from cache"

        testCase "Some false overrules a true cache value — a real correction, not a no-op" <| fun _ ->
            let cached = { allFalseNoVr with VersusOnline = true }
            let ovr : PlayFacetsOverride = {
                Solo = None; CoopCouch = None; CoopOnline = None; VersusCouch = None
                VersusOnline = Some false; RemotePlayTogether = None; Vr = None
            }
            let result = FacetDerivation.merge cached ovr
            Expect.isFalse result.VersusOnline "Steam said versus-online true; the manual correction says false and wins"

        testCase "Some NoVr overrules a cache VrSupported/VrOnly value" <| fun _ ->
            let cached = { allFalseNoVr with Vr = VrOnly }
            let ovr : PlayFacetsOverride = {
                Solo = None; CoopCouch = None; CoopOnline = None; VersusCouch = None
                VersusOnline = None; RemotePlayTogether = None; Vr = Some NoVr
            }
            Expect.equal (FacetDerivation.merge cached ovr).Vr NoVr "Some NoVr is a real, distinct statement that overrules VrOnly"

        testCase "Every field overridden produces exactly the override, independent of the cache" <| fun _ ->
            let cached = allFalseNoVr
            let ovr : PlayFacetsOverride = {
                Solo = Some true; CoopCouch = Some true; CoopOnline = Some true; VersusCouch = Some true
                VersusOnline = Some true; RemotePlayTogether = Some true; Vr = Some VrSupported
            }
            let expected : PlayFacets = {
                Solo = true; CoopCouch = true; CoopOnline = true; VersusCouch = true
                VersusOnline = true; RemotePlayTogether = true; Vr = VrSupported
            }
            Expect.equal (FacetDerivation.merge cached ovr) expected "Fully overridden result ignores the (all-false) cache entirely"
    ]
