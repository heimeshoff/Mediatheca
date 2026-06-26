module Mediatheca.Tests.JellyfinMaterializeTests

open Expecto
open System.Data
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Server.Jellyfin
open Mediatheca.Server.JellyfinImport

// --- Builders ---

let private mkUserData (played: bool) : JellyfinUserData = {
    Played = played
    PlayCount = if played then 1 else 0
    LastPlayedDate = if played then Some "2026-05-26T10:00:00.0000000Z" else None
    PlaybackPositionTicks = 0L
    IsFavorite = false
}

/// A Jellyfin episode item: aired numbering, 30-min runtime, premiere date.
let private mkEp (season: int) (episode: int) (played: bool) : JellyfinBaseItem = {
    Id = sprintf "jf-%d-%d" season episode
    Name = sprintf "Episode %d" episode
    Type = "Episode"
    ProductionYear = None
    RunTimeTicks = Some 18_000_000_000L // 30 minutes
    Genres = []
    Overview = Some (sprintf "Overview of episode %d" episode)
    ProviderIds = { Tmdb = None; Imdb = None }
    UserData = Some (mkUserData played)
    SeriesName = None
    SeriesId = None
    IndexNumber = Some episode
    ParentIndexNumber = Some season
    PremiereDate = Some "2026-05-26T00:00:00.0000000Z"
    PrimaryImageTag = None
}

// --- Real-connection helpers (for enrichment + progress criteria) ---

let private newConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    // getBySlug reads across several stores (cast, content blocks, friends) — init all.
    EventStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    conn

let private seedSeriesDetail (conn: SqliteConnection) (slug: string) (tmdbId: int) =
    conn
    |> Db.newCommand
        "INSERT INTO series_detail (slug, name, year, tmdb_id, status)
         VALUES (@slug, @name, 2022, @tmdb_id, 'Ended')"
    |> Db.setParams [
        "@slug", SqlType.String slug
        "@name", SqlType.String slug
        "@tmdb_id", SqlType.Int32 tmdbId
    ]
    |> Db.exec

/// Mirror SeriesRefresh.applyToProjection's episode upsert: INSERT OR REPLACE on
/// the PK, omitting `source` so SQLite resets it to the 'tmdb' default.
let private tmdbUpsertEpisode (conn: SqliteConnection) (slug: string) (season: int) (ep: int) =
    conn
    |> Db.newCommand """
        INSERT OR REPLACE INTO series_episodes (series_slug, season_number, episode_number, name, overview, runtime, air_date, still_ref, tmdb_rating)
        VALUES (@slug, @season, @ep, 'TMDB Title', 'TMDB overview', 42, '2026-05-26', NULL, 8.1)
    """
    |> Db.setParams [
        "@slug", SqlType.String slug
        "@season", SqlType.Int32 season
        "@ep", SqlType.Int32 ep
    ]
    |> Db.exec

let private findEpisode (detail: Mediatheca.Shared.SeriesDetail) (season: int) (ep: int) =
    detail.Seasons
    |> List.tryFind (fun s -> s.SeasonNumber = season)
    |> Option.bind (fun s -> s.Episodes |> List.tryFind (fun e -> e.EpisodeNumber = ep))

