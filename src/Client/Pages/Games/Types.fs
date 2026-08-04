module Mediatheca.Client.Pages.Games.Types

open Mediatheca.Shared

/// games-j6wkr: client-side-only filter over the already-merged
/// `GameListItem.PlayFacets` — mirrors the four badge categories
/// `PlayFacetsDisplay.facetBadges` computes (Solo/Co-op/Versus/Couch). No
/// server or SQL change; filtering happens in `Views.fs` against the DTO
/// field that arrived pre-merged (ADR-0053's `FacetDerivation.merge`).
type PlayFacetFilter =
    | Facet_solo
    | Facet_coop
    | Facet_versus
    | Facet_couch

type Model = {
    Games: GameListItem list
    SearchQuery: string
    StatusFilter: GameStatus option
    FacetFilter: PlayFacetFilter option
    IsLoading: bool
}

type Msg =
    | Load_games
    | Games_loaded of GameListItem list
    | Search_changed of string
    | Status_filter_changed of GameStatus option
    | Facet_filter_changed of PlayFacetFilter option
    | Open_search_modal
