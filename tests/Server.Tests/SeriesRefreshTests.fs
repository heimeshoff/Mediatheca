module Mediatheca.Tests.SeriesRefreshTests

open Expecto
open System.Data
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server

let private newConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    SeriesProjection.handler.Init conn
    conn

/// Insert a series_detail row with a given status and in_focus flag.
/// tmdbId must be unique within a connection (series_detail.tmdb_id is uniquely indexed).
let private seedSeries (conn: SqliteConnection) (slug: string) (tmdbId: int) (status: string) (inFocus: bool) =
    conn
    |> Db.newCommand
        "INSERT INTO series_detail (slug, name, year, tmdb_id, status, in_focus)
         VALUES (@slug, @name, 2010, @tmdb_id, @status, @in_focus)"
    |> Db.setParams [
        "@slug", SqlType.String slug
        "@name", SqlType.String slug
        "@tmdb_id", SqlType.Int32 tmdbId
        "@status", SqlType.String status
        "@in_focus", SqlType.Int32 (if inFocus then 1 else 0)
    ]
    |> Db.exec

/// Record a watch for the given series at `date('now', daysOffset)`.
/// daysOffset is a SQLite modifier like "-10 days".
let private seedWatch (conn: SqliteConnection) (slug: string) (daysOffset: string) =
    conn
    |> Db.newCommand
        (sprintf
            "INSERT OR REPLACE INTO series_episode_progress (series_slug, rewatch_id, season_number, episode_number, watched_date)
             VALUES (@slug, 'default', 1, 1, date('now', '%s'))"
            daysOffset)
    |> Db.setParams [ "@slug", SqlType.String slug ]
    |> Db.exec

[<Tests>]
let seriesRefreshTests =
    testList "SeriesRefresh.getRefreshCandidates" [

        testCase "Returning and InProduction series are always candidates" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "returning-show" 1 "Returning" false
            seedSeries conn "in-production-show" 2 "InProduction" false
            let candidates = SeriesRefresh.getRefreshCandidates conn
            Expect.contains candidates "returning-show" "Returning series should be a candidate"
            Expect.contains candidates "in-production-show" "InProduction series should be a candidate"

        testCase "Ended series with in_focus = 1 is a candidate" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "focused-ended" 3 "Ended" true
            let candidates = SeriesRefresh.getRefreshCandidates conn
            Expect.contains candidates "focused-ended" "Ended + in_focus should be a candidate"

        testCase "Ended series watched within 180 days is a candidate" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "recently-watched-ended" 4 "Ended" false
            seedWatch conn "recently-watched-ended" "-30 days"
            let candidates = SeriesRefresh.getRefreshCandidates conn
            Expect.contains candidates "recently-watched-ended" "Ended + recent watch should be a candidate"

        testCase "Ended series with no recency signal is excluded" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "finished-cold" 5 "Ended" false
            let candidates = SeriesRefresh.getRefreshCandidates conn
            Expect.isFalse (List.contains "finished-cold" candidates)
                "Ended series with no in_focus and no watch activity should not be a candidate"

        testCase "Ended series last watched beyond 180 days is excluded" <| fun _ ->
            use conn = newConn ()
            seedSeries conn "stale-ended" 6 "Ended" false
            seedWatch conn "stale-ended" "-200 days"
            let candidates = SeriesRefresh.getRefreshCandidates conn
            Expect.isFalse (List.contains "stale-ended" candidates)
                "Ended series last watched over 180 days ago should not be a candidate"
    ]
