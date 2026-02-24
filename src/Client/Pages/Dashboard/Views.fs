module Mediatheca.Client.Pages.Dashboard.Views

open Feliz
open Feliz.Router
open Mediatheca.Client.Pages.Dashboard.Types
open Mediatheca.Shared
open Mediatheca.Client
open Mediatheca.Client.Components

// ── Jellyfin ──

let private jellyfinPlayUrl (serverUrl: string) (itemId: string) =
    $"{serverUrl.TrimEnd('/')}/web/index.html#!/details?id={itemId}"

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
        match dt.LocalDateTime.DayOfWeek with
        | System.DayOfWeek.Monday -> "Mo"
        | System.DayOfWeek.Tuesday -> "Tu"
        | System.DayOfWeek.Wednesday -> "We"
        | System.DayOfWeek.Thursday -> "Th"
        | System.DayOfWeek.Friday -> "Fr"
        | System.DayOfWeek.Saturday -> "Sa"
        | System.DayOfWeek.Sunday -> "Su"
        | _ -> ""
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

// ── Section: Open (title + content, no card chrome) ──

let private sectionOpen (icon: unit -> ReactElement) (title: string) (children: ReactElement list) =
    Html.div [
        prop.className ("section-open " + DesignSystem.animateFadeInUp)
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

let private seriesPosterCard (jellyfinServerUrl: string option) (item: DashboardSeriesNextUp) =
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

                            // Jellyfin play button overlay (bottom-right)
                            match jellyfinServerUrl, item.JellyfinEpisodeId with
                            | Some serverUrl, Some episodeId ->
                                Html.a [
                                    prop.href (jellyfinPlayUrl serverUrl episodeId)
                                    prop.target "_blank"
                                    prop.rel "noopener noreferrer"
                                    prop.onClick (fun e -> e.stopPropagation())
                                    prop.className "absolute bottom-2 right-2 z-10 flex items-center justify-center w-8 h-8 rounded-full bg-base-100/55 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 text-primary hover:bg-primary hover:text-primary-content transition-all shadow-lg opacity-0 group-hover:opacity-100 cursor-pointer"
                                    prop.title "Play in Jellyfin"
                                    prop.children [
                                        Html.span [
                                            prop.className "w-4 h-4"
                                            prop.children [ Icons.play () ]
                                        ]
                                    ]
                                ]
                            | _ -> ()

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
                    if item.IsAbandoned then
                        Html.p [
                            prop.className "text-xs text-error font-medium"
                            prop.text "Abandoned"
                        ]
                    elif item.IsFinished then
                        Html.p [
                            prop.className "text-xs text-success font-medium"
                            prop.text "Finished"
                        ]
                    elif item.NextUpSeason > 0 then
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

let private seriesNextUpScroller (jellyfinServerUrl: string option) (items: DashboardSeriesNextUp list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCardOverflow Icons.tv "Next Up" [
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    for item in items do
                        seriesPosterCard jellyfinServerUrl item
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

let private movieInFocusPosterCard (jellyfinServerUrl: string option) (item: DashboardMovieInFocus) =
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

                            // Jellyfin play button overlay (bottom-right)
                            match jellyfinServerUrl, item.JellyfinId with
                            | Some serverUrl, Some jellyfinId ->
                                Html.a [
                                    prop.href (jellyfinPlayUrl serverUrl jellyfinId)
                                    prop.target "_blank"
                                    prop.rel "noopener noreferrer"
                                    prop.onClick (fun e -> e.stopPropagation())
                                    prop.className "absolute bottom-2 right-2 z-10 flex items-center justify-center w-8 h-8 rounded-full bg-base-100/55 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 text-primary hover:bg-primary hover:text-primary-content transition-all shadow-lg opacity-0 group-hover:opacity-100 cursor-pointer"
                                    prop.title "Play in Jellyfin"
                                    prop.children [
                                        Html.span [
                                            prop.className "w-4 h-4"
                                            prop.children [ Icons.play () ]
                                        ]
                                    ]
                                ]
                            | _ -> ()

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

let private moviesInFocusPosterSection (jellyfinServerUrl: string option) (items: DashboardMovieInFocus list) =
    if List.isEmpty items then
        Html.none
    else
        sectionOpen Icons.movie "Movies In Focus" [
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    for item in items do
                        movieInFocusPosterCard jellyfinServerUrl item
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

    // Group sessions by date
    let byDate =
        sessions
        |> List.groupBy (fun s -> s.Date)
        |> Map.ofList

    // Generate all 14 days (today - 13 days through today)
    let today = System.DateTimeOffset.Now.Date
    let days =
        [ for i in 13 .. -1 .. 0 do
            let date = today.AddDays(float -i)
            let dateStr = date.ToString("yyyy-MM-dd")
            match byDate |> Map.tryFind dateStr with
            | Some daySessions ->
                let segments =
                    daySessions
                    |> List.map (fun s ->
                        {| GameSlug = s.GameSlug
                           GameName = s.GameName
                           Minutes = s.MinutesPlayed
                           ColorIndex = gameColorMap |> Map.tryFind s.GameSlug |> Option.defaultValue 0 |})
                { Date = dateStr
                  Segments = segments
                  TotalMinutes = segments |> List.sumBy (fun s -> s.Minutes) }
            | None ->
                { Date = dateStr
                  Segments = []
                  TotalMinutes = 0 }
        ]

    let maxMinutes =
        if List.isEmpty days then 1
        else days |> List.map (fun d -> d.TotalMinutes) |> List.max |> max 1

    days, maxMinutes, games, gameColorMap

let private playSessionChartArea (sessions: DashboardPlaySession list) =
    if List.isEmpty sessions then
        Html.div [
            prop.className "flex items-center justify-center py-8 text-base-content/40 text-sm"
            prop.text "No play sessions in the last 14 days"
        ]
    else
        let days, maxMinutes, _games, _gameColorMap = buildChartData sessions

        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
                // Y-axis max label
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (formatPlayTime maxMinutes)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                // Chart area with bars
                Html.div [
                    prop.className "flex items-end gap-1 h-[140px] px-1"
                    prop.children [
                        for day in days do
                            let heightPct =
                                if day.TotalMinutes = 0 then 0.0
                                else float day.TotalMinutes / float maxMinutes * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    // Tooltip on hover (only if there's data)
                                    if day.TotalMinutes > 0 then
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
                                    // Stacked bar (or empty placeholder)
                                    if day.TotalMinutes > 0 then
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
                                    else
                                        // Empty bar placeholder
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    // Weekday label
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text (formatDayOfWeek day.Date)
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

/// Bar chart without legend (used on All tab where posters serve as legend)
let private playSessionBarChartNoLegend (sessions: DashboardPlaySession list) =
    playSessionChartArea sessions

/// Bar chart with legend (used on Games tab)
let private playSessionBarChart (sessions: DashboardPlaySession list) =
    if List.isEmpty sessions then
        playSessionChartArea sessions
    else
        let _days, _maxMinutes, games, gameColorMap = buildChartData sessions
        Html.div [
            prop.className "flex flex-col gap-3"
            prop.children [
                playSessionChartArea sessions
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

let private gamePosterFromSession (slug: string) (name: string) (coverRef: string option) (colorClass: string) =
    Html.a [
        prop.href (Router.format ("games", slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", slug)
        )
        prop.className "flex-shrink-0 w-[120px] sm:w-[130px] cursor-pointer group snap-start"
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " relative w-full")
                prop.children [
                    Html.div [
                        prop.className (DesignSystem.posterImageContainer + " poster-shadow")
                        prop.children [
                            match coverRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt name
                                    prop.className DesignSystem.posterImage
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center w-full h-full text-base-content/20 px-3 gap-2"
                                    prop.children [
                                        Icons.gamepad ()
                                        Html.p [
                                            prop.className "text-xs text-base-content/40 font-medium text-center line-clamp-2"
                                            prop.text name
                                        ]
                                    ]
                                ]
                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                ]
            ]
            Html.p [
                prop.className ("mt-2 px-0.5 text-sm font-semibold truncate group-hover:text-primary transition-colors " + colorClass)
                prop.text name
            ]
        ]
    ]

let private gamesRecentlyPlayedChart (sessions: DashboardPlaySession list) =
    let uniqueGames =
        sessions
        |> List.map (fun s -> s.GameSlug, (s.GameName, s.CoverRef))
        |> List.distinctBy fst

    let gameColorMap =
        uniqueGames
        |> List.mapi (fun i (slug, _) -> slug, i % chartColors.Length)
        |> Map.ofList

    sectionCardOverflow Icons.hourglass "Recently Played" [
        // Game poster row
        if not (List.isEmpty uniqueGames) then
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2 mb-3 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    for (slug, (name, coverRef)) in uniqueGames do
                        let colorIdx = gameColorMap |> Map.tryFind slug |> Option.defaultValue 0
                        gamePosterFromSession slug name coverRef chartColorTextClasses.[colorIdx]
                ]
            ]
        // Bar chart below (without legend)
        playSessionBarChartNoLegend sessions
    ]

// ── Hero Episode Spotlight (top of left column) ──

let private heroSpotlight (jellyfinServerUrl: string option) (item: DashboardSeriesNextUp) =
    let imageRef =
        match item.EpisodeStillRef with
        | Some stillRef -> Some stillRef
        | None -> item.BackdropRef
    Html.a [
        prop.href (Router.format ("series", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", item.Slug)
        )
        prop.className ("hero-spotlight relative w-full rounded-xl overflow-hidden cursor-pointer group " + DesignSystem.animateFadeInUp)
        prop.children [
            // Background image
            Html.div [
                prop.className "relative w-full aspect-[21/9] sm:aspect-[2.5/1]"
                prop.children [
                    match imageRef with
                    | Some ref ->
                        Html.img [
                            prop.src $"/images/{ref}"
                            prop.alt item.Name
                            prop.className "absolute inset-0 w-full h-full object-cover transition-transform duration-500 group-hover:scale-105"
                        ]
                    | None ->
                        Html.div [
                            prop.className "absolute inset-0 bg-gradient-to-br from-primary/20 to-base-300"
                        ]

                    // Gradient overlay fading to dark at bottom
                    Html.div [
                        prop.className "absolute inset-0 bg-gradient-to-t from-black/90 via-black/40 to-transparent"
                    ]

                    // Content overlay at bottom
                    Html.div [
                        prop.className "absolute bottom-0 left-0 right-0 p-4 sm:p-6"
                        prop.children [
                            // Series title
                            Html.h2 [
                                prop.className "text-xl sm:text-2xl font-display uppercase tracking-wider text-white/95 group-hover:text-primary transition-colors"
                                prop.text item.Name
                            ]
                            // Episode label
                            if item.NextUpSeason > 0 then
                                Html.p [
                                    prop.className "text-sm text-white/70 mt-1 font-medium"
                                    prop.text $"S{item.NextUpSeason}E{item.NextUpEpisode}: {item.NextUpTitle}"
                                ]
                            // Episode overview
                            match item.EpisodeOverview with
                            | Some overview when not (System.String.IsNullOrWhiteSpace overview) ->
                                Html.p [
                                    prop.className "text-sm text-white/55 mt-2 line-clamp-2 sm:line-clamp-3 max-w-[600px]"
                                    prop.text overview
                                ]
                            | _ -> ()
                            // Friend pills
                            if not (List.isEmpty item.WatchWithFriends) then
                                Html.div [
                                    prop.className "flex items-center gap-1 mt-2 flex-wrap"
                                    prop.children [
                                        for friend in item.WatchWithFriends do
                                            friendPill friend
                                    ]
                                ]
                        ]
                    ]

                    // Jellyfin play button (top-right)
                    match jellyfinServerUrl, item.JellyfinEpisodeId with
                    | Some serverUrl, Some episodeId ->
                        Html.a [
                            prop.href (jellyfinPlayUrl serverUrl episodeId)
                            prop.target "_blank"
                            prop.rel "noopener noreferrer"
                            prop.onClick (fun e -> e.stopPropagation())
                            prop.className "absolute top-3 right-3 z-10 flex items-center justify-center w-10 h-10 rounded-full bg-base-100/55 backdrop-blur-[24px] backdrop-saturate-[1.2] border border-base-content/15 text-primary hover:bg-primary hover:text-primary-content transition-all shadow-lg cursor-pointer"
                            prop.title "Play in Jellyfin"
                            prop.children [ Icons.play () ]
                        ]
                    | _ -> ()

                    // In Focus glow indicator
                    if item.InFocus then
                        Html.div [
                            prop.className "absolute top-3 left-3 z-10"
                            prop.children [
                                Html.span [
                                    prop.className "flex items-center justify-center w-7 h-7 rounded-full bg-warning/90 text-warning-content shadow-md"
                                    prop.children [ Icons.crosshairSmFilled () ]
                                ]
                            ]
                        ]
                ]
            ]
        ]
    ]

// ── Next Up — Open section scroller (All tab, below hero) ──

let private seriesNextUpOpenScroller (jellyfinServerUrl: string option) (items: DashboardSeriesNextUp list) =
    if List.isEmpty items then
        Html.none
    else
        sectionOpen Icons.tv "Next Up" [
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    for item in items do
                        seriesPosterCard jellyfinServerUrl item
                ]
            ]
        ]

// ── Games: In Focus — Poster Cards (restyle) ──

let private gameInFocusPosterCard (item: DashboardGameInFocus) =
    Html.a [
        prop.href (Router.format ("games", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", item.Slug)
        )
        prop.className "cursor-pointer group"
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " relative w-full")
                prop.children [
                    Html.div [
                        prop.className (DesignSystem.posterImageContainer + " poster-shadow")
                        prop.children [
                            match item.CoverRef with
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
                                        Icons.gamepad ()
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
                ]
            ]
        ]
    ]

let private gamesInFocusPosterSection (items: DashboardGameInFocus list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCard Icons.gamepad "Games In Focus" [
            Html.div [
                prop.className "grid grid-cols-2 sm:grid-cols-3 gap-3"
                prop.children [
                    for item in items do
                        gameInFocusPosterCard item
                ]
            ]
        ]

// ── Summary Stats for Recently Played ──

let private playSessionSummaryStats (sessions: DashboardPlaySession list) =
    let totalMinutes = sessions |> List.sumBy (fun s -> s.MinutesPlayed)
    let sessionCount = sessions |> List.length
    Html.div [
        prop.className "flex items-center gap-4 mt-3 pt-3 border-t border-base-content/10"
        prop.children [
            Html.div [
                prop.className "flex items-center gap-1.5 text-sm text-base-content/60"
                prop.children [
                    Html.span [
                        prop.className "text-primary/70"
                        prop.children [ Icons.hourglass () ]
                    ]
                    Html.span [
                        prop.className "font-medium"
                        prop.text (formatPlayTime totalMinutes)
                    ]
                    Html.span [
                        prop.text "played"
                    ]
                ]
            ]
            Html.div [
                prop.className "flex items-center gap-1.5 text-sm text-base-content/60"
                prop.children [
                    Html.span [
                        prop.className "text-primary/70"
                        prop.children [ Icons.gamepad () ]
                    ]
                    Html.span [
                        prop.className "font-medium"
                        prop.text (string sessionCount)
                    ]
                    Html.span [
                        prop.text "sessions"
                    ]
                ]
            ]
        ]
    ]

// ── Recently Played chart with summary stats ──

let private gamesRecentlyPlayedChartWithStats (sessions: DashboardPlaySession list) =
    let uniqueGames =
        sessions
        |> List.map (fun s -> s.GameSlug, (s.GameName, s.CoverRef))
        |> List.distinctBy fst

    let gameColorMap =
        uniqueGames
        |> List.mapi (fun i (slug, _) -> slug, i % chartColors.Length)
        |> Map.ofList

    sectionCardOverflow Icons.hourglass "Recently Played" [
        // Game poster row
        if not (List.isEmpty uniqueGames) then
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2 mb-3 snap-x snap-mandatory scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    for (slug, (name, coverRef)) in uniqueGames do
                        let colorIdx = gameColorMap |> Map.tryFind slug |> Option.defaultValue 0
                        gamePosterFromSession slug name coverRef chartColorTextClasses.[colorIdx]
                ]
            ]
        // Bar chart below (without legend)
        playSessionBarChartNoLegend sessions
        // Summary stats
        playSessionSummaryStats sessions
    ]

// ── New Games Card ──

let private newGameItem (item: DashboardNewGame) =
    Html.a [
        prop.href (Router.format ("games", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", item.Slug)
        )
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            // Small poster
            PosterCard.thumbnail item.CoverRef item.Name
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                        prop.text item.Name
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2 flex-wrap"
                        prop.children [
                            if not (System.String.IsNullOrWhiteSpace item.AddedDate) then
                                Html.span [
                                    prop.className "text-xs text-base-content/50"
                                    prop.text (formatDate item.AddedDate)
                                ]
                            // Family owner badges
                            if not (List.isEmpty item.FamilyOwners) then
                                for owner in item.FamilyOwners do
                                    Html.span [
                                        prop.className "inline-flex items-center gap-1 px-1.5 py-0.5 rounded-full text-[10px] bg-info/15 text-info/80"
                                        prop.children [
                                            match owner.ImageRef with
                                            | Some imgRef ->
                                                Html.img [
                                                    prop.src $"/images/{imgRef}"
                                                    prop.alt owner.Name
                                                    prop.className "w-3 h-3 rounded-full object-cover"
                                                ]
                                            | None -> ()
                                            Html.span [ prop.text owner.Name ]
                                        ]
                                    ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private newGamesSection (items: DashboardNewGame list) =
    if List.isEmpty items then
        Html.none
    else
        sectionCard Icons.gamepad "New Games" [
            for item in items do
                newGameItem item
        ]

// ── Steam Achievements Card ──

let private achievementItem (achievement: SteamAchievement) =
    Html.div [
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/30 transition-colors"
        prop.children [
            // Achievement icon
            match achievement.IconUrl with
            | Some iconUrl ->
                Html.img [
                    prop.src iconUrl
                    prop.alt achievement.AchievementName
                    prop.className "w-10 h-10 rounded-lg object-cover flex-shrink-0"
                ]
            | None ->
                Html.div [
                    prop.className "w-10 h-10 rounded-lg bg-base-content/10 flex items-center justify-center flex-shrink-0"
                    prop.children [
                        Html.span [
                            prop.className "text-warning/60"
                            prop.children [ Icons.trophy () ]
                        ]
                    ]
                ]
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate"
                        prop.text achievement.AchievementName
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/50 truncate"
                        prop.text achievement.GameName
                    ]
                    if not (System.String.IsNullOrWhiteSpace achievement.UnlockTime) then
                        Html.p [
                            prop.className "text-[10px] text-base-content/40"
                            prop.text (formatDate achievement.UnlockTime)
                        ]
                ]
            ]
        ]
    ]

let private achievementsSection (state: AchievementsState) =
    sectionCard Icons.trophy "Recent Achievements" [
        match state with
        | AchievementsNotLoaded | AchievementsLoading ->
            Html.div [
                prop.className "flex items-center justify-center py-6"
                prop.children [
                    Html.span [
                        prop.className "loading loading-spinner loading-md text-primary"
                    ]
                ]
            ]
        | AchievementsError msg ->
            Html.div [
                prop.className "flex items-center gap-2 py-4 px-2 text-sm text-base-content/50"
                prop.children [
                    Html.span [
                        prop.className "text-warning/60"
                        prop.children [ Icons.questionCircle () ]
                    ]
                    Html.span [ prop.text msg ]
                ]
            ]
        | AchievementsReady achievements ->
            if List.isEmpty achievements then
                Html.div [
                    prop.className "py-4 text-center text-sm text-base-content/40"
                    prop.text "No recent achievements"
                ]
            else
                Html.div [
                    prop.children [
                        for achievement in achievements do
                            achievementItem achievement
                    ]
                ]
    ]

// ── All Tab — 2-Column Grid Layout ──

let private allTabView (data: DashboardAllTab) =
    // Pick the first active (non-finished, non-abandoned) series for the hero spotlight
    let heroItem =
        data.SeriesNextUp
        |> List.tryFind (fun s -> not s.IsFinished && not s.IsAbandoned && s.NextUpSeason > 0)
    // Remaining series for the Next Up scroller (skip the hero item)
    let nextUpItems =
        match heroItem with
        | Some hero ->
            data.SeriesNextUp |> List.filter (fun s -> s.Slug <> hero.Slug)
        | None -> data.SeriesNextUp

    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            // 2-column grid on desktop
            Html.div [
                prop.className "grid grid-cols-1 lg:grid-cols-3 gap-4"
                prop.children [
                    // Left column (2/3)
                    Html.div [
                        prop.className "lg:col-span-2 flex flex-col gap-4"
                        prop.children [
                            // Hero Episode Spotlight
                            match heroItem with
                            | Some item -> heroSpotlight data.JellyfinServerUrl item
                            | None -> ()

                            // Next Up — open section (no card chrome)
                            seriesNextUpOpenScroller data.JellyfinServerUrl nextUpItems

                            // Movies In Focus
                            moviesInFocusPosterSection data.JellyfinServerUrl data.MoviesInFocus
                        ]
                    ]

                    // Right column (1/3)
                    Html.div [
                        prop.className "lg:col-span-1 flex flex-col gap-4"
                        prop.children [
                            // Recently Played bar chart with summary stats
                            gamesRecentlyPlayedChartWithStats data.PlaySessions

                            // Games In Focus — poster cards
                            gamesInFocusPosterSection data.GamesInFocus

                            // New Games
                            newGamesSection data.NewGames
                        ]
                    ]
                ]
            ]
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

let private gamesTabView (data: DashboardGamesTab) (achievementsState: AchievementsState) =
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

            // Steam Achievements
            achievementsSection achievementsState
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
                            | Some data -> gamesTabView data model.Achievements
                            | None -> loadingView
                ]
            ]
        ]
    ]
