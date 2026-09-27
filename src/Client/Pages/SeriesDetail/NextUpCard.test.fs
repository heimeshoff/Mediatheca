/// series-zdqwm: coverage for the Next Up card's content decision — which
/// lines it shows for a given Next Up / air-date combination, and whether it
/// renders at all. See `NextUpCard.fs`'s docblock for the rule itself.
module Mediatheca.Client.Pages.SeriesDetail.NextUpCardTests

open Fable.Mocha
open Mediatheca.Shared
open Mediatheca.Client.Pages.SeriesDetail.NextUpCard

/// Fills every field `decide` doesn't look at with a neutral default, so
/// each test case reads as its scenario rather than as DTO plumbing.
let private episode (n: int) : EpisodeDto =
    { EpisodeNumber = n
      Name = sprintf "Episode %d" n
      Overview = ""
      Runtime = None
      AirDate = None
      StillRef = None
      TmdbRating = None
      IsWatched = false
      WatchedDate = None
      MetadataPending = false }

/// An ISO date `n` days from today, so expected countdown wording is
/// deterministic regardless of when the suite runs.
let private isoInDays (n: int) =
    System.DateTime.Today.AddDays(float n).ToString("yyyy-MM-dd")

let nextUpCardTests =
    testList "series-zdqwm: NextUp card content decision" [

        testCase "(1) Next Up present + known episode air date -> episode line and \"Next episode airs\" line" <| fun () ->
            let date = isoInDays 3
            let result = decide (Some(1, episode 5)) (Some date) None

            match result with
            | ShowCard(Some(sNum, ep), Some airLine) ->
                Expect.equal sNum 1 "season carried through"
                Expect.equal ep.EpisodeNumber 5 "episode carried through"
                Expect.equal airLine (sprintf "Next episode airs %s (in 3 days)" date) "episode date takes precedence over the season fallback"
            | _ -> failtest "expected ShowCard with both an episode line and an air-date line"

        testCase "(2) Next Up present, only a season air date -> \"Returns\" fallback wording" <| fun () ->
            let date = isoInDays 10
            let result = decide (Some(2, episode 1)) None (Some date)

            match result with
            | ShowCard(Some(sNum, ep), Some airLine) ->
                Expect.equal sNum 2 "season carried through"
                Expect.equal ep.EpisodeNumber 1 "episode carried through"
                Expect.equal airLine (sprintf "Returns %s (in 10 days)" date) "season fallback wording used when no episode date is known"
            | _ -> failtest "expected ShowCard with both an episode line and the season fallback air-date line"

        testCase "(3) Next Up present, no air date known -> episode line only, exactly as before" <| fun () ->
            let result = decide (Some(1, episode 2)) None None

            match result with
            | ShowCard(Some(sNum, ep), None) ->
                Expect.equal sNum 1 "season carried through"
                Expect.equal ep.EpisodeNumber 2 "episode carried through"
            | _ -> failtest "expected ShowCard with only the episode line, no air-date line"

        testCase "(4) caught up (no Next Up) but a known air date -> card still shows, caught-up line instead of an episode" <| fun () ->
            let date = isoInDays 0
            let result = decide None (Some date) None

            match result with
            | ShowCard(None, Some airLine) ->
                Expect.equal airLine (sprintf "Next episode airs %s (today)" date) "the air-date line still renders even when caught up"
            | _ -> failtest "expected ShowCard with no episode line but an air-date line"

        testCase "(5) neither a Next Up episode nor a known air date -> no card at all" <| fun () ->
            let result = decide None None None

            Expect.equal result NoCard "nothing to show means no card"
    ]

Mocha.runTests nextUpCardTests |> ignore
