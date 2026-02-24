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

// ── All Tab: Cross-Media Hero Stats ──

let private formatTotalTime (totalMinutes: int) =
    if totalMinutes = 0 then "0h"
    else
        let days = totalMinutes / (60 * 24)
        let hours = (totalMinutes % (60 * 24)) / 60
        if days > 0 then
            if hours > 0 then $"{days}d {hours}h"
            else $"{days}d"
        elif hours > 0 then $"{hours}h"
        else $"{totalMinutes}m"

let private heroStatCard (label: string) (value: string) (sublabel: string) (colorClass: string) =
    Html.div [
        prop.className (DesignSystem.glassCard + " p-4 flex flex-col items-center justify-center text-center min-h-[90px]")
        prop.children [
            Html.span [
                prop.className $"text-2xl font-display font-bold {colorClass}"
                prop.text value
            ]
            Html.span [
                prop.className "text-xs text-base-content/50 uppercase tracking-wider mt-1"
                prop.text label
            ]
            if not (System.String.IsNullOrWhiteSpace sublabel) then
                Html.span [
                    prop.className "text-[10px] text-base-content/35 mt-0.5"
                    prop.text sublabel
                ]
        ]
    ]

let private crossMediaHeroStats (stats: DashboardCrossMediaStats) =
    let totalMinutes = stats.TotalMovieMinutes + stats.TotalSeriesMinutes + stats.TotalGameMinutes
    Html.div [
        prop.className (DesignSystem.glassCard + " p-4 " + DesignSystem.animateFadeInUp)
        prop.children [
            Html.div [
                prop.className "flex items-center gap-2 mb-3"
                prop.children [
                    Html.span [
                        prop.className "text-primary/70"
                        prop.children [ Icons.sparkles () ]
                    ]
                    Html.h2 [
                        prop.className "text-lg font-display uppercase tracking-wider"
                        prop.text "Media Overview"
                    ]
                ]
            ]
            // Stats grid
            Html.div [
                prop.className "grid grid-cols-2 sm:grid-cols-4 gap-3"
                prop.children [
                    // Total media time
                    heroStatCard "Total Media Time" (formatTotalTime totalMinutes) "" "text-primary"
                    // Active now
                    let activeCount = stats.ActiveSeriesCount + stats.ActiveGamesCount
                    let activeDetail =
                        let gameSuffix = if stats.ActiveGamesCount > 1 then "s" else ""
                        [ if stats.ActiveSeriesCount > 0 then $"{stats.ActiveSeriesCount} series"
                          if stats.ActiveGamesCount > 0 then $"{stats.ActiveGamesCount} game{gameSuffix}" ]
                        |> String.concat ", "
                    heroStatCard "Active Now" (string activeCount) activeDetail "text-success"
                    // This year
                    let yearItems =
                        let movieS = if stats.MoviesWatchedThisYear > 1 then "s" else ""
                        let epS = if stats.EpisodesWatchedThisYear > 1 then "s" else ""
                        [ if stats.MoviesWatchedThisYear > 0 then $"{stats.MoviesWatchedThisYear} movie{movieS}"
                          if stats.EpisodesWatchedThisYear > 0 then $"{stats.EpisodesWatchedThisYear} ep{epS}"
                          if stats.GamesBeatenThisYear > 0 then $"{stats.GamesBeatenThisYear} beaten" ]
                        |> String.concat ", "
                    let yearTotal = stats.MoviesWatchedThisYear + stats.EpisodesWatchedThisYear + stats.GamesBeatenThisYear
                    heroStatCard "This Year" (string yearTotal) yearItems "text-info"
                    // This month
                    let monthItems =
                        let movieS = if stats.MoviesWatchedThisMonth > 1 then "s" else ""
                        let epS = if stats.EpisodesWatchedThisMonth > 1 then "s" else ""
                        let gameS = if stats.GamesPlayedThisMonth > 1 then "s" else ""
                        [ if stats.MoviesWatchedThisMonth > 0 then $"{stats.MoviesWatchedThisMonth} movie{movieS}"
                          if stats.EpisodesWatchedThisMonth > 0 then $"{stats.EpisodesWatchedThisMonth} ep{epS}"
                          if stats.GamesPlayedThisMonth > 0 then $"{stats.GamesPlayedThisMonth} game{gameS}" ]
                        |> String.concat ", "
                    let monthTotal = stats.MoviesWatchedThisMonth + stats.EpisodesWatchedThisMonth + stats.GamesPlayedThisMonth
                    heroStatCard "This Month" (string monthTotal) monthItems "text-warning"
                ]
            ]
        ]
    ]

// ── All Tab: Weekly Activity Summary ──

let private weeklyActivitySummary (stats: DashboardCrossMediaStats) =
    let parts =
        let epS = if stats.WeekEpisodeCount > 1 then "s" else ""
        let movieS = if stats.WeekMovieCount > 1 then "s" else ""
        [ if stats.WeekEpisodeCount > 0 then
            $"{stats.WeekEpisodeCount} episode{epS}"
          if stats.WeekMovieCount > 0 then
            $"{stats.WeekMovieCount} movie{movieS}"
          if stats.WeekGameMinutes > 0 then
            $"{formatPlayTime stats.WeekGameMinutes} of gaming" ]
    if List.isEmpty parts then
        Html.div [
            prop.className ("text-sm text-base-content/40 italic " + DesignSystem.animateFadeInUp)
            prop.text "No activity this week yet"
        ]
    else
        Html.div [
            prop.className ("text-sm text-base-content/60 " + DesignSystem.animateFadeInUp)
            prop.children [
                Html.span [
                    prop.className "font-medium text-base-content/80"
                    prop.text "This week: "
                ]
                Html.span [
                    prop.text (parts |> String.concat ", ")
                ]
            ]
        ]

// ── All Tab: Activity Heatmap (GitHub-style) — standalone (no card chrome) ──

