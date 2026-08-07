module Mediatheca.Client.Pages.Games.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.Games.Types
open Mediatheca.Client
open Mediatheca.Client.Components

/// GameStatus unifies 1:1 with DesignSystem.LifecycleStatus (games-status-vocabulary-reconcile).
let private toLifecycleStatus (status: GameStatus) : DesignSystem.LifecycleStatus =
    match status with
    | Backlog -> DesignSystem.Backlog
    | InFocus -> DesignSystem.InFocus
    | Retired -> DesignSystem.Retired
    | Abandoned -> DesignSystem.Abandoned
    | Dismissed -> DesignSystem.Dismissed

let private formatPlayTime (minutes: int) =
    if minutes = 0 then ""
    elif minutes < 60 then $"{minutes}m"
    else
        let h = minutes / 60
        let m = minutes % 60
        if m = 0 then $"{h}h"
        else $"{h}h {m}m"

let private gameCard (game: GameListItem) =
    Html.a [
        prop.href (Router.format ("games", game.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", game.Slug))
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " group relative cursor-pointer w-full")
                prop.children [
                    Html.div [
                        prop.className DesignSystem.posterImageContainer
                        prop.children [
                            match game.CoverRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt game.Name
                                    prop.className DesignSystem.posterImage
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center w-full h-full text-base-content/20 px-3 gap-2"
                                    prop.children [
                                        Icons.gamepad ()
                                        Html.p [
                                            prop.className "text-xs text-base-content/40 font-medium text-center line-clamp-2"
                                            prop.text game.Name
                                        ]
                                    ]
                                ]

                            // Shine effect
                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                    // Info below the poster
                    Html.div [
                        prop.className "flex items-baseline justify-between mt-2 px-1"
                        prop.children [
                            let playTimeText = formatPlayTime game.TotalPlayTimeMinutes
                            if playTimeText <> "" then
                                Html.p [
                                    prop.className "text-xs text-base-content/50"
                                    prop.text playTimeText
                                ]
                            else
                                Html.p [
                                    prop.className "text-xs text-base-content/30"
                                    prop.text "No sessions"
                                ]
                            DesignSystem.statusBadge (toLifecycleStatus game.Status)
                        ]
                    ]
                    // Play facet badges (games-j6wkr, ADR-0053) — up to 4, from the
                    // already-merged GameListItem.PlayFacets — plus the Steam Deck
                    // compatibility badge (games-b8xnw) and the release-date badge
                    // (games-ev65k, unreleased games only), both cache-only, no
                    // override.
                    Html.div [
                        prop.className "mt-1.5 px-1 flex flex-wrap gap-1 items-center"
                        prop.children [
                            PlayFacetsDisplay.badgeRow game.PlayFacets
                            PlayFacetsDisplay.deckCompatBadge game.DeckCompat
                            PlayFacetsDisplay.releaseDateBadge game.ReleaseDate
                        ]
                    ]
                ]
            ]
        ]
    ]

let private statusFilterBadges (currentFilter: GameStatus option) (dispatch: Msg -> unit) =
    let allStatuses = [ Backlog; InFocus; Retired; Abandoned; Dismissed ]
    Html.div [
        prop.className "flex flex-wrap gap-2"
        prop.children [
            // "All" pill
            Html.button [
                prop.className (DesignSystem.pill (currentFilter.IsNone))
                prop.onClick (fun _ -> dispatch (Status_filter_changed None))
                prop.text "All"
            ]
            for status in allStatuses do
                let isActive = currentFilter = Some status
                Html.button [
                    prop.className (DesignSystem.pill isActive)
                    prop.onClick (fun _ ->
                        if isActive then dispatch (Status_filter_changed None)
                        else dispatch (Status_filter_changed (Some status)))
                    prop.text (DesignSystem.statusBadgeLabel (toLifecycleStatus status))
                ]
        ]
    ]

let private facetFilterLabel (facet: PlayFacetFilter) =
    match facet with
    | Facet_solo -> "Solo"
    | Facet_coop -> "Co-op"
    | Facet_versus -> "Versus"
    | Facet_couch -> "Couch"

/// Mirrors `PlayFacetsDisplay.facetBadges`'s categories so "filter by badge"
/// matches "shown badge" one-to-one.
let private matchesFacetFilter (filter: PlayFacetFilter option) (facets: PlayFacets) =
    match filter with
    | None -> true
    | Some Facet_solo -> facets.Solo
    | Some Facet_coop -> facets.CoopCouch || facets.CoopOnline
    | Some Facet_versus -> facets.VersusCouch || facets.VersusOnline
    | Some Facet_couch -> facets.CoopCouch || facets.VersusCouch

