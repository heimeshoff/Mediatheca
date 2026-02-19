module Mediatheca.Client.Pages.Dashboard.Views

open Feliz
open Feliz.Router
open Mediatheca.Client.Pages.Dashboard.Types
open Mediatheca.Shared
open Mediatheca.Client
open Mediatheca.Client.Components

// ── Helpers ──

let private formatPlayTime (minutes: int) =
    if minutes = 0 then "0h"
    elif minutes < 60 then $"{minutes}m"
    else
        let hours = minutes / 60
        let mins = minutes % 60
        if mins = 0 then $"{hours}h"
        else $"{hours}h {mins}m"

let private formatDate (dateStr: string) =
    try
        let dt = System.DateTimeOffset.Parse(dateStr)
        dt.LocalDateTime.ToString("MMM d")
    with _ -> dateStr

// ── Tab bar ──

let private tabBar (activeTab: DashboardTab) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "flex gap-1 p-1 rounded-xl bg-base-300/40 w-fit"
        prop.role "tablist"
        prop.children [
            let tab (label: string) tabValue =
                Html.button [
                    prop.className (
                        "px-4 py-2 rounded-lg text-sm font-medium transition-all duration-200 "
                        + if activeTab = tabValue then
                            "bg-primary/15 text-primary border border-primary/30"
                          else
                            "text-base-content/60 hover:text-base-content hover:bg-base-300/50 border border-transparent"
                    )
                    prop.role "tab"
                    prop.onClick (fun _ -> dispatch (SwitchTab tabValue))
                    prop.text label
                ]
            tab "All" All
            tab "Movies" MoviesTab
            tab "TV Series" SeriesTab
            tab "Games" GamesTab
        ]
    ]

// ── Section card wrapper ──

let private sectionCard (icon: unit -> ReactElement) (title: string) (children: ReactElement list) =
    Html.div [
        prop.className (DesignSystem.glassCard + " p-4 " + DesignSystem.animateFadeInUp)
        prop.children [
            Html.div [
                prop.className "flex items-center gap-2 mb-3"
                prop.children [
                    Html.span [
                        prop.className "text-primary/70"
                        prop.children [ icon () ]
                    ]
                    Html.h2 [
                        prop.className "text-lg font-display uppercase tracking-wider"
                        prop.text title
                    ]
                ]
            ]
            Html.div [
                prop.className "flex flex-col"
                prop.children children
            ]
        ]
    ]

// ── TV Series: Next Up ──

let private friendPill (friend: FriendRef) =
    Html.span [
        prop.className "inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs bg-secondary/15 text-secondary/80"
        prop.children [
            match friend.ImageRef with
            | Some imgRef ->
                Html.img [
                    prop.src $"/images/{imgRef}"
                    prop.alt friend.Name
                    prop.className "w-3.5 h-3.5 rounded-full object-cover"
                ]
            | None -> ()
            Html.span [ prop.text friend.Name ]
        ]
    ]

let private seriesNextUpItem (item: DashboardSeriesNextUp) =
    Html.a [
        prop.href (Router.format ("series", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", item.Slug)
        )
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            PosterCard.thumbnail item.PosterRef item.Name
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.div [
                        prop.className "flex items-center gap-1.5"
                        prop.children [
                            if item.InFocus then
                                Html.span [
                                    prop.className "text-warning/70 flex-shrink-0"
                                    prop.children [ Icons.crosshairSmFilled () ]
                                ]
                            Html.p [
                                prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                prop.text item.Name
                            ]
                            if item.IsFinished then
                                Html.span [
                                    prop.className "inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-success/15 text-success flex-shrink-0"
                                    prop.text "Finished"
                                ]
                            if item.IsAbandoned then
                                Html.span [
                                    prop.className "inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-error/15 text-error flex-shrink-0"
                                    prop.text "Abandoned"
                                ]
                        ]
                    ]
                    if item.NextUpSeason > 0 then
                        Html.p [
                            prop.className "text-xs text-base-content/50"
                            prop.text $"S{item.NextUpSeason}E{item.NextUpEpisode}: {item.NextUpTitle}"
                        ]
                    if not (List.isEmpty item.WatchWithFriends) then
                        Html.div [
                            prop.className "flex items-center gap-1 mt-0.5 flex-wrap"
                            prop.children [
                                for friend in item.WatchWithFriends do
                                    friendPill friend
                            ]
                        ]
                ]
            ]
        ]
    ]

