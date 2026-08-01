module Mediatheca.Tests.JellyfinStillTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Server.Jellyfin
open Mediatheca.Server.JellyfinImport

// Coverage for integration-007 (closes the still-image deferral recorded in
// ADR 0012's Consequences section): `JellyfinImport.fetchEpisodeStill` composes
// an injected `download` + `save` into a `still_ref`, strictly best-effort.
//
// `download`/`save` stand in for `Jellyfin.getPrimaryImageWithReauth` (run
// synchronously) and `ImageStore.saveImage` respectively — this is the seam
// that makes the compose step testable without HTTP or SQLite (same "pure
// orchestration over injected effects" idiom as `withReauthRetry` and
// `syncSeriesWatchHistory`).

let private newConn () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
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
/// the PK, writing TMDB's canonical still path and omitting `source` so SQLite
/// resets it to the 'tmdb' default.
let private tmdbUpsertEpisodeWithStill (conn: SqliteConnection) (slug: string) (season: int) (ep: int) (stillRef: string) =
    conn
    |> Db.newCommand """
        INSERT OR REPLACE INTO series_episodes (series_slug, season_number, episode_number, name, overview, runtime, air_date, still_ref, tmdb_rating)
        VALUES (@slug, @season, @ep, 'TMDB Title', 'TMDB overview', 42, '2026-05-26', @still_ref, 8.1)
    """
    |> Db.setParams [
        "@slug", SqlType.String slug
        "@season", SqlType.Int32 season
        "@ep", SqlType.Int32 ep
        "@still_ref", SqlType.String stillRef
    ]
    |> Db.exec

let private findEpisode (detail: Mediatheca.Shared.SeriesDetail) (season: int) (ep: int) =
    detail.Seasons
    |> List.tryFind (fun s -> s.SeasonNumber = season)
    |> Option.bind (fun s -> s.Episodes |> List.tryFind (fun e -> e.EpisodeNumber = ep))

/// A Jellyfin episode item with aired numbering S{season}E{episode}, matching
/// JellyfinMaterializeTests.fs's `mkEp`.
let private mkEp (season: int) (episode: int) : JellyfinBaseItem = {
    Id = sprintf "jf-%d-%d" season episode
    Name = sprintf "Episode %d" episode
    Type = "Episode"
    ProductionYear = None
    RunTimeTicks = Some 18_000_000_000L
    Genres = []
    Overview = Some (sprintf "Overview of episode %d" episode)
    ProviderIds = { Tmdb = None; Imdb = None }
    UserData = None
    SeriesName = None
    SeriesId = None
    IndexNumber = Some episode
    ParentIndexNumber = Some season
    PremiereDate = Some "2026-05-26T00:00:00.0000000Z"
    PrimaryImageTag = None
}

