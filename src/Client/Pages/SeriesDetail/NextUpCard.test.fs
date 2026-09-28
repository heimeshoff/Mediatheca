/// series-zdqwm: coverage for the Next Up card's content decision — which
/// lines it shows for a given Next Up / air-date combination, and whether it
/// renders at all. series-q7vhm extends this with which *episode* the
/// air-date line names. See `NextUpCard.fs`'s docblock for the rule itself.
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

/// series-q7vhm: an episode with a known air date, so `findAiringEpisode`
/// (via `decide`) can match it.
let private episodeWithAirDate (n: int) (airDate: string) : EpisodeDto =
    { episode n with AirDate = Some airDate }

/// series-q7vhm: a season carrying the given episodes, with the fields
/// `decide` doesn't look at filled with neutral defaults.
let private season (n: int) (episodes: EpisodeDto list) : SeasonDto =
    { SeasonNumber = n
      Name = sprintf "Season %d" n
      Overview = ""
      PosterRef = None
      AirDate = None
      Episodes = episodes
      WatchedCount = 0
      OverallWatchedCount = 0 }

/// An ISO date `n` days from today, so expected countdown wording is
/// deterministic regardless of when the suite runs.
let private isoInDays (n: int) =
    System.DateTime.Today.AddDays(float n).ToString("yyyy-MM-dd")

let nextUpCardTests =
    testList "series-zdqwm/series-q7vhm: NextUp card content decision" [

        testCase "(1) Next Up present + known episode air date, no matching episode in Seasons -> generic \"Next episode airs\" line" <| fun () ->
            let date = isoInDays 3
            let result = decide [] (Some(1, episode 5)) (Some date) None

            match result with
            | ShowCard(Some(sNum, ep), Some airLine) ->
                Expect.equal sNum 1 "season carried through"
                Expect.equal ep.EpisodeNumber 5 "episode carried through"
                Expect.equal airLine (sprintf "Next episode airs %s (in 3 days)" date) "episode date takes precedence over the season fallback"
            | _ -> failtest "expected ShowCard with both an episode line and an air-date line"

        testCase "(2) Next Up present, only a season air date -> \"Returns\" fallback wording" <| fun () ->
            let date = isoInDays 10
            let result = decide [] (Some(2, episode 1)) None (Some date)

            match result with
            | ShowCard(Some(sNum, ep), Some airLine) ->
                Expect.equal sNum 2 "season carried through"
                Expect.equal ep.EpisodeNumber 1 "episode carried through"
                Expect.equal airLine (sprintf "Returns %s (in 10 days)" date) "season fallback wording used when no episode date is known"
            | _ -> failtest "expected ShowCard with both an episode line and the season fallback air-date line"

        testCase "(3) Next Up present, no air date known -> episode line only, exactly as before" <| fun () ->
            let result = decide [] (Some(1, episode 2)) None None

            match result with
            | ShowCard(Some(sNum, ep), None) ->
                Expect.equal sNum 1 "season carried through"
                Expect.equal ep.EpisodeNumber 2 "episode carried through"
            | _ -> failtest "expected ShowCard with only the episode line, no air-date line"

        testCase "(4) caught up (no Next Up), no matching episode in Seasons -> card still shows, generic air line" <| fun () ->
            let date = isoInDays 0
            let result = decide [] None (Some date) None

            match result with
            | ShowCard(None, Some airLine) ->
                Expect.equal airLine (sprintf "Next episode airs %s (today)" date) "the air-date line still renders even when caught up"
            | _ -> failtest "expected ShowCard with no episode line but an air-date line"

        testCase "(5) neither a Next Up episode nor a known air date -> no card at all" <| fun () ->
            let result = decide [] None None None

            Expect.equal result NoCard "nothing to show means no card"

        testCase "(6) airing episode is the Next Up episode -> \"Airs\" wording, no season/episode repeated" <| fun () ->
            let date = isoInDays 3
            let ep3 = episodeWithAirDate 3 date
            let seasons = [ season 2 [ episode 1; episode 2; ep3 ] ]
            let result = decide seasons (Some(2, ep3)) (Some date) None

            match result with
            | ShowCard(Some(sNum, ep), Some airLine) ->
                Expect.equal sNum 2 "season carried through"
                Expect.equal ep.EpisodeNumber 3 "episode carried through"
                Expect.equal airLine (sprintf "Airs %s (in 3 days)" date) "same episode as Next Up -> no repeated numbers"
            | _ -> failtest "expected ShowCard with an episode line and the air-date line"

        testCase "(7) airing episode differs from Next Up -> names the airing episode's season and number" <| fun () ->
            let date = isoInDays 5
            let ep8 = episodeWithAirDate 8 date
            let seasons = [ season 2 [ episode 3; ep8 ] ]
            let result = decide seasons (Some(2, episode 3)) (Some date) None

            match result with
            | ShowCard(Some(sNum, ep), Some airLine) ->
                Expect.equal sNum 2 "Next Up season carried through"
                Expect.equal ep.EpisodeNumber 3 "Next Up episode carried through"
                Expect.equal airLine (sprintf "Season 2, Episode 8 airs %s (in 5 days)" date) "airing episode differs from Next Up -> named explicitly"
            | _ -> failtest "expected ShowCard with an episode line and the air-date line"

        testCase "(8) caught up but an episode air date known -> names season/episode, caught-up line kept" <| fun () ->
            let date = isoInDays 7
            let ep1 = episodeWithAirDate 1 date
            let seasons = [ season 3 [ ep1 ] ]
            let result = decide seasons None (Some date) None

            match result with
            | ShowCard(None, Some airLine) ->
                Expect.equal airLine (sprintf "Season 3, Episode 1 airs %s (in 7 days)" date) "names the airing episode when caught up"
            | _ -> failtest "expected ShowCard with no episode line (caught up) but a named air-date line"

        testCase "(9) two episodes share the next air date -> the lower season/episode is named" <| fun () ->
            let date = isoInDays 2
            let seasons = [ season 2 [ episodeWithAirDate 5 date; episodeWithAirDate 1 date ] ]
            let result = decide seasons None (Some date) None

            match result with
            | ShowCard(None, Some airLine) ->
                Expect.equal airLine (sprintf "Season 2, Episode 1 airs %s (in 2 days)" date) "lower episode number wins when dates tie"
            | _ -> failtest "expected ShowCard naming the lower season/episode"

        testCase "(10) episode air date known but no matching episode in Seasons -> falls back to generic wording" <| fun () ->
            let date = isoInDays 4
            let seasons = [ season 1 [ episode 1 ] ]
            let result = decide seasons (Some(1, episode 1)) (Some date) None

            match result with
            | ShowCard(Some(sNum, ep), Some airLine) ->
                Expect.equal sNum 1 "Next Up season carried through"
                Expect.equal ep.EpisodeNumber 1 "Next Up episode carried through"
                Expect.equal airLine (sprintf "Next episode airs %s (in 4 days)" date) "no matching episode in Seasons -> generic fallback wording"
            | _ -> failtest "expected ShowCard with the generic fallback air-date line"
    ]

Mocha.runTests nextUpCardTests |> ignore
