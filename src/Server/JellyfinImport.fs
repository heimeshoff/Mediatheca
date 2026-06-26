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

    /// A season/episode metadata row built from Jellyfin to fill a gap TMDB has
    /// not (yet) covered. Aired numbering matches the TMDB-seeded projection, so
    /// no remap layer is needed. `Runtime` is already in minutes.
    type MaterializedEpisode = {
        SeasonNumber: int
        EpisodeNumber: int
        Name: string
        Overview: string
        Runtime: int option
        AirDate: string option
        StillRef: string option
    }

    /// Outcome of materializing missing season/episode metadata from Jellyfin.
    type SeriesMaterializeResult = {
        EpisodesMaterialized: int
        SeasonsMaterialized: int
        Errors: string list
        Failed: bool
    }

    /// Convert a Jellyfin RunTimeTicks value (100-ns ticks) to whole minutes.
    let private ticksToMinutes (ticks: int64) : int =
        int (ticks / 600_000_000L)

    /// Materialize season/episode metadata from Jellyfin for episodes the
    /// TMDB-fed projection lacks (integration-m4k7p). TMDB stays authoritative:
    /// rows are written with provenance `'jellyfin'` (handled by the injected
    /// writers) so a later TMDB refresh enriches them in place. Not gated on
    /// `Played` — present-on-server is enough.
    ///
    /// Each series, and each episode within it, is fault-isolated (mirrors
    /// `syncSeriesWatchHistory`): a bad episode (missing index numbers, a write
    /// error, a thrown exception) is recorded into `Errors` and the loop
    /// continues. A still-image fetch is best-effort — `fetchStill` returning
    /// `None` (or throwing) degrades to `StillRef = None`, never an error.
    ///
    /// - `getExistingEpisodeKeys`   slug -> set of (season, episode) already in the projection
    /// - `getExistingSeasonNumbers` slug -> set of season numbers already in the projection
    /// - `fetchStill`               slug -> season -> episode -> jellyfinItemId -> still_ref option
    /// - `writeSeason`              slug -> season -> Result<unit, string> (synthetic, number-only)
    /// - `writeEpisode`             slug -> MaterializedEpisode -> Result<unit, string>
    let materializeMissingEpisodes
        (seriesBatch: (string * JellyfinBaseItem list) list)
        (getExistingEpisodeKeys: string -> Set<int * int>)
        (getExistingSeasonNumbers: string -> Set<int>)
        (fetchStill: string -> int -> int -> string -> string option)
        (writeSeason: string -> int -> Result<unit, string>)
        (writeEpisode: string -> MaterializedEpisode -> Result<unit, string>)
        : SeriesMaterializeResult =

        let mutable episodesMaterialized = 0
        let mutable seasonsMaterialized = 0
        let mutable errors: string list = []

        for (slug, episodes) in seriesBatch do
            try
                let existingKeys = getExistingEpisodeKeys slug
                // Seasons known to have a row: those already in the projection
                // plus any we synthesize during this pass (avoids a duplicate
                // INSERT for every episode of a freshly-materialized season).
                let mutable ensuredSeasons = getExistingSeasonNumbers slug
                for ep in episodes do
                    match ep.ParentIndexNumber, ep.IndexNumber with
                    | Some seasonNum, Some epNum ->
                        if existingKeys |> Set.contains (seasonNum, epNum) then
                            () // already present (TMDB or earlier materialization) — leave untouched
                        else
                            try
                                // A synthetic season row must exist first or the
                                // detail read (which iterates series_seasons) would
                                // orphan the episode and never render it.
                                let seasonReady =
                                    if ensuredSeasons |> Set.contains seasonNum then
                                        true
                                    else
                                        match writeSeason slug seasonNum with
                                        | Ok () ->
                                            ensuredSeasons <- ensuredSeasons |> Set.add seasonNum
                                            seasonsMaterialized <- seasonsMaterialized + 1
                                            true
                                        | Error e ->
                                            errors <- errors @ [ sprintf "Series '%s' S%02d (season row): %s" slug seasonNum e ]
                                            false
                                if seasonReady then
                                    let stillRef =
                                        try fetchStill slug seasonNum epNum ep.Id
                                        with _ -> None
                                    let mat: MaterializedEpisode = {
                                        SeasonNumber = seasonNum
                                        EpisodeNumber = epNum
                                        Name = ep.Name
                                        Overview = ep.Overview |> Option.defaultValue ""
                                        Runtime = ep.RunTimeTicks |> Option.map ticksToMinutes
                                        AirDate = ep.PremiereDate |> Option.map (fun d -> d.Substring(0, min 10 d.Length))
                                        StillRef = stillRef
                                    }
                                    match writeEpisode slug mat with
                                    | Ok () -> episodesMaterialized <- episodesMaterialized + 1
                                    | Error e ->
                                        errors <- errors @ [ sprintf "Series '%s' S%02dE%02d: %s" slug seasonNum epNum e ]
                            with ex ->
                                errors <- errors @ [ sprintf "Series '%s' S%02dE%02d threw: %s" slug seasonNum epNum ex.Message ]
                    | _ ->
                        // Missing aired numbering — cannot place the episode; record and continue.
                        errors <- errors @ [ sprintf "Series '%s' episode '%s' has no season/episode number; skipped" slug ep.Name ]
            with ex ->
                errors <- errors @ [ sprintf "Series '%s' aborted: %s" slug ex.Message ]

        { EpisodesMaterialized = episodesMaterialized
          SeasonsMaterialized = seasonsMaterialized
          Errors = errors
          Failed = not (List.isEmpty errors) }
