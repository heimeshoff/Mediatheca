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
    PlaytimeTracker.initialize conn
    // GameProjection.getBySlug joins with content_blocks, so initialize that table too
    ContentBlockProjection.handler.Init conn
    GameProjection.handler.Init conn
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

/// Append the Game_added_to_library event and run the projection so the slug exists in game_detail.
let private seedGame (conn: SqliteConnection) =
    let event = Games.Game_added_to_library sampleGameData
    let eventData = Games.Serialization.toEventData event
    let streamId = Games.streamId gameSlug
    EventStore.appendToStream conn streamId -1L [ eventData ] |> ignore
    Projection.runProjection conn GameProjection.handler

/// Helper: produces a runCmd callback bound to the connection and projection handlers.
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
                Ok ()

let private getTotalFromProjection (conn: SqliteConnection) (slug: string) : int =
    match GameProjection.getBySlug conn slug with
    | Some g -> g.TotalPlayTimeMinutes
    | None -> -1

let private countSessions (conn: SqliteConnection) (slug: string) : int =
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
let playtimeTrackerTests =
    testList "PlaytimeTracker manual sessions" [

        testCase "Adding a manual session for a fresh date creates a new row with steam_app_id = 0" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn

            let result =
                PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn)

            match result with
            | Ok dto ->
                Expect.equal dto.Date "2024-06-01" "Date should be the supplied date"
                Expect.equal dto.MinutesPlayed 60 "Minutes should be 60"
                Expect.equal dto.Source Manual "Source should be Manual"
                Expect.equal (countSessions conn gameSlug) 1 "Should have one session"
                Expect.equal (getTotalFromProjection conn gameSlug) 60 "Projection total should equal SUM"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Adding a manual session for an existing date merges minutes" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            // First insert via Steam-style record (steam_app_id = 123)
            PlaytimeTracker.recordPlaySession conn gameSlug 123 "2024-06-02" 30
            PlaytimeTracker.recomputeAndPublishTotal conn gameSlug (runCmd conn)

            let result =
                PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-02" 45 (runCmd conn)

            match result with
            | Ok dto ->
                Expect.equal dto.MinutesPlayed 75 "Minutes should be merged (30 + 45)"
                Expect.equal (countSessions conn gameSlug) 1 "Should still be one row"
                Expect.equal (getTotalFromProjection conn gameSlug) 75 "Projection total reflects merge"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Editing a session with a colliding date merges into the other row" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            // Create two sessions on different dates
            let added1 = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-03" 60 (runCmd conn)
            let added2 = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-04" 90 (runCmd conn)

            match added1, added2 with
            | Ok dto1, Ok dto2 ->
                // Edit dto2 to land on dto1's date — should merge into dto1
                let edit =
                    PlaytimeTracker.updatePlaySessionApi conn dto2.Id "2024-06-03" 90 (runCmd conn)

                match edit with
                | Ok merged ->
                    Expect.equal merged.Id dto1.Id "Merged session should have dto1's id"
                    Expect.equal merged.MinutesPlayed 150 "Minutes should be 60 + 90"
                    Expect.equal (countSessions conn gameSlug) 1 "Only one row should remain"
                    Expect.equal (getTotalFromProjection conn gameSlug) 150 "Projection reflects merged total"
                | Error e -> failtest $"Expected merge to succeed, got: {e}"
            | _ -> failtest "Setup failed"

        testCase "Editing a session changes date and minutes without collision" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let added = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-05" 30 (runCmd conn)
            match added with
            | Ok dto ->
                let edited =
                    PlaytimeTracker.updatePlaySessionApi conn dto.Id "2024-06-06" 50 (runCmd conn)
                match edited with
                | Ok updated ->
                    Expect.equal updated.Id dto.Id "Same id"
                    Expect.equal updated.Date "2024-06-06" "Date updated"
                    Expect.equal updated.MinutesPlayed 50 "Minutes updated"
                    Expect.equal (getTotalFromProjection conn gameSlug) 50 "Total reflects update"
                | Error e -> failtest $"Expected Ok, got: {e}"
            | Error e -> failtest $"Setup failed: {e}"

        testCase "Deleting a session removes it and updates total" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let added = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-07" 100 (runCmd conn)
            match added with
            | Ok dto ->
                Expect.equal (getTotalFromProjection conn gameSlug) 100 "Total before delete"
                let delResult = PlaytimeTracker.deletePlaySessionApi conn dto.Id (runCmd conn)
                match delResult with
                | Ok () ->
                    Expect.equal (countSessions conn gameSlug) 0 "Row should be gone"
                    Expect.equal (getTotalFromProjection conn gameSlug) 0 "Total should be 0"
                | Error e -> failtest $"Expected Ok, got: {e}"
            | Error e -> failtest $"Setup failed: {e}"

        testCase "Deleting a non-existent id is a no-op (Ok)" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let result = PlaytimeTracker.deletePlaySessionApi conn 99999L (runCmd conn)
            match result with
            | Ok () -> ()
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Total playtime equals SUM(minutes_played) after multiple ops" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn

            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-10" 30 (runCmd conn) |> ignore
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-11" 45 (runCmd conn) |> ignore
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-12" 25 (runCmd conn) |> ignore

            // 30 + 45 + 25 = 100
            Expect.equal (getTotalFromProjection conn gameSlug) 100 "Total after three adds"

            let sessions = PlaytimeTracker.getPlaySessionsForGame conn gameSlug
            let firstId = (sessions |> List.find (fun s -> s.Date = "2024-06-10")).Id
            PlaytimeTracker.deletePlaySessionApi conn firstId (runCmd conn) |> ignore

            // 45 + 25 = 70
            Expect.equal (getTotalFromProjection conn gameSlug) 70 "Total after delete"

        testCase "Validation: minutes <= 0 returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let result = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-15" 0 (runCmd conn)
            match result with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for 0 minutes"

        testCase "Validation: minutes > 1440 returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let result = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-15" 1441 (runCmd conn)
            match result with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for > 1440 minutes"

        testCase "Validation: malformed date returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let result = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "not-a-date" 30 (runCmd conn)
            match result with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for malformed date"

        testCase "Validation: future date returns Error" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            let future = DateTime.Now.AddDays(7.0).ToString("yyyy-MM-dd")
            let result = PlaytimeTracker.addManualPlaySessionApi conn gameSlug future 30 (runCmd conn)
            match result with
            | Error _ -> ()
            | Ok _ -> failtest "Expected error for future date"

        testCase "Manual sessions do not interfere with Steam delta tracking" <| fun _ ->
            // Steam delta is computed against steam_playtime_snapshot, not against game_play_session.
            // Adding manual sessions must not change the snapshot, so the next Steam delta is added
            // on top of manual sessions. Verify by simulating:
            //   1. Steam reports 100 min — first sync — baseline snapshot saved (no delta yet).
            //   2. User adds a manual 50 min session.
            //   3. Steam reports 130 min. Delta = 30. After this, total should be 100 (steam) + 50 (manual) + 30 (steam delta) = 180.
            let conn = createInMemoryConnection ()
            seedGame conn
            let steamAppId = 999

            // Simulate first sync: record initial session and snapshot
            PlaytimeTracker.recordPlaySession conn gameSlug steamAppId "2024-06-20" 100
            PlaytimeTracker.saveSnapshot conn steamAppId gameSlug 100
            PlaytimeTracker.recomputeAndPublishTotal conn gameSlug (runCmd conn)
            Expect.equal (getTotalFromProjection conn gameSlug) 100 "After first sync, total = 100"

            // User adds manual session
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-21" 50 (runCmd conn) |> ignore
            Expect.equal (getTotalFromProjection conn gameSlug) 150 "After manual, total = 150"

            // Snapshot should still be 100 (manual sessions don't touch the snapshot)
            match PlaytimeTracker.getLastSnapshot conn steamAppId with
            | Some (lastTotal, _) -> Expect.equal lastTotal 100 "Snapshot unaffected by manual session"
            | None -> failtest "Snapshot should still exist"

            // Simulate next sync: Steam reports 130 (delta = 30)
            PlaytimeTracker.recordPlaySession conn gameSlug steamAppId "2024-06-22" 30
            PlaytimeTracker.saveSnapshot conn steamAppId gameSlug 130
            PlaytimeTracker.recomputeAndPublishTotal conn gameSlug (runCmd conn)

            // Total = 100 (first steam) + 50 (manual) + 30 (delta) = 180
            Expect.equal (getTotalFromProjection conn gameSlug) 180 "Manual + Steam delta combined correctly"
    ]

