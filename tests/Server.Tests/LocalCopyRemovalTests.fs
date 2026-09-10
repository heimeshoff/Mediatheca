module Mediatheca.Tests.LocalCopyRemovalTests

/// integration-r4vzm (ADR-0071): "Remove local copy" server-side flow. Every
/// function in `LocalCopyRemoval.fs` is pure over injected effects, so the
/// whole matrix -- path mapping, deletion-scope, torrent matching, seed
/// risk, and the `plan`/`execute` orchestrators -- is testable with plain
/// lambdas and fixture data, no HTTP or SQLite.

open System
open Expecto
open Mediatheca.Shared
open Mediatheca.Server
open Mediatheca.Server.Jellyfin
open Mediatheca.Server.Qbittorrent
open Mediatheca.Server.LocalCopyRemoval

// --- Fixtures ---

let private mkTorrent (hash: string) (name: string) (savePath: string) (contentPath: string) (ratio: float) (seedingDays: float) : TorrentInfo = {
    Hash = hash
    Name = name
    SavePath = savePath
    ContentPath = contentPath
    Ratio = ratio
    SeedingTime = TimeSpan.FromDays(seedingDays)
    State = "uploading"
}

let private mkItem (path: string option) : JellyfinBaseItem = {
    Id = "jf-item"
    Name = "Dune"
    Type = "Movie"
    ProductionYear = None
    RunTimeTicks = None
    Genres = []
    Overview = None
    ProviderIds = { Tmdb = None; Imdb = None }
    UserData = None
    SeriesName = None
    SeriesId = None
    IndexNumber = None
    ParentIndexNumber = None
    PremiereDate = None
    PrimaryImageTag = None
    Path = path
}

let private movieTarget = MovieTarget "dune-2021"
let private seriesTarget = SeriesTarget "the-boys"

let private roots = { JellyfinRoot = "/media"; QbittorrentRoot = "/downloads" }

[<Tests>]
let mapJellyfinPathTests =
    testList "LocalCopyRemoval.mapJellyfinPath" [

        testCase "default roots -- maps a movie file under /media onto /downloads" <| fun _ ->
            let result = mapJellyfinPath (defaultMountRoots.JellyfinRoot, defaultMountRoots.QbittorrentRoot) "/media/movies/Dune (2021)/Dune.2021.mkv"
            Expect.equal result (Some "/downloads/movies/Dune (2021)/Dune.2021.mkv") "Prefix swapped"

        testCase "default roots -- the root itself maps onto the other root" <| fun _ ->
            let result = mapJellyfinPath (defaultMountRoots.JellyfinRoot, defaultMountRoots.QbittorrentRoot) "/media"
            Expect.equal result (Some "/downloads") "Root maps to root"

        testCase "custom roots -- maps correctly" <| fun _ ->
            let result = mapJellyfinPath ("/mnt/jellyfin-media", "/mnt/qb-downloads") "/mnt/jellyfin-media/shows/The Boys"
            Expect.equal result (Some "/mnt/qb-downloads/shows/The Boys") "Custom prefix swapped"

        testCase "a path not under the Jellyfin root yields None (PathOutsideMountMap upstream)" <| fun _ ->
            let result = mapJellyfinPath (defaultMountRoots.JellyfinRoot, defaultMountRoots.QbittorrentRoot) "/mnt/other/Dune.2021.mkv"
            Expect.equal result None "Path outside the mount map"

        testCase "a sibling folder that merely shares the root prefix textually is NOT matched" <| fun _ ->
            // /mediaX is not /media -- must not be treated as under it.
            let result = mapJellyfinPath (defaultMountRoots.JellyfinRoot, defaultMountRoots.QbittorrentRoot) "/mediaXtra/movies/Dune.mkv"
            Expect.equal result None "No false-positive prefix match"
    ]

[<Tests>]
let isAncestorOfTests =
    testList "LocalCopyRemoval.isAncestorOf" [

        testCase "a true ancestor is recognized" <| fun _ ->
            Expect.isTrue (isAncestorOf "/downloads/shows" "/downloads/shows/The Boys") "Parent is an ancestor of its child"

        testCase "segment-boundary aware -- /x/Dune is NOT an ancestor of /x/Dune 2" <| fun _ ->
            Expect.isFalse (isAncestorOf "/x/Dune" "/x/Dune 2") "Textual prefix without a segment boundary is not ancestry"

        testCase "a path is never its own ancestor" <| fun _ ->
            Expect.isFalse (isAncestorOf "/downloads/movies/Dune" "/downloads/movies/Dune") "Strict ancestry only"

        testCase "an unrelated path is not an ancestor" <| fun _ ->
            Expect.isFalse (isAncestorOf "/downloads/shows" "/downloads/movies/Dune") "No relation"
    ]

