namespace Mediatheca.Server

/// "Remove local copy" (integration-r4vzm, ADR-0071): projection-only cache
/// invalidation across Jellyfin + qBittorrent, run as a re-derivable
/// plan-then-execute flow -- not a domain event, not a persisted saga. Every
/// function below is pure over injected effects (same idiom as
/// `Jellyfin.withReauthRetry` and `JellyfinImport.syncSeriesWatchHistory`),
/// so the whole failure matrix is unit-testable with plain lambdas.
///
/// Mount roots (`MountRoots`) are a deployment fact, not a user setting --
/// read once from env vars in `Composition.fs` the way `TMDB_API_KEY` is,
/// and handed to this module in a plain config record. They are never
/// seeded into `SettingsStore`.
module LocalCopyRemoval =

    open Mediatheca.Shared
    open Mediatheca.Server.Jellyfin
    open Mediatheca.Server.Qbittorrent

    type MountRoots = {
        JellyfinRoot: string
        QbittorrentRoot: string
    }

    let defaultMountRoots: MountRoots = {
        JellyfinRoot = "/media"
        QbittorrentRoot = "/downloads"
    }

    // --- Pure path arithmetic ---

    let private trimSlash (s: string) = s.TrimEnd('/')

    /// Segment-boundary aware ancestor test: `/x/Dune` is NOT an ancestor of
    /// `/x/Dune 2` (a naive `StartsWith` would wrongly say it is). Strict --
    /// a path is never its own ancestor.
    let isAncestorOf (a: string) (b: string) : bool =
        let a = trimSlash a
        let b = trimSlash b
        a <> b && b.StartsWith(a + "/")

    /// Prefix-swap a Jellyfin-side path onto qBittorrent's mount, or `None`
    /// when the path is not under `jellyfinRoot` at all -- a broken
    /// deployment assumption that must fail loudly (ADR-0071).
    let mapJellyfinPath (jellyfinRoot: string, qbittorrentRoot: string) (path: string) : string option =
        let jRoot = trimSlash jellyfinRoot
        let qRoot = trimSlash qbittorrentRoot
        let p = trimSlash path
        if p = jRoot then Some qRoot
        elif p.StartsWith(jRoot + "/") then Some (qRoot + p.Substring(jRoot.Length))
        else None

    /// Number of path segments `path` sits below `root` (0 when equal).
    /// A path not under `root` at all is treated as maximally deep so this
    /// guard alone never mistakes it for a shallow/library-root scope.
    let private segmentDepth (root: string) (path: string) : int =
        let root = trimSlash root
        let path = trimSlash path
        if path = root then 0
        elif path.StartsWith(root + "/") then
            path.Substring(root.Length + 1).Split('/') |> Array.length
        else
            System.Int32.MaxValue

    let private parentOf (path: string) : string =
        let p = trimSlash path
        let idx = p.LastIndexOf('/')
        if idx <= 0 then p else p.Substring(0, idx)

    /// What Jellyfin will actually remove -- what the live torrents are
    /// matched against. A series' scope is its show folder. A movie's scope
    /// is the parent folder of its file UNLESS that parent is a library
    /// root (exactly one segment below `root`, e.g. `/downloads/movies`), in
    /// which case the movie is a bare file in a mixed folder and the scope
    /// is the file itself.
    let deletionScope (root: string) (target: LocalCopyTarget) (mappedPath: string) : string =
        match target with
        | SeriesTarget _ -> trimSlash mappedPath
        | MovieTarget _ ->
            let parent = parentOf mappedPath
            if segmentDepth root parent <= 1 then
                trimSlash mappedPath
            else
                parent

    // --- Torrent matching ---

    /// Match live torrents against the deletion scope `D`. A torrent whose
    /// content path `C` equals `D` or sits inside it is `ExactOrInside`
    /// (the everyday case -- one folder, one movie). A torrent whose OWN
    /// folder is a strict ancestor of `D` is a `PackAncestor`: deleting it
    /// with files would also delete sibling files outside `D`, so its extra
    /// file count is computed from `filesByHash` (only consulted for this
    /// branch -- `Qbittorrent.listFiles` names joined onto `SavePath`).
    /// Torrents matching neither are dropped (an empty result is the
    /// `FilesOnly` case, decided by the caller). Fully pure: no IO.
    let matchTorrents (scope: string) (filesByHash: Map<string, string list>) (torrents: TorrentInfo list) : (TorrentInfo * MatchKind) list =
        let scope = trimSlash scope
        torrents
        |> List.choose (fun t ->
            let c = trimSlash t.ContentPath
            if c = scope || isAncestorOf scope c then
                Some (t, ExactOrInside)
            elif isAncestorOf c scope then
                let files = filesByHash |> Map.tryFind t.Hash |> Option.defaultValue []
                let savePath = trimSlash t.SavePath
                let extra =
                    files
                    |> List.filter (fun name ->
                        let full = trimSlash (savePath + "/" + name.TrimStart('/'))
                        not (full = scope || isAncestorOf scope full))
                    |> List.length
                Some (t, PackAncestor extra)
            else
                None)

    // --- Seed risk ---

    /// Builder adjusts to IPTorrents' actual rule (ADR-0071 point 2:
    /// warn, never refuse -- named constants, not settings).
    let minRatio = 1.0
    let minSeedingDays = 14.0

    /// qBittorrent's sentinels are handled: `ratio = -1` (infinite ratio) is
    /// always `Safe`; the 9999 cap is always `Safe`.
    let classifySeedRisk (ratio: float) (seedingTime: System.TimeSpan) : SeedRisk =
        if ratio < 0.0 then Safe
        elif ratio >= 9999.0 then Safe
        elif ratio < minRatio && seedingTime.TotalDays < minSeedingDays then
            HitAndRun (sprintf "ratio %.2f (< %.1f) after only %.1f day(s) seeding (< %d)" ratio minRatio seedingTime.TotalDays (int minSeedingDays))
        else
            Safe

    let private buildPlannedTorrent (t: TorrentInfo, kind: MatchKind) : PlannedTorrent =
        let risk = classifySeedRisk t.Ratio t.SeedingTime
        { Hash = t.Hash
          Name = t.Name
          Ratio = t.Ratio
          SeedingTimeDays = t.SeedingTime.TotalDays
          Match = kind
          Risk = risk
          PreTicked =
            match risk, kind with
            | HitAndRun _, _ -> false
            | _, PackAncestor _ -> false
            | Safe, ExactOrInside -> true }

    /// Fetches file listings only for torrents whose own folder is a strict
    /// ancestor of `scope` (the only branch `matchTorrents` ever consults
    /// them for) -- the everyday one-folder-one-movie torrent never pays for
    /// a `torrents/files` round trip.
    let private resolveFilesMap (listFiles: TorrentInfo -> Async<Result<string list, string>>) (scope: string) (torrents: TorrentInfo list) : Async<Map<string, string list>> =
        async {
            let candidates = torrents |> List.filter (fun t -> isAncestorOf (trimSlash t.ContentPath) scope)
            let! results =
                candidates
                |> List.map (fun t -> async {
                    let! r = listFiles t
                    return (t.Hash, r |> Result.defaultValue [])
                })
                |> Async.Parallel
            return results |> Array.toList |> Map.ofList
        }

    // --- Plan ---

    type PlanEffects = {
        /// Slug -> stored Jellyfin id (a synchronous `JellyfinStore` read).
        ResolveJellyfinId: unit -> string option
        FetchItem: string -> Async<Result<JellyfinBaseItem option, string>>
        ListTorrents: unit -> Async<Result<TorrentInfo list, string>>
        ListFiles: TorrentInfo -> Async<Result<string list, string>>
    }

    /// Resolves the Jellyfin item, maps the mount prefix, derives the
    /// deletion scope, and matches live torrents into a `RemovalPlan`. A
    /// deletion scope fewer than two segments below `mountRoots` (the mount
    /// root itself, or a library root) is refused outright -- a broken
    /// deployment assumption must fail loudly, never silently fall back to
    /// a Jellyfin-only delete that would orphan a seeding torrent.
    let planLocalCopyRemoval (mountRoots: MountRoots) (effects: PlanEffects) (target: LocalCopyTarget) : Async<Result<RemovalPlan, string>> =
        async {
            match effects.ResolveJellyfinId () with
            | None ->
                return Ok { Path = ""; Case = AlreadyGoneInJellyfin; Torrents = [] }
            | Some jellyfinId ->
                let! itemResult = effects.FetchItem jellyfinId
                match itemResult with
                | Error e ->
                    return Error e
                | Ok None ->
                    return Ok { Path = ""; Case = AlreadyGoneInJellyfin; Torrents = [] }
                | Ok (Some item) ->
                    match item.Path with
                    | None ->
                        return Error "the Jellyfin item has no Path"
                    | Some path ->
                        match mapJellyfinPath (mountRoots.JellyfinRoot, mountRoots.QbittorrentRoot) path with
                        | None ->
                            return Ok { Path = path; Case = PathOutsideMountMap; Torrents = [] }
                        | Some mappedPath ->
                            let scope = deletionScope mountRoots.QbittorrentRoot target mappedPath
                            if segmentDepth mountRoots.QbittorrentRoot scope < 2 then
                                return Error (sprintf "refusing: the deletion scope '%s' is the mount root or a library root" scope)
                            else
                                let! torrentsResult = effects.ListTorrents ()
                                match torrentsResult with
                                | Error e ->
                                    return Error e
                                | Ok liveTorrents ->
                                    let! filesMap = resolveFilesMap effects.ListFiles scope liveTorrents
                                    let matched = matchTorrents scope filesMap liveTorrents
                                    let torrents = matched |> List.map buildPlannedTorrent
                                    let case = if List.isEmpty torrents then FilesOnly else TorrentsPresent
                                    return Ok { Path = mappedPath; Case = case; Torrents = torrents }
        }

    // --- Execute ---

    type ExecuteEffects = {
        /// Guards against interleaving with `JellyfinSync`'s `clearAll` +
        /// repopulate (ADR-0071 point 5) -- `JellyfinSync.getSyncStatus () =
        /// SyncInProgress` already reads the flag under the sync lock.
        IsSyncInProgress: unit -> bool
        /// Imports the target's Jellyfin play state through the existing
        /// event-producing paths (`Movies.Record_watch_session` /
        /// `Series.Mark_episode_watched`) before any delete -- Jellyfin
        /// discards user data with the item (ADR-0043 / ADR-0071).
        PreserveWatchHistory: unit -> Async<Result<unit, string>>
        /// Re-resolves the deletion scope from a FRESH Jellyfin fetch.
        /// `None` means the item is already gone or its path is outside the
        /// mount map -- either way there is nothing left to match torrents
        /// against.
        ResolveScope: unit -> Async<Result<string option, string>>
        ListTorrents: unit -> Async<Result<TorrentInfo list, string>>
        ListFiles: TorrentInfo -> Async<Result<string list, string>>
        DeleteTorrents: string list -> Async<Result<unit, string>>
        DeleteJellyfinItem: unit -> Async<Result<unit, string>>
        /// `Ok true` when the Jellyfin item is confirmed gone (404) AND none
        /// of the acknowledged hashes remain in a fresh `torrents/info`.
        VerifyGone: unit -> Async<Result<bool, string>>
        ClearLinks: unit -> unit
    }

    /// Fixed order, each step idempotent (ADR-0071): PreserveWatchHistory ->
    /// ResolvePath -> MatchTorrents -> DeleteTorrents -> DeleteJellyfinItem
    /// -> Verify -> ClearLinks. Aborts at the first failing step; links
    /// clear only after `Verify` succeeds.
    ///
    /// `MatchTorrents` closes the time-of-check/time-of-use window: the
    /// hash set matched against a FRESH torrent list must equal
    /// `acknowledgedHashes` exactly, or the step fails with nothing
    /// deleted. `DeleteTorrents` is skipped when nothing matched (the
    /// `FilesOnly` case).
    let execute (effects: ExecuteEffects) (acknowledgedHashes: string list) : Async<RemovalOutcome> =
        async {
            if effects.IsSyncInProgress () then
                return { Completed = []; Failure = Some (PreserveWatchHistory, "refused: a Jellyfin sync is in progress -- try again once it finishes") }
            else
                let! preserveResult = effects.PreserveWatchHistory ()
                match preserveResult with
                | Error e ->
                    return { Completed = []; Failure = Some (PreserveWatchHistory, e) }
                | Ok () ->
                    let completed1 = [ PreserveWatchHistory ]
                    let! scopeResult = effects.ResolveScope ()
                    match scopeResult with
                    | Error e ->
                        return { Completed = completed1; Failure = Some (ResolvePath, e) }
                    | Ok None ->
                        return { Completed = completed1; Failure = Some (ResolvePath, "could not resolve the deletion scope -- the Jellyfin item is missing or its path is outside the mount map") }
                    | Ok (Some scope) ->
                        let completed2 = completed1 @ [ ResolvePath ]
                        let! torrentsResult = effects.ListTorrents ()
                        match torrentsResult with
                        | Error e ->
                            return { Completed = completed2; Failure = Some (MatchTorrents, e) }
                        | Ok liveTorrents ->
                            let! filesMap = resolveFilesMap effects.ListFiles scope liveTorrents
                            let matched = matchTorrents scope filesMap liveTorrents
                            let matchedHashes = matched |> List.map (fun (t, _) -> t.Hash) |> Set.ofList
                            let ackSet = Set.ofList acknowledgedHashes
                            if matchedHashes <> ackSet then
                                return { Completed = completed2; Failure = Some (MatchTorrents, "the torrent list changed since the dialog was shown -- reopen it") }
                            else
                                let completed3 = completed2 @ [ MatchTorrents ]
                                let! deleteResult =
                                    if List.isEmpty matched then async { return Ok () }
                                    else effects.DeleteTorrents acknowledgedHashes
                                match deleteResult with
                                | Error e ->
                                    return { Completed = completed3; Failure = Some (DeleteTorrents, e) }
                                | Ok () ->
                                    let completed4 = completed3 @ [ DeleteTorrents ]
                                    let! jfDeleteResult = effects.DeleteJellyfinItem ()
                                    match jfDeleteResult with
                                    | Error e ->
                                        return { Completed = completed4; Failure = Some (DeleteJellyfinItem, e) }
                                    | Ok () ->
                                        let completed5 = completed4 @ [ DeleteJellyfinItem ]
                                        let! verifyResult = effects.VerifyGone ()
                                        match verifyResult with
                                        | Error e ->
                                            return { Completed = completed5; Failure = Some (Verify, e) }
                                        | Ok false ->
                                            return { Completed = completed5; Failure = Some (Verify, "verification failed: the Jellyfin item or a torrent is still present") }
                                        | Ok true ->
                                            let completed6 = completed5 @ [ Verify ]
                                            effects.ClearLinks ()
                                            return { Completed = completed6 @ [ ClearLinks ]; Failure = None }
        }
