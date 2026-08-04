module Mediatheca.Tests.GameFacetProjectionTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

/// games-a7dqx (ADR-0053): schema, `handleEvent`, the query-time merge
/// composition, and the safe read-composition switches this task adds to
/// `GameProjection.fs`/`MetadataCache.fs`.

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn
    conn

let private sampleGameData: Games.GameAddedData = {
    Name = "Portal 2"
    Year = 2011
    Genres = [ "Puzzle" ]
    Description = "A co-op puzzle game"
    ShortDescription = "Puzzles"
    WebsiteUrl = Some "https://portal2.com"
    CoverRef = Some "games/portal-2-2011-cover.jpg"
    BackdropRef = None
    RawgId = Some 4200
    RawgRating = Some 4.6
}

let private noOverride : PlayFacetsOverride = {
    Solo = None; CoopCouch = None; CoopOnline = None; VersusCouch = None
    VersusOnline = None; RemotePlayTogether = None; Vr = None
}

let private appendGameAdded (conn: SqliteConnection) (slug: string) (data: Games.GameAddedData) =
    EventStore.appendToStream conn (Games.streamId slug) -1L
        [ Games.Serialization.toEventData (Games.Game_added_to_library data) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private appendOverride (conn: SqliteConnection) (slug: string) (ovr: PlayFacetsOverride) =
    let streamId = Games.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position
        [ Games.Serialization.toEventData (Games.Game_play_facets_overridden ovr) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private allColumns (conn: SqliteConnection) (table: string) : string list =
    use cmd = conn.CreateCommand()
    cmd.CommandText <- sprintf "PRAGMA table_info(%s)" table
    use reader = cmd.ExecuteReader()
    [ while reader.Read() do yield reader.GetString(reader.GetOrdinal("name")) ]

[<Tests>]
let schemaTests =
    testList "GameProjection facet_override_* schema (games-a7dqx)" [

        testCase "game_detail gains 7 nullable facet_override_* columns" <| fun _ ->
            let conn = createConnection ()
            let cols = allColumns conn "game_detail" |> Set.ofList
            for col in [ "facet_override_solo"; "facet_override_coop_couch"; "facet_override_coop_online"
                         "facet_override_versus_couch"; "facet_override_versus_online"
                         "facet_override_remote_play_together"; "facet_override_vr" ] do
                Expect.isTrue (Set.contains col cols) (sprintf "game_detail should have column %s" col)

        testCase "GameProjection.handler.Init run twice is idempotent — no error, same columns" <| fun _ ->
            let conn = createConnection ()
            let before = allColumns conn "game_detail"
            GameProjection.handler.Init conn
            Expect.equal (allColumns conn "game_detail") before "Schema unchanged by a second Init"

        testCase "No existing game_detail column is dropped or renamed by this task's migration" <| fun _ ->
            let conn = createConnection ()
            let cols = allColumns conn "game_detail" |> Set.ofList
            for col in [ "description"; "short_description"; "website_url"; "hltb_hours"
                         "hltb_main_plus_hours"; "hltb_completionist_hours"; "play_modes"; "steam_last_played" ] do
                Expect.isTrue (Set.contains col cols) (sprintf "game_detail must still have pre-existing column %s" col)
    ]

[<Tests>]
let handleEventTests =
    testList "GameProjection.handleEvent Game_play_facets_overridden (games-a7dqx)" [

        testCase "Writes all 7 facet_override_* columns from the event payload" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let ovr : PlayFacetsOverride = {
                Solo = Some true; CoopCouch = Some false; CoopOnline = Some true
                VersusCouch = None; VersusOnline = None; RemotePlayTogether = Some true; Vr = Some VrSupported
            }
            appendOverride conn "portal-2-2011" ovr

            let row =
                conn
                |> Db.newCommand """
                    SELECT facet_override_solo, facet_override_coop_couch, facet_override_coop_online,
                           facet_override_versus_couch, facet_override_versus_online,
                           facet_override_remote_play_together, facet_override_vr
                    FROM game_detail WHERE slug = @slug
                """
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadInt32 "facet_override_solo",
                    rd.ReadInt32 "facet_override_coop_couch",
                    rd.ReadInt32 "facet_override_coop_online",
                    rd.IsDBNull(rd.GetOrdinal("facet_override_versus_couch")),
                    rd.IsDBNull(rd.GetOrdinal("facet_override_versus_online")),
                    rd.ReadInt32 "facet_override_remote_play_together",
                    rd.ReadString "facet_override_vr")
            Expect.equal row (Some (1, 0, 1, true, true, 1, "VrSupported")) "Booleans as 0/1, unset fields NULL, Vr as text"

        testCase "This arm never writes game_metadata_cache — the cache tier stays untouched" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            MetadataCache.seedFromProjections conn
            let cacheRowBefore =
                conn
                |> Db.newCommand "SELECT facet_solo FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd -> if rd.IsDBNull(rd.GetOrdinal("facet_solo")) then None else Some (rd.ReadInt32 "facet_solo"))
            appendOverride conn "portal-2-2011" { noOverride with Solo = Some true }
            let cacheRowAfter =
                conn
                |> Db.newCommand "SELECT facet_solo FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd -> if rd.IsDBNull(rd.GetOrdinal("facet_solo")) then None else Some (rd.ReadInt32 "facet_solo"))
            Expect.equal cacheRowAfter cacheRowBefore "game_metadata_cache.facet_solo is untouched by an override event"
    ]

[<Tests>]
let getPlayFacetsTests =
    testList "GameProjection.getPlayFacets — query-time merge composition (ADR-0053)" [

        testCase "No cache row, no override — degrades to all-false/NoVr, never a fabricated value" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let facets = GameProjection.getPlayFacets conn "portal-2-2011"
            Expect.equal facets
                (Some { Solo = false; CoopCouch = false; CoopOnline = false; VersusCouch = false; VersusOnline = false; RemotePlayTogether = false; Vr = NoVr })
                "Honest degradation — no cache row yet, no override, all-false/NoVr"

        testCase "Cache-derived facets flow through untouched when there is no override" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let cachedFacets : PlayFacets = {
                Solo = true; CoopCouch = true; CoopOnline = true; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = true; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" cachedFacets [ 2; 1; 9; 38; 39; 24; 44 ]

            let facets = GameProjection.getPlayFacets conn "portal-2-2011"
            Expect.equal facets (Some cachedFacets) "No override recorded — the cache default passes through unchanged"

        testCase "An override wins over the cache on the overridden field only" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let cachedFacets : PlayFacets = {
                Solo = true; CoopCouch = true; CoopOnline = true; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = true; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" cachedFacets [ 2; 1; 9; 38; 39; 24; 44 ]
            appendOverride conn "portal-2-2011" { noOverride with VersusOnline = Some true; Vr = Some VrOnly }

            let facets = GameProjection.getPlayFacets conn "portal-2-2011"
            match facets with
            | Some f ->
                Expect.isTrue f.VersusOnline "Overridden to true"
                Expect.equal f.Vr VrOnly "Overridden to VrOnly"
                Expect.isTrue f.Solo "Untouched, still the cache's true"
                Expect.isTrue f.CoopOnline "Untouched, still the cache's true"
            | None -> failtest "Expected a composed PlayFacets"

        testCase "Some false override beats a true cache value" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let cachedFacets : PlayFacets = {
                Solo = true; CoopCouch = false; CoopOnline = true; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = false; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" cachedFacets [ 2; 1; 9; 38 ]
            appendOverride conn "portal-2-2011" { noOverride with CoopOnline = Some false }

            let facets = GameProjection.getPlayFacets conn "portal-2-2011"
            Expect.equal (facets |> Option.map (fun f -> f.CoopOnline)) (Some false) "Steam said co-op-online true; the manual correction says false and wins"
    ]

[<Tests>]
let safeReadCompositionTests =
    testList "GameProjection safe read-composition switches (games-a7dqx)" [

        testCase "getBySlug.SteamLastPlayed computes MAX(date) over game_play_session, not the old game_detail column" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            // Stamp the OLD column directly to prove it is no longer read.
            conn
            |> Db.newCommand "UPDATE game_detail SET steam_last_played = '2020-01-01' WHERE slug = @slug"
            |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
            |> Db.exec

            let beforeAnySession = GameProjection.getBySlug conn "portal-2-2011"
            Expect.equal (beforeAnySession |> Option.bind (fun g -> g.SteamLastPlayed)) None
                "A game whose only history is dateless prior playtime (here: none at all) must read None, not the stale frozen column"

            EventStore.appendToStream conn (Games.streamId "portal-2-2011") 0L
                [ Games.Serialization.toEventData (Games.Play_session_recorded { Day = "2024-06-15"; Minutes = 60; Source = Manual }) ] |> ignore
            Projection.runProjection conn PlaySessionProjection.handler
            Projection.runProjection conn GameProjection.handler

            let afterSession = GameProjection.getBySlug conn "portal-2-2011"
            Expect.equal (afterSession |> Option.bind (fun g -> g.SteamLastPlayed)) (Some "2024-06-15")
                "Now derived from the real session date, not the stale 2020-01-01 the old column still holds"

        testCase "getBySlug reads description/hltb from game_metadata_cache, honest-degrading to \"\"/None on a cache miss" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            // The old game_detail columns still carry the original values —
            // proving the read genuinely switched, not that both happen to agree.
            let detail = GameProjection.getBySlug conn "portal-2-2011"
            match detail with
            | Some d ->
                Expect.equal d.Description "" "No game_metadata_cache row yet — honest degradation to empty string, not the game_detail value"
                Expect.equal d.HltbHours None "No cache row yet — None, not a frozen value"
            | None -> failtest "Expected a game"

            MetadataCache.seedFromProjections conn
            let seeded = GameProjection.getBySlug conn "portal-2-2011"
            match seeded with
            | Some d -> Expect.equal d.Description sampleGameData.Description "Once seeded, the cache row is what getBySlug now reads"
            | None -> failtest "Expected a game"

        testCase "getAll reads hltb_hours from game_metadata_cache" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let beforeSeed = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "portal-2-2011")
            Expect.equal (beforeSeed |> Option.bind (fun g -> g.HltbHours)) None "No cache row yet"

            MetadataCache.seedFromProjections conn
            conn
            |> Db.newCommand "UPDATE game_metadata_cache SET hltb_hours = 12.5 WHERE game_slug = @slug"
            |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
            |> Db.exec

            let afterSeed = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "portal-2-2011")
            Expect.equal (afterSeed |> Option.bind (fun g -> g.HltbHours)) (Some 12.5) "getAll now reads the cache column"

        testCase "genres stays sourced from game_detail.genres — not switched to game_metadata_cache" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" { sampleGameData with Genres = [ "Puzzle"; "Co-op" ] }
            MetadataCache.seedFromProjections conn
            // game_metadata_cache.genres is never written by anything in this task.
            let cacheGenres =
                conn
                |> Db.newCommand "SELECT genres FROM game_metadata_cache WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd -> if rd.IsDBNull(rd.GetOrdinal("genres")) then None else Some (rd.ReadString "genres"))
            Expect.equal cacheGenres (Some None) "the row exists (seeded), but its genres column is NULL — never populated by this task"

            let detail = GameProjection.getBySlug conn "portal-2-2011"
            Expect.equal (detail |> Option.map (fun d -> d.Genres)) (Some [ "Puzzle"; "Co-op" ]) "genres still reads correctly off game_detail"

        testCase "getGamesCompletedPerYear/getGamesBeatenThisYear drop the COALESCE fallback to game_detail.steam_last_played" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            // Mark Retired and stamp ONLY the old (now-unread) column — no
            // game_play_session row at all.
            EventStore.appendToStream conn (Games.streamId "portal-2-2011") 0L
                [ Games.Serialization.toEventData (Games.Game_status_changed Retired) ] |> ignore
            Projection.runProjection conn GameProjection.handler
            conn
            |> Db.newCommand "UPDATE game_detail SET steam_last_played = '2020-01-01' WHERE slug = @slug"
            |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
            |> Db.exec

            let completedPerYear = GameProjection.getGamesCompletedPerYear conn
            Expect.isEmpty completedPerYear "No game_play_session row exists — the stale game_detail column must not paper over the gap"

            let beatenThisYear = GameProjection.getGamesBeatenThisYear conn
            Expect.equal beatenThisYear 0 "Same honest-degradation stance for the cross-media 'beaten this year' count"
    ]

[<Tests>]
let driftAndRebuildTests =
    testList "GameProjection Drop/Init/replay rebuild reconstructs facet_override_* (games-a7dqx)" [

        testCase "A full Projection.rebuildProjection (drop + replay) reproduces game_detail's facet_override_* columns from Game_play_facets_overridden events in the log" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let ovr : PlayFacetsOverride = {
                Solo = Some true; CoopCouch = None; CoopOnline = Some false; VersusCouch = None
                VersusOnline = None; RemotePlayTogether = Some true; Vr = Some VrOnly
            }
            appendOverride conn "portal-2-2011" ovr

            let readOverrideRow () =
                conn
                |> Db.newCommand """
                    SELECT facet_override_solo, facet_override_coop_online, facet_override_remote_play_together, facet_override_vr
                    FROM game_detail WHERE slug = @slug
                """
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd ->
                    rd.ReadInt32 "facet_override_solo", rd.ReadInt32 "facet_override_coop_online",
                    rd.ReadInt32 "facet_override_remote_play_together", rd.ReadString "facet_override_vr")

            let beforeRebuild = readOverrideRow ()

            Projection.rebuildProjection conn GameProjection.handler

            let afterRebuild = readOverrideRow ()
            Expect.equal afterRebuild beforeRebuild "Drop + Init + full replay reproduces the exact same facet_override_* values — the codec/evolve/handleEvent chain round-trips through a real rebuild, not just declared"
            Expect.equal afterRebuild (Some (1, 0, 1, "VrOnly")) "sanity: the reconstructed values match what was originally written"

        testCase "checkProjectionDrift stays zero for GameProjection after a Game_play_facets_overridden event, with game_metadata_cache present" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            appendOverride conn "portal-2-2011" { noOverride with Solo = Some true }
            MetadataCache.seedFromProjections conn

            let shadow = new SqliteConnection("Data Source=:memory:")
            shadow.Open()
            let results = Administration.checkProjectionDrift conn shadow [ GameProjection.handler ] (fun _ -> ())

            let totalDiscrepancies = results |> List.sumBy (fun p -> List.length p.Discrepancies)
            Expect.equal totalDiscrepancies 0 "No existing write path was altered, only added — drift stays zero"
    ]
