module Mediatheca.Client.Components.PlayFacetsDisplay

/// games-j6wkr (ADR-0053/ADR-0054): read-only badges and editable Auto/On/Off
/// segmented controls for the seven play facets, shared between
/// `Pages/Games` (list cards + filters) and `Pages/GameDetail` (hero badges +
/// per-facet overrides) so the badge vocabulary and control chrome stay one
/// definition.
///
/// Badge mapping (the task's literal "up to 4 badges — Solo · Co-op ·
/// Versus · Couch"): Solo/Co-op/Versus badges fire off their couch-or-online
/// facet pair with an "online/couch" sub-label distinguishing which; the
/// standalone "Couch" badge is a fast-scan summary that fires whenever
/// *any* couch-playable mode exists (co-op or versus), independent of the
/// Co-op/Versus badges above it. At most 4 badges ever render.

open Feliz
open Mediatheca.Shared

// ── Badges (read-only, merged PlayFacets) ──

type FacetBadge = {
    Label: string
    SubLabel: string option
}

let facetBadges (facets: PlayFacets) : FacetBadge list =
    [
        if facets.Solo then
            { Label = "Solo"; SubLabel = None }
        if facets.CoopCouch || facets.CoopOnline then
            let sub =
                match facets.CoopCouch, facets.CoopOnline with
                | true, true -> "Couch + Online"
                | true, false -> "Couch"
                | false, _ -> "Online"
            { Label = "Co-op"; SubLabel = Some sub }
        if facets.VersusCouch || facets.VersusOnline then
            let sub =
                match facets.VersusCouch, facets.VersusOnline with
                | true, true -> "Couch + Online"
                | true, false -> "Couch"
                | false, _ -> "Online"
            { Label = "Versus"; SubLabel = Some sub }
        if facets.CoopCouch || facets.VersusCouch then
            { Label = "Couch"; SubLabel = None }
    ]

let private badgeChip (badge: FacetBadge) =
    Html.span [
        prop.className "inline-flex items-center gap-1 bg-base-content/10 text-base-content/70 px-2 py-0.5 rounded text-[10px] font-semibold uppercase tracking-wide"
        prop.children [
            Html.span [ prop.text badge.Label ]
            match badge.SubLabel with
            | Some sub ->
                Html.span [
                    prop.className "text-base-content/40 normal-case font-normal"
                    prop.text $"· {sub}"
                ]
            | None -> ()
        ]
    ]

/// Compact badge row — used on the game-list poster cards.
let badgeRow (facets: PlayFacets) : ReactElement =
    let badges = facetBadges facets
    if List.isEmpty badges then
        Html.none
    else
        Html.div [
            prop.className "flex flex-wrap gap-1"
            prop.children [ for b in badges -> badgeChip b ]
        ]

// ── Auto/On/Off segmented controls (editable, GameDetail only) ──

let private segmentedOption (label: string) (isActive: bool) (onClick: unit -> unit) =
    Html.button [
        prop.type' "button"
        prop.className (
            "px-3 py-1 rounded-md text-xs font-semibold whitespace-nowrap transition-all duration-200 cursor-pointer "
            + (if isActive then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content"))
        prop.onClick (fun _ -> onClick ())
        prop.text label
    ]

let private segmentedGroup (children: ReactElement list) =
    Html.div [
        prop.className "flex items-center gap-1 bg-base-200/50 rounded-lg p-1 flex-wrap"
        prop.children children
    ]

/// Tri-state Auto/On/Off control for one of the six boolean facets.
/// `autoValue` is the Steam-derived cached value shown when Auto is
/// selected; `current` is the raw override field (`None` = Auto).
let boolFacetControl (label: string) (autoValue: bool) (current: bool option) (onChange: bool option -> unit) : ReactElement =
    Html.div [
        prop.className "flex items-center justify-between gap-3 py-1.5 flex-wrap"
        prop.children [
            Html.span [ prop.className "text-sm text-base-content/70"; prop.text label ]
            segmentedGroup [
                segmentedOption (if autoValue then "Auto (On)" else "Auto (Off)") current.IsNone (fun () -> onChange None)
                segmentedOption "On" (current = Some true) (fun () -> onChange (Some true))
                segmentedOption "Off" (current = Some false) (fun () -> onChange (Some false))
            ]
        ]
    ]

/// 4-option Auto/No VR/Supported/VR only control for the `Vr` facet.
let vrFacetControl (label: string) (autoValue: VrSupport) (current: VrSupport option) (onChange: VrSupport option -> unit) : ReactElement =
    let autoText =
        match autoValue with
        | NoVr -> "Auto (No VR)"
        | VrSupported -> "Auto (Supported)"
        | VrOnly -> "Auto (VR only)"
    Html.div [
        prop.className "flex items-center justify-between gap-3 py-1.5 flex-wrap"
        prop.children [
            Html.span [ prop.className "text-sm text-base-content/70"; prop.text label ]
            segmentedGroup [
                segmentedOption autoText current.IsNone (fun () -> onChange None)
                segmentedOption "No VR" (current = Some NoVr) (fun () -> onChange (Some NoVr))
                segmentedOption "Supported" (current = Some VrSupported) (fun () -> onChange (Some VrSupported))
                segmentedOption "VR only" (current = Some VrOnly) (fun () -> onChange (Some VrOnly))
            ]
        ]
    ]