[<Tests>]
let fetchEpisodeStillTests =
    testList "JellyfinImport.fetchEpisodeStill" [

        testCase "download succeeds -> saves at the -jellyfin.jpg path and returns that ref" <| fun _ ->
            let mutable savedRef = None
            let mutable savedBytes = None
            let bytes = [| 1uy; 2uy; 3uy |]
            let result =
                fetchEpisodeStill
                    (fun _jellyfinId -> Ok bytes)
                    (fun ref b -> savedRef <- Some ref; savedBytes <- Some b)
                    "iwtv" 3 1 "jf-item-id"
            Expect.equal result (Some "stills/iwtv-s03e01-jellyfin.jpg") "Returns the distinct Jellyfin-suffixed ref"
            Expect.equal savedRef (Some "stills/iwtv-s03e01-jellyfin.jpg") "Saved at the same ref returned"
            Expect.equal savedBytes (Some bytes) "Saved the downloaded bytes verbatim"

        testCase "zero-pads season and episode numbers" <| fun _ ->
            let result =
                fetchEpisodeStill
                    (fun _ -> Ok [| 0uy |])
                    (fun _ _ -> ())
                    "show" 10 3 "id"
            Expect.equal result (Some "stills/show-s10e03-jellyfin.jpg") "Season/episode zero-padded to two digits"

        testCase "download failure (e.g. 404/no image) degrades to None, save never called" <| fun _ ->
            let mutable saveCalled = false
            let result =
                fetchEpisodeStill
                    (fun _ -> Error "HTTP 404")
                    (fun _ _ -> saveCalled <- true)
                    "iwtv" 3 1 "jf-item-id"
            Expect.equal result None "No still_ref on download failure"
            Expect.isFalse saveCalled "save is never invoked when download fails"

        testCase "download throwing degrades to None rather than propagating" <| fun _ ->
            let result =
                fetchEpisodeStill
                    (fun _ -> failwith "image server down")
                    (fun _ _ -> ())
                    "iwtv" 3 1 "jf-item-id"
            Expect.equal result None "Thrown exception from download degrades to None"

        testCase "save throwing (write error) degrades to None rather than propagating" <| fun _ ->
            let result =
                fetchEpisodeStill
                    (fun _ -> Ok [| 1uy |])
                    (fun _ _ -> failwith "disk full")
                    "iwtv" 3 1 "jf-item-id"
            Expect.equal result None "Thrown exception from save degrades to None"

        testCase "wired as materializeMissingEpisodes' fetchStill: success resolves StillRef, no errors" <| fun _ ->
            let episodes = System.Collections.Generic.List<MaterializedEpisode>()
            let bytes = [| 9uy |]
            let batch = [ "iwtv", [ mkEp 3 1 ] ]
            let result =
                materializeMissingEpisodes
                    batch
                    (fun _ -> Set.empty)
                    (fun _ -> Set.empty)
                    (fetchEpisodeStill (fun _ -> Ok bytes) (fun _ _ -> ()))
                    (fun _ _ -> Ok ())
                    (fun _ ep -> episodes.Add ep; Ok ())
            Expect.equal result.EpisodesMaterialized 1 "Episode materialized"
            Expect.isFalse result.Failed "No failures"
            Expect.equal (episodes.[0].StillRef) (Some "stills/iwtv-s03e01-jellyfin.jpg") "Still resolved into MaterializedEpisode"

        testCase "wired as materializeMissingEpisodes' fetchStill: failure leaves StillRef None, no errors, Failed=false" <| fun _ ->
            let episodes = System.Collections.Generic.List<MaterializedEpisode>()
            let batch = [ "iwtv", [ mkEp 3 1 ] ]
            let result =
                materializeMissingEpisodes
                    batch
                    (fun _ -> Set.empty)
                    (fun _ -> Set.empty)
                    (fetchEpisodeStill (fun _ -> Error "HTTP 404") (fun _ _ -> ()))
                    (fun _ _ -> Ok ())
                    (fun _ ep -> episodes.Add ep; Ok ())
            Expect.equal result.EpisodesMaterialized 1 "Episode still materialized despite still-fetch failure"
            Expect.isFalse result.Failed "A still-fetch failure never turns the run into a failure"
            Expect.isEmpty result.Errors "No error recorded for a best-effort still-fetch failure"
            Expect.equal (episodes.[0].StillRef) None "StillRef left None"

        // Acceptance criterion 3: a later TMDB refresh still overwrites the still
        // with TMDB's canonical path. Because the Jellyfin file lives at a
        // distinct `-jellyfin.jpg` path, it never occupies the path
        // `SeriesRefresh`'s `ImageStore.imageExists` short-circuit checks, so a
        // TMDB write always resets `still_ref` to its own canonical path via
        // INSERT OR REPLACE (m4k7p enrichment behaviour preserved).
        testCase "a materialized episode with a Jellyfin still is overwritten by a later TMDB refresh's canonical still" <| fun _ ->
            use conn = newConn ()
            seedSeriesDetail conn "iwtv" 1003
            SeriesProjection.materializeSeason conn "iwtv" 3
            SeriesProjection.materializeEpisode conn "iwtv"
                { SeasonNumber = 3; EpisodeNumber = 1; Name = "JF Title"
                  Overview = "JF overview"; Runtime = Some 30
                  AirDate = Some "2026-05-26"
                  StillRef = Some "stills/iwtv-s03e01-jellyfin.jpg" }

            let before = SeriesProjection.getBySlug conn "iwtv" None |> Option.get
            let epBefore = findEpisode before 3 1 |> Option.get
            Expect.equal epBefore.StillRef (Some "stills/iwtv-s03e01-jellyfin.jpg") "Jellyfin still present before TMDB enrichment"

            // TMDB later publishes the season with its own canonical still path.
            tmdbUpsertEpisodeWithStill conn "iwtv" 3 1 "stills/iwtv-s03e01.jpg"

            let after = SeriesProjection.getBySlug conn "iwtv" None |> Option.get
            let epAfter = findEpisode after 3 1 |> Option.get
            Expect.equal epAfter.StillRef (Some "stills/iwtv-s03e01.jpg") "still_ref repointed at TMDB's canonical path"
            Expect.isFalse epAfter.MetadataPending "source reset to 'tmdb' clears pending"
            Expect.equal after.Seasons.[0].Episodes.Length 1 "No duplicate episode row"
    ]