[<Tests>]
let materializeTests =
    testList "JellyfinImport.materializeMissingEpisodes" [

        // (a) present-on-server but absent from projection -> materialized,
        //     including a synthetic season row, with mapped fields.
        testCase "materializes missing episodes and their synthetic season" <| fun _ ->
            let seasons = System.Collections.Generic.List<int>()
            let episodes = System.Collections.Generic.List<MaterializedEpisode>()
            let batch = [ "iwtv", [ mkEp 3 1 true; mkEp 3 2 false; mkEp 3 3 false ] ]
            let result =
                materializeMissingEpisodes
                    batch
                    (fun _ -> Set.empty)
                    (fun _ -> Set.empty)
                    (fun _ _ _ _ -> None)
                    (fun _ s -> seasons.Add s; Ok ())
                    (fun _ ep -> episodes.Add ep; Ok ())
            Expect.equal result.EpisodesMaterialized 3 "All three S3 episodes materialized"
            Expect.equal result.SeasonsMaterialized 1 "Season 3 row created exactly once"
            Expect.equal (List.ofSeq seasons) [ 3 ] "writeSeason called once for season 3"
            Expect.isFalse result.Failed "No failures"
            let e1 = episodes |> Seq.find (fun e -> e.EpisodeNumber = 1)
            Expect.equal e1.SeasonNumber 3 "Season number mapped"
            Expect.equal e1.Name "Episode 1" "Name mapped"
            Expect.equal e1.Overview "Overview of episode 1" "Overview mapped"
            Expect.equal e1.Runtime (Some 30) "RunTimeTicks -> 30 minutes"
            Expect.equal e1.AirDate (Some "2026-05-26") "PremiereDate -> air_date (date portion)"
            Expect.equal e1.StillRef None "No still fetched -> None"

        // (b) already in the projection -> not duplicated, not touched.
        testCase "does not materialize episodes already in the projection" <| fun _ ->
            let mutable seasonWrites = 0
            let mutable episodeWrites = 0
            let batch = [ "iwtv", [ mkEp 1 1 true; mkEp 1 2 true ] ]
            let result =
                materializeMissingEpisodes
                    batch
                    (fun _ -> Set.ofList [ (1, 1); (1, 2) ]) // both already present
                    (fun _ -> Set.ofList [ 1 ])
                    (fun _ _ _ _ -> None)
                    (fun _ _ -> seasonWrites <- seasonWrites + 1; Ok ())
                    (fun _ _ -> episodeWrites <- episodeWrites + 1; Ok ())
            Expect.equal result.EpisodesMaterialized 0 "Nothing materialized"
            Expect.equal episodeWrites 0 "writeEpisode never called for existing rows"
            Expect.equal seasonWrites 0 "writeSeason never called when season exists"
            Expect.isFalse result.Failed "No failures"

        // (c) present-but-unwatched is still materialized (not gated on Played).
        testCase "materializes present-but-unwatched episodes" <| fun _ ->
            let episodes = System.Collections.Generic.List<MaterializedEpisode>()
            let batch = [ "iwtv", [ mkEp 3 1 false ] ] // Played = false
            let result =
                materializeMissingEpisodes
                    batch
                    (fun _ -> Set.empty)
                    (fun _ -> Set.empty)
                    (fun _ _ _ _ -> None)
                    (fun _ _ -> Ok ())
                    (fun _ ep -> episodes.Add ep; Ok ())
            Expect.equal result.EpisodesMaterialized 1 "Unwatched episode still materialized"
            Expect.equal episodes.Count 1 "Materialization not gated on Played"

        // fault isolation: a bad episode is recorded and the rest continue.
        testCase "isolates a faulting episode and a missing-number episode" <| fun _ ->
            let written = System.Collections.Generic.List<int>()
            let bad = { mkEp 3 2 true with IndexNumber = None } // no episode number
            let batch = [ "iwtv", [ mkEp 3 1 true; bad; mkEp 3 3 true ] ]
            let result =
                materializeMissingEpisodes
                    batch
                    (fun _ -> Set.empty)
                    (fun _ -> Set.empty)
                    (fun _ _ _ _ -> None)
                    (fun _ _ -> Ok ())
                    (fun _ ep ->
                        if ep.EpisodeNumber = 3 then Error "DB locked"
                        else written.Add ep.EpisodeNumber; Ok ())
            Expect.isTrue (written.Contains 1) "E1 still materialized"
            Expect.equal result.EpisodesMaterialized 1 "Only E1 succeeded"
            Expect.isTrue result.Failed "Run reports failure"
            Expect.equal (List.length result.Errors) 2 "Bad-number episode + E3 write error both recorded"

        // image-fetch failure degrades to NULL still, not an error.
        testCase "still-fetch failure degrades to None rather than erroring" <| fun _ ->
            let episodes = System.Collections.Generic.List<MaterializedEpisode>()
            let batch = [ "iwtv", [ mkEp 3 1 true ] ]
            let result =
                materializeMissingEpisodes
                    batch
                    (fun _ -> Set.empty)
                    (fun _ -> Set.empty)
                    (fun _ _ _ _ -> failwith "image server down")
                    (fun _ _ -> Ok ())
                    (fun _ ep -> episodes.Add ep; Ok ())
            Expect.equal result.EpisodesMaterialized 1 "Episode still materialized"
            Expect.isFalse result.Failed "Image failure is not a run failure"
            Expect.equal (episodes.[0].StillRef) None "Still degraded to None"

        // (d) later TMDB refresh enriches in place and clears MetadataPending,
        //     no duplicate, watch progress preserved.
        testCase "TMDB refresh enriches materialized rows in place and clears pending" <| fun _ ->
            use conn = newConn ()
            seedSeriesDetail conn "iwtv" 1001
            SeriesProjection.materializeSeason conn "iwtv" 3
            SeriesProjection.materializeEpisode conn "iwtv"
                { SeasonNumber = 3; EpisodeNumber = 1; Name = "JF Title"
                  Overview = "JF overview"; Runtime = Some 30
                  AirDate = Some "2026-05-26"; StillRef = None }
            // record watch progress for this episode (separate table)
            conn
            |> Db.newCommand
                "INSERT INTO series_episode_progress (series_slug, rewatch_id, season_number, episode_number, watched_date)
                 VALUES ('iwtv', 'default', 3, 1, '2026-06-01')"
            |> Db.exec

            let before = SeriesProjection.getBySlug conn "iwtv" None |> Option.get
            let epBefore = findEpisode before 3 1 |> Option.get
            Expect.isTrue epBefore.MetadataPending "Materialized episode is pending before enrichment"

            // TMDB later publishes the season: INSERT OR REPLACE without `source`.
            tmdbUpsertEpisode conn "iwtv" 3 1

            let after = SeriesProjection.getBySlug conn "iwtv" None |> Option.get
            let season3 = after.Seasons |> List.find (fun s -> s.SeasonNumber = 3)
            let epAfter = findEpisode after 3 1 |> Option.get
            Expect.isFalse epAfter.MetadataPending "source reset to 'tmdb' clears pending"
            Expect.equal epAfter.Name "TMDB Title" "Enriched with TMDB metadata"
            Expect.equal season3.Episodes.Length 1 "No duplicate episode row"
            Expect.isTrue epAfter.IsWatched "Watch progress preserved across enrichment"
            Expect.equal epAfter.WatchedDate (Some "2026-06-01") "Watched date preserved"

        // (e) a materialized episode that was played gets its progress attached
        //     (materialization writes the row the progress read joins against).
        testCase "played materialized episode renders as watched with its date" <| fun _ ->
            use conn = newConn ()
            seedSeriesDetail conn "iwtv" 1002
            // materialize first (as the sync does, before the watch-history write)
            SeriesProjection.materializeSeason conn "iwtv" 3
            SeriesProjection.materializeEpisode conn "iwtv"
                { SeasonNumber = 3; EpisodeNumber = 2; Name = "JF Title"
                  Overview = "JF overview"; Runtime = Some 30
                  AirDate = Some "2026-05-26"; StillRef = None }
            // then the watch-history write records progress
            conn
            |> Db.newCommand
                "INSERT INTO series_episode_progress (series_slug, rewatch_id, season_number, episode_number, watched_date)
                 VALUES ('iwtv', 'default', 3, 2, '2026-06-10')"
            |> Db.exec

            let detail = SeriesProjection.getBySlug conn "iwtv" None |> Option.get
            let ep = findEpisode detail 3 2 |> Option.get
            Expect.isTrue ep.MetadataPending "Episode is Jellyfin-sourced"
            Expect.isTrue ep.IsWatched "Played materialized episode shows as watched"
            Expect.equal ep.WatchedDate (Some "2026-06-10") "Watched date attached"
    ]