let private activityHeatmapContent (activityDays: DashboardActivityDay list) =
    let today = System.DateTimeOffset.Now.Date
    // Build a map of date string -> activity count
    let activityMap =
        activityDays
        |> List.map (fun d -> d.Date, d.MovieSessions + d.EpisodesWatched + d.GameSessions)
        |> Map.ofList
    let detailMap =
        activityDays
        |> List.map (fun d -> d.Date, d)
        |> Map.ofList

    // Calculate start date: go back ~52 weeks from today to the nearest Sunday
    let daysBack = 364
    let startDate = today.AddDays(float -daysBack)
    // Adjust start to Sunday
    let startDayOfWeek = int startDate.DayOfWeek
    let adjustedStart = startDate.AddDays(float -startDayOfWeek)

    // Generate all days from adjustedStart to today
    let totalDays = int (today - adjustedStart).TotalDays + 1
    let allDays =
        [ for i in 0 .. totalDays - 1 do
            adjustedStart.AddDays(float i) ]

    // Group by week (column) — each column is 7 days starting from Sunday
    let weeks =
        allDays
        |> List.chunkBySize 7

    let cellSize = 12
    let cellGap = 2
    let weekWidth = cellSize + cellGap
    let numWeeks = weeks.Length
    let chartWidth = numWeeks * weekWidth + 30 // 30 for day labels
    let chartHeight = 7 * (cellSize + cellGap) + 25 // 25 for month labels

    // Color levels based on activity count
    let getColorClass (count: int) =
        if count = 0 then "fill-base-content/5"
        elif count = 1 then "fill-primary/30"
        elif count <= 3 then "fill-primary/55"
        else "fill-primary/85"

    Html.div [
        prop.children [
            Html.div [
                prop.className "overflow-x-auto scrollbar-thin scrollbar-thumb-base-content/20 scrollbar-track-transparent"
                prop.children [
                    Svg.svg [
                        svg.width chartWidth
                        svg.height chartHeight
                        svg.viewBox (0, 0, chartWidth, chartHeight)
                        svg.children [
                            // Day-of-week labels
                            Svg.text [
                                svg.x 0; svg.y (1 * (cellSize + cellGap) + cellSize + 20)
                                svg.className "fill-base-content/30"
                                svg.custom ("fontSize", "9")
                                svg.text "Mon"
                            ]
                            Svg.text [
                                svg.x 0; svg.y (3 * (cellSize + cellGap) + cellSize + 20)
                                svg.className "fill-base-content/30"
                                svg.custom ("fontSize", "9")
                                svg.text "Wed"
                            ]
                            Svg.text [
                                svg.x 0; svg.y (5 * (cellSize + cellGap) + cellSize + 20)
                                svg.className "fill-base-content/30"
                                svg.custom ("fontSize", "9")
                                svg.text "Fri"
                            ]

                            // Month labels at top
                            let mutable lastMonth = -1
                            for weekIdx in 0 .. numWeeks - 1 do
                                let week = weeks.[weekIdx]
                                if not (List.isEmpty week) then
                                    let firstDay = week.[0]
                                    let month = firstDay.Month
                                    if month <> lastMonth then
                                        lastMonth <- month
                                        let monthLabel = firstDay.ToString("MMM")
                                        Svg.text [
                                            svg.x (30 + weekIdx * weekWidth)
                                            svg.y 10
                                            svg.className "fill-base-content/30"
                                            svg.custom ("fontSize", "9")
                                            svg.text monthLabel
                                        ]

                            // Heatmap cells
                            for weekIdx in 0 .. numWeeks - 1 do
                                let week = weeks.[weekIdx]
                                for dayIdx in 0 .. week.Length - 1 do
                                    let day = week.[dayIdx]
                                    let dateStr = day.ToString("yyyy-MM-dd")
                                    let count = activityMap |> Map.tryFind dateStr |> Option.defaultValue 0
                                    let colorClass = getColorClass count
                                    let x = 30 + weekIdx * weekWidth
                                    let y = 18 + dayIdx * (cellSize + cellGap)

                                    Svg.g [
                                        svg.className "group"
                                        svg.children [
                                            Svg.rect [
                                                svg.x x; svg.y y
                                                svg.width cellSize; svg.height cellSize
                                                svg.custom ("rx", "2")
                                                svg.className ($"{colorClass} hover:stroke-base-content/40 hover:stroke-1 transition-colors cursor-pointer")
                                            ]
                                            // Tooltip rendered as title element for SVG
                                            let tooltipParts =
                                                match detailMap |> Map.tryFind dateStr with
                                                | Some d ->
                                                    let epS = if d.EpisodesWatched > 1 then "s" else ""
                                                    let movieS = if d.MovieSessions > 1 then "s" else ""
                                                    let gameS = if d.GameSessions > 1 then "s" else ""
                                                    let parts =
                                                        [ if d.EpisodesWatched > 0 then $"{d.EpisodesWatched} episode{epS}"
                                                          if d.MovieSessions > 0 then $"{d.MovieSessions} movie{movieS}"
                                                          if d.GameSessions > 0 then $"{d.GameSessions} game{gameS}" ]
                                                    day.ToString("MMM d") + ": " + (if List.isEmpty parts then "No activity" else parts |> String.concat ", ")
                                                | None ->
                                                    day.ToString("MMM d") + ": No activity"
                                            Svg.title tooltipParts
                                        ]
                                    ]
                        ]
                    ]

                    // Legend
                    Html.div [
                        prop.className "flex items-center justify-end gap-1 mt-2 text-[10px] text-base-content/40"
                        prop.children [
                            Html.span [ prop.text "Less" ]
                            for level in [ "bg-base-content/5"; "bg-primary/30"; "bg-primary/55"; "bg-primary/85" ] do
                                Html.div [
                                    prop.className $"w-3 h-3 rounded-sm {level}"
                                ]
                            Html.span [ prop.text "More" ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── All Tab: Cross-Media Monthly Stacked Bar Chart (embedded, no card chrome) ──

let private monthlyBreakdownContent (breakdown: DashboardMonthlyBreakdown list) =
    if List.isEmpty breakdown then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No activity data yet"
        ]
    else
        // Fill in all 12 months
        let today = System.DateTimeOffset.Now
        let allMonths =
            [ for i in 11 .. -1 .. 0 do
                let dt = today.AddMonths(-i)
                let key = dt.ToString("yyyy-MM")
                let label = dt.ToString("MMM")
                let entry = breakdown |> List.tryFind (fun m -> m.Month = key)
                match entry with
                | Some m -> key, label, m.MovieMinutes, m.SeriesMinutes, m.GameMinutes
                | None -> key, label, 0, 0, 0 ]

        let maxMinutes =
            allMonths
            |> List.map (fun (_, _, m, s, g) -> m + s + g)
            |> List.max
            |> max 1

        let maxHours = float maxMinutes / 60.0

        // Calculate totals across all 12 months for legend labels
        let totalMovieMinutes =
            allMonths |> List.sumBy (fun (_, _, m, _, _) -> m)
        let totalSeriesMinutes =
            allMonths |> List.sumBy (fun (_, _, _, s, _) -> s)
        let totalGameMinutes =
            allMonths |> List.sumBy (fun (_, _, _, _, g) -> g)
        let formatLegendTime (minutes: int) =
            if minutes < 60 then $"{minutes}m"
            else $"{minutes / 60}h"

        Html.div [
            prop.children [
                // Legend with totals
                Html.div [
                    prop.className "flex items-center gap-4 mb-3 text-xs text-base-content/60"
                    prop.children [
                        Html.div [
                            prop.className "flex items-center gap-1.5"
                            prop.children [
                                Html.div [ prop.className "w-3 h-3 rounded-sm bg-info/80" ]
                                Html.span [ prop.text $"{formatLegendTime totalMovieMinutes} Movies" ]
                            ]
                        ]
                        Html.div [
                            prop.className "flex items-center gap-1.5"
                            prop.children [
                                Html.div [ prop.className "w-3 h-3 rounded-sm bg-secondary/80" ]
                                Html.span [ prop.text $"{formatLegendTime totalSeriesMinutes} TV Series" ]
                            ]
                        ]
                        Html.div [
                            prop.className "flex items-center gap-1.5"
                            prop.children [
                                Html.div [ prop.className "w-3 h-3 rounded-sm bg-warning/80" ]
                                Html.span [ prop.text $"{formatLegendTime totalGameMinutes} Games" ]
                            ]
                        ]
                    ]
                ]
                // Y-axis label
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (sprintf "%.0fh" maxHours)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                // Stacked bar chart
                Html.div [
                    prop.className "flex items-end gap-1 h-[140px] px-1"
                    prop.children [
                        for (_key, label, movieMin, seriesMin, gameMin) in allMonths do
                            let totalMin = movieMin + seriesMin + gameMin
                            let totalPct =
                                if totalMin = 0 then 0.0
                                else float totalMin / float maxMinutes * 100.0
                            let moviePct = if totalMin = 0 then 0.0 else float movieMin / float totalMin * 100.0
                            let seriesPct = if totalMin = 0 then 0.0 else float seriesMin / float totalMin * 100.0
                            let gamePct = if totalMin = 0 then 0.0 else float gameMin / float totalMin * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    // Tooltip
                                    if totalMin > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1.5 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                if movieMin > 0 then
                                                    Html.div [
                                                        prop.className "flex items-center gap-1"
                                                        prop.children [
                                                            Html.div [ prop.className "w-2 h-2 rounded-sm bg-info/80" ]
                                                            Html.span [ prop.text (sprintf "Movies: %s" (formatPlayTime movieMin)) ]
                                                        ]
                                                    ]
                                                if seriesMin > 0 then
                                                    Html.div [
                                                        prop.className "flex items-center gap-1"
                                                        prop.children [
                                                            Html.div [ prop.className "w-2 h-2 rounded-sm bg-secondary/80" ]
                                                            Html.span [ prop.text (sprintf "TV: %s" (formatPlayTime seriesMin)) ]
                                                        ]
                                                    ]
                                                if gameMin > 0 then
                                                    Html.div [
                                                        prop.className "flex items-center gap-1"
                                                        prop.children [
                                                            Html.div [ prop.className "w-2 h-2 rounded-sm bg-warning/80" ]
                                                            Html.span [ prop.text (sprintf "Games: %s" (formatPlayTime gameMin)) ]
                                                        ]
                                                    ]
                                            ]
                                        ]
                                    // Stacked bar segments
                                    if totalMin > 0 then
                                        Html.div [
                                            prop.className "w-full flex flex-col justify-end rounded-t-sm overflow-hidden"
                                            prop.style [ style.height (length.percent totalPct) ]
                                            prop.children [
                                                // Games (top = warning)
                                                if gameMin > 0 then
                                                    Html.div [
                                                        prop.className "w-full bg-warning/70 hover:bg-warning/90 transition-colors"
                                                        prop.style [ style.height (length.percent gamePct) ]
                                                    ]
                                                // Series (middle = secondary)
                                                if seriesMin > 0 then
                                                    Html.div [
                                                        prop.className "w-full bg-secondary/70 hover:bg-secondary/90 transition-colors"
                                                        prop.style [ style.height (length.percent seriesPct) ]
                                                    ]
                                                // Movies (bottom = info)
                                                if movieMin > 0 then
                                                    Html.div [
                                                        prop.className "w-full bg-info/70 hover:bg-info/90 transition-colors"
                                                        prop.style [ style.height (length.percent moviePct) ]
                                                    ]
                                            ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    // Month label
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text label
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── All Tab: Combined Activity Section (heatmap + monthly breakdown) ──

let private activitySection (activityDays: DashboardActivityDay list) (breakdown: DashboardMonthlyBreakdown list) =
    sectionOpen Icons.calendar "Activity" [
        Html.div [
            prop.className "grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4"
            prop.children [
                // Heatmap (left on desktop, top on mobile)
                activityHeatmapContent activityDays
                // Monthly breakdown (right on desktop, bottom on mobile)
                monthlyBreakdownContent breakdown
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
            // 1. Activity section — heatmap + monthly breakdown side by side
            activitySection data.ActivityDays data.MonthlyBreakdown

            // 2-column grid on desktop
            Html.div [
                prop.className "grid grid-cols-1 lg:grid-cols-3 gap-4"
                prop.children [
                    // Left column (2/3)
                    Html.div [
                        prop.className "lg:col-span-2 flex flex-col gap-4"
                        prop.children [
                            // 2. Hero Episode Spotlight
                            match heroItem with
                            | Some item -> heroSpotlight data.JellyfinServerUrl item
                            | None -> ()

                            // 3. Next Up — open section (no card chrome)
                            seriesNextUpOpenScroller data.JellyfinServerUrl nextUpItems

                            // 4. Movies In Focus
                            moviesInFocusPosterSection data.JellyfinServerUrl data.MoviesInFocus
                        ]
                    ]

                    // Right column (1/3)
                    Html.div [
                        prop.className "lg:col-span-1 flex flex-col gap-4"
                        prop.children [
                            // 5. Games play activity chart (existing 14-day chart)
                            gamesRecentlyPlayedChartWithStats data.PlaySessions

                            // 6. Games In Focus — poster cards
                            gamesInFocusPosterSection data.GamesInFocus

                            // 7. New Games
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
            match stats.AverageRating with
            | Some avg -> statBadge "Avg Rating" (sprintf "%.1f" avg)
            | None -> ()
            if stats.WatchlistCount > 0 then
                statBadge "Watchlist" (string stats.WatchlistCount)
        ]
    ]

// ── Ratings Distribution Bar Chart ──

let private ratingsDistributionChart (distribution: (int * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No ratings yet"
        ]
    else
        let maxCount =
            distribution |> List.map snd |> List.max |> max 1
        // Fill in all ratings 1-10 even if some are missing
        let fullDistribution =
            [ for r in 1..10 do
                let count =
                    distribution |> List.tryFind (fun (rating, _) -> rating = r)
                    |> Option.map snd |> Option.defaultValue 0
                r, count ]
        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
                // Y-axis max label
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (string maxCount)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                // Bar chart
                Html.div [
                    prop.className "flex items-end gap-1.5 h-[120px] px-1"
                    prop.children [
                        for (rating, count) in fullDistribution do
                            let heightPct =
                                if count = 0 then 0.0
                                else float count / float maxCount * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    // Tooltip on hover
                                    if count > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (sprintf "%d movie%s" count (if count = 1 then "" else "s"))
                                                ]
                                            ]
                                        ]
                                    // Bar
                                    if count > 0 then
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-primary opacity-80 hover:opacity-100 transition-all duration-300"
                                            prop.style [ style.height (length.percent heightPct) ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    // Rating label
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text (string rating)
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── Genre Breakdown Horizontal Bars ──

let private genreBreakdownBars (distribution: (string * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No genre data yet"
        ]
    else
        let maxCount =
            distribution |> List.head |> snd |> max 1
        Html.div [
            prop.className "flex flex-col gap-1.5"
            prop.children [
                for i, (genre, count) in distribution |> List.mapi (fun i x -> i, x) do
                    let widthPct = float count / float maxCount * 100.0
                    let opacity = 1.0 - (float i * 0.06)
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.span [
                                prop.className "text-xs text-base-content/70 w-20 text-right truncate flex-shrink-0"
                                prop.text genre
                            ]
                            Html.div [
                                prop.className "flex-1 h-5 rounded-sm overflow-hidden bg-base-content/5 relative"
                                prop.children [
                                    Html.div [
                                        prop.className "h-full rounded-sm bg-primary transition-all duration-500"
                                        prop.style [
                                            style.width (length.percent widthPct)
                                            style.opacity opacity
                                        ]
                                    ]
                                ]
                            ]
                            Html.span [
                                prop.className "text-xs text-base-content/50 w-6 text-right flex-shrink-0"
                                prop.text (string count)
                            ]
                        ]
                    ]
            ]
        ]

// ── Monthly Watch Activity Chart ──

let private monthlyActivityChart (activity: (string * int * int) list) =
    if List.isEmpty activity then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No watch activity yet"
        ]
    else
        let maxMovies =
            activity |> List.map (fun (_, m, _) -> m) |> List.max |> max 1
        // Fill in all 12 months
        let today = System.DateTimeOffset.Now
        let allMonths =
            [ for i in 11 .. -1 .. 0 do
                let dt = today.AddMonths(-i)
                let key = dt.ToString("yyyy-MM")
                let label = dt.ToString("MMM")
                let entry =
                    activity |> List.tryFind (fun (m, _, _) -> m = key)
                match entry with
                | Some (_, movies, minutes) -> key, label, movies, minutes
                | None -> key, label, 0, 0 ]
        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
                // Y-axis max label
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (string maxMovies)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                // Bar chart
                Html.div [
                    prop.className "flex items-end gap-1 h-[120px] px-1"
                    prop.children [
                        for (_key, label, movies, minutes) in allMonths do
                            let heightPct =
                                if movies = 0 then 0.0
                                else float movies / float maxMovies * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    // Tooltip
                                    if movies > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (sprintf "%d movie%s" movies (if movies = 1 then "" else "s"))
                                                ]
                                                Html.div [
                                                    prop.className "text-base-content/60"
                                                    prop.text (formatPlayTime minutes)
                                                ]
                                            ]
                                        ]
                                    // Bar
                                    if movies > 0 then
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-info opacity-80 hover:opacity-100 transition-all duration-300"
                                            prop.style [ style.height (length.percent heightPct) ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    // Month label
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text label
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── Person Stats (Actors / Directors) ──

let private personStatsSection (people: DashboardPersonStats list) (emptyMessage: string) =
    if List.isEmpty people then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text emptyMessage
        ]
    else
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                for person in people do
                    Html.div [
                        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/30 transition-colors"
                        prop.children [
                            // Person image or placeholder
                            match person.ImageRef with
                            | Some imageRef ->
                                Html.img [
                                    prop.src (sprintf "/images/%s" imageRef)
                                    prop.alt person.Name
                                    prop.className "w-10 h-10 rounded-full object-cover flex-shrink-0"
                                ]
                            | None ->
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300/60 flex items-center justify-center flex-shrink-0"
                                    prop.children [
                                        Html.span [
                                            prop.className "text-sm text-base-content/40 font-medium"
                                            prop.text (person.Name.Substring(0, 1).ToUpper())
                                        ]
                                    ]
                                ]
                            Html.div [
                                prop.className "flex-1 min-w-0"
                                prop.children [
                                    Html.p [
                                        prop.className "font-semibold text-sm truncate"
                                        prop.text person.Name
                                    ]
                                    Html.p [
                                        prop.className "text-xs text-base-content/50"
                                        prop.text (sprintf "%d movie%s" person.MovieCount (if person.MovieCount = 1 then "" else "s"))
                                    ]
                                ]
                            ]
                        ]
                    ]
            ]
        ]

// ── Most Watched With (Friends) ──

let private watchedWithSection (watchedWith: DashboardWatchedWithStats list) =
    if List.isEmpty watchedWith then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No shared sessions yet"
        ]
    else
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                for friend in watchedWith do
                    Html.a [
                        prop.href (Router.format ("friends", friend.Slug))
                        prop.onClick (fun e ->
                            e.preventDefault()
                            Router.navigate ("friends", friend.Slug)
                        )
                        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
                        prop.children [
                            match friend.ImageRef with
                            | Some imageRef ->
                                Html.img [
                                    prop.src (sprintf "/images/%s" imageRef)
                                    prop.alt friend.Name
                                    prop.className "w-10 h-10 rounded-full object-cover flex-shrink-0"
                                ]
                            | None ->
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300/60 flex items-center justify-center flex-shrink-0"
                                    prop.children [
                                        Html.span [
                                            prop.className "text-sm text-base-content/40 font-medium"
                                            prop.text (friend.Name.Substring(0, 1).ToUpper())
                                        ]
                                    ]
                                ]
                            Html.div [
                                prop.className "flex-1 min-w-0"
                                prop.children [
                                    Html.p [
                                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                        prop.text friend.Name
                                    ]
                                    Html.p [
                                        prop.className "text-xs text-base-content/50"
                                        prop.text (sprintf "%d session%s" friend.SessionCount (if friend.SessionCount = 1 then "" else "s"))
                                    ]
                                ]
                            ]
                        ]
                    ]
            ]
        ]

// ── Country Distribution ──

let private countryDistributionBars (distribution: (string * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No country data yet"
        ]
    else
        let maxCount =
            distribution |> List.head |> snd |> max 1
        Html.div [
            prop.className "flex flex-col gap-1.5"
            prop.children [
                for i, (country, count) in distribution |> List.mapi (fun i x -> i, x) do
                    let widthPct = float count / float maxCount * 100.0
                    let opacity = 1.0 - (float i * 0.04)
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.span [
                                prop.className "text-xs text-base-content/70 w-28 text-right truncate flex-shrink-0"
                                prop.text country
                            ]
                            Html.div [
                                prop.className "flex-1 h-5 rounded-sm overflow-hidden bg-base-content/5 relative"
                                prop.children [
                                    Html.div [
                                        prop.className "h-full rounded-sm bg-secondary transition-all duration-500"
                                        prop.style [
                                            style.width (length.percent widthPct)
                                            style.opacity opacity
                                        ]
                                    ]
                                ]
                            ]
                            Html.span [
                                prop.className "text-xs text-base-content/50 w-6 text-right flex-shrink-0"
                                prop.text (string count)
                            ]
                        ]
                    ]
            ]
        ]

// ── Recently Watched Item ──

let private movieRecentlyWatchedItem (item: DashboardRecentlyWatched) =
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
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.p [
                                prop.className "text-xs text-base-content/50"
                                prop.text (sprintf "%d" item.Year)
                            ]
                            Html.span [
                                prop.className "text-xs text-base-content/30"
                                prop.text "|"
                            ]
                            Html.p [
                                prop.className "text-xs text-base-content/50"
                                prop.text (formatDate item.WatchDate)
                            ]
                            if not (List.isEmpty item.Friends) then
                                Html.span [
                                    prop.className "text-[10px] text-base-content/30 ml-1"
                                    prop.children [
                                        Icons.friends ()
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Recently Added Item ──

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

// ── Movies Tab View ──

let private moviesTabView (data: DashboardMoviesTab) =
    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            // 1. Stats badges
            movieStatsRow data.Stats

            // 2. Ratings distribution chart
            sectionCard Icons.star "Ratings Distribution" [
                ratingsDistributionChart data.Stats.RatingDistribution
            ]

            // 3. Genre breakdown bars
            sectionCard Icons.tag "Genre Breakdown" [
                genreBreakdownBars data.Stats.GenreDistribution
            ]

            // 4. Monthly watch activity
            sectionCard Icons.calendar "Monthly Activity" [
                monthlyActivityChart data.Stats.MonthlyActivity
            ]

            // 5. Actors & Directors (side by side on desktop)
            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
                prop.children [
                    sectionCard Icons.user "Most Watched Actors" [
                        personStatsSection data.TopActors "No actor data yet"
                    ]
                    sectionCard Icons.user "Most Watched Directors" [
                        personStatsSection data.TopDirectors "No director data yet"
                    ]
                ]
            ]

            // 6. Most watched with (friends)
            if not (List.isEmpty data.TopWatchedWith) then
                sectionCard Icons.friends "Most Watched With" [
                    watchedWithSection data.TopWatchedWith
                ]

            // 7. Country distribution
            if not (List.isEmpty data.Stats.CountryDistribution) then
                sectionCard Icons.globe "Movie Origins" [
                    countryDistributionBars data.Stats.CountryDistribution
                ]

            // 8. Recently watched
            if not (List.isEmpty data.RecentlyWatched) then
                sectionCard Icons.movie "Recently Watched" [
                    for item in data.RecentlyWatched do
                        movieRecentlyWatchedItem item
                ]

            // 9. Recently added (watchlist / unwatched)
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
            if stats.CurrentlyWatching > 0 then
                statBadge "Watching" (string stats.CurrentlyWatching)
            match stats.AverageRating with
            | Some avg -> statBadge "Avg Rating" (sprintf "%.1f" avg)
            | None -> ()
            match stats.CompletionRate with
            | Some rate -> statBadge "Completed" (sprintf "%.0f%%" rate)
            | None -> ()
        ]
    ]

// ── Series Next Up Item with Progress Bar ──

let private seriesNextUpItemEnhanced (item: DashboardSeriesNextUp) =
    let progressPct =
        if item.EpisodeCount > 0 then
            float item.WatchedEpisodeCount / float item.EpisodeCount * 100.0
        else 0.0
    let timeRemaining =
        match item.AverageRuntimeMinutes with
        | Some avgRt when item.EpisodeCount > item.WatchedEpisodeCount ->
            let remaining = (item.EpisodeCount - item.WatchedEpisodeCount) * avgRt
            Some remaining
        | _ -> None
    Html.a [
        prop.href (Router.format ("series", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", item.Slug)
        )
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            // Poster with progress bar overlay at bottom
            Html.div [
                prop.className "relative flex-shrink-0"
                prop.children [
                    PosterCard.thumbnail item.PosterRef item.Name
                    // Progress bar at bottom of poster
                    if item.EpisodeCount > 0 then
                        Html.div [
                            prop.className "absolute bottom-0 left-0 right-0 h-1 bg-base-content/10 rounded-b"
                            prop.children [
                                Html.div [
                                    prop.className "h-full bg-primary rounded-b transition-all duration-500"
                                    prop.style [ style.width (length.percent progressPct) ]
                                ]
                            ]
                        ]
                ]
            ]
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
                    // Progress text and time remaining
                    Html.div [
                        prop.className "flex items-center gap-2 mt-0.5"
                        prop.children [
                            if item.EpisodeCount > 0 then
                                Html.span [
                                    prop.className "text-[11px] text-base-content/40"
                                    prop.text (sprintf "%d of %d episodes (%d%%)" item.WatchedEpisodeCount item.EpisodeCount (int progressPct))
                                ]
                            match timeRemaining with
                            | Some mins ->
                                Html.span [
                                    prop.className "text-[11px] text-base-content/30"
                                    prop.text "|"
                                ]
                                Html.span [
                                    prop.className "text-[11px] text-info/60"
                                    prop.text (sprintf "~%s remaining" (formatPlayTime mins))
                                ]
                            | None -> ()
                        ]
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

// ── Episode Activity Chart (14 days, stacked by series) ──

type private EpisodeDayData = {
    Date: string
    Segments: {| SeriesSlug: string; SeriesName: string; EpisodeCount: int; ColorIndex: int |} list
    TotalEpisodes: int
    HasBinge: bool
}

let private buildEpisodeChartData (activity: DashboardEpisodeActivity list) =
    // Get unique series and assign color indices
    let seriesList =
        activity
        |> List.map (fun a -> a.SeriesSlug, a.SeriesName)
        |> List.distinct

    let seriesColorMap =
        seriesList
        |> List.mapi (fun i (slug, _) -> slug, i % chartColors.Length)
        |> Map.ofList

    // Group by date
    let byDate =
        activity
        |> List.groupBy (fun a -> a.Date)
        |> Map.ofList

    // Generate all 14 days
    let today = System.DateTimeOffset.Now.Date
    let days =
        [ for i in 13 .. -1 .. 0 do
            let date = today.AddDays(float -i)
            let dateStr = date.ToString("yyyy-MM-dd")
            match byDate |> Map.tryFind dateStr with
            | Some dayActivity ->
                let segments =
                    dayActivity
                    |> List.map (fun a ->
                        {| SeriesSlug = a.SeriesSlug
                           SeriesName = a.SeriesName
                           EpisodeCount = a.EpisodeCount
                           ColorIndex = seriesColorMap |> Map.tryFind a.SeriesSlug |> Option.defaultValue 0 |})
                let hasBinge = dayActivity |> List.exists (fun a -> a.EpisodeCount >= 3)
                { Date = dateStr
                  Segments = segments
                  TotalEpisodes = segments |> List.sumBy (fun s -> s.EpisodeCount)
                  HasBinge = hasBinge }
            | None ->
                { Date = dateStr
                  Segments = []
                  TotalEpisodes = 0
                  HasBinge = false }
        ]

    let maxEpisodes =
        if List.isEmpty days then 1
        else days |> List.map (fun d -> d.TotalEpisodes) |> List.max |> max 1

    days, maxEpisodes, seriesList, seriesColorMap

let private episodeActivityChart (activity: DashboardEpisodeActivity list) =
    if List.isEmpty activity then
        Html.div [
            prop.className "flex items-center justify-center py-8 text-base-content/40 text-sm"
            prop.text "No episodes watched in the last 14 days"
        ]
    else
        let days, maxEpisodes, seriesList, seriesColorMap = buildEpisodeChartData activity

        Html.div [
            prop.className "flex flex-col gap-3"
            prop.children [
                Html.div [
                    prop.className "flex flex-col gap-0"
                    prop.children [
                        // Y-axis max label
                        Html.div [
                            prop.className "flex items-center gap-1 mb-0.5"
                            prop.children [
                                Html.span [
                                    prop.className "text-[10px] text-base-content/30 font-medium"
                                    prop.text (string maxEpisodes)
                                ]
                                Html.div [
                                    prop.className "flex-1 border-t border-base-content/10"
                                ]
                            ]
                        ]
                        // Chart bars
                        Html.div [
                            prop.className "flex items-end gap-1 h-[140px] px-1"
                            prop.children [
                                for day in days do
                                    let heightPct =
                                        if day.TotalEpisodes = 0 then 0.0
                                        else float day.TotalEpisodes / float maxEpisodes * 100.0
                                    Html.div [
                                        prop.className "flex-1 flex flex-col justify-end relative group"
                                        prop.style [ style.height (length.percent 100) ]
                                        prop.children [
                                            // Tooltip
                                            if day.TotalEpisodes > 0 then
                                                Html.div [
                                                    prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                                    prop.children [
                                                        Html.div [
                                                            prop.className "font-medium"
                                                            prop.text (formatDate day.Date)
                                                        ]
                                                        Html.div [
                                                            prop.className "text-base-content/60"
                                                            prop.text (sprintf "%d episode%s" day.TotalEpisodes (if day.TotalEpisodes = 1 then "" else "s"))
                                                        ]
                                                        if day.HasBinge then
                                                            Html.div [
                                                                prop.className "text-warning font-medium"
                                                                prop.text "Binge session!"
                                                            ]
                                                    ]
                                                ]
                                            // Stacked bar
                                            if day.TotalEpisodes > 0 then
                                                Html.div [
                                                    prop.className "w-full flex flex-col-reverse rounded-t-sm overflow-hidden transition-all duration-300 relative"
                                                    prop.style [ style.height (length.percent heightPct) ]
                                                    prop.children [
                                                        for seg in day.Segments do
                                                            let segPct = float seg.EpisodeCount / float day.TotalEpisodes * 100.0
                                                            Html.div [
                                                                prop.className (chartColorClasses.[seg.ColorIndex] + " opacity-80 hover:opacity-100 transition-opacity")
                                                                prop.style [ style.height (length.percent segPct) ]
                                                                prop.title $"{seg.SeriesName}: {seg.EpisodeCount} ep"
                                                            ]
                                                    ]
                                                ]
                                            else
                                                Html.div [
                                                    prop.className "w-full rounded-t-sm bg-base-content/5"
                                                    prop.style [ style.height (length.px 2) ]
                                                ]
                                            // Binge indicator
                                            if day.HasBinge then
                                                Html.div [
                                                    prop.className "absolute -top-4 left-1/2 -translate-x-1/2 px-1 py-0.5 rounded text-[8px] font-bold bg-warning/20 text-warning whitespace-nowrap"
                                                    prop.title "Binge day (3+ episodes of same series)"
                                                    prop.text "BINGE"
                                                ]
                                            // Day label
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
                // Legend
                if seriesList.Length > 1 then
                    Html.div [
                        prop.className "flex flex-wrap gap-x-3 gap-y-1 px-1"
                        prop.children [
                            for (slug, name) in seriesList do
                                let colorIdx = seriesColorMap |> Map.tryFind slug |> Option.defaultValue 0
                                Html.div [
                                    prop.className "flex items-center gap-1.5"
                                    prop.children [
                                        Html.div [
                                            prop.className (chartColorClasses.[colorIdx] + " w-2.5 h-2.5 rounded-full opacity-80")
                                        ]
                                        Html.span [
                                            prop.className "text-[11px] text-base-content/60 truncate max-w-[120px]"
                                            prop.text name
                                        ]
                                    ]
                                ]
                        ]
                    ]
            ]
        ]

// ── Series Monthly Episode Activity Chart ──

let private monthlyEpisodeActivityChart (activity: (string * int) list) =
    if List.isEmpty activity then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No episode activity yet"
        ]
    else
        let maxEpisodes =
            activity |> List.map snd |> List.max |> max 1
        // Fill in all 12 months
        let today = System.DateTimeOffset.Now
        let allMonths =
            [ for i in 11 .. -1 .. 0 do
                let dt = today.AddMonths(-i)
                let key = dt.ToString("yyyy-MM")
                let label = dt.ToString("MMM")
                let entry =
                    activity |> List.tryFind (fun (m, _) -> m = key)
                match entry with
                | Some (_, episodes) -> key, label, episodes
                | None -> key, label, 0 ]
        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
                // Y-axis max label
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (string maxEpisodes)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                // Bar chart
                Html.div [
                    prop.className "flex items-end gap-1 h-[120px] px-1"
                    prop.children [
                        for (_key, label, episodes) in allMonths do
                            let heightPct =
                                if episodes = 0 then 0.0
                                else float episodes / float maxEpisodes * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    // Tooltip
                                    if episodes > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (sprintf "%d episode%s" episodes (if episodes = 1 then "" else "s"))
                                                ]
                                            ]
                                        ]
                                    // Bar
                                    if episodes > 0 then
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-info opacity-80 hover:opacity-100 transition-all duration-300"
                                            prop.style [ style.height (length.percent heightPct) ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    // Month label
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text label
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── Series Ratings Distribution (reuses same pattern as movies) ──

let private seriesRatingsDistributionChart (distribution: (int * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No ratings yet"
        ]
    else
        let maxCount =
            distribution |> List.map snd |> List.max |> max 1
        let fullDistribution =
            [ for r in 1..10 do
                let count =
                    distribution |> List.tryFind (fun (rating, _) -> rating = r)
                    |> Option.map snd |> Option.defaultValue 0
                r, count ]
        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (string maxCount)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                Html.div [
                    prop.className "flex items-end gap-1.5 h-[120px] px-1"
                    prop.children [
                        for (rating, count) in fullDistribution do
                            let heightPct =
                                if count = 0 then 0.0
                                else float count / float maxCount * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    if count > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (sprintf "%d series" count)
                                                ]
                                            ]
                                        ]
                                    if count > 0 then
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-secondary opacity-80 hover:opacity-100 transition-all duration-300"
                                            prop.style [ style.height (length.percent heightPct) ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text (string rating)
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── Series Genre Breakdown (reuses same pattern as movies) ──

let private seriesGenreBreakdownBars (distribution: (string * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No genre data yet"
        ]
    else
        let maxCount =
            distribution |> List.head |> snd |> max 1
        Html.div [
            prop.className "flex flex-col gap-1.5"
            prop.children [
                for i, (genre, count) in distribution |> List.mapi (fun i x -> i, x) do
                    let widthPct = float count / float maxCount * 100.0
                    let opacity = 1.0 - (float i * 0.06)
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.span [
                                prop.className "text-xs text-base-content/70 w-20 text-right truncate flex-shrink-0"
                                prop.text genre
                            ]
                            Html.div [
                                prop.className "flex-1 h-5 rounded-sm overflow-hidden bg-base-content/5 relative"
                                prop.children [
                                    Html.div [
                                        prop.className "h-full rounded-sm bg-secondary transition-all duration-500"
                                        prop.style [
                                            style.width (length.percent widthPct)
                                            style.opacity opacity
                                        ]
                                    ]
                                ]
                            ]
                            Html.span [
                                prop.className "text-xs text-base-content/50 w-6 text-right flex-shrink-0"
                                prop.text (string count)
                            ]
                        ]
                    ]
            ]
        ]

// ── Series Most Watched With (Friends) ──

let private seriesWatchedWithSection (watchedWith: DashboardSeriesWatchedWith list) =
    if List.isEmpty watchedWith then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No shared rewatch sessions yet"
        ]
    else
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                for friend in watchedWith do
                    Html.a [
                        prop.href (Router.format ("friends", friend.Slug))
                        prop.onClick (fun e ->
                            e.preventDefault()
                            Router.navigate ("friends", friend.Slug)
                        )
                        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-300/50 transition-colors cursor-pointer group"
                        prop.children [
                            match friend.ImageRef with
                            | Some imageRef ->
                                Html.img [
                                    prop.src (sprintf "/images/%s" imageRef)
                                    prop.alt friend.Name
                                    prop.className "w-10 h-10 rounded-full object-cover flex-shrink-0"
                                ]
                            | None ->
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300/60 flex items-center justify-center flex-shrink-0"
                                    prop.children [
                                        Html.span [
                                            prop.className "text-sm text-base-content/40 font-medium"
                                            prop.text (friend.Name.Substring(0, 1).ToUpper())
                                        ]
                                    ]
                                ]
                            Html.div [
                                prop.className "flex-1 min-w-0"
                                prop.children [
                                    Html.p [
                                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                        prop.text friend.Name
                                    ]
                                    Html.p [
                                        prop.className "text-xs text-base-content/50"
                                        prop.text (sprintf "%d episode%s together" friend.EpisodeCount (if friend.EpisodeCount = 1 then "" else "s"))
                                    ]
                                ]
                            ]
                        ]
                    ]
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
            // 1. Stats badges
            seriesStatsRow data.Stats

            // 2. Episode activity chart (14-day)
            sectionCard Icons.chartBar "Episode Activity" [
                episodeActivityChart data.EpisodeActivity
            ]

            // 3. Next Up with progress bars and time remaining
            if not (List.isEmpty data.NextUp) then
                sectionCard Icons.tv "Next Up" [
                    for item in data.NextUp do
                        seriesNextUpItemEnhanced item
                ]

            // 4. Monthly episode activity
            sectionCard Icons.calendar "Monthly Activity" [
                monthlyEpisodeActivityChart data.Stats.MonthlyActivity
            ]

            // 5. Ratings & Genre (side by side on desktop)
            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
                prop.children [
                    sectionCard Icons.star "Ratings Distribution" [
                        seriesRatingsDistributionChart data.Stats.RatingDistribution
                    ]
                    sectionCard Icons.tag "Genre Breakdown" [
                        seriesGenreBreakdownBars data.Stats.GenreDistribution
                    ]
                ]
            ]

            // 6. Most watched with (friends)
            if not (List.isEmpty data.TopWatchedWith) then
                sectionCard Icons.friends "Most Watched With" [
                    seriesWatchedWithSection data.TopWatchedWith
                ]

            // 7. Recently Finished
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

            // 8. Recently Abandoned
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
            if stats.BacklogSize > 0 then
                statBadge "Backlog" (string stats.BacklogSize)
            match stats.CompletionRate with
            | Some rate -> statBadge "Completion" (sprintf "%.0f%%" rate)
            | None -> ()
            match stats.AverageRating with
            | Some avg -> statBadge "Avg Rating" (sprintf "%.1f" avg)
            | None -> ()
        ]
    ]

// ── Backlog Time Estimate Hero Card ──

let private backlogTimeEstimateCard (stats: DashboardGameStats) =
    if stats.BacklogGameCount = 0 then
        Html.none
    else
        let hoursDisplay =
            if stats.BacklogTimeHours >= 24.0 then
                let days = stats.BacklogTimeHours / 24.0
                sprintf "~%.0f days (~%.0f hrs)" days stats.BacklogTimeHours
            else
                sprintf "~%.0f hours" stats.BacklogTimeHours
        Html.div [
            prop.className (DesignSystem.glassCard + " p-5 " + DesignSystem.animateFadeInUp)
            prop.children [
                Html.div [
                    prop.className "flex items-center gap-2 mb-2"
                    prop.children [
                        Html.span [
                            prop.className "text-warning/70"
                            prop.children [ Icons.hourglass () ]
                        ]
                        Html.h2 [
                            prop.className "text-lg font-display uppercase tracking-wider"
                            prop.text "Backlog Estimate"
                        ]
                    ]
                ]
                Html.div [
                    prop.className "text-center py-2"
                    prop.children [
                        Html.div [
                            prop.className "text-3xl font-display font-bold text-warning"
                            prop.text hoursDisplay
                        ]
                        Html.div [
                            prop.className "text-sm text-base-content/50 mt-1"
                            prop.children [
                                Html.text (sprintf "across %d game%s" stats.BacklogGameCount (if stats.BacklogGameCount = 1 then "" else "s"))
                                if stats.BacklogGamesWithoutHltb > 0 then
                                    Html.text (sprintf " (%d without HLTB data)" stats.BacklogGamesWithoutHltb)
                            ]
                        ]
                    ]
                ]
            ]
        ]

// ── Status Distribution Chart (Stacked Horizontal Bar) ──

let private gameStatusColors (status: string) =
    match status with
    | "Backlog" -> "bg-base-content/30"
    | "InFocus" -> "bg-warning"
    | "Playing" -> "bg-info"
    | "Completed" -> "bg-success"
    | "Abandoned" -> "bg-error"
    | "OnHold" -> "bg-base-content/50"
    | "Dismissed" -> "bg-base-content/20"
    | _ -> "bg-primary"

let private gameStatusDistributionChart (distribution: (string * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No games yet"
        ]
    else
        let total = distribution |> List.sumBy snd |> max 1
        Html.div [
            prop.className "flex flex-col gap-3"
            prop.children [
                // Stacked horizontal bar
                Html.div [
                    prop.className "flex h-8 rounded-lg overflow-hidden"
                    prop.children [
                        for (status, count) in distribution do
                            let widthPct = float count / float total * 100.0
                            if widthPct > 0.0 then
                                Html.div [
                                    prop.className (sprintf "%s opacity-80 hover:opacity-100 transition-all duration-300 relative group" (gameStatusColors status))
                                    prop.style [ style.width (length.percent widthPct) ]
                                    prop.children [
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.text (sprintf "%s: %d (%.0f%%)" status count (float count / float total * 100.0))
                                        ]
                                    ]
                                ]
                    ]
                ]
                // Legend
                Html.div [
                    prop.className "flex flex-wrap gap-x-4 gap-y-1"
                    prop.children [
                        for (status, count) in distribution do
                            Html.div [
                                prop.className "flex items-center gap-1.5"
                                prop.children [
                                    Html.div [
                                        prop.className (sprintf "w-3 h-3 rounded-sm %s" (gameStatusColors status))
                                    ]
                                    Html.span [
                                        prop.className "text-xs text-base-content/60"
                                        prop.text (sprintf "%s %d" status count)
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── Games Ratings Distribution ──

let private gameRatingsDistributionChart (distribution: (int * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No ratings yet"
        ]
    else
        let maxCount =
            distribution |> List.map snd |> List.max |> max 1
        let fullDistribution =
            [ for r in 1..10 do
                let count =
                    distribution |> List.tryFind (fun (rating, _) -> rating = r)
                    |> Option.map snd |> Option.defaultValue 0
                r, count ]
        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (string maxCount)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                Html.div [
                    prop.className "flex items-end gap-1.5 h-[120px] px-1"
                    prop.children [
                        for (rating, count) in fullDistribution do
                            let heightPct =
                                if count = 0 then 0.0
                                else float count / float maxCount * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    if count > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (sprintf "%d game%s" count (if count = 1 then "" else "s"))
                                                ]
                                            ]
                                        ]
                                    if count > 0 then
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-accent opacity-80 hover:opacity-100 transition-all duration-300"
                                            prop.style [ style.height (length.percent heightPct) ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text (string rating)
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── Games Genre Breakdown ──

let private gameGenreBreakdownBars (distribution: (string * int) list) =
    if List.isEmpty distribution then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No genre data yet"
        ]
    else
        let maxCount =
            distribution |> List.head |> snd |> max 1
        Html.div [
            prop.className "flex flex-col gap-1.5"
            prop.children [
                for i, (genre, count) in distribution |> List.mapi (fun i x -> i, x) do
                    let widthPct = float count / float maxCount * 100.0
                    let opacity = 1.0 - (float i * 0.06)
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            Html.span [
                                prop.className "text-xs text-base-content/70 w-20 text-right truncate flex-shrink-0"
                                prop.text genre
                            ]
                            Html.div [
                                prop.className "flex-1 h-5 rounded-sm overflow-hidden bg-base-content/5 relative"
                                prop.children [
                                    Html.div [
                                        prop.className "h-full rounded-sm bg-accent transition-all duration-500"
                                        prop.style [
                                            style.width (length.percent widthPct)
                                            style.opacity opacity
                                        ]
                                    ]
                                ]
                            ]
                            Html.span [
                                prop.className "text-xs text-base-content/50 w-6 text-right flex-shrink-0"
                                prop.text (string count)
                            ]
                        ]
                    ]
            ]
        ]

// ── Monthly Play Time Chart ──

let private monthlyPlayTimeChart (activity: (string * int) list) =
    if List.isEmpty activity then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No play time data yet"
        ]
    else
        let today = System.DateTimeOffset.Now
        let allMonths =
            [ for i in 11 .. -1 .. 0 do
                let dt = today.AddMonths(-i)
                let key = dt.ToString("yyyy-MM")
                let label = dt.ToString("MMM")
                let entry =
                    activity |> List.tryFind (fun (m, _) -> m = key)
                match entry with
                | Some (_, minutes) -> key, label, minutes
                | None -> key, label, 0 ]
        let maxMinutes =
            allMonths |> List.map (fun (_, _, m) -> m) |> List.max |> max 1
        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
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
                Html.div [
                    prop.className "flex items-end gap-1 h-[120px] px-1"
                    prop.children [
                        for (_key, label, minutes) in allMonths do
                            let heightPct =
                                if minutes = 0 then 0.0
                                else float minutes / float maxMinutes * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    if minutes > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (formatPlayTime minutes)
                                                ]
                                            ]
                                        ]
                                    if minutes > 0 then
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-info opacity-80 hover:opacity-100 transition-all duration-300"
                                            prop.style [ style.height (length.percent heightPct) ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text label
                                    ]
                                ]
                            ]
                    ]
                ]
            ]
        ]

// ── HLTB Comparison Chart ──

let private hltbComparisonChart (comparisons: DashboardHltbComparison list) =
    if List.isEmpty comparisons then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No completed games with HLTB data yet"
        ]
    else
        let maxHours =
            comparisons
            |> List.map (fun c -> max (float c.PlayMinutes / 60.0) c.HltbMainHours)
            |> List.max |> max 1.0
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                for comp in comparisons do
                    let yourHours = float comp.PlayMinutes / 60.0
                    let yourPct = yourHours / maxHours * 100.0
                    let hltbPct = comp.HltbMainHours / maxHours * 100.0
                    let diff =
                        if comp.HltbMainHours > 0.0 then
                            (yourHours - comp.HltbMainHours) / comp.HltbMainHours * 100.0
                        else 0.0
                    let diffLabel =
                        if diff > 5.0 then sprintf "+%.0f%% slower" diff
                        elif diff < -5.0 then sprintf "%.0f%% faster" (abs diff)
                        else "on par"
                    Html.div [
                        prop.className "flex flex-col gap-0.5"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-between"
                                prop.children [
                                    Html.a [
                                        prop.href (Router.format ("games", comp.Slug))
                                        prop.onClick (fun e ->
                                            e.preventDefault()
                                            Router.navigate ("games", comp.Slug)
                                        )
                                        prop.className "text-xs text-base-content/70 truncate max-w-[160px] hover:text-primary transition-colors"
                                        prop.text comp.Name
                                    ]
                                    Html.span [
                                        prop.className (
                                            "text-[10px] font-medium "
                                            + if diff > 5.0 then "text-warning/70"
                                              elif diff < -5.0 then "text-success/70"
                                              else "text-base-content/40"
                                        )
                                        prop.text diffLabel
                                    ]
                                ]
                            ]
                            // Two bars: your time and HLTB
                            Html.div [
                                prop.className "flex flex-col gap-0.5"
                                prop.children [
                                    Html.div [
                                        prop.className "flex items-center gap-1.5"
                                        prop.children [
                                            Html.div [
                                                prop.className "flex-1 h-3 rounded-sm overflow-hidden bg-base-content/5 relative"
                                                prop.children [
                                                    Html.div [
                                                        prop.className "h-full rounded-sm bg-primary opacity-80"
                                                        prop.style [ style.width (length.percent yourPct) ]
                                                    ]
                                                ]
                                            ]
                                            Html.span [
                                                prop.className "text-[10px] text-base-content/50 w-10 text-right flex-shrink-0"
                                                prop.text (sprintf "%.0fh" yourHours)
                                            ]
                                        ]
                                    ]
                                    Html.div [
                                        prop.className "flex items-center gap-1.5"
                                        prop.children [
                                            Html.div [
                                                prop.className "flex-1 h-3 rounded-sm overflow-hidden bg-base-content/5 relative"
                                                prop.children [
                                                    Html.div [
                                                        prop.className "h-full rounded-sm bg-base-content/25"
                                                        prop.style [ style.width (length.percent hltbPct) ]
                                                    ]
                                                ]
                                            ]
                                            Html.span [
                                                prop.className "text-[10px] text-base-content/40 w-10 text-right flex-shrink-0"
                                                prop.text (sprintf "%.0fh" comp.HltbMainHours)
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                // Legend
                Html.div [
                    prop.className "flex gap-4 mt-2 pt-2 border-t border-base-content/10"
                    prop.children [
                        Html.div [
                            prop.className "flex items-center gap-1.5"
                            prop.children [
                                Html.div [ prop.className "w-3 h-3 rounded-sm bg-primary opacity-80" ]
                                Html.span [
                                    prop.className "text-[10px] text-base-content/50"
                                    prop.text "Your time"
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "flex items-center gap-1.5"
                            prop.children [
                                Html.div [ prop.className "w-3 h-3 rounded-sm bg-base-content/25" ]
                                Html.span [
                                    prop.className "text-[10px] text-base-content/50"
                                    prop.text "HLTB average"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]

// ── Games Completed Per Year ──

let private gamesCompletedPerYearChart (data: (int * int) list) =
    if List.isEmpty data then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No completion data yet"
        ]
    else
        let maxCount =
            data |> List.map snd |> List.max |> max 1
        Html.div [
            prop.className "flex flex-col gap-0"
            prop.children [
                Html.div [
                    prop.className "flex items-center gap-1 mb-0.5"
                    prop.children [
                        Html.span [
                            prop.className "text-[10px] text-base-content/30 font-medium"
                            prop.text (string maxCount)
                        ]
                        Html.div [
                            prop.className "flex-1 border-t border-base-content/10"
                        ]
                    ]
                ]
                Html.div [
                    prop.className "flex items-end gap-1.5 h-[120px] px-1"
                    prop.children [
                        for (year, count) in data do
                            let heightPct =
                                if count = 0 then 0.0
                                else float count / float maxCount * 100.0
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    if count > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (sprintf "%d game%s" count (if count = 1 then "" else "s"))
                                                ]
                                            ]
                                        ]
                                    if count > 0 then
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-success opacity-80 hover:opacity-100 transition-all duration-300"
                                            prop.style [ style.height (length.percent heightPct) ]
                                        ]
                                    else
                                        Html.div [
                                            prop.className "w-full rounded-t-sm bg-base-content/5"
                                            prop.style [ style.height (length.px 2) ]
                                        ]
                                    Html.div [
                                        prop.className "text-[10px] text-base-content/40 text-center mt-1 leading-none"
                                        prop.text (string year)
                                    ]
                                ]
                            ]
                    ]
                ]
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
            // 1. Stats badges
            gameStatsRow data.Stats

            // 2. Backlog time estimate hero card
            backlogTimeEstimateCard data.Stats

            // 3. Status distribution chart
            sectionCard Icons.chartBar "Status Distribution" [
                gameStatusDistributionChart data.Stats.StatusDistribution
            ]

            // 4. Monthly play time trend
            sectionCard Icons.calendar "Monthly Play Time" [
                monthlyPlayTimeChart data.Stats.MonthlyPlayTime
            ]

            // 5. HLTB comparison chart
            if not (List.isEmpty data.HltbComparisons) then
                sectionCard Icons.hourglass "Your Time vs HLTB" [
                    hltbComparisonChart data.HltbComparisons
                ]

            // 6 & 7. Genre breakdown and ratings (side by side on desktop)
            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
                prop.children [
                    sectionCard Icons.tag "Genre Breakdown" [
                        gameGenreBreakdownBars data.Stats.GenreDistribution
                    ]
                    sectionCard Icons.star "Ratings Distribution" [
                        gameRatingsDistributionChart data.Stats.RatingDistribution
                    ]
                ]
            ]

            // 8. Games completed per year
            if not (List.isEmpty data.Stats.CompletedPerYear) then
                sectionCard Icons.trophy "Games Completed Per Year" [
                    gamesCompletedPerYearChart data.Stats.CompletedPerYear
                ]

            // 9. Recently Played
            if not (List.isEmpty data.RecentlyPlayed) then
                sectionCard Icons.hourglass "Recently Played" [
                    for item in data.RecentlyPlayed do
                        gameRecentlyPlayedItem item
                ]

            // 10. Recently Added
            if not (List.isEmpty data.RecentlyAdded) then
                sectionCard Icons.gamepad "Recently Added" [
                    for item in data.RecentlyAdded do
                        gameRecentlyAddedItem item
                ]

            // 11. Steam Achievements
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