let private facetFilterPills (currentFilter: PlayFacetFilter option) (dispatch: Msg -> unit) =
    let allFacets = [ Facet_solo; Facet_coop; Facet_versus; Facet_couch ]
    Html.div [
        prop.className "flex flex-wrap gap-2"
        prop.children [
            for facet in allFacets do
                let isActive = currentFilter = Some facet
                Html.button [
                    prop.className (DesignSystem.pill isActive)
                    prop.onClick (fun _ ->
                        if isActive then dispatch (Facet_filter_changed None)
                        else dispatch (Facet_filter_changed (Some facet)))
                    prop.text (facetFilterLabel facet)
                ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pageContainer + " " + DesignSystem.animateFadeIn)
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display text-gradient-primary"
                        prop.text "Games"
                    ]
                    Daisy.button.button [
                        button.primary
                        prop.className "gap-2"
                        prop.onClick (fun _ -> dispatch Open_search_modal)
                        prop.children [
                            Html.span [ prop.text "+" ]
                            Html.span [ prop.text "Add Game" ]
                        ]
                    ]
                ]
            ]
            // Search bar and filters
            Html.div [
                prop.className "flex flex-col sm:flex-row gap-3 mb-6"
                prop.children [
                    Html.div [
                        prop.className "relative flex-1"
                        prop.children [
                            Html.div [
                                prop.className "absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none text-base-content/40"
                                prop.children [
                                    Svg.svg [
                                        svg.className "w-5 h-5"
                                        svg.fill "none"
                                        svg.viewBox (0, 0, 24, 24)
                                        svg.stroke "currentColor"
                                        svg.custom ("strokeWidth", 1.5)
                                        svg.children [
                                            Svg.path [
                                                svg.custom ("strokeLinecap", "round")
                                                svg.custom ("strokeLinejoin", "round")
                                                svg.d "m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z"
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            Daisy.input [
                                prop.className "w-full pl-10"
                                prop.placeholder "Search games..."
                                prop.value model.SearchQuery
                                prop.onChange (Search_changed >> dispatch)
                            ]
                        ]
                    ]
                ]
            ]
            // Status filter badges
            Html.div [
                prop.className "mb-3"
                prop.children [
                    statusFilterBadges model.StatusFilter dispatch
                ]
            ]
            // Play facet filters (games-j6wkr) — pure client-side, over the
            // already-merged GameListItem.PlayFacets.
            Html.div [
                prop.className "mb-6"
                prop.children [
                    facetFilterPills model.FacetFilter dispatch
                ]
            ]
            // Upcoming section (games-ev65k) — soonest-first, TBA last,
            // server-sorted (GameProjection.getUpcomingGames). Absent (not
            // empty-rendered) when there is nothing unreleased.
            if not (List.isEmpty model.UpcomingGames) then
                Html.div [
                    prop.className "mb-8"
                    prop.children [
                        Html.h2 [
                            prop.className "text-lg font-bold font-display mb-3 flex items-center gap-2"
                            prop.children [
                                Html.span [ prop.className "w-1 h-5 bg-info rounded-full inline-block" ]
                                Html.text "Upcoming"
                            ]
                        ]
                        Html.div [
                            prop.className "flex gap-4 overflow-x-auto pb-2"
                            prop.children [
                                for game in model.UpcomingGames do
                                    Html.div [
                                        prop.className "flex-none w-32 sm:w-36"
                                        prop.children [ gameCard game ]
                                    ]
                            ]
                        ]
                    ]
                ]
            if model.IsLoading then
                Html.div [
                    prop.className "flex justify-center py-12"
                    prop.children [
                        Daisy.loading [ loading.spinner; loading.lg ]
                    ]
                ]
            else
                let fuzzyFiltered =
                    if model.SearchQuery = "" then model.Games
                    else
                        let query, yearFilter = FuzzyMatch.extractYear model.SearchQuery
                        let items = model.Games |> List.map (fun g -> (g.Name, g))
                        let matched = FuzzyMatch.fuzzyFilter query items
                        match yearFilter with
                        | Some year -> matched |> List.filter (fun g -> g.Year = year)
                        | None -> matched
                let filtered =
                    fuzzyFiltered
                    |> List.filter (fun g ->
                        match model.StatusFilter with
                        | None -> g.Status <> Dismissed
                        | Some f -> f = g.Status
                    )
                    |> List.filter (fun g -> matchesFacetFilter model.FacetFilter g.PlayFacets)
                if List.isEmpty filtered then
                    Html.div [
                        prop.className ("text-center py-20 " + DesignSystem.animateFadeIn)
                        prop.children [
                            Html.div [
                                prop.className "text-base-content/20 mb-4"
                                prop.children [
                                    Svg.svg [
                                        svg.className "w-16 h-16 mx-auto"
                                        svg.fill "none"
                                        svg.viewBox (0, 0, 24, 24)
                                        svg.stroke "currentColor"
                                        svg.custom ("strokeWidth", 1)
                                        svg.children [
                                            Svg.path [
                                                svg.custom ("strokeLinecap", "round")
                                                svg.custom ("strokeLinejoin", "round")
                                                svg.d "M14.25 6.087c0-.355.186-.676.401-.959.221-.29.349-.634.349-1.003 0-1.036-1.007-1.875-2.25-1.875s-2.25.84-2.25 1.875c0 .369.128.713.349 1.003.215.283.401.604.401.959v0a.64.64 0 0 1-.657.643 48.39 48.39 0 0 0-4.163.3C4.318 7.567 3 9.374 3 11.386v0c0 1.867 1.2 3.528 2.996 4.2A5.056 5.056 0 0 0 7.5 16c.91 0 1.783.247 2.544.68a3.082 3.082 0 0 0 1.456.37h1a3.08 3.08 0 0 0 1.456-.37A5.046 5.046 0 0 1 16.5 16a5.06 5.06 0 0 1 1.504.414C19.8 14.914 21 13.253 21 11.386v0c0-2.012-1.318-3.82-3.38-4.386a48.47 48.47 0 0 0-4.163-.3.64.64 0 0 1-.657-.643v0Z"
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            Html.p [
                                prop.className "text-base-content/50 font-medium"
                                prop.text "No games found."
                            ]
                            Html.p [
                                prop.className "mt-2 text-base-content/30 text-sm"
                                prop.text "Add a game to get started."
                            ]
                        ]
                    ]
                else
                    Html.div [
                        prop.className ("grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4 " + DesignSystem.animateFadeIn)
                        prop.children [
                            for game in filtered do
                                gameCard game
                        ]
                    ]
        ]
    ]
