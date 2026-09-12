module Mediatheca.Client.Pages.Dashboard.Types

open Mediatheca.Shared

type DashboardTab =
    | All
    | MoviesTab
    | SeriesTab
    | GamesTab

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

module DashboardCard =
    /// The query an expanded card re-runs with its row limit lifted.
    let query (card: DashboardCard) : DashboardCardQuery =
        match card with
        | AllNextEpisode | SeriesNextUp -> SeriesNextUpQuery
        | AllMoviesToWatch | MoviesToWatch -> MoviesToWatchQuery
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