[<Tests>]
let deletionScopeTests =
    testList "LocalCopyRemoval.deletionScope" [

        testCase "a movie in its own folder -- scope is the folder" <| fun _ ->
            let scope = deletionScope "/downloads" movieTarget "/downloads/movies/Dune (2021)/Dune.2021.mkv"
            Expect.equal scope "/downloads/movies/Dune (2021)" "Scope is the movie's own folder"

        testCase "a bare-file movie directly in a library root -- scope is the file" <| fun _ ->
            let scope = deletionScope "/downloads" movieTarget "/downloads/movies/Dune.2021.mkv"
            Expect.equal scope "/downloads/movies/Dune.2021.mkv" "Scope is the bare file, not the library root"

        testCase "a series -- scope is the show folder" <| fun _ ->
            let scope = deletionScope "/downloads" seriesTarget "/downloads/shows/The Boys"
            Expect.equal scope "/downloads/shows/The Boys" "Scope is the whole show folder"
    ]

[<Tests>]
let matchTorrentsTests =
    testList "LocalCopyRemoval.matchTorrents" [

        testCase "a movie in its own folder -- ExactOrInside" <| fun _ ->
            let scope = "/downloads/movies/Dune (2021)"
            let t = mkTorrent "h1" "Dune 2021" "/downloads/movies/Dune (2021)" "/downloads/movies/Dune (2021)" 1.5 20.0
            let matched = matchTorrents scope Map.empty [ t ]
            Expect.equal matched [ (t, ExactOrInside) ] "Content path equals the scope"

        testCase "a bare-file movie -- ExactOrInside" <| fun _ ->
            let scope = "/downloads/movies/Dune.2021.mkv"
            let t = mkTorrent "h1" "Dune 2021" "/downloads/movies" "/downloads/movies/Dune.2021.mkv" 1.5 20.0
            let matched = matchTorrents scope Map.empty [ t ]
            Expect.equal matched [ (t, ExactOrInside) ] "Content path equals the bare-file scope"

        testCase "a multi-torrent series -- every episode torrent is ExactOrInside" <| fun _ ->
            let scope = "/downloads/shows/The Boys"
            let t1 = mkTorrent "h1" "The.Boys.S01E01" "/downloads/shows/The Boys/Season 01" "/downloads/shows/The Boys/Season 01/E01.mkv" 2.0 30.0
            let t2 = mkTorrent "h2" "The.Boys.S01E02" "/downloads/shows/The Boys/Season 01" "/downloads/shows/The Boys/Season 01/E02.mkv" 2.0 30.0
            let matched = matchTorrents scope Map.empty [ t1; t2 ]
            Expect.equal (matched |> List.map snd) [ ExactOrInside; ExactOrInside ] "Both episode torrents match inside the show folder"

        testCase "two cross-seeds on the same folder -- both listed" <| fun _ ->
            let scope = "/downloads/movies/Dune (2021)"
            let t1 = mkTorrent "h1" "Dune 2021 [Group A]" "/downloads/movies/Dune (2021)" "/downloads/movies/Dune (2021)" 3.0 40.0
            let t2 = mkTorrent "h2" "Dune 2021 [Group B]" "/downloads/movies/Dune (2021)" "/downloads/movies/Dune (2021)" 0.5 40.0
            let matched = matchTorrents scope Map.empty [ t1; t2 ]
            Expect.equal (matched |> List.map (fun (t, _) -> t.Hash) |> Set.ofList) (Set.ofList [ "h1"; "h2" ]) "Both cross-seeds matched"

        testCase "a pack torrent whose content path is a strict ancestor of the scope -- PackAncestor with the right extra-file count" <| fun _ ->
            let scope = "/downloads/movies-mixed/Dune (2021)"
            let t = mkTorrent "h1" "Movie Pack Vol 3" "/downloads/movies-mixed" "/downloads/movies-mixed" 1.0 10.0
            let files = [ "Dune (2021)/Dune.2021.mkv"; "Dune (2021)/Dune.2021.srt"; "Arrival (2016)/Arrival.mkv"; "Legend (2015)/Legend.mkv" ]
            let matched = matchTorrents scope (Map.ofList [ "h1", files ]) [ t ]
            match matched with
            | [ (_, PackAncestor n) ] -> Expect.equal n 2 "2 files (Arrival, Legend) sit outside the Dune scope"
            | other -> failtestf "Expected a single PackAncestor match, got %A" other

        testCase "zero matches -- empty result (FilesOnly at the plan level)" <| fun _ ->
            let scope = "/downloads/movies/Dune (2021)"
            let t = mkTorrent "h1" "Unrelated" "/downloads/movies/Arrival (2016)" "/downloads/movies/Arrival (2016)" 1.0 10.0
            let matched = matchTorrents scope Map.empty [ t ]
            Expect.isEmpty matched "No torrent matches this scope"
    ]

