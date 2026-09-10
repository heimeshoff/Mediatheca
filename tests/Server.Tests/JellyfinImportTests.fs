module Mediatheca.Tests.JellyfinImportTests

open Expecto
open Mediatheca.Server
open Mediatheca.Server.Jellyfin

// --- Test data builders ---

let private mkUserData (played: bool) (lastPlayed: string option) : JellyfinUserData = {
    Played = played
    PlayCount = if played then 1 else 0
    LastPlayedDate = lastPlayed
    PlaybackPositionTicks = 0L
    IsFavorite = false
}

/// A Jellyfin episode item with season/episode numbers and a Played flag.
let private mkEpisode (season: int) (episode: int) (played: bool) : JellyfinBaseItem = {
    Id = sprintf "ep-%d-%d" season episode
    Name = sprintf "S%02dE%02d" season episode
    Type = "Episode"
    ProductionYear = None
    RunTimeTicks = None
    Genres = []
    Overview = None
    ProviderIds = { Tmdb = None; Imdb = None }
    UserData = Some (mkUserData played (Some "2026-05-26T10:00:00.0000000Z"))
    SeriesName = None
    SeriesId = None
    IndexNumber = Some episode
    ParentIndexNumber = Some season
    PremiereDate = None
    PrimaryImageTag = None
    Path = None
}

[<Tests>]
let jellyfinImportTests =
    testList "JellyfinImport.syncSeriesWatchHistory" [

        testCase "writes every played, not-yet-watched episode across all series" <| fun _ ->
            let written = System.Collections.Generic.List<string * int * int>()
            let batch =
                [ "the-boys", [ mkEpisode 5 5 true; mkEpisode 5 6 true ]
                  "gen-v",    [ mkEpisode 2 4 true; mkEpisode 2 5 true ] ]
            let result =
                JellyfinImport.syncSeriesWatchHistory
                    batch
                    (fun _slug -> "default")
                    (fun _slug _rewatch -> Set.empty)
                    (fun slug _rewatch season ep _date ->
                        written.Add(slug, season, ep)
                        Ok ())
            Expect.equal result.EpisodesAdded 4 "All four played episodes written"
            Expect.isEmpty result.Errors "No errors"
            Expect.isFalse result.Failed "Run did not fail"
            Expect.equal (written.Count) 4 "writeEpisode called four times"

        testCase "skips episodes already watched and unplayed episodes" <| fun _ ->
            let batch =
                [ "the-boys", [ mkEpisode 5 4 true; mkEpisode 5 5 true; mkEpisode 5 6 false ] ]
            let result =
                JellyfinImport.syncSeriesWatchHistory
                    batch
                    (fun _ -> "default")
                    (fun _ _ -> Set.ofList [ (5, 4) ])  // S5E4 already watched
                    (fun _ _ _ _ _ -> Ok ())
            Expect.equal result.EpisodesAdded 1 "Only S5E5 written"
            Expect.equal result.ItemsSkipped 2 "S5E4 (already watched) + S5E6 (unplayed) skipped"
            Expect.isFalse result.Failed "Run did not fail"

        // The core regression for integration-001: one series raising during
        // Phase 2 must NOT abort the whole run — other series still get written
        // and the run reports a failure.
        testCase "fault in one series does not abort the others and run reports failure" <| fun _ ->
            let written = System.Collections.Generic.List<string * int * int>()
            let batch =
                [ "broken-series", [ mkEpisode 1 1 true; mkEpisode 1 2 true ]
                  "the-boys",      [ mkEpisode 5 5 true; mkEpisode 5 6 true ]
                  "gen-v",         [ mkEpisode 2 4 true ] ]
            let result =
                JellyfinImport.syncSeriesWatchHistory
                    batch
                    (fun _ -> "default")
                    (fun _ _ -> Set.empty)
                    (fun slug rewatch season ep _date ->
                        if slug = "broken-series" then
                            // Simulate a SqliteException escaping executeCommand mid-loop.
                            raise (System.Exception("DB locked"))
                        written.Add(slug, season, ep)
                        Ok ())
            // Other series still got their episodes despite the throw.
            Expect.equal result.EpisodesAdded 3 "the-boys (2) + gen-v (1) still written"
            Expect.isTrue (written.Contains(("the-boys", 5, 5))) "The Boys S5E5 written"
            Expect.isTrue (written.Contains(("the-boys", 5, 6))) "The Boys S5E6 written"
            Expect.isTrue (written.Contains(("gen-v", 2, 4))) "Gen V S2E4 written"
            // The failure is surfaced, not swallowed.
            Expect.isTrue result.Failed "Run reports failure"
            Expect.isNonEmpty result.Errors "Error list mentions the broken series"
            Expect.isTrue
                (result.Errors |> List.exists (fun e -> e.Contains("broken-series")))
                "Error names the offending series"

        testCase "writeEpisode returning Error is recorded per-item without aborting" <| fun _ ->
            let batch =
                [ "the-boys", [ mkEpisode 5 5 true; mkEpisode 5 6 true ] ]
            let result =
                JellyfinImport.syncSeriesWatchHistory
                    batch
                    (fun _ -> "default")
                    (fun _ _ -> Set.empty)
                    (fun _slug _rewatch _season ep _date ->
                        if ep = 5 then Error "Concurrency conflict, please retry"
                        else Ok ())
            Expect.equal result.EpisodesAdded 1 "S5E6 still written after S5E5 failed"
            Expect.isTrue result.Failed "Run reports failure"
            Expect.equal (List.length result.Errors) 1 "One error recorded"
    ]

