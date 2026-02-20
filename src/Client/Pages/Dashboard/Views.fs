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

let private formatShortDate (dateStr: string) =
    try
        let dt = System.DateTimeOffset.Parse(dateStr)
        dt.LocalDateTime.ToString("dd")
    with _ -> dateStr

let private formatDayOfWeek (dateStr: string) =
    try
        let dt = System.DateTimeOffset.Parse(dateStr)
        dt.LocalDateTime.ToString("ddd")
    with _ -> ""

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

/// Section card that allows overflow (for horizontal scrollers)
let private sectionCardOverflow (icon: unit -> ReactElement) (title: string) (children: ReactElement list) =
    Html.div [
        prop.className (DesignSystem.glassCard + " p-4 " + DesignSystem.animateFadeInUp + " overflow-hidden")
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
                prop.children children
            ]
        ]
    ]

// ── TV Series: Next Up (list row — used by Series tab) ──

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

// ── TV Series: Next Up — Poster Card Scroller (All tab) ──

let private seriesPosterCard (item: DashboardSeriesNextUp) =
    Html.a [
        prop.href (Router.format ("series", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", item.Slug)
        )
        prop.className "flex-shrink-0 w-[140px] sm:w-[150px] cursor-pointer group snap-start"
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " relative w-full")
                prop.children [
                    Html.div [
                        prop.className (DesignSystem.posterImageContainer + " " + "poster-shadow")
                        prop.children [
                            match item.PosterRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt item.Name
                                    prop.className DesignSystem.posterImage
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center w-full h-full text-base-content/20 px-3 gap-2"
                                    prop.children [
                                        Icons.tv ()
                                        Html.p [
                                            prop.className "text-xs text-base-content/40 font-medium text-center line-clamp-2"
                                            prop.text item.Name
                                        ]
                                    ]
                                ]

                            // In Focus glow indicator
                            if item.InFocus then
                                Html.div [
                                    prop.className "absolute top-1.5 left-1.5 z-10"
                                    prop.children [
                                        Html.span [
                                            prop.className "flex items-center justify-center w-6 h-6 rounded-full bg-warning/90 text-warning-content shadow-md"
                                            prop.children [ Icons.crosshairSmFilled () ]
                                        ]
                                    ]
                                ]

                            // Finished / Abandoned badge
                            if item.IsFinished then
                                Html.div [
                                    prop.className "absolute top-1.5 right-1.5 z-10"
                                    prop.children [
                                        Html.span [
                                            prop.className "inline-flex px-1.5 py-0.5 rounded text-[10px] font-bold bg-success/90 text-success-content shadow-md"
                                            prop.text "DONE"
                                        ]
                                    ]
                                ]
                            if item.IsAbandoned then
                                Html.div [
                                    prop.className "absolute top-1.5 right-1.5 z-10"
                                    prop.children [
                                        Html.span [
                                            prop.className "inline-flex px-1.5 py-0.5 rounded text-[10px] font-bold bg-error/90 text-error-content shadow-md"
                                            prop.text "DROP"
                                        ]
                                    ]
                                ]

                            // Shine effect
                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                ]
            ]
            // Text below poster
            Html.div [
                prop.className "mt-2 px-0.5"
                prop.children [
                    Html.p [
                        prop.className "text-sm font-semibold truncate group-hover:text-primary transition-colors"
                        prop.text item.Name
                    ]
                    if item.NextUpSeason > 0 then
                        Html.p [
                            prop.className "text-xs text-base-content/50 truncate"
                            prop.text $"S{item.NextUpSeason}E{item.NextUpEpisode}"
                        ]
                    if not (List.isEmpty item.WatchWithFriends) then
                        Html.div [
                            prop.className "flex items-center gap-1 mt-1 flex-wrap"
                            prop.children [
                                for friend in item.WatchWithFriends do
                                    friendPill friend
                            ]
                        ]
                ]
            ]
        ]
    ]

let private seriesNextUpScroller (items: DashboardSeriesNextUp list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCardOverflow Icons.tv "Next Up" [
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    for item in items do
                        seriesPosterCard item
                ]
            ]
        ]

