module Mediatheca.Tests.EventLogFilterTests

// administration-z6ymt: the offline NDJSON filter that drops the eleven
// demoted Game metadata event types ahead of the ADR-0038 wipe-first purge.
// Exercises `EventLogFilter.filterNdjson` directly — plain Expecto,
// StringReader/StringWriter, no HTTP — the same shape
// `EventStoreNdjsonTests.fs` (ADR-0029) established.

open System
open System.IO
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

// ── Pure, line-level fixtures (no EventStore involved) ──

let private makeLine (globalPosition: int64) (eventType: string) : string =
    sprintf "{\"globalPosition\":%d,\"streamId\":\"Game-x\",\"streamPosition\":0,\"eventType\":\"%s\",\"data\":\"{}\",\"metadata\":\"{}\",\"timestamp\":\"2026-01-01T00:00:00.0000000+00:00\"}"
        globalPosition eventType

/// Excluded from the purge set (ADR-0043's identity-card clause) — never
/// dropped, regardless of duplication.
let private identityCardTypes = [
    "Game_rawg_id_set"
    "Game_steam_app_id_set"
    "Game_family_owner_added"
    "Game_family_owner_removed"
    "Game_steam_library_date_set"
]

/// h4mrd's reconstructed play-session history (ADR-0050) — load-bearing,
/// never dropped.
let private h4mrdTypes = [
    "Play_session_recorded"
    "Prior_play_time_recorded"
    "Steam_observed_total_reconciled"
]

let private purgeEligible = EventLogFilter.purgeEligibleEventTypes |> Set.toList

let private runFilter (lines: string list) : string list * EventLogFilter.FilterSummary =
    let input = String.Join("\n", lines)
    use reader = new StringReader(input)
    use writer = new StringWriter()
    let summary = EventLogFilter.filterNdjson EventLogFilter.purgeEligibleEventTypes reader writer
    // `writer.WriteLine` uses the platform's `Environment.NewLine` ("\r\n" on
    // Windows); splitting on '\n' alone leaves a trailing '\r' on every line
    // but the last, so it's trimmed here — a test-side normalization only,
    // not a claim that `filterNdjson` itself alters line content.
    let outputLines =
        writer.ToString().Split('\n')
        |> Array.map (fun l -> l.TrimEnd('\r'))
        |> Array.filter (fun l -> l.Trim() <> "")
        |> Array.toList
    outputLines, summary

[<Tests>]
let eventLogFilterTests =
    testList "EventLogFilter (pure, line-level)" [

        testCase "drops 100% of purge-eligible lines, retains every other line byte-identical, kept + dropped = input" <| fun _ ->
            // One line per purge-eligible type, duplicated identity-card
            // lines, Series_refreshed, and the three h4mrd types — the exact
            // mixed fixture the task's first acceptance criterion names.
            let purgeLines =
                purgeEligible |> List.mapi (fun i t -> makeLine (int64 (i + 1)) t)
            let identityLines =
                identityCardTypes @ identityCardTypes // deliberately duplicated
                |> List.mapi (fun i t -> makeLine (int64 (i + 1 + 1000)) t)
            let seriesLine = makeLine 2000L "Series_refreshed"
            let h4mrdLines =
                h4mrdTypes |> List.mapi (fun i t -> makeLine (int64 (i + 1 + 3000)) t)

            let allLines = purgeLines @ identityLines @ [ seriesLine ] @ h4mrdLines
            let outputLines, summary = runFilter allLines

            Expect.equal summary.InputLines (List.length allLines) "every line was read"
            Expect.equal summary.DroppedLines (List.length purgeEligible) "exactly the purge-eligible lines were dropped"
            Expect.equal summary.KeptLines (summary.InputLines - summary.DroppedLines) "kept + dropped = input"
            Expect.equal (summary.KeptLines + summary.DroppedLines) summary.InputLines "kept + dropped = input (restated as the executable post-condition)"
            Expect.equal summary.UnparseableLines 0 "every fixture line is valid JSON with a string eventType"

            // Every dropped type is a member of the deny list.
            for eventType in summary.DroppedByType |> Map.toList |> List.map fst do
                Expect.contains purgeEligible eventType "every dropped line's type must be in the 11-type purge set"

            let expectedKeptLines =
                allLines |> List.filter (fun l -> purgeEligible |> List.forall (fun t -> not (l.Contains(sprintf "\"eventType\":\"%s\"" t))))
            Expect.equal outputLines expectedKeptLines "kept lines pass through byte-identical, in original order"

        testCase "none of the five identity-card types is ever dropped, even duplicated" <| fun _ ->
            let lines =
                (identityCardTypes @ identityCardTypes @ identityCardTypes)
                |> List.mapi (fun i t -> makeLine (int64 (i + 1)) t)
            let outputLines, summary = runFilter lines

            Expect.equal summary.DroppedLines 0 "no identity-card line is ever dropped"
            Expect.equal outputLines lines "every identity-card line (including duplicates) passes through byte-identical"

        testCase "Series_refreshed lines are never dropped" <| fun _ ->
            let lines = [ 1..5 ] |> List.map (fun i -> makeLine (int64 i) "Series_refreshed")
            let outputLines, summary = runFilter lines

            Expect.equal summary.DroppedLines 0 "Series_refreshed is a live event type — the no-change-row filter is deferred, not partially shipped"
            Expect.equal outputLines lines "every Series_refreshed line passes through byte-identical"

        testCase "Play_session_recorded / Prior_play_time_recorded / Steam_observed_total_reconciled are never dropped" <| fun _ ->
            let lines = h4mrdTypes |> List.mapi (fun i t -> makeLine (int64 (i + 1)) t)
            let outputLines, summary = runFilter lines

            Expect.equal summary.DroppedLines 0 "h4mrd's reconstructed history is load-bearing (ADR-0050) — never dropped"
            Expect.equal outputLines lines "every h4mrd line passes through byte-identical"

        testCase "blank lines pass through and count toward kept, not dropped" <| fun _ ->
            let lines = [ makeLine 1L "Game_categorized"; ""; makeLine 2L "Game_rawg_id_set" ]
            let input = String.Join("\n", lines)
            use reader = new StringReader(input)
            use writer = new StringWriter()
            let summary = EventLogFilter.filterNdjson EventLogFilter.purgeEligibleEventTypes reader writer

            Expect.equal summary.InputLines 3 "three lines including the blank one"
            Expect.equal summary.DroppedLines 1 "only the Game_categorized line is dropped"
            Expect.equal summary.KeptLines 2 "the blank line and the identity line are both kept"

        testCase "an unparseable line is kept (fail-safe) and counted separately, never silently dropped" <| fun _ ->
            let lines = [ makeLine 1L "Game_categorized"; "this is not json"; makeLine 2L "Game_rawg_id_set" ]
            let outputLines, summary = runFilter lines

            Expect.equal summary.DroppedLines 1 "only the genuinely classifiable purge-eligible line is dropped"
            Expect.equal summary.UnparseableLines 1 "the malformed line is flagged, not silently classified"
            Expect.contains outputLines "this is not json" "the unparseable line is kept, fail-safe"
    ]