[<Tests>]
let promoteToInFocusTests =
    // Task 048: any newly recorded play session promotes the game to InFocus,
    // regardless of prior status (Backlog, OnHold, Completed, Abandoned, Dismissed all qualify).
    // Already-InFocus games are skipped to avoid emitting redundant events.
    testList "PlaytimeTracker auto-promote to InFocus" [

        testCase "Backlog game with new playtime is promoted to InFocus" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            // Default status is Backlog
            Expect.equal (getStatus conn gameSlug) (Some Backlog) "Starts in Backlog"

            let promoted = PlaytimeTracker.promoteToInFocusIfNeeded conn gameSlug (runCmd conn)

            Expect.isTrue promoted "Should report promotion"
            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Status now InFocus"

        testCase "Completed game with new playtime is promoted to InFocus" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug Completed
            Expect.equal (getStatus conn gameSlug) (Some Completed) "Starts in Completed"

            let promoted = PlaytimeTracker.promoteToInFocusIfNeeded conn gameSlug (runCmd conn)

            Expect.isTrue promoted "Should report promotion (replaying a finished game)"
            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Status now InFocus"

        testCase "Abandoned game is promoted to InFocus on new play" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug Abandoned
            let promoted = PlaytimeTracker.promoteToInFocusIfNeeded conn gameSlug (runCmd conn)
            Expect.isTrue promoted "Should report promotion"
            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Status now InFocus"

        testCase "OnHold game is promoted to InFocus on new play" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug OnHold
            let promoted = PlaytimeTracker.promoteToInFocusIfNeeded conn gameSlug (runCmd conn)
            Expect.isTrue promoted "Should report promotion"
            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Status now InFocus"

        testCase "Dismissed game is promoted to InFocus on new play" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug Dismissed
            let promoted = PlaytimeTracker.promoteToInFocusIfNeeded conn gameSlug (runCmd conn)
            Expect.isTrue promoted "Should report promotion"
            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Status now InFocus"

        testCase "Already-InFocus game emits no Game_status_changed event and reports no promotion" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug InFocus
            let beforeCount = countStatusChangeEvents conn gameSlug
            let promoted = PlaytimeTracker.promoteToInFocusIfNeeded conn gameSlug (runCmd conn)
            let afterCount = countStatusChangeEvents conn gameSlug
            Expect.isFalse promoted "Should NOT report promotion"
            Expect.equal afterCount beforeCount "No additional Game_status_changed events"
            Expect.equal (getStatus conn gameSlug) (Some InFocus) "Still InFocus"

        testCase "Manual session for a Backlog game promotes to InFocus" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            // Default Backlog
            let result =
                PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn)
            match result with
            | Ok _ -> Expect.equal (getStatus conn gameSlug) (Some InFocus) "Manual session promotes to InFocus"
            | Error e -> failtest $"Expected Ok, got: {e}"

        testCase "Manual session for an InFocus game does not emit a redundant status event" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            setStatus conn gameSlug InFocus
            let before = countStatusChangeEvents conn gameSlug
            PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn) |> ignore
            let after = countStatusChangeEvents conn gameSlug
            Expect.equal after before "No new Game_status_changed event emitted"

        testCase "Manual session edit on a Completed game promotes to InFocus" <| fun _ ->
            let conn = createInMemoryConnection ()
            seedGame conn
            // Add a session first while still in Backlog (which itself promotes to InFocus),
            // then move to Completed, then edit the session — should re-promote.
            let added = PlaytimeTracker.addManualPlaySessionApi conn gameSlug "2024-06-01" 60 (runCmd conn)
            setStatus conn gameSlug Completed
            Expect.equal (getStatus conn gameSlug) (Some Completed) "Now Completed"
            match added with
            | Ok dto ->
                PlaytimeTracker.updatePlaySessionApi conn dto.Id "2024-06-02" 90 (runCmd conn) |> ignore
                Expect.equal (getStatus conn gameSlug) (Some InFocus) "Edit promotes back to InFocus"
            | Error e -> failtest $"Setup failed: {e}"
    ]
