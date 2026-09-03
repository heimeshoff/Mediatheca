module Mediatheca.Tests.PlaytimeTrackerTests

open System
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

let private createInMemoryConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    // GameProjection.getBySlug joins with content_blocks, so initialize that table too
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
    GameJournal.initialize conn
    PlaySessionProjection.handler.Init conn
    // games-a7dqx: GameProjection.getBySlug/getAll etc. now LEFT JOIN
    // game_metadata_cache — it must exist even though these tests don't
    // exercise its contents, mirroring Composition.buildApp's real startup
    // order (MetadataCache.initialize before any request is served).
    MetadataCache.initialize conn
    conn

let private sampleGameData: Games.GameAddedData = {
    Name = "Test Game"
    Year = 2024
    Genres = [ "Action" ]
    Description = "A test game"
    ShortDescription = "Test"
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private gameSlug = "test-game-2024"

/// Append the Game_added_to_library event and run the projections so the slug exists in game_detail.
let private seedGame (conn: SqliteConnection) =
    let event = Games.Game_added_to_library sampleGameData
    let eventData = Games.Serialization.toEventData event
    let streamId = Games.streamId gameSlug
    EventStore.appendToStream conn streamId -1L [ eventData ] |> ignore
    Projection.runProjection conn GameProjection.handler
    Projection.runProjection conn PlaySessionProjection.handler

/// Helper: produces a runCmd callback bound to the connection and both projections.
let private runCmd (conn: SqliteConnection) (slug: string) (cmd: Games.GameCommand) : Result<unit, string> =
    let streamId = Games.streamId slug
    let storedEvents = EventStore.readStream conn streamId
    let events = storedEvents |> List.choose Games.Serialization.fromStoredEvent
    let state = Games.reconstitute events
    let position = EventStore.getStreamPosition conn streamId
    match Games.decide state cmd with
    | Error e -> Error e
    | Ok newEvents ->
        if List.isEmpty newEvents then Ok ()
        else
            let eventDataList = newEvents |> List.map Games.Serialization.toEventData
            match EventStore.appendToStream conn streamId position eventDataList with
            | EventStore.ConcurrencyConflict _ -> Error "Concurrency conflict"
            | EventStore.Success _ ->
                Projection.runProjection conn GameProjection.handler
                Projection.runProjection conn PlaySessionProjection.handler
                Ok ()

let private getTotalFromProjection (conn: SqliteConnection) (slug: string) : int =
    match GameProjection.getBySlug conn slug with
    | Some g -> g.TotalPlayTimeMinutes
    | None -> -1

let private countSessionRows (conn: SqliteConnection) (slug: string) : int =
    conn
    |> Db.newCommand "SELECT COUNT(*) as cnt FROM game_play_session WHERE game_slug = @slug"
    |> Db.setParams [ "slug", SqlType.String slug ]
    |> Db.querySingle (fun rd -> rd.ReadInt32 "cnt")
    |> Option.defaultValue 0

let private setStatus (conn: SqliteConnection) (slug: string) (status: GameStatus) =
    runCmd conn slug (Games.Change_status status) |> ignore

let private getStatus (conn: SqliteConnection) (slug: string) : GameStatus option =
    GameProjection.getGameStatus conn slug

/// Count Game_status_changed events recorded for a slug.
let private countStatusChangeEvents (conn: SqliteConnection) (slug: string) : int =
    let streamId = Games.streamId slug
    EventStore.readStream conn streamId
    |> List.filter (fun e -> e.EventType = "Game_status_changed")
    |> List.length

[<Tests>]
let manualSessionApiTests =
    testList "PlaytimeTracker manual sessions (natural key)" [

        testCase "Adding a manual session for a fresh date creates a new row" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn

            let result =
                PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn)

            match result with
            | Ok dto ->
                Expect.equal dto.Date "2024-06-01" "Date should be the supplied date"
                Expect.equal dto.MinutesPlayed 60 "Minutes should be 60"
                Expect.equal dto.Source Manual "Source should be Manual"
                Expect.equal (countSessionRows conn gameSlug) 1 "Should have one session row"
                Expect.equal (getTotalFromProjection conn gameSlug) 60 "Projection total should equal the session"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Adding a manual session for an existing date merges minutes (integration-004 shape, at the aggregate level)" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-02" 30 (runCmd conn) |> ignore

            let result =
                PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-02" 45 (runCmd conn)

            match result with
            | Ok dto ->
                Expect.equal dto.MinutesPlayed 75 "Minutes should be merged (30 + 45)"
                Expect.equal (countSessionRows conn gameSlug) 1 "Should still be one row"
                Expect.equal (getTotalFromProjection conn gameSlug) 75 "Projection total reflects merge"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Editing a session with a colliding new date merges into the other day" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-03" 60 (runCmd conn) |> ignore
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-04" 90 (runCmd conn) |> ignore

            let edit: PlaySessionEdit = { GameSlug = gameSlug; Date = "2024-06-04"; NewDate = "2024-06-03"; NewMinutes = 90 }
            match PlaytimeTracker.updatePlaySessionApi conn edit (runCmd conn) with
            | Ok merged ->
                Expect.equal merged.Date "2024-06-03" "Merged session should land on the collision day"
                Expect.equal merged.MinutesPlayed 150 "Minutes should be 60 + 90"
                Expect.equal (countSessionRows conn gameSlug) 1 "Only one row should remain"
                Expect.equal (getTotalFromProjection conn gameSlug) 150 "Projection reflects merged total"
            | Error e -> failtest $"Expected merge to succeed, got: {e}"

        testCase "Editing a session changes date and minutes without collision" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-05" 30 (runCmd conn) |> ignore

            let edit: PlaySessionEdit = { GameSlug = gameSlug; Date = "2024-06-05"; NewDate = "2024-06-06"; NewMinutes = 50 }
            match PlaytimeTracker.updatePlaySessionApi conn edit (runCmd conn) with
            | Ok updated ->
                Expect.equal updated.Date "2024-06-06" "Date updated"
                Expect.equal updated.MinutesPlayed 50 "Minutes updated"
                Expect.equal (getTotalFromProjection conn gameSlug) 50 "Total reflects update"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Deleting a session removes it and updates total" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-07" 100 (runCmd conn) |> ignore
            Expect.equal (getTotalFromProjection conn gameSlug) 100 "Total before delete"

            match PlaytimeTracker.deletePlaySessionApi conn gameSlug "2024-06-07" (runCmd conn) with
            | Ok () ->
                Expect.equal (countSessionRows conn gameSlug) 0 "Row should be gone"
                Expect.equal (getTotalFromProjection conn gameSlug) 0 "Total should be 0"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Deleting a nonexistent (slug, day) is a no-op (Ok)" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            match PlaytimeTracker.deletePlaySessionApi conn gameSlug "1999-01-01" (runCmd conn) with
            | Ok () -> ()
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Validation: minutes <= 0 returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            match PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-15" 0 (runCmd conn) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for 0 minutes"

        testCase "Validation: minutes > 1440 returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            match PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-15" 1441 (runCmd conn) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for > 1440 minutes"

        testCase "Validation: malformed date returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            match PlaytimeTracker.addManualPlaySessionApi conn gameSlug "not-a-date" 30 (runCmd conn) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for malformed date"

        testCase "Validation: future date returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let future = DateTime.Now.AddDays(7.0).ToString("yyyy-MM-dd")
            match PlaytimeTracker.addManualPlaySessionApi conn gameSlug future 30 (runCmd conn) with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for future date"

        testCase "Same-day Steam delta merges into the existing session row instead of being dropped (integration-004 regression, at the projection level)" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let day = "2024-06-20"

            runCmd conn gameSlug (Games.Record_steam_observed_total (100, day)) |> ignore
            Expect.equal (getTotalFromProjection conn gameSlug) 100 "Sanity: after first sync, total = 100"

            // Second Steam sync attributes a further +60 delta to the same gaming day.
            runCmd conn gameSlug (Games.Record_steam_observed_total (160, day)) |> ignore

            let sessions = PlaytimeTracker.getPlaySessionsForGame conn gameSlug
            let dayRows = sessions |> List.filter (fun s -> s.Date = day)
            Expect.equal (List.length dayRows) 1 "Still exactly one row for the gaming day"
            Expect.equal (List.head dayRows).MinutesPlayed 160 "Same-day delta is summed, not dropped"
            Expect.equal (getTotalFromProjection conn gameSlug) 160 "Projection total reflects merged minutes"

        testCase "A game with only prior playtime produces zero session rows, and dashboard/summary queries return nothing for it" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            runCmd conn gameSlug (Games.Record_prior_play_time 30000) |> ignore

            Expect.equal (countSessionRows conn gameSlug) 0 "Prior playtime writes no session row"
            Expect.equal (getTotalFromProjection conn gameSlug) 30000 "Total reflects prior playtime"
            match GameProjection.getBySlug conn gameSlug with
            | Some g -> Expect.equal g.PriorPlayTimeMinutes 30000 "game_detail.prior_play_time reflects the recorded amount"
            | None -> failtest "Expected game to exist"

            let dashboard = PlaytimeTracker.getDashboardPlaySessions conn 3650
            Expect.isEmpty (dashboard |> List.filter (fun s -> s.GameSlug = gameSlug)) "Dashboard should show no sessions for this game"
            let summary = PlaytimeTracker.getPlaytimeSummary conn "2000-01-01" "2100-01-01"
            Expect.isEmpty (summary |> List.filter (fun s -> s.GameSlug = gameSlug)) "Playtime summary should show no sessions for this game"

        testCase "TotalPlayTimeMinutes stays in lock-step across the aggregate and both projection tables" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            runCmd conn gameSlug (Games.Record_prior_play_time 500) |> ignore
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-10" 30 (runCmd conn) |> ignore
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-11" 45 (runCmd conn) |> ignore
            runCmd conn gameSlug (Games.Correct_play_session_minutes ("2024-06-11", 60)) |> ignore

            let streamId = Games.streamId gameSlug
            let events = EventStore.readStream conn streamId |> List.choose Games.Serialization.fromStoredEvent
            let state = Games.reconstitute events
            let aggregateTotal =
                match state with
                | Games.Active g -> g.TotalPlayTimeMinutes
                | _ -> failwith "Expected Active state"

            let sessionSum =
                conn
                |> Db.newCommand "SELECT COALESCE(SUM(minutes_played), 0) as total FROM game_play_session WHERE game_slug = @slug"
                |> Db.setParams [ "slug", SqlType.String gameSlug ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "total")
                |> Option.defaultValue 0
            let priorPlayTime =
                conn
                |> Db.newCommand "SELECT prior_play_time FROM game_detail WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String gameSlug ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "prior_play_time")
                |> Option.defaultValue 0
            let gameListTotal =
                conn
                |> Db.newCommand "SELECT total_play_time FROM game_list WHERE slug = @slug"
                |> Db.setParams [ "slug", SqlType.String gameSlug ]
                |> Db.querySingle (fun rd -> rd.ReadInt32 "total_play_time")
                |> Option.defaultValue -1
            let gameDetailTotal = getTotalFromProjection conn gameSlug

            Expect.equal aggregateTotal (priorPlayTime + sessionSum) "Aggregate total should equal prior_play_time + SUM(minutes_played)"
            Expect.equal gameListTotal aggregateTotal "game_list.total_play_time should match the aggregate"
            Expect.equal gameDetailTotal aggregateTotal "game_detail.total_play_time should match the aggregate"
    ]

