module Mediatheca.Tests.GameFacetProjectionTests

open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

/// games-a7dqx (ADR-0053): schema, `handleEvent`, the query-time merge
/// composition, and the safe read-composition switches this task adds to
/// `GameProjection.fs`/`MetadataCache.fs`.
///
/// games-v4nqe adds: the identity-card writer's ON-CONFLICT slice discipline,
/// the column-drop migration itself, the demoted-events-replay-as-no-ops
/// proof at the projection layer (mirroring `GamesTests.fs`'s aggregate-layer
/// proof), and `getAll`/`getBySlug` wiring `PlayFacets`/`PlayFacetsOverride`
/// into the DTO. `genres` is deliberately NOT part of the identity-card cache
/// slice or the column drop — ADR-0055 (amending ADR-0043) keeps it an
/// event-carried `game_list`/`game_detail` projection column, reverting
/// iteration 1's attempt to cache-source it.

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    SettingsStore.initialize conn
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    GameJournal.initialize conn
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
            GameJournal.initialize conn
            Expect.equal (allColumns conn "game_detail") before "Schema unchanged by a second Init"

        testCase "games-a7dqx shipped additively: family_owners and other non-demoted columns survive untouched" <| fun _ ->
            let conn = createConnection ()
            let cols = allColumns conn "game_detail" |> Set.ofList
            for col in [ "cover_ref"; "backdrop_ref"; "rawg_id"; "rawg_rating"; "family_owners"; "steam_library_date" ] do
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
    testList "GameProjection safe read-composition switches (games-a7dqx / games-v4nqe)" [

        testCase "getBySlug.SteamLastPlayed computes MAX(date) over game_play_session — game_detail.steam_last_played no longer exists to read" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData

            let beforeAnySession = GameProjection.getBySlug conn "portal-2-2011"
            Expect.equal (beforeAnySession |> Option.bind (fun g -> g.SteamLastPlayed)) None
                "A game whose only history is dateless prior playtime (here: none at all) must read None"

            EventStore.appendToStream conn (Games.streamId "portal-2-2011") 0L
                [ Games.Serialization.toEventData (Games.Play_session_recorded { Day = "2024-06-15"; Minutes = 60; Source = Manual }) ] |> ignore
            Projection.runProjection conn PlaySessionProjection.handler
            Projection.runProjection conn GameProjection.handler

            let afterSession = GameProjection.getBySlug conn "portal-2-2011"
            Expect.equal (afterSession |> Option.bind (fun g -> g.SteamLastPlayed)) (Some "2024-06-15")
                "Derived from the real session date"

        testCase "getBySlug reads description/hltb from game_metadata_cache, honest-degrading to \"\"/None on a cache miss" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let detail = GameProjection.getBySlug conn "portal-2-2011"
            match detail with
            | Some d ->
                Expect.equal d.Description "" "No game_metadata_cache row yet — honest degradation to empty string"
                Expect.equal d.HltbHours None "No cache row yet — None, not a frozen value"
            | None -> failtest "Expected a game"

            MetadataCache.upsertGameIdentityCard conn "portal-2-2011" {
                Description = sampleGameData.Description
                ShortDescription = sampleGameData.ShortDescription
                WebsiteUrl = sampleGameData.WebsiteUrl
            }
            let seeded = GameProjection.getBySlug conn "portal-2-2011"
            match seeded with
            | Some d -> Expect.equal d.Description sampleGameData.Description "Once written, the cache row is what getBySlug now reads"
            | None -> failtest "Expected a game"

        testCase "getAll reads hltb_hours from game_metadata_cache" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let beforeSeed = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "portal-2-2011")
            Expect.equal (beforeSeed |> Option.bind (fun g -> g.HltbHours)) None "No cache row yet"

            MetadataCache.upsertGameHltbHours conn "portal-2-2011" (Some 12.5) None None

            let afterSeed = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "portal-2-2011")
            Expect.equal (afterSeed |> Option.bind (fun g -> g.HltbHours)) (Some 12.5) "getAll now reads the cache column"

        testCase "genres stays event-carried on game_list/game_detail — never touches game_metadata_cache (ADR-0055, amending ADR-0043)" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" { sampleGameData with Genres = [ "Puzzle"; "Co-op" ] }

            // Game_added_to_library's handleEvent arm writes genres directly
            // to game_list/game_detail — no game_metadata_cache row exists at
            // all yet, and genres must still read correctly.
            let detail = GameProjection.getBySlug conn "portal-2-2011"
            Expect.equal (detail |> Option.map (fun d -> d.Genres)) (Some [ "Puzzle"; "Co-op" ]) "genres reads from game_detail.genres, no cache row needed"

            let listItem = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "portal-2-2011")
            Expect.equal (listItem |> Option.map (fun g -> g.Genres)) (Some [ "Puzzle"; "Co-op" ]) "getAll's Genres field also reads from game_list.genres"

            // A full projection rebuild reproduces genres deterministically
            // from the event log alone — no cache backfill needed.
            Projection.rebuildProjection conn GameProjection.handler
            let afterRebuild = GameProjection.getBySlug conn "portal-2-2011"
            Expect.equal (afterRebuild |> Option.map (fun d -> d.Genres)) (Some [ "Puzzle"; "Co-op" ]) "Drop + Init + replay reproduces genres — event-carried, never lost"

        testCase "getGamesCompletedPerYear/getGamesBeatenThisYear have no stale column to fall back to at all — honest degradation" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            // Mark Retired — no game_play_session row at all.
            EventStore.appendToStream conn (Games.streamId "portal-2-2011") 0L
                [ Games.Serialization.toEventData (Games.Game_status_changed Retired) ] |> ignore
            Projection.runProjection conn GameProjection.handler

            let completedPerYear = GameProjection.getGamesCompletedPerYear conn
            Expect.isEmpty completedPerYear "No game_play_session row exists — nothing to fall back to, correctly excluded"

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

