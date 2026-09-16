module Mediatheca.Client.Pages.Dashboard.Types

open Mediatheca.Shared

type DashboardTab =
    | All
    | MoviesTab
    | SeriesTab
    | GamesTab
    | BooksTab

type AchievementsState =
    | AchievementsNotLoaded
    | AchievementsLoading
    | AchievementsReady of SteamAchievement list
    | AchievementsError of string

/// One expandable, query-backed card on the dashboard. Identity is per card,
/// not per query: the All tab's "Next episode" and the Series tab's "Next Up"
/// both read `SeriesNextUpQuery`, but each grows inside its own tab.
type DashboardCard =
    | AllNextEpisode
    | AllMoviesToWatch
    | AllGamesInFocus
    | MoviesRecentlyWatched
    | MoviesRecentlyAdded
    | MoviesToWatch
    | MoviesTopActors
    | MoviesTopDirectors
    | MoviesTopWatchedWith
    | SeriesNextUp
    | SeriesReturningSoon
    | SeriesRecentlyFinished
    | SeriesRecentlyAbandoned
    | SeriesTopWatchedWith
    | GamesRecentlyPlayed
    | GamesRecentlyAdded
    | GamesUpcoming
    | GamesRecentAchievements
    /// intelligence-dnv2y: the All-tab "Reading" card — In Focus books plus
    /// the 7-day finished linger (mirrors `AllMoviesToWatch`'s split from its
    /// tab's own strict card).
    | AllReading
    /// intelligence-dnv2y: the Books tab's three rails, each strict (no
    /// linger — the All tab's `AllReading` card above carries that).
    | BooksReading
    | BooksFinished
    | BooksAdded

module DashboardCard =
    /// The query an expanded card re-runs with its row limit lifted.
    let query (card: DashboardCard) : DashboardCardQuery =
        match card with
        | AllNextEpisode | SeriesNextUp -> SeriesNextUpQuery
        // intelligence-b1nz5: split from `MoviesToWatch`'s query — the
        // All-tab card also lingers on recently-watched movies; the Movies
        // tab's own card stays strictly unwatched-only.
        | AllMoviesToWatch -> AllMoviesToWatchQuery
        | MoviesToWatch -> MoviesToWatchQuery
        | AllGamesInFocus -> GamesInFocusQuery
        | MoviesRecentlyWatched -> MoviesRecentlyWatchedQuery
        | MoviesRecentlyAdded -> MoviesRecentlyAddedQuery
        | MoviesTopActors -> MoviesTopActorsQuery
        | MoviesTopDirectors -> MoviesTopDirectorsQuery
        | MoviesTopWatchedWith -> MoviesTopWatchedWithQuery
        | SeriesReturningSoon -> SeriesReturningSoonQuery
        | SeriesRecentlyFinished -> SeriesRecentlyFinishedQuery
        | SeriesRecentlyAbandoned -> SeriesRecentlyAbandonedQuery
        | SeriesTopWatchedWith -> SeriesTopWatchedWithQuery
        | GamesRecentlyPlayed -> GamesRecentlyPlayedQuery
        | GamesRecentlyAdded -> GamesRecentlyAddedQuery
        | GamesUpcoming -> GamesUpcomingQuery
        | GamesRecentAchievements -> SteamRecentAchievementsQuery
        | AllReading -> AllCurrentlyReading
        | BooksReading -> BooksCurrentlyReading
        | BooksFinished -> BooksRecentlyFinished
        | BooksAdded -> BooksRecentlyAdded

/// The unlimited items of the expanded card. While the fetch is in flight
/// (and after a failed one) the view keeps showing the tab's own limited
/// items, so expanding is instant and never blanks the card.
type ExpandedItems =
    | ExpandedLoading
    | ExpandedReady of DashboardCardItems
    | ExpandedFailed of string

type ExpandedCard = {
    Card: DashboardCard
    Items: ExpandedItems
}

type Model = {
    ActiveTab: DashboardTab
    AllTabData: DashboardAllTab option
    MoviesTabData: DashboardMoviesTab option
    SeriesTabData: DashboardSeriesTab option
    GamesTabData: DashboardGamesTab option
    BooksTabData: DashboardBooksTab option
    Achievements: AchievementsState
    IsLoading: bool
    IsSyncing: bool
    /// The one card currently grown over the active tab's area, if any.
    Expanded: ExpandedCard option
}

type Msg =
    | SwitchTab of DashboardTab
    | AllTabLoaded of DashboardAllTab
    | MoviesTabLoaded of DashboardMoviesTab
    | SeriesTabLoaded of DashboardSeriesTab
    | GamesTabLoaded of DashboardGamesTab
    | BooksTabLoaded of DashboardBooksTab
    | TabLoadError of string
    | AchievementsLoaded of Result<SteamAchievement list, string>
    | TriggerPlaytimeSync
    | PlaytimeSyncCompleted of Result<PlaytimeSyncResult, string>
    | Open_search_modal
    | ExpandCard of DashboardCard
    | CollapseCard
    /// Tagged with the card it was fetched for, so a reply that lands after a
    /// collapse or after another card took over is dropped, not applied.
    | ExpandedItemsLoaded of DashboardCard * Result<DashboardCardItems, string>
