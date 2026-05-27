namespace Mediatheca.Server

/// Fault-isolating core of the Jellyfin series watch-history sync.
///
/// Extracted from `Api.runJellyfinImport` so that a throw on a single series
/// or episode during Phase 2 no longer aborts the entire import (integration-001).
/// The previous structure wrapped the whole import in one try/with: a
/// `SqliteException` escaping `executeCommand` mid-loop bubbled all the way out,
/// silently discarded the partial progress *and* the accumulated error list, and
/// the caller could not distinguish "nothing to sync" from "exploded halfway".
///
/// This module operates purely on already-fetched episode data and an injected
/// `writeEpisode` command executor, so it is fully testable without HTTP or SQLite.
module JellyfinImport =

    open Mediatheca.Server.Jellyfin

    /// Outcome of syncing series episode watch history.
    /// `Failed` is true when ANY series/episode produced an error (a per-item
    /// `Error` result OR an escaping exception) — partial failure is still failure
    /// so it surfaces as `JellyfinSyncStatus.SyncFailed` rather than a silent Ok.
    type SeriesWatchSyncResult = {
        EpisodesAdded: int
        ItemsSkipped: int
        Errors: string list
        Failed: bool
    }

    /// Sync watch history for a batch of (slug, jellyfin episodes) pairs.
    ///
    /// Each series, and each episode write within it, is wrapped so a fault is
    /// recorded into `Errors` and the loop continues to the next item.
    ///
    /// - `getDefaultRewatchId`  slug -> rewatch session id to write into
    /// - `getAlreadyWatched`    slug -> rewatchId -> set of (season, episode) already recorded
    /// - `writeEpisode`         slug -> rewatchId -> season -> episode -> date -> Result<unit, string>
    let syncSeriesWatchHistory
        (seriesBatch: (string * JellyfinBaseItem list) list)
        (getDefaultRewatchId: string -> string)
        (getAlreadyWatched: string -> string -> Set<int * int>)
        (writeEpisode: string -> string -> int -> int -> string -> Result<unit, string>)
        : SeriesWatchSyncResult =

        let mutable episodesAdded = 0
        let mutable itemsSkipped = 0
        let mutable errors: string list = []

        for (slug, episodes) in seriesBatch do
            // Isolate per-series failures (e.g. a throw from getDefaultRewatchId /
            // getAlreadyWatched, or anything unexpected) so one bad series cannot
            // abort the rest of the batch.
            try
                let defaultRewatchId = getDefaultRewatchId slug
                let alreadyWatched = getAlreadyWatched slug defaultRewatchId
                for ep in episodes do
                    let epPlayed = ep.UserData |> Option.map (fun ud -> ud.Played) |> Option.defaultValue false
                    match epPlayed, ep.ParentIndexNumber, ep.IndexNumber with
                    | true, Some seasonNum, Some epNum ->
                        if alreadyWatched |> Set.contains (seasonNum, epNum) then
                            itemsSkipped <- itemsSkipped + 1
                        else
                            let watchDate =
                                ep.UserData
                                |> Option.bind (fun ud -> ud.LastPlayedDate)
                                |> Option.map (fun d -> d.Substring(0, min 10 d.Length))
                                |> Option.defaultValue (System.DateTime.UtcNow.ToString("yyyy-MM-dd"))
                            // Isolate per-episode failures: a Result Error is recorded,
                            // and an escaping exception (e.g. SqliteException) is caught
                            // so the remaining episodes/series still get processed.
                            try
                                match writeEpisode slug defaultRewatchId seasonNum epNum watchDate with
                                | Ok () -> episodesAdded <- episodesAdded + 1
                                | Error e ->
                                    errors <- errors @ [ sprintf "Series '%s' S%02dE%02d: %s" slug seasonNum epNum e ]
                            with ex ->
                                errors <- errors @ [ sprintf "Series '%s' S%02dE%02d threw: %s" slug seasonNum epNum ex.Message ]
                    | _ -> itemsSkipped <- itemsSkipped + 1
            with ex ->
                errors <- errors @ [ sprintf "Series '%s' aborted: %s" slug ex.Message ]

        { EpisodesAdded = episodesAdded
          ItemsSkipped = itemsSkipped
          Errors = errors
          Failed = not (List.isEmpty errors) }
