module Mediatheca.Tests.RomMPlayButtonTests

/// integration-q748k (ADR-0088 concept extended): coverage for `GameDetail.
/// RommPlayUrl`, computed by `GameProjection.getBySlug`'s LEFT JOIN through
/// `romm_rom_platform` (upserted by `RomMSync`, see `RomMSyncTests.fs`'s own
/// "upserts the linked rom's platform slug" test) and `RomM.playerRouteFor`
/// (pure-function coverage lives in `RomMTests.fs`). No HTTP here at all --
/// every scenario seeds the projection tables directly, the same way
/// `RomMSyncTests.fs`'s `seedGame` does.

open System
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

let private seedGame (conn: SqliteConnection) (slug: string) (name: string) (year: int) =
    let data: Games.GameAddedData =
        { Name = name; Year = year; Genres = []; Description = ""; ShortDescription = ""
          WebsiteUrl = None; CoverRef = None; BackdropRef = None; RawgId = None; RawgRating = None }
    let streamId = Games.streamId slug
    EventStore.appendToStream conn streamId -1L [ Games.Serialization.toEventData (Games.Game_added_to_library data) ] |> ignore
    Projection.runProjection conn GameProjection.handler

/// Mirrors `RomMSync.resolveSlug`'s own `Set_romm_rom_id` command, but
/// applied directly to the event stream -- these tests are about
/// `getBySlug`'s read side, not the sync's linking flow (already covered
/// by `RomMSyncTests.fs`).
let private linkRommRomId (conn: SqliteConnection) (slug: string) (romId: int) =
    let streamId = Games.streamId slug
    let pos = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId pos [ Games.Serialization.toEventData (Games.Game_romm_rom_id_set romId) ] |> ignore
    Projection.runProjection conn GameProjection.handler

[<Tests>]
let rommPlayButtonTests =
    testList "GameProjection.getBySlug -- RommPlayUrl (integration-q748k, ADR-0088)" [

        testCase "Some \"{base}/rom/{id}/ejs\" for a RomM-linked game on an EmulatorJS platform" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "chrono-trigger-1995" "Chrono Trigger" 1995
            linkRommRomId db.Connection "chrono-trigger-1995" 42
            GameProjection.upsertRommRomPlatform db.Connection 42 "snes"
            SettingsStore.setSetting db.Connection "romm_base_url" "https://romm.example.com"
            match GameProjection.getBySlug db.Connection "chrono-trigger-1995" with
            | Some game -> Expect.equal game.RommPlayUrl (Some "https://romm.example.com/rom/42/ejs") "Play URL built from base url + rom id + player route"
            | None -> failtest "Expected the game to be found"

        testCase "None when the game has no RomM rom id" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "no-romm-game" "No RomM Game" 2020
            SettingsStore.setSetting db.Connection "romm_base_url" "https://romm.example.com"
            match GameProjection.getBySlug db.Connection "no-romm-game" with
            | Some game -> Expect.isNone game.RommPlayUrl "No romm_rom_id linked -- no Play button"
            | None -> failtest "Expected the game to be found"

        testCase "None when romm_base_url is not configured" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "unconfigured-base-url" "Unconfigured Base URL" 2020
            linkRommRomId db.Connection "unconfigured-base-url" 43
            GameProjection.upsertRommRomPlatform db.Connection 43 "snes"
            match GameProjection.getBySlug db.Connection "unconfigured-base-url" with
            | Some game -> Expect.isNone game.RommPlayUrl "No romm_base_url setting saved -- no Play button"
            | None -> failtest "Expected the game to be found"

        testCase "None when the linked rom's platform has no in-browser player (e.g. Switch)" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "switch-game" "Switch Game" 2021
            linkRommRomId db.Connection "switch-game" 44
            GameProjection.upsertRommRomPlatform db.Connection 44 "switch"
            SettingsStore.setSetting db.Connection "romm_base_url" "https://romm.example.com"
            match GameProjection.getBySlug db.Connection "switch-game" with
            | Some game -> Expect.isNone game.RommPlayUrl "Switch has no in-browser player -- no Play button"
            | None -> failtest "Expected the game to be found"

        testCase "None when no platform row exists yet for the linked rom" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "no-platform-row" "No Platform Row" 2022
            linkRommRomId db.Connection "no-platform-row" 45
            // Deliberately no `upsertRommRomPlatform` call -- e.g. a rom
            // linked before this concept existed, not yet backfilled by a
            // sync run.
            SettingsStore.setSetting db.Connection "romm_base_url" "https://romm.example.com"
            match GameProjection.getBySlug db.Connection "no-platform-row" with
            | Some game -> Expect.isNone game.RommPlayUrl "No romm_rom_platform row -- no Play button"
            | None -> failtest "Expected the game to be found"

        testCase "A trailing slash on romm_base_url does not produce a double slash" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "trailing-slash-game" "Trailing Slash Game" 2023
            linkRommRomId db.Connection "trailing-slash-game" 46
            GameProjection.upsertRommRomPlatform db.Connection 46 "nes"
            SettingsStore.setSetting db.Connection "romm_base_url" "https://romm.example.com/"
            match GameProjection.getBySlug db.Connection "trailing-slash-game" with
            | Some game -> Expect.equal game.RommPlayUrl (Some "https://romm.example.com/rom/46/ejs") "Trailing slash trimmed -- no double slash before /rom/"
            | None -> failtest "Expected the game to be found"

        testCase "The Play URL never contains the RomM API token" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            seedGame db.Connection "token-safety-game" "Token Safety Game" 2024
            linkRommRomId db.Connection "token-safety-game" 47
            GameProjection.upsertRommRomPlatform db.Connection 47 "gba"
            SettingsStore.setSetting db.Connection "romm_base_url" "https://romm.example.com"
            SettingsStore.setSetting db.Connection "romm_api_token" "rmm_supersecrettoken0000000000000000000000000000000000"
            match GameProjection.getBySlug db.Connection "token-safety-game" with
            | Some game ->
                match game.RommPlayUrl with
                | Some url -> Expect.isFalse (url.Contains("rmm_supersecrettoken")) "The API token never appears in the Play URL"
                | None -> failtest "Expected a Play URL to be built"
            | None -> failtest "Expected the game to be found"
    ]