/// List-row version for the Series tab (unchanged)
let private seriesNextUpSection (items: DashboardSeriesNextUp list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCard Icons.tv "Next Up" [
            for item in items do
                seriesNextUpItem item
        ]

// ── Movies: In Focus — Poster Cards (All tab) ──

let private movieInFocusPosterCard (item: DashboardMovieInFocus) =
    Html.a [
        prop.href (Router.format ("movies", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("movies", item.Slug)
        )
        prop.className "flex-shrink-0 w-[120px] sm:w-[130px] cursor-pointer group snap-start"
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " relative w-full")
                prop.children [
                    Html.div [
                        prop.className (DesignSystem.posterImageContainer + " poster-shadow")
                        prop.children [
                            match item.PosterRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt item.Name
                                    prop.className DesignSystem.posterImage
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center w-full h-full text-base-content/20 px-3 gap-2"
                                    prop.children [
                                        Icons.movie ()
                                        Html.p [
                                            prop.className "text-xs text-base-content/40 font-medium text-center line-clamp-2"
                                            prop.text item.Name
                                        ]
                                    ]
                                ]

                            // Crosshair badge
                            Html.div [
                                prop.className "absolute top-1.5 left-1.5 z-10"
                                prop.children [
                                    Html.span [
                                        prop.className "flex items-center justify-center w-6 h-6 rounded-full bg-warning/90 text-warning-content shadow-md"
                                        prop.children [ Icons.crosshairSmFilled () ]
                                    ]
                                ]
                            ]

                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                ]
            ]
            Html.div [
                prop.className "mt-2 px-0.5"
                prop.children [
                    Html.p [
                        prop.className "text-sm font-semibold truncate group-hover:text-primary transition-colors"
                        prop.text item.Name
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/50"
                        prop.text (string item.Year)
                    ]
                ]
            ]
        ]
    ]

let private moviesInFocusPosterSection (items: DashboardMovieInFocus list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCardOverflow Icons.movie "Movies In Focus" [
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    for item in items do
                        movieInFocusPosterCard item
                ]
            ]
        ]

// ── Movies: In Focus (list row — kept for reference/fallback) ──

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

// ── Games: Recently Played (list row — used by Games tab) ──

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

// ── Stacked Bar Chart — Play Sessions (All tab) ──

/// Chart color palette -- 8 distinct colors from the theme
let private chartColors = [|
    "oklch(86.133% 0.141 139.549)"   // primary (green)
    "oklch(86.078% 0.142 206.182)"   // info (cyan)
    "oklch(73.375% 0.165 35.353)"    // secondary (orange)
    "oklch(74.229% 0.133 311.379)"   // accent (purple)
    "oklch(86.163% 0.142 94.818)"    // warning (yellow)
    "oklch(86.171% 0.142 166.534)"   // success (teal)
    "oklch(82.418% 0.099 33.756)"    // error (salmon)
    "oklch(70% 0.12 260)"            // custom blue
|]

/// Tailwind-compatible color classes for backgrounds
let private chartColorClasses = [|
    "bg-primary"
    "bg-info"
    "bg-secondary"
    "bg-accent"
    "bg-warning"
    "bg-success"
    "bg-error"
    "bg-[oklch(70%_0.12_260)]"
|]

let private chartColorTextClasses = [|
    "text-primary"
    "text-info"
    "text-secondary"
    "text-accent"
    "text-warning"
    "text-success"
    "text-error"
    "text-[oklch(70%_0.12_260)]"
|]

type private DayData = {
    Date: string
    Segments: {| GameSlug: string; GameName: string; Minutes: int; ColorIndex: int |} list
    TotalMinutes: int
}

let private buildChartData (sessions: DashboardPlaySession list) =
    // Get unique games and assign color indices
    let games =
        sessions
        |> List.map (fun s -> s.GameSlug, s.GameName)
        |> List.distinct

    let gameColorMap =
        games
        |> List.mapi (fun i (slug, _) -> slug, i % chartColors.Length)
        |> Map.ofList

    let gameNameMap =
        games |> Map.ofList

    // Group sessions by date
    let byDate =
        sessions
        |> List.groupBy (fun s -> s.Date)
        |> List.sortBy fst

    // Build day data
    let days =
        byDate
        |> List.map (fun (date, daySessions) ->
            let segments =
                daySessions
                |> List.map (fun s ->
                    {| GameSlug = s.GameSlug
                       GameName = s.GameName
                       Minutes = s.MinutesPlayed
                       ColorIndex = gameColorMap |> Map.tryFind s.GameSlug |> Option.defaultValue 0 |})
            { Date = date
              Segments = segments
              TotalMinutes = segments |> List.sumBy (fun s -> s.Minutes) })

    let maxMinutes =
        if List.isEmpty days then 1
        else days |> List.map (fun d -> d.TotalMinutes) |> List.max |> max 1

    days, maxMinutes, games, gameColorMap