/// A Jellyfin movie item with a Played flag -- the movie sibling of `mkEpisode`.
let private mkMovie (id: string) (played: bool) : JellyfinBaseItem = {
    Id = id
    Name = id
    Type = "Movie"
    ProductionYear = None
    RunTimeTicks = None
    Genres = []
    Overview = None
    ProviderIds = { Tmdb = None; Imdb = None }
    UserData = Some (mkUserData played (Some "2026-05-26T10:00:00.0000000Z"))
    SeriesName = None
    SeriesId = None
    IndexNumber = None
    ParentIndexNumber = None
    PremiereDate = None
    PrimaryImageTag = None
    Path = None
}

// integration-r4vzm: `syncMovieWatchHistory`, extracted from
// `Api.runJellyfinImport`'s previously-inline Phase 2 movie loop so
// `LocalCopyRemoval.execute`'s preserve-watch-history step can reuse the
// exact same write-session logic for a single movie.
[<Tests>]
let jellyfinMovieImportTests =
    testList "JellyfinImport.syncMovieWatchHistory" [

        testCase "writes every played, not-yet-recorded movie" <| fun _ ->
            let written = System.Collections.Generic.List<string * string>()
            let batch = [ "dune-2021", mkMovie "jf-dune" true; "arrival-2016", mkMovie "jf-arrival" true ]
            let result =
                JellyfinImport.syncMovieWatchHistory
                    batch
                    (fun _slug _date -> false)
                    (fun _slug -> Some 155)
                    (fun slug date runtime -> written.Add((slug, date)); Ok ())
            Expect.equal result.MoviesAdded 2 "Both movies written"
            Expect.equal result.ItemsSkipped 0 "Nothing skipped"
            Expect.isFalse result.Failed "No failures"
            Expect.isTrue (written.Contains(("dune-2021", "2026-05-26"))) "Dune written on its play date"
            Expect.isTrue (written.Contains(("arrival-2016", "2026-05-26"))) "Arrival written on its play date"

        testCase "an unplayed movie is skipped, writeSession never called for it" <| fun _ ->
            let mutable writeCalled = false
            let batch = [ "dune-2021", mkMovie "jf-dune" false ]
            let result =
                JellyfinImport.syncMovieWatchHistory
                    batch
                    (fun _ _ -> false)
                    (fun _ -> None)
                    (fun _ _ _ -> writeCalled <- true; Ok ())
            Expect.equal result.MoviesAdded 0 "Nothing added"
            Expect.equal result.ItemsSkipped 1 "Skipped as unplayed"
            Expect.isFalse writeCalled "writeSession never invoked for an unplayed movie"

        testCase "a session already existing on that date is skipped (no double-record on re-sync)" <| fun _ ->
            let mutable writeCalled = false
            let batch = [ "dune-2021", mkMovie "jf-dune" true ]
            let result =
                JellyfinImport.syncMovieWatchHistory
                    batch
                    (fun _ _ -> true)
                    (fun _ -> None)
                    (fun _ _ _ -> writeCalled <- true; Ok ())
            Expect.equal result.ItemsSkipped 1 "Skipped -- already recorded"
            Expect.isFalse writeCalled "writeSession never invoked when a session already exists on that date"

        testCase "a writeSession Error is recorded per-item without aborting the rest of the batch" <| fun _ ->
            let batch = [ "broken-movie", mkMovie "jf-broken" true; "dune-2021", mkMovie "jf-dune" true ]
            let result =
                JellyfinImport.syncMovieWatchHistory
                    batch
                    (fun _ _ -> false)
                    (fun _ -> None)
                    (fun slug _date _runtime -> if slug = "broken-movie" then Error "Concurrency conflict" else Ok ())
            Expect.equal result.MoviesAdded 1 "The healthy movie still written after the broken one failed"
            Expect.isTrue result.Failed "Run reports failure"
            Expect.isTrue (result.Errors |> List.exists (fun e -> e.Contains("broken-movie"))) "Error names the offending movie"

        testCase "a thrown exception from writeSession is caught and recorded, not propagated" <| fun _ ->
            let batch = [ "dune-2021", mkMovie "jf-dune" true ]
            let result =
                JellyfinImport.syncMovieWatchHistory
                    batch
                    (fun _ _ -> false)
                    (fun _ -> None)
                    (fun _ _ _ -> failwith "SqliteException: database is locked")
            Expect.isTrue result.Failed "Run reports failure"
            Expect.isTrue (result.Errors |> List.exists (fun e -> e.Contains("threw"))) "Error records the thrown exception, loop did not abort"
    ]
