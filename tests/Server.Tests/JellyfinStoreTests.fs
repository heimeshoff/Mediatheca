module Mediatheca.Tests.JellyfinStoreTests

/// integration-r4vzm (ADR-0071): the per-item clears "Remove local copy"
/// needs -- `clearAll` is a full-sync repopulate primitive the removal flow
/// never touches. `getSeriesJellyfinId` was the one getter missing before
/// this task (every other id had a sibling already).

open Microsoft.Data.Sqlite
open Expecto
open Mediatheca.Server

let private newConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    JellyfinStore.initialize conn
    conn

[<Tests>]
let jellyfinStoreTests =
    testList "JellyfinStore (integration-r4vzm additions)" [

        testCase "getSeriesJellyfinId returns the stored id" <| fun _ ->
            use conn = newConn ()
            JellyfinStore.setSeriesJellyfinId conn "the-boys" "jf-series-1"
            Expect.equal (JellyfinStore.getSeriesJellyfinId conn "the-boys") (Some "jf-series-1") "Round-trips the stored id"

        testCase "getSeriesJellyfinId returns None for an unknown slug" <| fun _ ->
            use conn = newConn ()
            Expect.equal (JellyfinStore.getSeriesJellyfinId conn "unknown") None "No row -- None"

        testCase "clearMovieJellyfinId removes exactly that movie's row" <| fun _ ->
            use conn = newConn ()
            JellyfinStore.setMovieJellyfinId conn "dune-2021" "jf-movie-1"
            JellyfinStore.setMovieJellyfinId conn "arrival-2016" "jf-movie-2"
            JellyfinStore.clearMovieJellyfinId conn "dune-2021"
            Expect.equal (JellyfinStore.getMovieJellyfinId conn "dune-2021") None "Cleared"
            Expect.equal (JellyfinStore.getMovieJellyfinId conn "arrival-2016") (Some "jf-movie-2") "Sibling row untouched"

        testCase "clearMovieJellyfinId called twice is a no-op" <| fun _ ->
            use conn = newConn ()
            JellyfinStore.setMovieJellyfinId conn "dune-2021" "jf-movie-1"
            JellyfinStore.clearMovieJellyfinId conn "dune-2021"
            JellyfinStore.clearMovieJellyfinId conn "dune-2021"
            Expect.equal (JellyfinStore.getMovieJellyfinId conn "dune-2021") None "Still cleared, no error on the second call"

        testCase "clearSeriesJellyfinId removes the series row AND all its episode rows" <| fun _ ->
            use conn = newConn ()
            JellyfinStore.setSeriesJellyfinId conn "the-boys" "jf-series-1"
            JellyfinStore.setEpisodeJellyfinId conn "the-boys" 1 1 "jf-ep-1"
            JellyfinStore.setEpisodeJellyfinId conn "the-boys" 1 2 "jf-ep-2"
            // A sibling series' episode must survive.
            JellyfinStore.setSeriesJellyfinId conn "gen-v" "jf-series-2"
            JellyfinStore.setEpisodeJellyfinId conn "gen-v" 1 1 "jf-genv-ep-1"

            JellyfinStore.clearSeriesJellyfinId conn "the-boys"

            Expect.equal (JellyfinStore.getSeriesJellyfinId conn "the-boys") None "Series row cleared"
            Expect.equal (JellyfinStore.getEpisodeJellyfinId conn "the-boys" 1 1) None "Episode 1 cleared"
            Expect.equal (JellyfinStore.getEpisodeJellyfinId conn "the-boys" 1 2) None "Episode 2 cleared"
            Expect.equal (JellyfinStore.getSeriesJellyfinId conn "gen-v") (Some "jf-series-2") "Sibling series untouched"
            Expect.equal (JellyfinStore.getEpisodeJellyfinId conn "gen-v" 1 1) (Some "jf-genv-ep-1") "Sibling series' episode untouched"

        testCase "clearSeriesJellyfinId called twice is a no-op" <| fun _ ->
            use conn = newConn ()
            JellyfinStore.setSeriesJellyfinId conn "the-boys" "jf-series-1"
            JellyfinStore.setEpisodeJellyfinId conn "the-boys" 1 1 "jf-ep-1"
            JellyfinStore.clearSeriesJellyfinId conn "the-boys"
            JellyfinStore.clearSeriesJellyfinId conn "the-boys"
            Expect.equal (JellyfinStore.getSeriesJellyfinId conn "the-boys") None "Still cleared"
            Expect.equal (JellyfinStore.getEpisodeJellyfinId conn "the-boys" 1 1) None "Still cleared"

        testCase "clearEpisodeJellyfinId removes exactly that episode, leaving series and siblings" <| fun _ ->
            use conn = newConn ()
            JellyfinStore.setSeriesJellyfinId conn "the-boys" "jf-series-1"
            JellyfinStore.setEpisodeJellyfinId conn "the-boys" 1 1 "jf-ep-1"
            JellyfinStore.setEpisodeJellyfinId conn "the-boys" 1 2 "jf-ep-2"
            JellyfinStore.clearEpisodeJellyfinId conn "the-boys" 1 1
            Expect.equal (JellyfinStore.getEpisodeJellyfinId conn "the-boys" 1 1) None "Cleared"
            Expect.equal (JellyfinStore.getEpisodeJellyfinId conn "the-boys" 1 2) (Some "jf-ep-2") "Sibling episode untouched"
            Expect.equal (JellyfinStore.getSeriesJellyfinId conn "the-boys") (Some "jf-series-1") "Series row untouched"

        testCase "clearEpisodeJellyfinId called twice is a no-op" <| fun _ ->
            use conn = newConn ()
            JellyfinStore.setEpisodeJellyfinId conn "the-boys" 1 1 "jf-ep-1"
            JellyfinStore.clearEpisodeJellyfinId conn "the-boys" 1 1
            JellyfinStore.clearEpisodeJellyfinId conn "the-boys" 1 1
            Expect.equal (JellyfinStore.getEpisodeJellyfinId conn "the-boys" 1 1) None "Still cleared"
    ]
