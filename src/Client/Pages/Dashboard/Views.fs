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

// ── Tab bar ──

let private tabBar (activeTab: DashboardTab) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "flex gap-6"
        prop.role "tablist"
        prop.children [
            let tab (label: string) tabValue =
                Html.button [
                    prop.className (DesignSystem.underlineTabClass (activeTab = tabValue))
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

// ── Library search control (header line, right-aligned) ──

let private searchLibraryButton (dispatch: Msg -> unit) =
    Html.button [
        prop.className "flex items-center gap-2 px-3 py-1.5 rounded-lg text-sm text-base-content/50 hover:text-base-content hover:bg-base-300/40 transition-colors cursor-pointer"
        prop.title "Ctrl + K"
        prop.onClick (fun _ -> dispatch Open_search_modal)
        prop.children [
            Icons.magnifyingGlass ()
            Html.span [ prop.text "Search your library" ]
        ]
    ]

// ── Header line — tabs + search, single row ──

let private headerLine (activeTab: DashboardTab) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "flex items-center justify-between gap-4 mb-6"
        prop.children [
            tabBar activeTab dispatch
            searchLibraryButton dispatch
        ]
    ]

// ── Section card wrapper ──

/// The chrome a dashboard section wears. An expanded card keeps its collapsed
/// chrome, so it still reads as the same card — only grown.
type private Chrome =
    /// Velvet card; body stacked in a column.
    | Card
    /// Velvet card clipping its overflow (for horizontal scrollers).
    | CardOverflow
    /// Title + content, no card chrome.
    | Open

let private chromeClass (chrome: Chrome) =
    match chrome with
    | Card -> DesignSystem.velvetCard + " p-4 " + DesignSystem.animateFadeInUp
    | CardOverflow -> DesignSystem.velvetCard + " p-4 " + DesignSystem.animateFadeInUp + " overflow-hidden"
    | Open -> "section-open " + DesignSystem.animateFadeInUp

/// Icon + title, with any trailing controls (expand/collapse) pushed to the
/// card's top-right corner.
let private sectionHeader (icon: unit -> ReactElement) (title: string) (trailing: ReactElement list) =
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
            if not (List.isEmpty trailing) then
                Html.div [
                    prop.className "ml-auto flex items-center gap-2"
                    prop.children trailing
                ]
        ]
    ]

let private section (chrome: Chrome) (trailing: ReactElement list) (icon: unit -> ReactElement) (title: string) (children: ReactElement list) =
    Html.div [
        prop.className (chromeClass chrome)
        prop.children [
            sectionHeader icon title trailing
            Html.div [
                match chrome with
                | Card -> prop.className "flex flex-col"
                | CardOverflow | Open -> ()
                prop.children children
            ]
        ]
    ]

let private sectionCard (icon: unit -> ReactElement) (title: string) (children: ReactElement list) =
    section Card [] icon title children

/// Section card that allows overflow (for horizontal scrollers)
let private sectionCardOverflow (icon: unit -> ReactElement) (title: string) (children: ReactElement list) =
    section CardOverflow [] icon title children

/// Section: Open (title + content, no card chrome)
let private sectionOpen (icon: unit -> ReactElement) (title: string) (children: ReactElement list) =
    section Open [] icon title children

/// The horizontal poster rail every scroller card uses when collapsed.
let private posterScroller (children: ReactElement list) =
    Html.div [
        prop.className ("flex gap-3 overflow-x-auto py-2 px-2 scroll-px-2 snap-x snap-mandatory " + DesignSystem.scrollbarHidden)
        prop.children children
    ]

let private emptyNote (text: string) =
    Html.div [
        prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
        prop.text text
    ]

// ── Expandable cards ──
//
// Every query-backed card gets the same default: an expand button in its
// top-right corner. Expanded, the card grows over the whole tab area (the
// other cards stay mounted but hidden underneath, so the page keeps its
// height and scroll position) and shows its query with the row limit lifted.
// A horizontal scroller re-flows into a wrapping row; a vertical list
// re-flows into a grid.

/// How the expanded card lays out its items, derived from its collapsed shape.
type private ExpandedLayout =
    /// Collapsed: horizontal poster scroller. Expanded: the same fixed-width
    /// tiles, wrapping.
    | WrappingRow
    /// Collapsed: vertical list rows. Expanded: the rows re-flowed into
    /// responsive grid columns.
    | TileGrid
    /// Collapsed: already an auto-fill poster grid. Expanded: the same grid,
    /// uncapped.
    | PosterGrid

let private expandedLayoutClass (layout: ExpandedLayout) =
    match layout with
    | WrappingRow -> "flex flex-wrap gap-3 py-2 px-2"
    | TileGrid -> "grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-x-4 gap-y-1"
    | PosterGrid -> "grid grid-cols-[repeat(auto-fill,minmax(0,130px))] gap-3"

type private ExpandableCard<'item> = {
    Card: DashboardCard
    Chrome: Chrome
    Icon: unit -> ReactElement
    Title: string
    /// The collapsed body, rendered from the tab's limited items as before.
    Collapsed: ReactElement list
    ExpandedLayout: ExpandedLayout
    /// The tab's own limited items — what the expanded card shows until the
    /// unlimited fetch lands (or if it fails).
    Items: 'item list
    /// Picks this card's items out of the fetched result; None on a shape
    /// mismatch, which falls back to `Items`.
    Unpack: DashboardCardItems -> 'item list option
    RenderItem: 'item -> ReactElement
}

/// A card's two faces, closed over its item type, so a tab can keep all its
/// cards in one list and look the expanded one up by identity.
type private CardHandle = {
    Handle: DashboardCard
    CollapsedView: ReactElement
    ExpandedView: ExpandedItems -> ReactElement
}

