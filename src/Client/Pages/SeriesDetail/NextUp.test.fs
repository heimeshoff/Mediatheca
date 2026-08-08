/// series-x4qte: client-side regression coverage for `NextUp.compute`'s frontier
/// rule (ADR-0063) — the rule was previously proven only by mirroring the SQL
/// view and by server-side Expecto tests, never by exercising the client
/// function that actually renders. See `NextUp.fs`'s docblock for the rule
/// itself.
module Mediatheca.Client.Pages.SeriesDetail.NextUpTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.SeriesDetail.NextUp

/// Fills every field `compute` doesn't look at with a neutral default, so
/// each test case reads as its scenario rather than as DTO plumbing.
let private episode (n: int) (isWatched: bool) : EpisodeDto =
    { EpisodeNumber = n
      Name = sprintf "Episode %d" n
      Overview = ""
      Runtime = None
      AirDate = None
      StillRef = None
      TmdbRating = None
      IsWatched = isWatched
      WatchedDate = None
      MetadataPending = false }

let private season (n: int) (episodes: EpisodeDto list) : SeasonDto =
    { SeasonNumber = n
      Name = sprintf "Season %d" n
      Overview = ""
      PosterRef = None
      AirDate = None
      Episodes = episodes
      WatchedCount = episodes |> List.filter (fun e -> e.IsWatched) |> List.length
      OverallWatchedCount = 0 }

let nextUpTests =
    testList "series-x4qte: NextUp.compute frontier rule" [

        testCase "(1) a gap behind the frontier is skipped" <| fun () ->
            // Season 1 has 11 episodes. (1,3) is an unwatched gap; (1,4)-(1,10)
            // are watched, making (1,10) the frontier. Next up must be (1,11),
            // not the gap at (1,3).
            let episodes =
                [ episode 1 true
                  episode 2 true
                  episode 3 false
                  yield! [ 4 .. 10 ] |> List.map (fun n -> episode n true)
                  episode 11 false ]

            let result = compute [ season 1 episodes ]

            match result with
            | None -> failtest "expected an episode past the frontier"
            | Some (sNum, ep) ->
                Expect.equal sNum 1 "next-up season should be 1"
                Expect.equal ep.EpisodeNumber 11 "next-up episode should be 11 (past the frontier at (1,10)), not the gap at 3"

        testCase "(2) a plain contiguous watch run (S1E1-E2 watched of 5) -> S1E3" <| fun () ->
            let episodes =
                [ episode 1 true
                  episode 2 true
                  episode 3 false
                  episode 4 false
                  episode 5 false ]

            let result = compute [ season 1 episodes ]

            match result with
            | None -> failtest "expected episode 3 to be next up"
            | Some (sNum, ep) ->
                Expect.equal sNum 1 "still season 1"
                Expect.equal ep.EpisodeNumber 3 "the episode immediately after the contiguous run (1,1)-(1,2)"

        testCase "(3) no watched episodes anywhere -> the first episode overall" <| fun () ->
            let episodes = [ episode 1 false; episode 2 false; episode 3 false ]

            let result = compute [ season 1 episodes ]

            match result with
            | None -> failtest "expected the first episode overall when nothing has been watched"
            | Some (sNum, ep) ->
                Expect.equal sNum 1 "no frontier — falls back to season 1"
                Expect.equal ep.EpisodeNumber 1 "no frontier — falls back to episode 1"

        testCase "(4) nothing exists past the frontier -> None, even with gaps behind it" <| fun () ->
            // Season 1 has 5 episodes, season 2 has 3. (1,3) is an unwatched
            // gap. The frontier is (2,3), the last episode of the last
            // season — nothing left to recommend regardless of the gap.
            let s1 =
                [ episode 1 true
                  episode 2 true
                  episode 3 false
                  episode 4 true
                  episode 5 true ]

            let s2 = [ episode 1 true; episode 2 true; episode 3 true ]

            let result = compute [ season 1 s1; season 2 s2 ]

            Expect.isNone result "no episode exists past the frontier (2,3) — gap at (1,3) is history, not a queue"

        testCase "(5) a cross-season frontier (S1E10 watched, S2E1 unwatched) -> S2E1" <| fun () ->
            // Confirms lexicographic (season, episode) tuple ordering: the
            // frontier sits at the final episode of season 1, and next up
            // must cross forward into season 2.
            let s1 = [ 1 .. 10 ] |> List.map (fun n -> episode n true)
            let s2 = [ episode 1 false; episode 2 false ]

            let result = compute [ season 1 s1; season 2 s2 ]

            match result with
            | None -> failtest "expected next up to cross into season 2"
            | Some (sNum, ep) ->
                Expect.equal sNum 2 "the frontier is at the end of season 1, so next up crosses into season 2"
                Expect.equal ep.EpisodeNumber 1 "the first episode of season 2"

        testCase "(6) an empty seasons list -> None without throwing" <| fun () ->
            let result = compute []

            Expect.isNone result "no seasons at all means no next-up episode, and compute must not throw"

        testCase "(7) unordered input still yields the correct episode" <| fun () ->
            // Seasons and episodes passed out of order — pins the
            // normalization `compute` does before finding the frontier.
            // Frontier is (1,10) (as in case 1); next up should be (1,11).
            let s2 = [ episode 2 false; episode 1 false ]

            let s1Episodes =
                [ episode 11 false
                  episode 3 false
                  episode 10 true
                  episode 1 true
                  episode 7 true
                  episode 2 true
                  episode 9 true
                  episode 4 true
                  episode 6 true
                  episode 8 true
                  episode 5 true ]

            let result = compute [ season 2 s2; season 1 s1Episodes ]

            match result with
            | None -> failtest "expected an episode past the frontier"
            | Some (sNum, ep) ->
                Expect.equal sNum 1 "next-up season should be 1 even though season 2 was passed first"
                Expect.equal ep.EpisodeNumber 11 "next-up episode should be 11, the correct episode despite unordered input"
    ]

Mocha.runTests nextUpTests |> ignore