[<Tests>]
let classifySeedRiskTests =
    testList "LocalCopyRemoval.classifySeedRisk" [

        testCase "ratio boundary -- exactly minRatio is Safe" <| fun _ ->
            Expect.equal (classifySeedRisk minRatio (TimeSpan.FromDays(1.0))) Safe "At the ratio boundary, not below it"

        testCase "ratio boundary -- just below minRatio, but seeded long enough, is Safe" <| fun _ ->
            Expect.equal (classifySeedRisk (minRatio - 0.01) (TimeSpan.FromDays(minSeedingDays))) Safe "Seeding-days boundary satisfied"

        testCase "seedingDays boundary -- exactly minSeedingDays is Safe" <| fun _ ->
            Expect.equal (classifySeedRisk 0.1 (TimeSpan.FromDays(minSeedingDays))) Safe "At the seeding-days boundary, not below it"

        testCase "flagged case -- below both thresholds is HitAndRun" <| fun _ ->
            match classifySeedRisk 0.2 (TimeSpan.FromDays(2.0)) with
            | HitAndRun _ -> ()
            | Safe -> failtest "Expected HitAndRun below both thresholds"

        testCase "ratio = -1 sentinel (infinite ratio) is always Safe" <| fun _ ->
            Expect.equal (classifySeedRisk -1.0 (TimeSpan.FromDays(0.1))) Safe "Infinite-ratio sentinel is Safe regardless of seeding time"

        testCase "ratio = 9999 cap sentinel is always Safe" <| fun _ ->
            Expect.equal (classifySeedRisk 9999.0 (TimeSpan.FromDays(0.1))) Safe "Capped-ratio sentinel is Safe regardless of seeding time"
    ]

// --- plan ---

let private planEffects
    (resolveId: unit -> string option)
    (fetchItem: string -> Async<Result<JellyfinBaseItem option, string>>)
    (listTorrents: unit -> Async<Result<TorrentInfo list, string>>)
    (listFiles: TorrentInfo -> Async<Result<string list, string>>)
    : PlanEffects =
    { ResolveJellyfinId = resolveId
      FetchItem = fetchItem
      ListTorrents = listTorrents
      ListFiles = listFiles }