[<Tests>]
let promoteToInFocusTests =
    // games-p6vkz: only recording a NEW session promotes, regardless of prior
    // status (Backlog, Retired, Abandoned, Dismissed all qualify). Correcting,
    // moving, removing, or recording prior playtime must never promote —
    // ADR-0042's any-status rule moved into Games.decide.
    testList "PlaytimeTracker auto-promote to InFocus (via the manual session API)" [

        testCase "Backlog game with a new manual session is promoted to InFocus" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            Expect.equal (getStatus conn gameSlug) (Some Backlog) "Starts in Backlog"

            match PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn) with
            | Ok _ -> Expect.equal (getStatus conn gameSlug) (Some InFocus) "Manual session promotes to InFocus"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Retired game with a new manual session is promoted to InFocus" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug Retired

            match PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn) with
            | Ok _ -> Expect.equal (getStatus conn gameSlug) (Some InFocus) "Promotes from Retired"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Manual session for an InFocus game does not emit a redundant status event" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug InFocus
            let before = countStatusChangeEvents conn gameSlug
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn) |> ignore
            let after = countStatusChangeEvents conn gameSlug
            Expect.equal after before "No new Game_status_changed event emitted"

        testCase "Editing an existing session does NOT re-promote a Retired game (games-p6vkz narrowing)" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn) |> ignore
            // Promoted to InFocus by the add above; move it back to Retired.
            setStatus conn gameSlug Retired
            let before = countStatusChangeEvents conn gameSlug

            let edit: PlaySessionEdit = { GameSlug = gameSlug; Date = "2024-06-01"; NewDate = "2024-06-02"; NewMinutes = 90 }
            PlaytimeTracker.updatePlaySessionApi conn edit (runCmd conn) |> ignore

            Expect.equal (getStatus conn gameSlug) (Some Retired) "Editing a session must not yank a Retired game back into focus"
            Expect.equal (countStatusChangeEvents conn gameSlug) before "No additional Game_status_changed event from the edit"

        testCase "Deleting a session does NOT promote" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn) |> ignore
            setStatus conn gameSlug Abandoned
            let before = countStatusChangeEvents conn gameSlug

            PlaytimeTracker.deletePlaySessionApi conn gameSlug "2024-06-01" (runCmd conn) |> ignore

            Expect.equal (getStatus conn gameSlug) (Some Abandoned) "Deleting a session must not promote"
            Expect.equal (countStatusChangeEvents conn gameSlug) before "No additional Game_status_changed event from the delete"
    ]