let private headerIconButton (icon: unit -> ReactElement) (label: string) (onClick: unit -> unit) =
    Html.button [
        prop.className "p-1.5 rounded-lg text-base-content/50 hover:text-base-content hover:bg-base-300/40 transition-colors cursor-pointer"
        prop.title label
        prop.ariaLabel label
        prop.onClick (fun _ -> onClick ())
        prop.children [ icon () ]
    ]

let private expandable (dispatch: Msg -> unit) (spec: ExpandableCard<'item>) : CardHandle =
    let collapsed =
        section spec.Chrome
            [ headerIconButton Icons.arrowsPointingOut "Expand" (fun () -> dispatch (ExpandCard spec.Card)) ]
            spec.Icon spec.Title spec.Collapsed
    let expanded (state: ExpandedItems) =
        let items =
            match state with
            | ExpandedReady fetched -> spec.Unpack fetched |> Option.defaultValue spec.Items
            | ExpandedLoading | ExpandedFailed _ -> spec.Items
        let status =
            match state with
            | ExpandedLoading ->
                Html.span [
                    prop.className "loading loading-spinner loading-xs text-primary/70"
                    prop.title "Loading everything"
                ]
            | ExpandedReady _ ->
                Html.span [
                    prop.className "font-mono text-xs text-base-content/50"
                    prop.text (string (List.length items))
                ]
            | ExpandedFailed message ->
                Html.span [
                    prop.className "text-xs text-warning/70"
                    prop.title message
                    prop.text "Showing the first few only"
                ]
        Html.div [
            prop.id State.expandedCardElementId
            prop.className (chromeClass spec.Chrome + " col-start-1 row-start-1 z-10 scroll-mt-20")
            prop.children [
                sectionHeader spec.Icon spec.Title [
                    status
                    headerIconButton Icons.arrowsPointingIn "Collapse" (fun () -> dispatch CollapseCard)
                ]
                Html.div [
                    prop.className (expandedLayoutClass spec.ExpandedLayout)
                    prop.children (items |> List.map spec.RenderItem)
                ]
            ]
        ]
    { Handle = spec.Card; CollapsedView = collapsed; ExpandedView = expanded }

/// The active tab's content, with the expanded card (if any) grown over it.
/// Both sit in the same grid cell: the cell is as tall as the taller of the
/// two, so the page keeps its height while the other cards are hidden
/// underneath rather than unmounted.
let private tabArea (expanded: ExpandedCard option) (cards: CardHandle list) (content: ReactElement list) =
    let grown =
        expanded
        |> Option.bind (fun e ->
            cards
            |> List.tryFind (fun c -> c.Handle = e.Card)
            |> Option.map (fun c -> c.ExpandedView e.Items))
    match grown with
    | None ->
        Html.div [
            prop.className "flex flex-col gap-4"
            prop.children content
        ]
    | Some surface ->
        Html.div [
            // `grid-cols-1` = `minmax(0, 1fr)`: without the 0 floor the single column
            // would grow to the hidden scrollers' min-content width and push the
            // expanded card (and its collapse button) far past the viewport.
            prop.className "grid grid-cols-1"
            prop.children [
                Html.div [
                    prop.className "col-start-1 row-start-1 flex flex-col gap-4 invisible"
                    prop.ariaHidden true
                    prop.children content
                ]
                surface
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

// ── Movies to Watch — Poster Cards (All tab) ──

let private movieToWatchPosterCard (jellyfinServerUrl: string option) (item: DashboardMovieToWatch) =
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

                            // Crosshair badge — only for in-focus movies
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

                            // Jellyfin play button overlay (bottom-right) — always visible ghost style
                            match jellyfinServerUrl, item.JellyfinId with
                            | Some serverUrl, Some jellyfinId ->
                                Html.a [
                                    prop.href (jellyfinPlayUrl serverUrl jellyfinId)
                                    prop.target "_blank"
                                    prop.rel "noopener noreferrer"
                                    prop.onClick (fun e -> e.stopPropagation())
                                    prop.className "absolute bottom-2 right-2 z-10 flex items-center justify-center w-8 h-8 rounded-full bg-base-100 border border-base-content/15 text-primary hover:bg-primary hover:text-primary-content transition-all shadow-lg cursor-pointer"
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

/// "Movies to Watch" filmstrip item — wraps a `DashboardMovieToWatch` into a
/// `DesignSystem.FilmstripItem`. The InFocus crosshair badge and the
/// Jellyfin play button are built here (they need `Icons.crosshairSmFilled`
/// / `Icons.play` / `jellyfinPlayUrl`, all page-local) and handed to the
/// design-system row as pre-rendered, self-positioned slots, and the nav
/// target is handed in as an `Href` + `OnNavigate` callback pair -- same
/// caller-supplied-slot shape as `seriesNextEpisodeCard` (intelligence-h7v2q).
let private movieToWatchFilmstripItem (jellyfinServerUrl: string option) (item: DashboardMovieToWatch) : DesignSystem.FilmstripItem =
    let inFocusBadge =
        if item.InFocus then
            Some (
                Html.div [
                    prop.className "absolute top-1.5 left-1.5 z-10"
                    prop.children [
                        Html.span [
                            prop.className "flex items-center justify-center w-6 h-6 rounded-full bg-warning/90 text-warning-content shadow-md"
                            prop.children [ Icons.crosshairSmFilled () ]
                        ]
                    ]
                ]
            )
        else
            None
    let jellyfinButton =
        match jellyfinServerUrl, item.JellyfinId with
        | Some serverUrl, Some jellyfinId ->
            Some (
                Html.a [
                    prop.href (jellyfinPlayUrl serverUrl jellyfinId)
                    prop.target "_blank"
                    prop.rel "noopener noreferrer"
                    prop.onClick (fun e -> e.stopPropagation())
                    prop.className "absolute bottom-2 right-2 z-10 flex items-center justify-center w-8 h-8 rounded-full bg-base-100 border border-base-content/15 text-primary hover:bg-primary hover:text-primary-content transition-all shadow-lg cursor-pointer"
                    prop.title "Play in Jellyfin"
                    prop.children [
                        Html.span [
                            prop.className "w-4 h-4"
                            prop.children [ Icons.play () ]
                        ]
                    ]
                ]
            )
        | _ -> None
    {
        DesignSystem.FilmstripItem.Key = item.Slug
        PosterRef = item.PosterRef
        Title = item.Name
        Meta = string item.Year
        Href = Some (Router.format ("movies", item.Slug))
        OnNavigate = Some (fun () -> Router.navigate ("movies", item.Slug))
        InFocusBadge = inFocusBadge
        JellyfinButton = jellyfinButton
    }

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

// ── Next episode — cinematic hero cards (All tab, below hero) ──

/// series-ww1rb: real per-season/per-episode watch state, composed server-side
/// in `SeriesProjection.getDashboardSeriesNextUp` (ADR-0048) — one dot per
/// episode of the CURRENT season only, holes preserved, not a whole-series
/// prefix fill. `IsComplete` flips both rows from the gold "where am I" scale
/// to green: a finished series still on the dashboard has no frontier left to
/// point at, so nothing renders half-lit.
let private seriesProgressOf (item: DashboardSeriesNextUp) : DesignSystem.SeriesProgressProps =
    { SeasonsTouched = item.SeasonsTouched
      ActiveSeasonIndex = item.ActiveSeasonIndex
      CurrentSeasonWatched = item.CurrentSeasonWatched
      CurrentSeasonNextUpIndex = item.CurrentSeasonNextUpIndex
      IsComplete = item.IsFinished }

/// Series card for the "Next episode" section: `DesignSystem.nextEpisodeHeroCard`
/// wrapped in the same navigate-to-series-detail anchor the poster cards use.
/// The Jellyfin play button is built here (it needs `Icons.play` and the
/// `jellyfinPlayUrl` helper, both page-local) and handed to the design-system
/// card as a pre-rendered, self-positioned slot.
let private seriesNextEpisodeCard (jellyfinServerUrl: string option) (item: DashboardSeriesNextUp) =
    let episodeLabel =
        if item.NextUpSeason > 0 then
            Some $"S{item.NextUpSeason}E{item.NextUpEpisode}: {item.NextUpTitle}"
        else
            None
    let jellyfinButton =
        match jellyfinServerUrl, item.JellyfinEpisodeId with
        | Some serverUrl, Some episodeId ->
            Some (
                Html.a [
                    prop.href (jellyfinPlayUrl serverUrl episodeId)
                    prop.target "_blank"
                    prop.rel "noopener noreferrer"
                    prop.onClick (fun e -> e.stopPropagation())
                    prop.className "absolute top-3 right-3 z-10 flex items-center justify-center w-10 h-10 rounded-full bg-base-100 border border-base-content/15 text-primary hover:bg-primary hover:text-primary-content transition-all shadow-lg cursor-pointer"
                    prop.title "Play in Jellyfin"
                    prop.children [
                        Html.span [
                            prop.className "w-4 h-4"
                            prop.children [ Icons.play () ]
                        ]
                    ]
                ]
            )
        | _ -> None
    Html.a [
        prop.href (Router.format ("series", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", item.Slug)
        )
        prop.className "flex-shrink-0 w-[280px] sm:w-[320px] cursor-pointer group snap-start"
        prop.children [
            DesignSystem.nextEpisodeHeroCard {
                SeriesName = item.Name
                EpisodeLabel = episodeLabel
                BackdropRef = item.BackdropRef
                PosterRef = item.PosterRef
                Progress = seriesProgressOf item
                WatchedWith =
                    item.WatchWithFriends
                    |> List.map (fun f ->
                        { DesignSystem.NextEpisodeHeroFriend.ImageRef = f.ImageRef
                          Name = f.Name
                          Href = Router.format ("friends", f.Slug)
                          OnClick = fun () -> Router.navigate ("friends", f.Slug) })
                JellyfinButton = jellyfinButton
            }
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

/// The collapsed body of the "Recent Achievements" card.
let private achievementsBody (state: AchievementsState) : ReactElement list =
    [
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

// ── Books placeholder (right column, matches Games column chrome) ──

let private booksColumnPlaceholder =
    sectionOpen Icons.catalog "Books" [
        Html.p [
            prop.className "text-base-content/40 text-sm font-medium text-center py-6"
            prop.text "Books coming soon."
        ]
    ]

// ── All Tab — 3a layout: TV row, Movies row, Games/Books split ──

let private allTabView (data: DashboardAllTab) (expanded: ExpandedCard option) (dispatch: Msg -> unit) =
    // 1. TV Series — full-width Next Up poster row (no hero lead card)
    let nextEpisode =
        expandable dispatch {
            Card = AllNextEpisode
            Chrome = Open
            Icon = Icons.tv
            Title = "Next episode"
            Collapsed = [ posterScroller [ for item in data.SeriesNextUp do seriesNextEpisodeCard data.JellyfinServerUrl item ] ]
            ExpandedLayout = WrappingRow
            Items = data.SeriesNextUp
            Unpack = (function SeriesNextUpItems items -> Some items | _ -> None)
            RenderItem = seriesNextEpisodeCard data.JellyfinServerUrl
        }
    // 2. Movies to Watch — posters inside the filmstrip well
    // (`DesignSystem.filmstripRow`, intelligence-p9m4t). Expanded, the
    // sprocketed strip gives way to the Movies tab's poster tiles, wrapping.
    let moviesToWatch =
        expandable dispatch {
            Card = AllMoviesToWatch
            Chrome = Open
            Icon = Icons.movie
            Title = "Movies to Watch"
            Collapsed = [ DesignSystem.filmstripRow (data.MoviesToWatch |> List.map (movieToWatchFilmstripItem data.JellyfinServerUrl)) ]
            ExpandedLayout = WrappingRow
            Items = data.MoviesToWatch
            Unpack = (function MoviesToWatchItems items -> Some items | _ -> None)
            RenderItem = movieToWatchPosterCard data.JellyfinServerUrl
        }
    // 3. Games in focus — already an auto-fill poster grid when collapsed.
    let games =
        expandable dispatch {
            Card = AllGamesInFocus
            Chrome = Open
            Icon = Icons.gamepad
            Title = "Games"
            Collapsed = [
                Html.div [
                    prop.className (expandedLayoutClass PosterGrid)
                    prop.children [ for item in data.GamesInFocus do gameInFocusPosterCard item ]
                ]
            ]
            ExpandedLayout = PosterGrid
            Items = data.GamesInFocus
            Unpack = (function GamesInFocusItems items -> Some items | _ -> None)
            RenderItem = gameInFocusPosterCard
        }
    tabArea expanded [ nextEpisode; moviesToWatch; games ] [
        if not (List.isEmpty data.SeriesNextUp) then
            nextEpisode.CollapsedView

        if not (List.isEmpty data.MoviesToWatch) then
            moviesToWatch.CollapsedView

        // Games (left) / Books (right) two-column split — stays single-column
        // until xl (raised from lg) so both columns get comfortable room rather
        // than squeezing two-up at mid widths.
        Html.div [
            prop.className "grid grid-cols-1 xl:grid-cols-2 gap-4"
            prop.children [
                if not (List.isEmpty data.GamesInFocus) then
                    games.CollapsedView
                booksColumnPlaceholder
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

let private personStatsItem (person: DashboardPersonStats) =
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

let private personStatsSection (people: DashboardPersonStats list) (emptyMessage: string) =
    if List.isEmpty people then
        emptyNote emptyMessage
    else
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                for person in people do
                    personStatsItem person
            ]
        ]

// ── Most Watched With (Friends) ──

let private watchedWithItem (friend: DashboardWatchedWithStats) =
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

let private watchedWithSection (watchedWith: DashboardWatchedWithStats list) =
    if List.isEmpty watchedWith then
        emptyNote "No shared sessions yet"
    else
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                for friend in watchedWith do
                    watchedWithItem friend
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

// ── Recently Watched Poster Card (for horizontal scroller) ──

let private recentlyWatchedPosterCard (item: DashboardRecentlyWatched) =
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
                        prop.text (formatDate item.WatchDate)
                    ]
                    if not (List.isEmpty item.Friends) then
                        Html.p [
                            prop.className "text-[10px] text-base-content/40 truncate"
                            prop.text (item.Friends |> String.concat ", ")
                        ]
                ]
            ]
        ]
    ]

// ── Movies Tab View ──

let private moviesTabView (data: DashboardMoviesTab) (expanded: ExpandedCard option) (dispatch: Msg -> unit) =
    // Recently Watched — horizontal poster scroller
    let recentlyWatched =
        expandable dispatch {
            Card = MoviesRecentlyWatched
            Chrome = CardOverflow
            Icon = Icons.movie
            Title = "Recently Watched"
            Collapsed = [
                if List.isEmpty data.RecentlyWatched then
                    emptyNote "No movies watched yet"
                else
                    posterScroller [ for item in data.RecentlyWatched do recentlyWatchedPosterCard item ]
            ]
            ExpandedLayout = WrappingRow
            Items = data.RecentlyWatched
            Unpack = (function RecentlyWatchedMovieItems items -> Some items | _ -> None)
            RenderItem = recentlyWatchedPosterCard
        }
    // Recently Added — narrower column, list format
    let recentlyAdded =
        expandable dispatch {
            Card = MoviesRecentlyAdded
            Chrome = Card
            Icon = Icons.movie
            Title = "Recently Added"
            Collapsed = [
                if List.isEmpty data.RecentlyAdded then
                    emptyNote "No recently added movies"
                else
                    for item in data.RecentlyAdded |> List.truncate 6 do
                        movieRecentlyAddedItem item
            ]
            ExpandedLayout = TileGrid
            Items = data.RecentlyAdded
            Unpack = (function MovieItems items -> Some items | _ -> None)
            RenderItem = movieRecentlyAddedItem
        }
    // Movies to Watch — full-width horizontal poster scroller
    let moviesToWatch =
        expandable dispatch {
            Card = MoviesToWatch
            Chrome = CardOverflow
            Icon = Icons.movie
            Title = "Movies to Watch"
            Collapsed = [ posterScroller [ for item in data.MoviesToWatch do movieToWatchPosterCard data.JellyfinServerUrl item ] ]
            ExpandedLayout = WrappingRow
            Items = data.MoviesToWatch
            Unpack = (function MoviesToWatchItems items -> Some items | _ -> None)
            RenderItem = movieToWatchPosterCard data.JellyfinServerUrl
        }
    let people (card: DashboardCard) (title: string) (items: DashboardPersonStats list) (emptyMessage: string) =
        expandable dispatch {
            Card = card
            Chrome = Card
            Icon = Icons.user
            Title = title
            Collapsed = [ personStatsSection items emptyMessage ]
            ExpandedLayout = TileGrid
            Items = items
            Unpack = (function PersonItems items -> Some items | _ -> None)
            RenderItem = personStatsItem
        }
    let topActors = people MoviesTopActors "Most Watched Actors" data.TopActors "No actor data yet"
    let topDirectors = people MoviesTopDirectors "Most Watched Directors" data.TopDirectors "No director data yet"
    let topWatchedWith =
        expandable dispatch {
            Card = MoviesTopWatchedWith
            Chrome = Card
            Icon = Icons.friends
            Title = "Most Watched With"
            Collapsed = [ watchedWithSection data.TopWatchedWith ]
            ExpandedLayout = TileGrid
            Items = data.TopWatchedWith
            Unpack = (function MovieWatchedWithItems items -> Some items | _ -> None)
            RenderItem = watchedWithItem
        }
    tabArea expanded [ recentlyWatched; recentlyAdded; moviesToWatch; topActors; topDirectors; topWatchedWith ] [
        // Stats badges
        movieStatsRow data.Stats

        // Row 1: Recently Watched (~2/3) | Recently Added (~1/3)
        Html.div [
            prop.className "grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4"
            prop.children [
                recentlyWatched.CollapsedView
                recentlyAdded.CollapsedView
            ]
        ]

        // Row 2: Monthly Activity (50%) | Ratings Distribution (50%)
        Html.div [
            prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
            prop.children [
                sectionCard Icons.calendar "Monthly Activity" [
                    monthlyActivityChart data.Stats.MonthlyActivity
                ]
                sectionCard Icons.star "Ratings Distribution" [
                    ratingsDistributionChart data.Stats.RatingDistribution
                ]
            ]
        ]

        // Row 3: Movies to Watch — full-width horizontal poster scroller
        if not (List.isEmpty data.MoviesToWatch) then
            moviesToWatch.CollapsedView

        // Row 4: Most Watched Actors | Most Watched Directors | Most Watched With
        Html.div [
            prop.className "grid grid-cols-1 md:grid-cols-3 gap-4"
            prop.children [
                topActors.CollapsedView
                topDirectors.CollapsedView
                topWatchedWith.CollapsedView
            ]
        ]

        // Row 5: Genre Breakdown — pie/donut chart
        sectionCard Icons.tag "Genre Breakdown" [
            Charts.donutChart data.Stats.GenreDistribution "No genre data yet"
        ]

        // Country distribution (bonus, kept from before)
        if not (List.isEmpty data.Stats.CountryDistribution) then
            sectionCard Icons.globe "Movie Origins" [
                countryDistributionBars data.Stats.CountryDistribution
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

// ── Series Most Watched With (Friends) ──

let private seriesWatchedWithItem (friend: DashboardSeriesWatchedWith) =
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

let private seriesWatchedWithSection (watchedWith: DashboardSeriesWatchedWith list) =
    if List.isEmpty watchedWith then
        emptyNote "No shared rewatch sessions yet"
    else
        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                for friend in watchedWith do
                    seriesWatchedWithItem friend
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

/// Poster card for series tab Next Up scroller — includes progress bar and episode info
let private seriesTabPosterCard (jellyfinServerUrl: string option) (item: DashboardSeriesNextUp) =
    let progressPct =
        if item.EpisodeCount > 0 then
            float item.WatchedEpisodeCount / float item.EpisodeCount * 100.0
        else 0.0
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
                                    prop.className "absolute bottom-2 right-2 z-10 flex items-center justify-center w-8 h-8 rounded-full bg-base-100 border border-base-content/15 text-primary hover:bg-primary hover:text-primary-content transition-all shadow-lg opacity-0 group-hover:opacity-100 cursor-pointer"
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
                    if item.NextUpSeason > 0 then
                        Html.p [
                            prop.className "text-xs text-base-content/50 truncate"
                            prop.text $"S{item.NextUpSeason}E{item.NextUpEpisode}"
                        ]
                    // Progress bar
                    if item.EpisodeCount > 0 then
                        Html.div [
                            prop.className "mt-1"
                            prop.children [
                                Html.div [
                                    prop.className "w-full h-1.5 rounded-full bg-base-content/10 overflow-hidden"
                                    prop.children [
                                        Html.div [
                                            prop.className "h-full rounded-full bg-primary/70 transition-all"
                                            prop.style [ style.width (length.percent progressPct) ]
                                        ]
                                    ]
                                ]
                                Html.p [
                                    prop.className "text-[10px] text-base-content/40 mt-0.5"
                                    prop.text $"{item.WatchedEpisodeCount}/{item.EpisodeCount} ep"
                                ]
                            ]
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

let private formatReturningDate (isoDate: string) : string =
    let trimmed =
        match isoDate.IndexOf('T') with
        | -1 -> isoDate
        | i -> isoDate.[..i-1]
    match System.DateTime.TryParse(trimmed) with
    | true, d -> d.ToString("MMM d")
    | _ -> trimmed

let private returningCountdown (isoDate: string) : string option =
    let trimmed =
        match isoDate.IndexOf('T') with
        | -1 -> isoDate
        | i -> isoDate.[..i-1]
    match System.DateTime.TryParse(trimmed) with
    | true, d ->
        let days = (d.Date - System.DateTime.Today).Days
        if days < 0 then None
        elif days = 0 then Some "today"
        elif days = 1 then Some "tomorrow"
        else Some (sprintf "in %d days" days)
    | _ -> None

let private returningSoonItem (item: ReturningSoonItem) =
    Html.a [
        prop.href (Router.format ("series", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", item.Slug))
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
                        prop.className "flex items-center gap-1.5 text-xs text-base-content/50"
                        prop.children [
                            Html.span [
                                prop.text (
                                    (if item.IsSeasonLevel then "Returns " else "Airs ")
                                    + formatReturningDate item.NextAirDate)
                            ]
                            match returningCountdown item.NextAirDate with
                            | Some countdown ->
                                Html.span [
                                    prop.className "text-primary/70"
                                    prop.text (sprintf "(%s)" countdown)
                                ]
                            | None -> ()
                        ]
                    ]
                ]
            ]
        ]
    ]

let private seriesTabView (data: DashboardSeriesTab) (expanded: ExpandedCard option) (dispatch: Msg -> unit) =
    // Filter out abandoned series from Next Up
    let withoutAbandoned (items: DashboardSeriesNextUp list) =
        items |> List.filter (fun s -> not s.IsAbandoned)
    let nextUpItems = withoutAbandoned data.NextUp
    // Next Up — full-width horizontal poster scroller
    let nextUp =
        expandable dispatch {
            Card = SeriesNextUp
            Chrome = CardOverflow
            Icon = Icons.tv
            Title = "Next Up"
            Collapsed = [ posterScroller [ for item in nextUpItems do seriesTabPosterCard data.JellyfinServerUrl item ] ]
            ExpandedLayout = WrappingRow
            Items = nextUpItems
            Unpack = (function SeriesNextUpItems items -> Some (withoutAbandoned items) | _ -> None)
            RenderItem = seriesTabPosterCard data.JellyfinServerUrl
        }
    // Returning Soon — up to 5 returning series sorted ascending by next air date
    let returningSoon =
        expandable dispatch {
            Card = SeriesReturningSoon
            Chrome = Card
            Icon = Icons.tv
            Title = "Returning Soon"
            Collapsed = [
                Html.div [
                    prop.className "flex flex-col gap-2"
                    prop.children [ for item in data.ReturningSoon do returningSoonItem item ]
                ]
            ]
            ExpandedLayout = TileGrid
            Items = data.ReturningSoon
            Unpack = (function ReturningSoonItems items -> Some items | _ -> None)
            RenderItem = returningSoonItem
        }
    let badge (className: string) (text: string) =
        Html.span [
            prop.className ("inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium flex-shrink-0 " + className)
            prop.text text
        ]
    let recentSeries (card: DashboardCard) (icon: unit -> ReactElement) (title: string) (items: SeriesListItem list) (itemBadge: ReactElement) =
        expandable dispatch {
            Card = card
            Chrome = Card
            Icon = icon
            Title = title
            Collapsed = [ for item in items do seriesCompactItem item itemBadge ]
            ExpandedLayout = TileGrid
            Items = items
            Unpack = (function SeriesItems items -> Some items | _ -> None)
            RenderItem = (fun item -> seriesCompactItem item itemBadge)
        }
    let recentlyFinished =
        recentSeries SeriesRecentlyFinished Icons.trophy "Recently Finished" data.RecentlyFinished (badge "bg-success/15 text-success" "Finished")
    let recentlyAbandoned =
        recentSeries SeriesRecentlyAbandoned Icons.tv "Recently Abandoned" data.RecentlyAbandoned (badge "bg-error/15 text-error" "Abandoned")
    // Most watched with (friends)
    let topWatchedWith =
        expandable dispatch {
            Card = SeriesTopWatchedWith
            Chrome = Card
            Icon = Icons.friends
            Title = "Most Watched With"
            Collapsed = [ seriesWatchedWithSection data.TopWatchedWith ]
            ExpandedLayout = TileGrid
            Items = data.TopWatchedWith
            Unpack = (function SeriesWatchedWithItems items -> Some items | _ -> None)
            RenderItem = seriesWatchedWithItem
        }
    tabArea expanded [ nextUp; returningSoon; recentlyFinished; recentlyAbandoned; topWatchedWith ] [
        // 1. Stats badges
        seriesStatsRow data.Stats

        // Row 1: Next Up
        if not (List.isEmpty nextUpItems) then
            nextUp.CollapsedView

        if not (List.isEmpty data.ReturningSoon) then
            returningSoon.CollapsedView

        // Row 2: Recently Finished | Recently Abandoned (side-by-side)
        if not (List.isEmpty data.RecentlyFinished) || not (List.isEmpty data.RecentlyAbandoned) then
            Html.div [
                prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
                prop.children [
                    if not (List.isEmpty data.RecentlyFinished) then
                        recentlyFinished.CollapsedView
                    if not (List.isEmpty data.RecentlyAbandoned) then
                        recentlyAbandoned.CollapsedView
                ]
            ]

        // Row 3: Monthly Activity | Ratings Distribution | Genre Breakdown (pie chart)
        Html.div [
            prop.className "grid grid-cols-1 md:grid-cols-3 gap-4"
            prop.children [
                sectionCard Icons.calendar "Monthly Activity" [
                    monthlyEpisodeActivityChart data.Stats.MonthlyActivity
                ]
                sectionCard Icons.star "Ratings Distribution" [
                    seriesRatingsDistributionChart data.Stats.RatingDistribution
                ]
                sectionCard Icons.tag "Genre Breakdown" [
                    Charts.donutChart data.Stats.GenreDistribution "No genre data"
                ]
            ]
        ]

        if not (List.isEmpty data.TopWatchedWith) then
            topWatchedWith.CollapsedView
    ]

// ── Games Tab ──

let private gameStatsRow (stats: DashboardGameStats) =
    Html.div [
        prop.className "flex gap-3 flex-wrap mb-4"
        prop.children [
            statBadge "Games" (string stats.TotalGames)
            statBadge "Play Time" (formatPlayTime stats.TotalPlayTimeMinutes)
            statBadge "Retired" (string stats.GamesCompleted)
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

// ── In-Focus Estimate Hero Card ──

let private inFocusEstimateCard (estimate: InFocusEstimate) =
    if estimate.GameCount = 0 then
        Html.none
    else
        let totalMinutes = estimate.TotalRemainingMinutes
        let hoursDisplay =
            if totalMinutes >= 60 * 24 then
                let days = float totalMinutes / (60.0 * 24.0)
                let hrs = float totalMinutes / 60.0
                sprintf "~%.0f days (~%.0f hrs)" days hrs
            elif totalMinutes >= 60 then
                sprintf "~%d hours" (totalMinutes / 60)
            else
                sprintf "%d min" totalMinutes
        Html.div [
            prop.className (DesignSystem.velvetCard + " p-5 " + DesignSystem.animateFadeInUp)
            prop.children [
                Html.div [
                    prop.className "flex items-center gap-2 mb-2"
                    prop.children [
                        Html.span [
                            prop.className "text-info/70"
                            prop.children [ Icons.hourglass () ]
                        ]
                        Html.h2 [
                            prop.className "text-lg font-display uppercase tracking-wider"
                            prop.text "In-Focus Estimate"
                        ]
                    ]
                ]
                Html.div [
                    prop.className "text-center py-2"
                    prop.children [
                        Html.div [
                            prop.className "text-3xl font-display font-bold text-info"
                            prop.text (sprintf "%s remaining" hoursDisplay)
                        ]
                        Html.div [
                            prop.className "text-sm text-base-content/50 mt-1"
                            prop.children [
                                Html.text (sprintf "across %d in-focus game%s" estimate.GameCount (if estimate.GameCount = 1 then "" else "s"))
                                if estimate.GamesWithoutHltb > 0 then
                                    Html.text (sprintf " (%d without HLTB data)" estimate.GamesWithoutHltb)
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

let private gameRecentlyPlayedPosterCard (item: DashboardGameRecentlyPlayed) =
    Html.a [
        prop.href (Router.format ("games", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", item.Slug)
        )
        prop.className "flex-shrink-0 w-[120px] sm:w-[130px] cursor-pointer group snap-start"
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
                        prop.className "text-xs text-base-content/50 truncate"
                        prop.text (sprintf "%s \u00B7 %s" (formatPlayTime item.TotalPlayTimeMinutes) (formatDate item.LastPlayedDate))
                    ]
                ]
            ]
        ]
    ]

let private gameRecentlyAddedPosterCard (item: GameListItem) =
    Html.a [
        prop.href (Router.format ("games", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", item.Slug)
        )
        prop.className "flex-shrink-0 w-[120px] sm:w-[130px] cursor-pointer group snap-start"
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

/// intelligence-qh8mj: the Upcoming rail's card — `gameRecentlyAddedPosterCard`'s
/// shape (same 130px poster size, per intelligence-c3vqm), with the bottom
/// line swapped for the release-date badge (games-ev65k's
/// `PlayFacetsDisplay.releaseDateBadge`, "Upcoming" for a TBA date) instead
/// of the release year.
let private gameUpcomingPosterCard (item: GameListItem) =
    Html.a [
        prop.href (Router.format ("games", item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("games", item.Slug)
        )
        prop.className "flex-shrink-0 w-[120px] sm:w-[130px] cursor-pointer group snap-start"
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
                    Html.div [
                        prop.className "mt-0.5"
                        prop.children [ PlayFacetsDisplay.releaseDateBadge item.ReleaseDate ]
                    ]
                ]
            ]
        ]
    ]

// ── Per-Game Color-Coded Monthly Play Time Chart ──

let private perGameMonthlyPlayTimeChart (monthlyData: GameMonthlyPlayTime list) =
    if List.isEmpty monthlyData then
        Html.div [
            prop.className "flex items-center justify-center py-6 text-base-content/40 text-sm"
            prop.text "No play time data yet"
        ]
    else
        let today = System.DateTimeOffset.Now
        let allMonthKeys =
            [ for i in 11 .. -1 .. 0 do
                let dt = today.AddMonths(-i)
                dt.ToString("yyyy-MM"), dt.ToString("MMM") ]

        // Get unique games across all data, ordered by total play time desc
        let gamesByTotal =
            monthlyData
            |> List.groupBy (fun d -> d.GameSlug, d.GameName)
            |> List.map (fun ((slug, name), entries) -> slug, name, entries |> List.sumBy (fun e -> e.MinutesPlayed))
            |> List.sortByDescending (fun (_, _, total) -> total)

        // Build per-month stacked data
        let monthData =
            allMonthKeys
            |> List.map (fun (key, label) ->
                let gameMinutes =
                    gamesByTotal
                    |> List.map (fun (slug, name, _) ->
                        let mins =
                            monthlyData
                            |> List.tryFind (fun d -> d.Month = key && d.GameSlug = slug)
                            |> Option.map (fun d -> d.MinutesPlayed)
                            |> Option.defaultValue 0
                        slug, name, mins)
                key, label, gameMinutes)

        let maxMinutes =
            monthData
            |> List.map (fun (_, _, games) -> games |> List.sumBy (fun (_, _, m) -> m))
            |> List.max |> max 1

        Html.div [
            prop.className "flex flex-col gap-2"
            prop.children [
                // Y-axis label
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
                // Stacked bar chart
                Html.div [
                    prop.className "flex items-end gap-1 h-[140px] px-1"
                    prop.children [
                        for (_key, label, gameMinutes) in monthData do
                            let totalForMonth = gameMinutes |> List.sumBy (fun (_, _, m) -> m)
                            Html.div [
                                prop.className "flex-1 flex flex-col justify-end items-center relative group"
                                prop.style [ style.height (length.percent 100) ]
                                prop.children [
                                    // Tooltip
                                    if totalForMonth > 0 then
                                        Html.div [
                                            prop.className "absolute bottom-full left-1/2 -translate-x-1/2 mb-1 px-2 py-1 rounded-md bg-base-300/90 text-xs text-base-content whitespace-nowrap opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-20 shadow-lg"
                                            prop.children [
                                                Html.div [
                                                    prop.className "font-medium"
                                                    prop.text (formatPlayTime totalForMonth)
                                                ]
                                            ]
                                        ]
                                    // Stacked segments
                                    Html.div [
                                        prop.className "w-full flex flex-col-reverse"
                                        prop.style [
                                            let heightPct = if totalForMonth = 0 then 0.0 else float totalForMonth / float maxMinutes * 100.0
                                            style.height (length.percent heightPct)
                                        ]
                                        prop.children [
                                            for gi, (_, _, mins) in gameMinutes |> List.mapi (fun i x -> i, x) do
                                                if mins > 0 then
                                                    let segPct = float mins / float totalForMonth * 100.0
                                                    let colorIdx = gi % Charts.chartColors.Length
                                                    Html.div [
                                                        prop.className "w-full first:rounded-t-sm opacity-80 hover:opacity-100 transition-all duration-300"
                                                        prop.style [
                                                            style.height (length.percent segPct)
                                                            style.backgroundColor Charts.chartColors.[colorIdx]
                                                        ]
                                                    ]
                                        ]
                                    ]
                                    if totalForMonth = 0 then
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
                // Legend
                Html.div [
                    prop.className "flex flex-wrap gap-x-3 gap-y-1 mt-2"
                    prop.children [
                        for gi, (_, name, _) in gamesByTotal |> List.mapi (fun i x -> i, x) do
                            let colorIdx = gi % Charts.chartBgColors.Length
                            Html.div [
                                prop.className "flex items-center gap-1.5"
                                prop.children [
                                    Html.div [
                                        prop.className (sprintf "w-2.5 h-2.5 rounded-sm %s opacity-80" Charts.chartBgColors.[colorIdx])
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

let private gamesTabView (data: DashboardGamesTab) (achievementsState: AchievementsState) (expanded: ExpandedCard option) (dispatch: Msg -> unit) =
    // Recently Played | Recently Added | Upcoming — poster scrollers
    let posterRail (card: DashboardCard) (icon: unit -> ReactElement) (title: string) (items: 'item list) (emptyMessage: string) (unpack: DashboardCardItems -> 'item list option) (render: 'item -> ReactElement) =
        expandable dispatch {
            Card = card
            Chrome = CardOverflow
            Icon = icon
            Title = title
            Collapsed = [
                if List.isEmpty items then
                    emptyNote emptyMessage
                else
                    posterScroller [ for item in items do render item ]
            ]
            ExpandedLayout = WrappingRow
            Items = items
            Unpack = unpack
            RenderItem = render
        }
    let recentlyPlayed =
        posterRail GamesRecentlyPlayed Icons.hourglass "Recently Played" data.RecentlyPlayed "No games played yet"
            (function RecentlyPlayedGameItems items -> Some items | _ -> None)
            gameRecentlyPlayedPosterCard
    let recentlyAdded =
        posterRail GamesRecentlyAdded Icons.gamepad "Recently Added" data.RecentlyAdded "No games added yet"
            (function GameItems items -> Some items | _ -> None)
            gameRecentlyAddedPosterCard
    // Upcoming rail (games-ev65k, moved here by intelligence-qh8mj) —
    // absent, not empty-rendered, when nothing is unreleased.
    let upcoming =
        posterRail GamesUpcoming Icons.calendar "Upcoming" data.Upcoming "Nothing upcoming"
            (function GameItems items -> Some items | _ -> None)
            gameUpcomingPosterCard
    let achievements =
        expandable dispatch {
            Card = GamesRecentAchievements
            Chrome = Card
            Icon = Icons.trophy
            Title = "Recent Achievements"
            Collapsed = achievementsBody achievementsState
            ExpandedLayout = TileGrid
            Items =
                match achievementsState with
                | AchievementsReady items -> items
                | AchievementsNotLoaded | AchievementsLoading | AchievementsError _ -> []
            Unpack = (function AchievementItems items -> Some items | _ -> None)
            RenderItem = achievementItem
        }
    tabArea expanded [ recentlyPlayed; recentlyAdded; upcoming; achievements ] [
        // Stats badges
        gameStatsRow data.Stats

        // Row 1: In-Focus Estimate hero card (full width)
        inFocusEstimateCard data.InFocusEstimate

        // Row 2: Recently Played | Recently Added (poster scrollers)
        Html.div [
            prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
            prop.children [
                recentlyPlayed.CollapsedView
                recentlyAdded.CollapsedView
            ]
        ]

        if not (List.isEmpty data.Upcoming) then
            upcoming.CollapsedView

        // Row 3: Status Distribution (pie) | Genre Breakdown (spider/radar)
        Html.div [
            prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
            prop.children [
                sectionCard Icons.chartBar "Status Distribution" [
                    Charts.donutChart data.Stats.StatusDistribution "No games yet"
                ]
                sectionCard Icons.tag "Genre Breakdown" [
                    Charts.radarChart data.Stats.GenreDistribution "No genre data yet"
                ]
            ]
        ]

        // Row 4: Monthly Play Time (2/3) | Recent Achievements (1/3)
        Html.div [
            prop.className "grid grid-cols-1 lg:grid-cols-[2fr_1fr] gap-4"
            prop.children [
                sectionCard Icons.calendar "Monthly Play Time" [
                    perGameMonthlyPlayTimeChart data.MonthlyPlayTimePerGame
                ]
                achievements.CollapsedView
            ]
        ]

        // Additional sections below the main layout
        // HLTB comparison chart
        if not (List.isEmpty data.HltbComparisons) then
            sectionCard Icons.hourglass "Your Time vs HLTB" [
                hltbComparisonChart data.HltbComparisons
            ]

        // Ratings and completed per year
        Html.div [
            prop.className "grid grid-cols-1 md:grid-cols-2 gap-4"
            prop.children [
                sectionCard Icons.star "Ratings Distribution" [
                    gameRatingsDistributionChart data.Stats.RatingDistribution
                ]
                if not (List.isEmpty data.Stats.CompletedPerYear) then
                    sectionCard Icons.trophy "Games Retired Per Year" [
                        gamesCompletedPerYearChart data.Stats.CompletedPerYear
                    ]
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
                    // Header line — tabs (underline pattern) + library search, same row
                    headerLine model.ActiveTab dispatch

                    // Tab content
                    if model.IsLoading then
                        loadingView
                    else
                        match model.ActiveTab with
                        | All ->
                            match model.AllTabData with
                            | Some data -> allTabView data model.Expanded dispatch
                            | None -> loadingView
                        | MoviesTab ->
                            match model.MoviesTabData with
                            | Some data -> moviesTabView data model.Expanded dispatch
                            | None -> loadingView
                        | SeriesTab ->
                            match model.SeriesTabData with
                            | Some data -> seriesTabView data model.Expanded dispatch
                            | None -> loadingView
                        | GamesTab ->
                            match model.GamesTabData with
                            | Some data -> gamesTabView data model.Achievements model.Expanded dispatch
                            | None -> loadingView
                ]
            ]
        ]
    ]