let private playSessionBarChart (sessions: DashboardPlaySession list) =
    if List.isEmpty sessions then
        Html.div [
            prop.className "flex items-center justify-center py-8 text-base-content/40 text-sm"
            prop.text "No play sessions in the last 14 days"
        ]
    else
        let days, maxMinutes, games, gameColorMap = buildChartData sessions

        Html.div [
            prop.className "flex flex-col gap-3"
            prop.children [
                // Chart area
                Html.div [
                    prop.className "flex items-end gap-1 h-[140px] px-1"
                    prop.children [
                        for day in days do
                            let heightPct = float day.TotalMinutes / float maxMinutes * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    // Tooltip on hover
                                    Html.div [
                                        prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                        prop.children [
                                            Html.div [
                                                prop.className "font-medium"
                                                prop.text (formatDate day.Date)
                                            ]
                                            Html.div [
                                                prop.className "text-base-content/60"
                                                prop.text (formatPlayTime day.TotalMinutes)
                                            ]
                                        ]
                                    ]
                                    // Stacked bar
                                    Html.div [
                                        prop.className "w-full flex flex-col-reverse rounded-t-sm overflow-hidden transition-all duration-300"
                                        prop.style [ style.height (length.percent heightPct) ]
                                        prop.children [
                                            for seg in day.Segments do
                                                let segPct = float seg.Minutes / float day.TotalMinutes * 100.0
                                                Html.div [
                                                    prop.className (chartColorClasses.[seg.ColorIndex] + " opacity-80 hover:opacity-100 transition-opacity")
                                                    prop.style [ style.height (length.percent segPct) ]
                                                    prop.title $"{seg.GameName}: {formatPlayTime seg.Minutes}"
                                                ]
                                        ]
                                    ]
                                    // Day label
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text (formatShortDate day.Date)
                                    ]
                                ]
                            ]
                    ]
                ]

                // Legend
                Html.div [
                    prop.className "flex flex-wrap gap-x-3 gap-y-1 pt-1"
                    prop.children [
                        for (slug, name) in games do
                            let colorIdx = gameColorMap |> Map.tryFind slug |> Option.defaultValue 0
                            Html.a [
                                prop.href (Router.format ("games", slug))
                                prop.onClick (fun e ->
                                    e.preventDefault()
                                    Router.navigate ("games", slug)
                                )
                                prop.className "flex items-center gap-1.5 text-xs text-base-content/60 hover:text-base-content transition-colors cursor-pointer"
                                prop.children [
                                    Html.div [
                                        prop.className (chartColorClasses.[colorIdx] + " w-2.5 h-2.5 rounded-full opacity-80")
                                    ]
                                    Html.span [ prop.text name ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

let private gamesRecentlyPlayedChart (sessions: DashboardPlaySession list) =
    sectionCard Icons.hourglass "Recently Played" [
        playSessionBarChart sessions
    ]

// ── All Tab — 2-Column Grid Layout ──

let private allTabView (data: DashboardAllTab) =
    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            // Top row: 3-column grid on desktop (2 left + 1 right)
            Html.div [
                prop.className "grid grid-cols-1 lg:grid-cols-3 gap-4"
                prop.children [
                    // Left column (2/3): TV Series Next Up poster scroller
                    Html.div [
                        prop.className "lg:col-span-2"
                        prop.children [
                            seriesNextUpScroller data.SeriesNextUp
                        ]
                    ]

                    // Right column (1/3): Games Recently Played bar chart + Games In Focus
                    Html.div [
                        prop.className "lg:col-span-1 flex flex-col gap-4"
                        prop.children [
                            gamesRecentlyPlayedChart data.PlaySessions
                            gamesInFocusSection data.GamesInFocus
                        ]
                    ]
                ]
            ]

            // Bottom row: Movies In Focus (full width, poster scroller)
            moviesInFocusPosterSection data.MoviesInFocus
        ]
    ]

// ── Movies Tab ──

let private statBadge (label: string) (value: string) =
    Html.div [
        prop.className "flex flex-col items-center px-3 py-1.5 rounded-lg bg-base-300/40"
        prop.children [
            Html.span [
                prop.className "text-lg font-display font-bold text-primary"
                prop.text value
            ]
            Html.span [
                prop.className "text-[11px] text-base-content/50 uppercase tracking-wider"
                prop.text label
            ]
        ]
    ]

let private movieStatsRow (stats: DashboardMovieStats) =
    Html.div [
        prop.className "flex gap-3 flex-wrap mb-4"
        prop.children [
            statBadge "Movies" (string stats.TotalMovies)
            statBadge "Sessions" (string stats.TotalWatchSessions)
            statBadge "Watch Time" (formatPlayTime stats.TotalWatchTimeMinutes)
        ]
    ]

let private movieRecentlyAddedItem (item: MovieListItem) =
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
                    Html.p [
                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                        prop.text item.Name
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/50"
                        prop.text (string item.Year)
                    ]
                ]
            ]
        ]
    ]