/// games-v4nqe: the identity-card writer's ON-CONFLICT slice discipline —
/// the acceptance criterion this task pins explicitly (an identity-card
/// write must never null a row's facet/category-id/fetched_at columns).
[<Tests>]
let identityCardWriterTests =
    testList "MetadataCache.upsertGameIdentityCard slice discipline (games-v4nqe)" [

        testCase "An identity-card write survives alongside an existing row's facet/category-id/fetched_at values" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let facets : PlayFacets = {
                Solo = true; CoopCouch = true; CoopOnline = false; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = true; Vr = VrSupported
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" facets [ 2; 1; 9; 53 ]

            MetadataCache.upsertGameIdentityCard conn "portal-2-2011" {
                Description = "A new description"
                ShortDescription = "New short desc"
                WebsiteUrl = Some "https://new-url.example"
            }

            let row =
                conn
                |> Db.newCommand """
                    SELECT facet_solo, facet_coop_couch, facet_remote_play_together, facet_vr, steam_category_ids, fetched_at,
                           description, short_description, website_url
                    FROM game_metadata_cache WHERE game_slug = @slug
                """
                |> Db.setParams [ "slug", SqlType.String "portal-2-2011" ]
                |> Db.querySingle (fun rd ->
                    {| Solo = rd.ReadInt32 "facet_solo"
                       CoopCouch = rd.ReadInt32 "facet_coop_couch"
                       RemotePlayTogether = rd.ReadInt32 "facet_remote_play_together"
                       Vr = rd.ReadString "facet_vr"
                       CategoryIds = rd.ReadString "steam_category_ids"
                       FetchedAtIsNull = rd.IsDBNull(rd.GetOrdinal("fetched_at"))
                       Description = rd.ReadString "description"
                       ShortDescription = rd.ReadString "short_description"
                       WebsiteUrl = rd.ReadString "website_url" |})
            match row with
            | Some r ->
                Expect.equal (r.Solo, r.CoopCouch, r.RemotePlayTogether, r.Vr) (1, 1, 1, "VrSupported") "Facet columns survive an identity-card write — not INSERT OR REPLACE"
                Expect.stringContains r.CategoryIds "53" "steam_category_ids survives too"
                Expect.isFalse r.FetchedAtIsNull "fetched_at (the facet backfill's own resume cursor) is untouched by an identity-card write"
                Expect.equal r.Description "A new description" "description IS updated by the identity-card write"
                Expect.equal r.ShortDescription "New short desc" "short_description IS updated"
                Expect.equal r.WebsiteUrl "https://new-url.example" "website_url IS updated"
            | None -> failtest "expected the row to still exist"

        testCase "tryGetGameIdentityCard degrades to empty defaults, never a fabricated value, when no cache row exists yet" <| fun _ ->
            let conn = createConnection ()
            MetadataCache.initialize conn
            let card = MetadataCache.tryGetGameIdentityCard conn "never-seen-slug"
            Expect.equal card.Description "" "Honest degradation to empty string"
            Expect.equal card.ShortDescription "" "Honest degradation to empty string"
            Expect.equal card.WebsiteUrl None "Honest degradation to None"

        testCase "A read-modify-write via tryGetGameIdentityCard + upsertGameIdentityCard echoes untouched fields unchanged" <| fun _ ->
            let conn = createConnection ()
            MetadataCache.initialize conn
            MetadataCache.upsertGameIdentityCard conn "portal-2-2011" {
                Description = "Original description"
                ShortDescription = "Original short"
                WebsiteUrl = Some "https://original.example"
            }
            // Mirrors Api.fs's `updateGameIdentityCache` helper: only override
            // short_description, echo everything else back unchanged.
            let current = MetadataCache.tryGetGameIdentityCard conn "portal-2-2011"
            MetadataCache.upsertGameIdentityCard conn "portal-2-2011" { current with ShortDescription = "Refreshed short" }

            let updated = MetadataCache.tryGetGameIdentityCard conn "portal-2-2011"
            Expect.equal updated.Description "Original description" "Description echoed unchanged"
            Expect.equal updated.ShortDescription "Refreshed short" "ShortDescription is the only field that changed"
            Expect.equal updated.WebsiteUrl (Some "https://original.example") "WebsiteUrl echoed unchanged"
    ]

/// games-v4nqe: the column-drop migration this task ships. `genres` is
/// deliberately excluded from the drop list — ADR-0055 (amending ADR-0043)
/// reverted iteration 1's plan to drop `game_list.genres`/`game_detail.genres`
/// and cache-source them, so there is no genres copy-migration to test here
/// at all (that migration was removed along with the plan).
[<Tests>]
let migrationTests =
    testList "GameProjection column-drop migration (games-v4nqe)" [

        testCase "dropDeprecatedColumns removes every column this task's disposition table names, from both game_list and game_detail — genres survives (ADR-0055)" <| fun _ ->
            let conn = createConnection ()
            GameProjection.dropDeprecatedColumns conn
            let listCols = allColumns conn "game_list" |> Set.ofList
            let detailCols = allColumns conn "game_detail" |> Set.ofList
            for col in [ "hltb_hours" ] do
                Expect.isFalse (Set.contains col listCols) (sprintf "game_list.%s should be dropped" col)
            for col in [ "description"; "short_description"; "website_url"
                         "hltb_hours"; "hltb_main_plus_hours"; "hltb_completionist_hours"
                         "play_modes"; "steam_last_played" ] do
                Expect.isFalse (Set.contains col detailCols) (sprintf "game_detail.%s should be dropped" col)
            // Sanity: columns NOT in the disposition table survive, genres included.
            Expect.isTrue (Set.contains "genres" listCols) "game_list.genres must survive — ADR-0055 keeps it event-carried"
            for col in [ "genres"; "cover_ref"; "backdrop_ref"; "rawg_id"; "rawg_rating"; "family_owners" ] do
                Expect.isTrue (Set.contains col detailCols) (sprintf "game_detail.%s must survive the drop" col)

        testCase "dropDeprecatedColumns is idempotent — running it twice does not error" <| fun _ ->
            let conn = createConnection ()
            GameProjection.dropDeprecatedColumns conn
            GameProjection.dropDeprecatedColumns conn
            Expect.isTrue true "no exception on the second run"
    ]

/// games-v4nqe: the projection-layer half of the four-part rule's no-op
/// proof (`GamesTests.fs`'s `demotedEventsAreNoOpsTests` covers the
/// aggregate/`evolve` half) — a pre-cutover stream containing these events
/// still replays through `GameProjection.handleEvent` without error or
/// touching the columns those events used to write.
[<Tests>]
let demotedEventsReplayTests =
    testList "GameProjection.handleEvent demoted events replay as no-ops (games-v4nqe)" [

        testCase "Game_categorized, Game_hltb_hours_set, Game_description_set, Game_short_description_set, Game_website_url_set, Game_play_mode_added/removed, Game_steam_last_played_set all replay without error" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let streamId = Games.streamId "portal-2-2011"
            let position = EventStore.getStreamPosition conn streamId
            let demotedEvents =
                [ Games.Game_categorized [ "Horror" ]
                  Games.Game_hltb_hours_set (Some 12.0, None, None)
                  Games.Game_description_set "changed"
                  Games.Game_short_description_set "changed short"
                  Games.Game_website_url_set (Some "https://changed.example")
                  Games.Game_play_mode_added "Co-op"
                  Games.Game_play_mode_removed "Co-op"
                  Games.Game_steam_last_played_set (Some "2024-06-20") ]
                |> List.map Games.Serialization.toEventData
            EventStore.appendToStream conn streamId position demotedEvents |> ignore

            // No exception is the primary assertion (handleEvent no longer
            // has arms that write to the dropped columns at all) — replay
            // via the full projection runner.
            Projection.runProjection conn GameProjection.handler

            let detail = GameProjection.getBySlug conn "portal-2-2011"
            match detail with
            | Some d ->
                // GameProjection.handleEvent's Game_added_to_library arm
                // (hazard 1) never writes game_metadata_cache — only the
                // imperative creation code path does, not exercised here —
                // so Description honestly degrades to empty, proving that
                // arm is a genuine no-op rather than accidentally reading a
                // stale value.
                Expect.equal d.Description "" "No cache row exists — honest degradation, not a value these demoted events wrote"
                // Genres is event-carried (ADR-0055) and unaffected by any of
                // these demoted events, including the legacy Game_categorized
                // no-op — it still reflects Game_added_to_library's payload.
                Expect.equal d.Genres [ "Puzzle" ] "Game_categorized is a no-op; genres is still exactly what Game_added_to_library carried"
            | None -> failtest "Expected the game to still exist"
    ]

/// games-v4nqe: `getAll`/`getBySlug` wiring `PlayFacets`/`PlayFacetsOverride`
/// into the DTO assembly (the acceptance criteria this task's read
/// composition section calls for) — `getPlayFacetsTests` above already
/// exercises the merge itself via `GameProjection.getPlayFacets`; this group
/// proves the two public list/detail DTOs carry the same composed value.
[<Tests>]
let dtoFacetWiringTests =
    testList "GameProjection getAll/getBySlug wire PlayFacets/PlayFacetsOverride (games-v4nqe)" [

        testCase "getBySlug.PlayFacets merges the cache default with the override, and PlayFacetsOverride carries the raw record" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let cachedFacets : PlayFacets = {
                Solo = true; CoopCouch = true; CoopOnline = true; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = true; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" cachedFacets [ 2; 1; 9; 38; 39; 24; 44 ]
            let ovr = { noOverride with CoopOnline = Some false; Vr = Some VrOnly }
            appendOverride conn "portal-2-2011" ovr

            let detail = GameProjection.getBySlug conn "portal-2-2011"
            match detail with
            | Some d ->
                Expect.isTrue d.PlayFacets.Solo "Untouched, still the cache's true"
                Expect.isFalse d.PlayFacets.CoopOnline "Overridden to false"
                Expect.equal d.PlayFacets.Vr VrOnly "Overridden to VrOnly"
                Expect.equal d.PlayFacetsOverride ovr "The raw override record — not the merged value — for the client's next overrideGamePlayFacets call"
            | None -> failtest "Expected a game"

        testCase "getAll.PlayFacets merges the cache default with the override for every list row" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData
            let cachedFacets : PlayFacets = {
                Solo = false; CoopCouch = false; CoopOnline = true; VersusCouch = false
                VersusOnline = false; RemotePlayTogether = false; Vr = NoVr
            }
            MetadataCache.upsertGameFacets conn "portal-2-2011" cachedFacets [ 9; 38 ]
            appendOverride conn "portal-2-2011" { noOverride with Solo = Some true }

            let listItem = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "portal-2-2011")
            match listItem with
            | Some g ->
                Expect.isTrue g.PlayFacets.Solo "Overridden to true"
                Expect.isTrue g.PlayFacets.CoopOnline "Untouched, still the cache's true"
            | None -> failtest "Expected a game in getAll"

        testCase "No cache row, no override — getBySlug/getAll both degrade to all-false/NoVr, never a fabricated value" <| fun _ ->
            let conn = createConnection ()
            appendGameAdded conn "portal-2-2011" sampleGameData

            let detail = GameProjection.getBySlug conn "portal-2-2011"
            let allFacetsFalse (f: PlayFacets) =
                f = { Solo = false; CoopCouch = false; CoopOnline = false; VersusCouch = false; VersusOnline = false; RemotePlayTogether = false; Vr = NoVr }
            match detail with
            | Some d -> Expect.isTrue (allFacetsFalse d.PlayFacets) "getBySlug honest degradation"
            | None -> failtest "Expected a game"

            let listItem = GameProjection.getAll conn |> List.tryFind (fun g -> g.Slug = "portal-2-2011")
            match listItem with
            | Some g -> Expect.isTrue (allFacetsFalse g.PlayFacets) "getAll honest degradation"
            | None -> failtest "Expected a game in getAll"
    ]