/// Appends a raw Game_status_changed event carrying a literal legacy status string,
/// bypassing Games.Serialization.toEventData's encoder (which would only ever emit
/// current-vocabulary strings). Simulates an event actually written before
/// games-status-vocabulary-reconcile, since the migration is upcast-only — no event
/// rewriting.
let private appendLegacyStatusChangedEvent (conn: SqliteConnection) (slug: string) (legacyStatus: string) =
    let eventData: EventStore.EventData =
        { EventType = "Game_status_changed"
          Data = sprintf """{"status":"%s"}""" legacyStatus
          Metadata = "{}" }
    let streamId = Games.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ eventData ] |> ignore

[<Tests>]
let legacyStatusUpcastTests =
    testList "GameProjection legacy status upcast (games-status-vocabulary-reconcile)" [

        testCase "Replaying a store with legacy 'OnHold' status events lands the game in InFocus" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            appendLegacyStatusChangedEvent conn gameSlug "OnHold"
            Projection.runProjection conn GameProjection.handler

            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Legacy OnHold upcasts to InFocus on replay"

        testCase "Replaying a store with legacy 'Completed' status events lands the game in Retired" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            appendLegacyStatusChangedEvent conn gameSlug "Completed"
            Projection.runProjection conn GameProjection.handler

            Expect.equal (getStatus conn gameSlug) (Some Retired) "Legacy Completed upcasts to Retired on replay"

        testCase "Rebuilding a projection over a store containing legacy strings leaves no legacy status strings in game_list" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            appendLegacyStatusChangedEvent conn gameSlug "OnHold"

            let onHoldSlug = "on-hold-game"
            let completedSlug = "completed-game"
            let addGame (slug: string) (name: string) =
                let event = Games.Game_added_to_library { sampleGameData with Name = name }
                let eventData = Games.Serialization.toEventData event
                EventStore.appendToStream conn (Games.streamId slug) -1L [ eventData ] |> ignore
            addGame onHoldSlug "On Hold Game"
            appendLegacyStatusChangedEvent conn onHoldSlug "OnHold"
            addGame completedSlug "Completed Game"
            appendLegacyStatusChangedEvent conn completedSlug "Completed"

            let progressSnapshots = System.Collections.Generic.List<Projection.RebuildProgress>()
            Projection.rebuildProjectionWithProgress conn GameProjection.handler (fun p -> progressSnapshots.Add p)

            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Legacy OnHold upcasts to InFocus after rebuild"
            Expect.equal (getStatus conn onHoldSlug) (Some InFocus) "Legacy OnHold upcasts to InFocus after rebuild"
            Expect.equal (getStatus conn completedSlug) (Some Retired) "Legacy Completed upcasts to Retired after rebuild"

            let rawStatuses =
                conn
                |> Db.newCommand "SELECT status FROM game_list"
                |> Db.query (fun rd -> rd.ReadString "status")
            Expect.isFalse (rawStatuses |> List.contains "OnHold") "No legacy 'OnHold' strings remain in game_list"
            Expect.isFalse (rawStatuses |> List.contains "Completed") "No legacy 'Completed' strings remain in game_list"
    ]
