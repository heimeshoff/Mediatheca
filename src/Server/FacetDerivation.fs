namespace Mediatheca.Server

open Mediatheca.Shared

/// Pure derivation of ADR-0053's `PlayFacets` from Steam's numeric Store
/// category ids, plus the pure merge with a manual `PlayFacetsOverride`
/// (games-a7dqx). No I/O, no database — every id here was verified against
/// a live `appdetails?l=english` fetch during implementation (see the
/// module doc comment below the id table for the sample appIds and what
/// they returned).
///
/// Named `FacetDerivation` rather than `PlayFacets` (ADR-0053's own
/// pseudocode uses the latter) to avoid a same-text-different-namespace
/// clash with `Mediatheca.Shared.PlayFacets` — that type is `open`ed
/// unqualified in every caller of this module (`open Mediatheca.Shared`),
/// and a same-named module in `Mediatheca.Server` sitting alongside it via
/// implicit sibling-module visibility would make `PlayFacets.merge` and
/// `PlayFacets.Solo` (a record field) genuinely ambiguous to the compiler.
module FacetDerivation =

    // ── Steam Store category ids (verified live, 2026-08-04) ──
    //
    // Fetched `https://store.steampowered.com/api/appdetails?appids=<id>&l=english`
    // for a spread of well-known titles and recorded every `categories[].id`
    // observed, cross-checked against what each title is actually known to
    // support:
    //
    //   Cyberpunk 2077 (1091500)   — solo only: id 2 present, no 1/9/49.
    //   It Takes Two (1426210)    — co-op only, both couch+online, remote
    //                                play together: ids 1,9,38,39,24,44 —
    //                                no 2 (no solo mode), no 49.
    //   Portal 2 (620)            — solo + co-op (couch+online) + remote play
    //                                together: ids 2,1,9,38,39,24,44.
    //   Left 4 Dead 2 (550)       — solo + co-op online + versus online +
    //                                remote play together, no couch (no 24
    //                                /37/39 on the PC Steam release): ids
    //                                2,1,49,36,9,38,44.
    //   Half-Life: Alyx (546560)  — VR-only, solo, no multiplayer: ids
    //                                2,54,31 (31 = "VR Support", a broader
    //                                tag that co-occurs with 54 here).
    //   Rocket League (252950)    — solo + co-op/versus, both couch+online,
    //                                cross-platform, remote play together:
    //                                ids 2,1,49,36,37,9,38,39,24,27,44.
    //   Terraria (105600)         — solo + co-op online + versus online, no
    //                                couch, no remote play together: ids
    //                                2,1,49,36,9,38.
    //   No Man's Sky (275850)     — solo + co-op online + versus online + VR
    //                                optional, no couch/remote-play-together:
    //                                ids 2,1,49,36,9,38,27,53 (53 = "VR
    //                                Supported", distinct id from Alyx's 31/54
    //                                — Steam is inconsistent about which VR
    //                                id(s) a title carries).
    //   Beat Saber (620980)       — VR-only, solo + versus online: ids
    //                                2,1,49,36,54 (no 31/53 here — 54 alone
    //                                is sufficient to mean "VR-only").
    //   Elite Dangerous (359320)  — solo + bare "Co-op" (id 9, no 38/24 —
    //                                the "wing" feature) resolving to online
    //                                per decision 2, + MMO (id 20) resolving
    //                                VersusOnline, + VR optional: ids
    //                                2,1,20,9,53,31.
    //   Left 4 Dead (500)         — solo + bare "Co-op" (id 9, no 38/24),
    //                                same online-resolution case as Elite
    //                                Dangerous: ids 2,1,9.
    //   Valheim (892970)          — solo + co-op online only, no versus, no
    //                                remote play together: ids 2,1,9,38.
    //   Counter-Strike 2 (730)    — versus online via "Cross-Platform
    //                                Multiplayer" (id 27) with NEITHER id 49
    //                                (PvP) nor id 9 (Co-op) present — proves
    //                                id 27 must be treated as its own
    //                                VersusOnline signal, not merely a
    //                                modifier on an explicit PvP tag.
    //
    // Remote Play on Phone/Tablet/TV (ids 41/42/43) are distinct from
    // "Remote Play Together" (id 44) — thrown away per decision 3 (only 44
    // is kept, as `RemotePlayTogether`).

    [<Literal>]
    let private CatSolo = 2
    [<Literal>]
    let private CatMultiPlayer = 1
    [<Literal>]
    let private CatCoop = 9
    [<Literal>]
    let private CatOnlineCoop = 38
    [<Literal>]
    let private CatLanCoop = 48
    [<Literal>]
    let private CatSharedSplitScreenCoop = 39
    [<Literal>]
    let private CatSharedSplitScreen = 24
    [<Literal>]
    let private CatPvp = 49
    [<Literal>]
    let private CatOnlinePvp = 36
    [<Literal>]
    let private CatLanPvp = 47
    [<Literal>]
    let private CatSharedSplitScreenPvp = 37
    [<Literal>]
    let private CatCrossPlatformMultiplayer = 27
    [<Literal>]
    let private CatMmo = 20
    [<Literal>]
    let private CatRemotePlayTogether = 44
    [<Literal>]
    let private CatVrOnly = 54
    [<Literal>]
    let private CatVrSupported = 53
    [<Literal>]
    let private CatVrSupportGeneric = 31

    /// The pure id -> facet table, including decision 2's umbrella-resolves-
    /// -to-online rule (bare "Co-op"/"Multi-player"/"PvP" — no locality
    /// qualifier at all — resolve to the online facet). A bare "Multi-player"
    /// (id 1) with no other multiplayer-structure id present at all is the
    /// genuinely ambiguous residual case (Steam gives no co-op/versus
    /// signal whatsoever) — resolved here to `CoopOnline`, the more common
    /// real-world reading; Counter-Strike 2's fixture above confirms this
    /// fallback does NOT fire when any other structure id (27/20/9/49/etc.)
    /// is present, so it only ever affects the small residual cohort
    /// decision 2 estimated at 44 games total (all three bare tags
    /// combined).
    let deriveFacets (categoryIds: int list) : PlayFacets =
        let has id = categoryIds |> List.contains id
        let hasCoop = has CatCoop
        let hasPvp = has CatPvp
        let hasSplitScreen = has CatSharedSplitScreen

        let coopCouch = has CatSharedSplitScreenCoop || (hasCoop && hasSplitScreen)
        let versusCouch = has CatSharedSplitScreenPvp || (hasPvp && hasSplitScreen)

        let coopOnlineExplicit = has CatOnlineCoop || has CatLanCoop
        let versusOnlineExplicit =
            has CatOnlinePvp || has CatLanPvp || has CatCrossPlatformMultiplayer || has CatMmo

        let bareCoop = hasCoop && not coopCouch && not coopOnlineExplicit
        let barePvp = hasPvp && not versusCouch && not versusOnlineExplicit
        let bareMultiplayerOnly =
            has CatMultiPlayer && not hasCoop && not hasPvp
            && not coopOnlineExplicit && not versusOnlineExplicit

        {
            Solo = has CatSolo
            CoopCouch = coopCouch
            CoopOnline = coopOnlineExplicit || bareCoop || bareMultiplayerOnly
            VersusCouch = versusCouch
            VersusOnline = versusOnlineExplicit || barePvp
            RemotePlayTogether = has CatRemotePlayTogether
            Vr =
                if has CatVrOnly then VrOnly
                elif has CatVrSupported || has CatVrSupportGeneric then VrSupported
                else NoVr
        }

    /// ADR-0053's pure merge: override wins where set (`Some v`, including
    /// `Some false`/`Some NoVr`), the cache-derived default fills the rest
    /// (`None`). No cleverness — one line per facet.
    let merge (cached: PlayFacets) (ovr: PlayFacetsOverride) : PlayFacets =
        {
            Solo = ovr.Solo |> Option.defaultValue cached.Solo
            CoopCouch = ovr.CoopCouch |> Option.defaultValue cached.CoopCouch
            CoopOnline = ovr.CoopOnline |> Option.defaultValue cached.CoopOnline
            VersusCouch = ovr.VersusCouch |> Option.defaultValue cached.VersusCouch
            VersusOnline = ovr.VersusOnline |> Option.defaultValue cached.VersusOnline
            RemotePlayTogether = ovr.RemotePlayTogether |> Option.defaultValue cached.RemotePlayTogether
            Vr = ovr.Vr |> Option.defaultValue cached.Vr
        }