[<Tests>]
let planTests =
    testList "LocalCopyRemoval.planLocalCopyRemoval" [

        testCase "no stored Jellyfin id -- AlreadyGoneInJellyfin, no fetch attempted" <| fun _ ->
            let mutable fetchCalled = false
            let effects = planEffects (fun () -> None) (fun _ -> fetchCalled <- true; async { return Ok None }) (fun () -> async { return Ok [] }) (fun _ -> async { return Ok [] })
            let result = planLocalCopyRemoval roots effects movieTarget |> Async.RunSynchronously
            Expect.equal result (Ok { Path = ""; Case = AlreadyGoneInJellyfin; Torrents = [] }) "Already gone -- no torrents"
            Expect.isFalse fetchCalled "Never fetches when there is no id to fetch"

        testCase "item fetch returns None (404) -- AlreadyGoneInJellyfin" <| fun _ ->
            let effects = planEffects (fun () -> Some "jf-1") (fun _ -> async { return Ok None }) (fun () -> async { return Ok [] }) (fun _ -> async { return Ok [] })
            let result = planLocalCopyRemoval roots effects movieTarget |> Async.RunSynchronously
            Expect.equal result (Ok { Path = ""; Case = AlreadyGoneInJellyfin; Torrents = [] }) "404 on the item -- already gone"

        testCase "a path outside the mount map -- PathOutsideMountMap" <| fun _ ->
            let effects = planEffects (fun () -> Some "jf-1") (fun _ -> async { return Ok (Some (mkItem (Some "/other/Dune.mkv"))) }) (fun () -> async { return Ok [] }) (fun _ -> async { return Ok [] })
            let result = planLocalCopyRemoval roots effects movieTarget |> Async.RunSynchronously
            match result with
            | Ok { Case = PathOutsideMountMap; Torrents = [] } -> ()
            | other -> failtestf "Expected PathOutsideMountMap, got %A" other

        testCase "rejects a deletion scope that is the mount root itself" <| fun _ ->
            let effects = planEffects (fun () -> Some "jf-1") (fun _ -> async { return Ok (Some (mkItem (Some "/media"))) }) (fun () -> async { return Ok [] }) (fun _ -> async { return Ok [] })
            let result = planLocalCopyRemoval roots effects seriesTarget |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringContains msg "mount root" "Refuses with a clear reason"
            | Ok _ -> failtest "Expected a refusal for a root-level scope"

        testCase "rejects a deletion scope that is a library root (one segment below the mount root)" <| fun _ ->
            // A series whose Path IS the library root itself, e.g. a
            // misconfigured Jellyfin library pointed straight at /media/shows.
            let effects = planEffects (fun () -> Some "jf-1") (fun _ -> async { return Ok (Some (mkItem (Some "/media/shows"))) }) (fun () -> async { return Ok [] }) (fun _ -> async { return Ok [] })
            let result = planLocalCopyRemoval roots effects seriesTarget |> Async.RunSynchronously
            match result with
            | Error msg -> Expect.stringContains msg "library root" "Refuses with a clear reason"
            | Ok _ -> failtest "Expected a refusal for a library-root scope"

        testCase "a bare-file movie directly in a library root is NOT rejected (the everyday mixed-folder case)" <| fun _ ->
            let effects = planEffects (fun () -> Some "jf-1") (fun _ -> async { return Ok (Some (mkItem (Some "/media/movies/Dune.2021.mkv"))) }) (fun () -> async { return Ok [] }) (fun _ -> async { return Ok [] })
            let result = planLocalCopyRemoval roots effects movieTarget |> Async.RunSynchronously
            match result with
            | Ok { Case = FilesOnly } -> ()
            | other -> failtestf "Expected a valid FilesOnly plan (no torrents fixture), got %A" other

        testCase "matched torrents build PlannedTorrent rows with correct PreTicked" <| fun _ ->
            let safeTorrent = mkTorrent "h1" "Dune 2021" "/downloads/movies/Dune (2021)" "/downloads/movies/Dune (2021)" 3.0 30.0
            let hitAndRunTorrent = mkTorrent "h2" "Dune 2021 [rare group]" "/downloads/movies/Dune (2021)" "/downloads/movies/Dune (2021)" 0.1 1.0
            // Its OWN folder is "/downloads/movies" -- a strict ancestor of the
            // "/downloads/movies/Dune (2021)" scope, so it's a pack.
            let packTorrent = mkTorrent "h3" "Movie Pack" "/downloads/movies" "/downloads/movies" 3.0 30.0
            let effects =
                planEffects
                    (fun () -> Some "jf-1")
                    (fun _ -> async { return Ok (Some (mkItem (Some "/media/movies/Dune (2021)/Dune.2021.mkv"))) })
                    (fun () -> async { return Ok [ safeTorrent; hitAndRunTorrent; packTorrent ] })
                    (fun t -> async { return Ok (if t.Hash = "h3" then [ "Dune (2021)/Dune.2021.mkv"; "Other Movie/other.mkv" ] else []) })
            let result = planLocalCopyRemoval roots effects movieTarget |> Async.RunSynchronously
            match result with
            | Ok plan ->
                Expect.equal plan.Case TorrentsPresent "Torrents matched"
                let byHash = plan.Torrents |> List.map (fun t -> t.Hash, t) |> Map.ofList
                Expect.isTrue (Map.find "h1" byHash).PreTicked "Safe + ExactOrInside -> pre-ticked"
                Expect.isFalse (Map.find "h2" byHash).PreTicked "HitAndRun -> never pre-ticked"
                Expect.isFalse (Map.find "h3" byHash).PreTicked "PackAncestor -> never pre-ticked"
                match (Map.find "h3" byHash).Match with
                | PackAncestor n -> Expect.equal n 1 "One file (Other Movie) outside the scope"
                | other -> failtestf "Expected PackAncestor, got %A" other
            | Error e -> failtestf "Expected a plan, got Error %s" e

        testCase "no matched torrents -- FilesOnly" <| fun _ ->
            let effects =
                planEffects
                    (fun () -> Some "jf-1")
                    (fun _ -> async { return Ok (Some (mkItem (Some "/media/movies/Dune (2021)/Dune.2021.mkv"))) })
                    (fun () -> async { return Ok [] })
                    (fun _ -> async { return Ok [] })
            let result = planLocalCopyRemoval roots effects movieTarget |> Async.RunSynchronously
            match result with
            | Ok { Case = FilesOnly; Torrents = [] } -> ()
            | other -> failtestf "Expected FilesOnly, got %A" other
    ]