let private seriesNextUpSection (items: DashboardSeriesNextUp list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCard Icons.tv "Next Up" [
            for item in items do
                seriesNextUpItem item
        ]

// ── Movies: In Focus ──

let private movieInFocusItem (item: DashboardMovieInFocus) =
    Html.a [
        prop.href (Router.format ("movies", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("movies", item.Slug)
        )
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            PosterCard.thumbnail item.PosterRef item.Name
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.div [
                        prop.className "flex items-center gap-1.5"
                        prop.children [
                            Html.span [
                                prop.className "text-warning/70 flex-shrink-0"
                                prop.children [ Icons.crosshairSmFilled () ]
                            ]
                            Html.p [
                                prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                prop.text item.Name
                            ]
                        ]
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/50"
                        prop.text (string item.Year)
                    ]
                ]
            ]
        ]
    ]

let private moviesInFocusSection (items: DashboardMovieInFocus list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCard Icons.movie "Movies In Focus" [
            for item in items do
                movieInFocusItem item
        ]

// ── Games: In Focus ──

let private gameInFocusItem (item: DashboardGameInFocus) =
    Html.a [
        prop.href (Router.format ("games", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", item.Slug)
        )
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            PosterCard.thumbnail item.CoverRef item.Name
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.div [
                        prop.className "flex items-center gap-1.5"
                        prop.children [
                            Html.span [
                                prop.className "text-warning/70 flex-shrink-0"
                                prop.children [ Icons.crosshairSmFilled () ]
                            ]
                            Html.p [
                                prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                prop.text item.Name
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private gamesInFocusSection (items: DashboardGameInFocus list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCard Icons.gamepad "Games In Focus" [
            for item in items do
                gameInFocusItem item
        ]

// ── Games: Recently Played ──

let private gameRecentlyPlayedItem (item: DashboardGameRecentlyPlayed) =
    Html.a [
        prop.href (Router.format ("games", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", item.Slug)
        )
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            PosterCard.thumbnail item.CoverRef item.Name
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                        prop.text item.Name
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2 text-xs text-base-content/50"
                        prop.children [
                            Html.span [
                                prop.text (formatPlayTime item.TotalPlayTimeMinutes)
                            ]
                            Html.span [ prop.text "\u00B7" ]
                            Html.span [
                                prop.text (formatDate item.LastPlayedDate)
                            ]
                            match item.HltbHours with
                            | Some hltb when hltb > 0.0 ->
                                let playedHours = float item.TotalPlayTimeMinutes / 60.0
                                Html.span [ prop.text "\u00B7" ]
                                Html.span [
                                    prop.className "text-info/70"
                                    prop.text $"%.0f{playedHours}h / %.0f{hltb}h"
                                ]
                            | _ -> ()
                        ]
                    ]
                ]
            ]
        ]
    ]

let private gamesRecentlyPlayedSection (items: DashboardGameRecentlyPlayed list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCard Icons.hourglass "Recently Played" [
            for item in items do
                gameRecentlyPlayedItem item
        ]

// ── All Tab ──

let private allTabView (data: DashboardAllTab) =
    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            seriesNextUpSection data.SeriesNextUp
            moviesInFocusSection data.MoviesInFocus
            gamesInFocusSection data.GamesInFocus
            gamesRecentlyPlayedSection data.GamesRecentlyPlayed
        ]
    ]

// ── Placeholder tab ──

let private placeholderTab (label: string) =
    Html.div [
        prop.className (DesignSystem.glassCard + " p-8 text-center " + DesignSystem.animateFadeIn)
        prop.children [
            Html.p [
                prop.className "text-base-content/40 text-sm font-medium"
                prop.text $"{label} tab coming soon."
            ]
        ]
    ]

// ── Loading spinner ──

let private loadingView =
    Html.div [
        prop.className "flex items-center justify-center py-16"
        prop.children [
            Html.span [
                prop.className "loading loading-spinner loading-lg text-primary"
            ]
        ]
    ]

// ── Main view ──

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className DesignSystem.animateFadeIn
        prop.children [
            Html.div [
                prop.className DesignSystem.pageContainer
                prop.children [
                    // Page title
                    Html.h1 [
                        prop.className (DesignSystem.pageTitle + " mb-4")
                        prop.text "Dashboard"
                    ]

                    // Tab bar
                    Html.div [
                        prop.className "mb-6"
                        prop.children [ tabBar model.ActiveTab dispatch ]
                    ]

                    // Tab content
                    if model.IsLoading then
                        loadingView
                    else
                        match model.ActiveTab with
                        | All ->
                            match model.AllTabData with
                            | Some data -> allTabView data
                            | None -> loadingView
                        | MoviesTab ->
                            placeholderTab "Movies"
                        | SeriesTab ->
                            placeholderTab "TV Series"
                        | GamesTab ->
                            placeholderTab "Games"
                ]
            ]
        ]
    ]
