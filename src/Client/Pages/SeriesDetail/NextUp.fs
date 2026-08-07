module Mediatheca.Client.Pages.SeriesDetail.NextUp

open Mediatheca.Shared

/// series-k4zpn: Next Up is the first episode, ordered by (season, episode),
/// that has no watch record and comes strictly *after* the furthest-watched
/// episode — not merely the first unwatched episode overall. A skipped
/// episode must not pin Next Up forever; gaps behind the furthest-watched
/// point are history, not a queue.
///
/// The "frontier" is the max `(SeasonNumber, EpisodeNumber)` tuple (F#'s
/// structural tuple comparison is already lexicographic, unlike SQLite —
/// see the `series_next_up` view in `MetadataCache.fs` for the SQL-side
/// equivalent) among episodes with `IsWatched = true`. With no watched
/// episodes at all, there is no frontier and the rule degenerates to "the
/// first episode overall". With nothing left after the frontier, there is
/// no Next Up, regardless of unwatched gaps sitting behind it.
///
/// Pure and DTO-only — deliberately has no Feliz dependency — so it is
/// shared, unchanged, by both the series-detail hero "Next Up" card and the
/// Episodes-tab "NEXT" badge / "Coming Next" divider (`Views.fs`), which is
/// what mechanically guarantees the two surfaces always agree, and so it is
/// unit-testable without driving the DOM.
///
/// Watch-record scope is whatever the caller's `SeasonDto list` already
/// carries in `EpisodeDto.IsWatched` — for the series-detail page that is
/// the *selected rewatch session*'s scope (deliberately not unified with
/// the server view's union-across-rewatches scope; see the task notes).
let compute (seasons: SeasonDto list) : (int * EpisodeDto) option =
    let ordered =
        seasons
        |> List.collect (fun s -> s.Episodes |> List.map (fun e -> s.SeasonNumber, e))
        |> List.sortBy (fun (sNum, e) -> sNum, e.EpisodeNumber)

    let frontier: (int * int) option =
        ordered
        |> List.filter (fun (_, e) -> e.IsWatched)
        |> List.map (fun (sNum, e) -> sNum, e.EpisodeNumber)
        |> List.sortDescending
        |> List.tryHead

    let isPastFrontier (sNum: int, e: EpisodeDto) =
        match frontier with
        | None -> true
        | Some (fSeason, fEpisode) ->
            sNum > fSeason || (sNum = fSeason && e.EpisodeNumber > fEpisode)

    ordered
    |> List.tryFind (fun (sNum, e) -> not e.IsWatched && isPastFrontier (sNum, e))