// --- execute ---

let private baseExecuteEffects () =
    { IsSyncInProgress = fun () -> false
      PreserveWatchHistory = fun () -> async { return Ok () }
      ResolveScope = fun () -> async { return Ok (Some "/downloads/movies/Dune (2021)") }
      ListTorrents = fun () -> async { return Ok [ mkTorrent "h1" "Dune 2021" "/downloads/movies/Dune (2021)" "/downloads/movies/Dune (2021)" 3.0 30.0 ] }
      ListFiles = fun _ -> async { return Ok [] }
      DeleteTorrents = fun _ -> async { return Ok () }
      DeleteJellyfinItem = fun () -> async { return Ok () }
      VerifyGone = fun () -> async { return Ok true }
      ClearLinks = fun () -> () }

[<Tests>]
let executeTests =
    testList "LocalCopyRemoval.execute" [

        testCase "full success -- every step completes in order" <| fun _ ->
            let mutable clearCalled = false
            let effects = { baseExecuteEffects () with ClearLinks = fun () -> clearCalled <- true }
            let outcome = execute effects [ "h1" ] |> Async.RunSynchronously
            Expect.equal outcome.Completed [ PreserveWatchHistory; ResolvePath; MatchTorrents; DeleteTorrents; DeleteJellyfinItem; Verify; ClearLinks ] "All seven steps completed in order"
            Expect.isNone outcome.Failure "No failure"
            Expect.isTrue clearCalled "ClearLinks invoked"

        testCase "the watch-history lambda fires before any delete lambda" <| fun _ ->
            let order = ResizeArray<string>()
            let effects =
                { baseExecuteEffects () with
                    PreserveWatchHistory = fun () -> async { order.Add("preserve"); return Ok () }
                    DeleteTorrents = fun _ -> async { order.Add("deleteTorrents"); return Ok () }
                    DeleteJellyfinItem = fun () -> async { order.Add("deleteJellyfin"); return Ok () } }
            execute effects [ "h1" ] |> Async.RunSynchronously |> ignore
            Expect.equal (order |> List.ofSeq) [ "preserve"; "deleteTorrents"; "deleteJellyfin" ] "Preserve runs before both deletes"

        testCase "a watch-history failure aborts before any delete" <| fun _ ->
            let mutable deleteCalled = false
            let effects =
                { baseExecuteEffects () with
                    PreserveWatchHistory = fun () -> async { return Error "Jellyfin unreachable" }
                    DeleteTorrents = fun _ -> async { deleteCalled <- true; return Ok () } }
            let outcome = execute effects [ "h1" ] |> Async.RunSynchronously
            Expect.equal outcome.Completed [] "Nothing completed"
            Expect.equal outcome.Failure (Some (PreserveWatchHistory, "Jellyfin unreachable")) "Fails at PreserveWatchHistory"
            Expect.isFalse deleteCalled "Delete never invoked"

        testCase "an acknowledged set that differs from the fresh match set fails at MatchTorrents with nothing deleted" <| fun _ ->
            let mutable deleteCalled = false
            let effects = { baseExecuteEffects () with DeleteTorrents = fun _ -> async { deleteCalled <- true; return Ok () } }
            // Only h1 is live; the caller acknowledged h1 AND a stale h2.
            let outcome = execute effects [ "h1"; "h2" ] |> Async.RunSynchronously
            Expect.equal outcome.Completed [ PreserveWatchHistory; ResolvePath ] "Stops right before MatchTorrents completes"
            match outcome.Failure with
            | Some (MatchTorrents, msg) -> Expect.stringContains msg "changed" "Names the TOCTOU condition"
            | other -> failtestf "Expected a MatchTorrents failure, got %A" other
            Expect.isFalse deleteCalled "Nothing deleted"

        testCase "a torrent-delete failure aborts before the Jellyfin delete" <| fun _ ->
            let mutable jellyfinDeleteCalled = false
            let effects =
                { baseExecuteEffects () with
                    DeleteTorrents = fun _ -> async { return Error "qBittorrent unreachable" }
                    DeleteJellyfinItem = fun () -> async { jellyfinDeleteCalled <- true; return Ok () } }
            let outcome = execute effects [ "h1" ] |> Async.RunSynchronously
            Expect.equal outcome.Completed [ PreserveWatchHistory; ResolvePath; MatchTorrents ] "Stops after MatchTorrents"
            Expect.equal outcome.Failure (Some (DeleteTorrents, "qBittorrent unreachable")) "Fails at DeleteTorrents"
            Expect.isFalse jellyfinDeleteCalled "Jellyfin delete never invoked"

        testCase "a Jellyfin-delete failure (e.g. a second 401 after one re-auth) aborts before clearLinks" <| fun _ ->
            let mutable clearCalled = false
            let effects =
                { baseExecuteEffects () with
                    DeleteJellyfinItem = fun () -> async { return Error "Jellyfin rejected the token again after re-authentication; aborting (no retry loop)" }
                    ClearLinks = fun () -> clearCalled <- true }
            let outcome = execute effects [ "h1" ] |> Async.RunSynchronously
            Expect.equal outcome.Completed [ PreserveWatchHistory; ResolvePath; MatchTorrents; DeleteTorrents ] "Stops after DeleteTorrents"
            match outcome.Failure with
            | Some (DeleteJellyfinItem, _) -> ()
            | other -> failtestf "Expected a DeleteJellyfinItem failure, got %A" other
            Expect.isFalse clearCalled "ClearLinks never invoked"

        testCase "a verify failure after both deletes still does NOT call clearLinks" <| fun _ ->
            let mutable clearCalled = false
            let effects =
                { baseExecuteEffects () with
                    VerifyGone = fun () -> async { return Ok false }
                    ClearLinks = fun () -> clearCalled <- true }
            let outcome = execute effects [ "h1" ] |> Async.RunSynchronously
            Expect.equal outcome.Completed [ PreserveWatchHistory; ResolvePath; MatchTorrents; DeleteTorrents; DeleteJellyfinItem ] "Stops after DeleteJellyfinItem"
            match outcome.Failure with
            | Some (Verify, _) -> ()
            | other -> failtestf "Expected a Verify failure, got %A" other
            Expect.isFalse clearCalled "ClearLinks never invoked after a failed verify"

        testCase "FilesOnly (nothing matched) skips deleteTorrents" <| fun _ ->
            let mutable deleteCalled = false
            let effects =
                { baseExecuteEffects () with
                    ListTorrents = fun () -> async { return Ok [] }
                    DeleteTorrents = fun _ -> async { deleteCalled <- true; return Ok () } }
            let outcome = execute effects [] |> Async.RunSynchronously
            Expect.equal outcome.Completed [ PreserveWatchHistory; ResolvePath; MatchTorrents; DeleteTorrents; DeleteJellyfinItem; Verify; ClearLinks ] "DeleteTorrents step still recorded as completed (a no-op skip, not a failure)"
            Expect.isFalse deleteCalled "The DeleteTorrents effect lambda itself is never invoked"

        testCase "refuses while the sync-in-progress guard reports true -- nothing else runs" <| fun _ ->
            let mutable preserveCalled = false
            let effects =
                { baseExecuteEffects () with
                    IsSyncInProgress = fun () -> true
                    PreserveWatchHistory = fun () -> async { preserveCalled <- true; return Ok () } }
            let outcome = execute effects [ "h1" ] |> Async.RunSynchronously
            Expect.equal outcome.Completed [] "Nothing completed"
            match outcome.Failure with
            | Some (_, msg) -> Expect.stringContains msg "sync is in progress" "Names the refusal reason"
            | None -> failtest "Expected a refusal"
            Expect.isFalse preserveCalled "No effect lambda runs once refused"
    ]