// ── Domain fixtures (real EventStore + GameProjection) ──

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    GameProjection.handler.Init conn
    // `GameProjection.getBySlug` joins/subqueries tables owned by other
    // projections it never writes to — all must exist even though these
    // fixtures never rebuild those other handlers (nothing here asserts on
    // their rows, only that GameProjection's own columns are row-identical
    // before/after the purge): `game_play_session` (PlaySessionProjection),
    // `content_blocks` (ContentBlockProjection), `friend_list`
    // (FriendProjection, for `resolveFriendRefs`).
    PlaySessionProjection.handler.Init conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MetadataCache.initialize conn

let private sampleGameData: Games.GameAddedData = {
    Name = "Grounded"
    Year = 2024
    Genres = [ "Survival"; "Co-op" ]
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private appendGameEvent (conn: SqliteConnection) (slug: string) (event: Games.GameEvent) =
    let streamId = Games.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    match EventStore.appendToStream conn streamId position [ Games.Serialization.toEventData event ] with
    | EventStore.Success _ -> ()
    | EventStore.ConcurrencyConflict _ -> failtest "unexpected concurrency conflict seeding a fixture"

let private exportToString (conn: SqliteConnection) : string =
    use writer = new StringWriter()
    EventStore.exportNdjson conn writer
    writer.ToString()

let private createInMemoryDb () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    bootstrap conn
    conn

[<Tests>]
let eventLogFilterDomainTests =
    testList "EventLogFilter (domain fixtures — real EventStore/GameProjection)" [

        testCase "Game_categorized rows are dropped AND a full projection replay before/after shows genres unchanged (ADR-0055 boundary)" <| fun _ ->
            use connA = createInMemoryDb ()
            appendGameEvent connA "grounded" (Games.Game_added_to_library sampleGameData)
            appendGameEvent connA "grounded" (Games.Game_categorized [ "Horror" ]) // attempts to change genres — already a no-op per ADR-0055

            Projection.rebuildProjection connA GameProjection.handler
            let genresBefore =
                match GameProjection.getBySlug connA "grounded" with
                | Some detail -> detail.Genres
                | None -> failtest "grounded should exist before the purge"
            Expect.equal genresBefore sampleGameData.Genres "genres come from Game_added_to_library's payload, unaffected by Game_categorized"

            let exportedA = exportToString connA
            use reader = new StringReader(exportedA)
            use writer = new StringWriter()
            let summary = EventLogFilter.filterNdjson EventLogFilter.purgeEligibleEventTypes reader writer
            Expect.equal summary.DroppedLines 1 "exactly the one Game_categorized line is dropped"

            use connB = createInMemoryDb ()
            use filteredReader = new StringReader(writer.ToString())
            match EventStore.importNdjson connB filteredReader with
            | Error e -> failtest (sprintf "import into the fresh, filtered store should succeed, got %A" e)
            | Ok _ -> ()
            Projection.rebuildProjection connB GameProjection.handler

            let genresAfter =
                match GameProjection.getBySlug connB "grounded" with
                | Some detail -> detail.Genres
                | None -> failtest "grounded should still exist after the purge"
            Expect.equal genresAfter genresBefore "genres are unchanged by purging Game_categorized — the ADR-0055 boundary holds"

        testCase "replay-determinism: purging via filter + wipe-first-style reimport yields row-identical GameProjection state (0 discrepancies)" <| fun _ ->
            use connA = createInMemoryDb ()

            // "grounded": every one of the 11 purge-eligible types, once
            // each, interleaved with every excluded (kept) type — the exact
            // shape the task's replay-determinism criterion names.
            appendGameEvent connA "grounded" (Games.Game_added_to_library sampleGameData)
            appendGameEvent connA "grounded" (Games.Game_categorized [ "Horror" ])
            appendGameEvent connA "grounded" (Games.Game_rawg_id_set (123, Some 4.5))
            appendGameEvent connA "grounded" (Games.Game_hltb_hours_set (Some 50.0, None, None))
            appendGameEvent connA "grounded" (Games.Game_steam_app_id_set 456)
            appendGameEvent connA "grounded" (Games.Game_description_set "a description")
            appendGameEvent connA "grounded" (Games.Game_short_description_set "short")
            appendGameEvent connA "grounded" (Games.Game_website_url_set (Some "https://example.com"))
            appendGameEvent connA "grounded" (Games.Game_family_owner_added "marco")
            appendGameEvent connA "grounded" (Games.Game_play_mode_added "Co-op")
            appendGameEvent connA "grounded" (Games.Game_play_mode_removed "Co-op")
            appendGameEvent connA "grounded" (Games.Game_steam_last_played_set (Some "2024-06-20"))
            appendGameEvent connA "grounded" (Games.Game_store_added "Steam")
            appendGameEvent connA "grounded" (Games.Game_store_removed "Steam")
            appendGameEvent connA "grounded" (Games.Game_steam_library_date_set (Some "2020-01-01"))
            appendGameEvent connA "grounded" (Games.Prior_play_time_recorded 100)
            appendGameEvent connA "grounded" (Games.Play_session_recorded { Day = "2026-01-01"; Minutes = 60; Source = Manual })
            appendGameEvent connA "grounded" (Games.Game_play_time_set 500)
            appendGameEvent connA "grounded" (Games.Steam_observed_total_reconciled 999)

            // A control slug with only kept events, unaffected by the purge.
            appendGameEvent connA "solo" (Games.Game_added_to_library { sampleGameData with Name = "Solo"; Genres = [ "Puzzle" ] })
            appendGameEvent connA "solo" (Games.Game_rawg_id_set (789, None))

            Projection.rebuildProjection connA GameProjection.handler
            let beforeAll = GameProjection.getAll connA
            let beforeGrounded = GameProjection.getBySlug connA "grounded"
            let beforeSolo = GameProjection.getBySlug connA "solo"

            let exportedA = exportToString connA
            use reader = new StringReader(exportedA)
            use writer = new StringWriter()
            let summary = EventLogFilter.filterNdjson EventLogFilter.purgeEligibleEventTypes reader writer
            Expect.equal summary.DroppedLines 11 "exactly the 11 purge-eligible lines (all from 'grounded') are dropped"
            Expect.equal (summary.KeptLines + summary.DroppedLines) summary.InputLines "kept + dropped = input"

            // The ADR-0038 wipe-first shape without touching a live database:
            // a fresh store standing in for "wiped", importing the filtered
            // export, then Rebuild-all — the exact operator sequence the
            // runbook documents.
            use connB = createInMemoryDb ()
            use filteredReader = new StringReader(writer.ToString())
            match EventStore.importNdjson connB filteredReader with
            | Error e -> failtest (sprintf "import into the fresh, filtered store should succeed, got %A" e)
            | Ok _ -> ()
            Projection.rebuildProjection connB GameProjection.handler

            let afterAll = GameProjection.getAll connB
            let afterGrounded = GameProjection.getBySlug connB "grounded"
            let afterSolo = GameProjection.getBySlug connB "solo"

            Expect.equal (List.length afterAll) (List.length beforeAll) "same number of games before and after the purge"
            Expect.equal afterAll beforeAll "GameProjection.getAll is row-identical before and after the purge — 0 discrepancies"
            Expect.equal afterGrounded beforeGrounded "the purged game's own detail row is unchanged"
            Expect.equal afterSolo beforeSolo "the untouched control game's detail row is unchanged"
    ]