let private moviesTabView (data: DashboardMoviesTab) =
    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            movieStatsRow data.Stats
            if not (List.isEmpty data.RecentlyAdded) then
                sectionCard Icons.movie "Recently Added" [
                    for item in data.RecentlyAdded do
                        movieRecentlyAddedItem item
                ]
        ]
    ]

// ── Series Tab ──

let private seriesStatsRow (stats: DashboardSeriesStats) =
    Html.div [
        prop.className "flex gap-3 flex-wrap mb-4"
        prop.children [
            statBadge "Series" (string stats.TotalSeries)
            statBadge "Episodes" (string stats.TotalEpisodesWatched)
            statBadge "Watch Time" (formatPlayTime stats.TotalWatchTimeMinutes)
        ]
    ]

let private seriesCompactItem (item: SeriesListItem) (badge: ReactElement) =
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
                            Html.p [
                                prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                prop.text item.Name
                            ]
                            badge
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

let private seriesTabView (data: DashboardSeriesTab) =
    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            seriesStatsRow data.Stats

            // Next Up — full list (not truncated)
            if not (List.isEmpty data.NextUp) then
                sectionCard Icons.tv "Next Up" [
                    for item in data.NextUp do
                        seriesNextUpItem item
                ]

            // Recently Finished
            if not (List.isEmpty data.RecentlyFinished) then
                sectionCard Icons.trophy "Recently Finished" [
                    for item in data.RecentlyFinished do
                        seriesCompactItem item (
                            Html.span [
                                prop.className "inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-success/15 text-success flex-shrink-0"
                                prop.text "Finished"
                            ]
                        )
                ]

            // Recently Abandoned
            if not (List.isEmpty data.RecentlyAbandoned) then
                sectionCard Icons.tv "Recently Abandoned" [
                    for item in data.RecentlyAbandoned do
                        seriesCompactItem item (
                            Html.span [
                                prop.className "inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-error/15 text-error flex-shrink-0"
                                prop.text "Abandoned"
                            ]
                        )
                ]
        ]
    ]

// ── Games Tab ──

let private gameStatsRow (stats: DashboardGameStats) =
    Html.div [
        prop.className "flex gap-3 flex-wrap mb-4"
        prop.children [
            statBadge "Games" (string stats.TotalGames)
            statBadge "Play Time" (formatPlayTime stats.TotalPlayTimeMinutes)
            statBadge "Completed" (string stats.GamesCompleted)
            statBadge "In Progress" (string stats.GamesInProgress)
        ]
    ]

let private gameRecentlyAddedItem (item: GameListItem) =
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
                    Html.p [
                        prop.className "text-xs text-base-content/50"
                        prop.text (string item.Year)
                    ]
                ]
            ]
        ]
    ]

let private gamesTabView (data: DashboardGamesTab) =
    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            gameStatsRow data.Stats

            if not (List.isEmpty data.RecentlyAdded) then
                sectionCard Icons.gamepad "Recently Added" [
                    for item in data.RecentlyAdded do
                        gameRecentlyAddedItem item
                ]

            if not (List.isEmpty data.RecentlyPlayed) then
                sectionCard Icons.hourglass "Recently Played" [
                    for item in data.RecentlyPlayed do
                        gameRecentlyPlayedItem item
                ]
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
                            match model.MoviesTabData with
                            | Some data -> moviesTabView data
                            | None -> loadingView
                        | SeriesTab ->
                            match model.SeriesTabData with
                            | Some data -> seriesTabView data
                            | None -> loadingView
                        | GamesTab ->
                            match model.GamesTabData with
                            | Some data -> gamesTabView data
                            | None -> loadingView
                ]
            ]
        ]
    ]
