module Mediatheca.Tests.PlaySessionMigrationTests

// games-h4mrd: `PlaySessionMigration.plan` as a pure function — no database,
// no SSE, no guards. Every case here constructs its inputs directly and
// asserts on the returned `MigrationPlan`; the DB-touching executor
// (`Administration.fs`) and its idempotency/guard/end-to-end behaviour are
// covered separately in `AdminPlaySessionMigrationTests.fs`.

open System
open Expecto
open Mediatheca.Server
open Mediatheca.Server.Games
open Mediatheca.Shared
open Mediatheca.Server.PlaySessionMigration

/// A deterministic `DateTimeOffset` whose `.LocalDateTime` is EXACTLY the
/// wall-clock time given — independent of the machine's local timezone, so
/// `PlaytimeTracker.toGamingDay`'s day derivation is reproducible in CI.
let private ts (y: int) (m: int) (d: int) (h: int) : DateTimeOffset =
    let dt = DateTime(y, m, d, h, 0, 0)
    DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt))

/// Sync hour 4 (the production default) with noon timestamps: `toGamingDay`
/// subtracts 4h30, landing well inside the same calendar day, so each
/// fixture's gaming day equals its timestamp's own date — easy to reason
/// about in assertions.
let private syncHour = 4

let private dayOf (t: DateTimeOffset) = PlaytimeTracker.toGamingDay syncHour t.LocalDateTime

let private sessionDaysOf (events: GameEvent list) =
    events |> List.choose (function Play_session_recorded d -> Some d.Day | _ -> None)

[<Tests>]
let playSessionMigrationTests =
    testList "PlaySessionMigration" [

        testCase "reconstruction-only: first observation becomes a dateless Prior_play_time_recorded, not a session" <| fun _ ->
            let events = [ 100, ts 2026 1 1 12 ]
            let result = plan [ "Game-solo", events ] Map.empty Map.empty syncHour

            Expect.equal result.ReconstructedSlugs [ "solo" ] "solo should be reconstructed"
            Expect.equal result.TableCoveredSlugs [] "no table-covered games"
            Expect.equal result.PriorPlayTimeLumpCount 1 "exactly one prior-playtime lump"
            match result.StreamEvents with
            | [ ("Game-solo", [ Prior_play_time_recorded 100 ]) ] -> ()
            | other -> failtest (sprintf "Expected exactly one Prior_play_time_recorded 100 for solo, got %A" other)
            Expect.equal result.ExpectedRowCount 0 "prior playtime writes no dated row"

        testCase "reconstruction-only: t0 = 0 emits no prior-playtime lump" <| fun _ ->
            let events = [ 0, ts 2026 1 1 12; 150, ts 2026 1 2 12 ]
            let result = plan [ "Game-fresh", events ] Map.empty Map.empty syncHour

            Expect.equal result.PriorPlayTimeLumpCount 0 "t0 = 0 is not a prior-playtime fact worth recording"
            match result.StreamEvents |> List.tryFind (fst >> (=) "Game-fresh") with
            | Some (_, evs) ->
                Expect.isFalse (evs |> List.exists (function Prior_play_time_recorded _ -> true | _ -> false)) "no prior-playtime event"
                Expect.equal (sessionDaysOf evs) [ dayOf (ts 2026 1 2 12) ] "the 150-minute delta becomes one dated session"
            | None -> failtest "fresh should appear in the plan"

        testCase "reconstruction-only: every subsequent positive delta becomes a Play_session_recorded dated at its own event's gaming day, sourced SteamSync" <| fun _ ->
            let t1, t2, t3 = ts 2026 1 1 12, ts 2026 1 5 12, ts 2026 1 9 12
            let events = [ 500, t1; 561, t2; 900, t3 ]
            let result = plan [ "Game-solo", events ] Map.empty Map.empty syncHour

            match result.StreamEvents with
            | [ ("Game-solo", [ Prior_play_time_recorded 500; Play_session_recorded d1; Play_session_recorded d2 ]) ] ->
                Expect.equal d1 { Day = dayOf t2; Minutes = 61; Source = SteamSync } "first delta: 561-500=61"
                Expect.equal d2 { Day = dayOf t3; Minutes = 339; Source = SteamSync } "second delta: 900-561=339"
            | other -> failtest (sprintf "Unexpected events for solo: %A" other)
            Expect.equal result.ExpectedRowCount 2 "two distinct dated sessions"

        testCase "reconstruction-only: a negative or zero delta emits nothing, adjusts nothing, and is counted" <| fun _ ->
            // 300 -> 500 (delta +200, session) -> 400 (delta -100, negative, skipped,
            // baseline stays 500) -> 600 (delta +100 against the UNCHANGED 500 baseline, session).
            let t1, t2, t3, t4 = ts 2026 1 1 12, ts 2026 1 2 12, ts 2026 1 3 12, ts 2026 1 4 12
            let events = [ 300, t1; 500, t2; 400, t3; 600, t4 ]
            let result = plan [ "Game-dipper", events ] Map.empty Map.empty syncHour

            Expect.equal result.NegativeDeltasSkipped 1 "exactly one negative delta"
            match result.StreamEvents with
            | [ ("Game-dipper", evs) ] ->
                Expect.equal (sessionDaysOf evs) [ dayOf t2; dayOf t4 ] "two sessions recorded, the dip produced none"
                let minutesOf =
                    evs |> List.choose (function Play_session_recorded d -> Some d.Minutes | _ -> None)
                Expect.equal minutesOf [ 200; 100 ] "deltas are 200 then 100 (against the pre-dip 500 baseline, not the dipped 400)"
            | other -> failtest (sprintf "Unexpected: %A" other)

        testCase "table-covered: a slug present in game_play_session wins outright — reconstruction (including its prior-playtime lump) is fully discarded" <| fun _ ->
            let cumulative = [ 100, ts 2026 1 1 12; 900, ts 2026 6 1 12 ] // would reconstruct to a big prior lump if not discarded
            let tableRows =
                Map.ofList [ "tablewins", [
                    { Date = "2026-02-01"; Minutes = 300; Source = SteamSync }
                    { Date = "2026-03-01"; Minutes = 600; Source = SteamSync }
                ] ]
            let result = plan [ "Game-tablewins", cumulative ] tableRows Map.empty syncHour

            Expect.equal result.TableCoveredSlugs [ "tablewins" ] "table wins"
            Expect.equal result.ReconstructedSlugs [] "reconstruction must not run for a table-covered slug"
            Expect.equal result.PriorPlayTimeLumpCount 0 "no prior-playtime lump for a table-covered slug"
            match result.StreamEvents with
            | [ ("Game-tablewins", [ Play_session_recorded d1; Play_session_recorded d2 ]) ] ->
                Expect.equal d1 { Day = "2026-02-01"; Minutes = 300; Source = SteamSync } "first table row"
                Expect.equal d2 { Day = "2026-03-01"; Minutes = 600; Source = SteamSync } "second table row"
            | other -> failtest (sprintf "Unexpected: %A" other)

        testCase "table-covered: Manual rows (steam_app_id = 0 in the old schema) round-trip as Source = Manual" <| fun _ ->
            let tableRows = Map.ofList [ "mixedsrc", [ { Date = "2026-01-10"; Minutes = 45; Source = Manual } ] ]
            let result = plan [ "Game-mixedsrc", [ 45, ts 2026 1 10 12 ] ] tableRows Map.empty syncHour
            match result.StreamEvents with
            | [ ("Game-mixedsrc", [ Play_session_recorded d ]) ] -> Expect.equal d.Source Manual "Manual source preserved"
            | other -> failtest (sprintf "Unexpected: %A" other)

        testCase "table-covered: a slug failing the Σ table rows = t_last integrity gate is refused entirely — zero events, reported" <| fun _ ->
            let cumulative = [ 100, ts 2026 1 1 12; 1000, ts 2026 6 1 12 ] // t_last = 1000
            let tableRows = Map.ofList [ "broken", [ { Date = "2026-02-01"; Minutes = 300; Source = SteamSync } ] ] // sums to 300 <> 1000
            let result = plan [ "Game-broken", cumulative ] tableRows Map.empty syncHour

            Expect.equal result.StreamEvents [] "a failing integrity gate refuses ALL events for that slug"
            Expect.equal result.TableCoveredSlugs [] "a refused slug is neither table-covered nor reconstructed in the output"
            match result.IntegrityFailures with
            | [ f ] ->
                Expect.equal f.Slug "broken" "the failing slug is named"
                Expect.equal f.TableTotal 300 "reported table total"
                Expect.equal f.LastEventTotal 1000 "reported last-event total"
            | other -> failtest (sprintf "Expected exactly one integrity failure, got %A" other)

        testCase "table-covered with no cumulative history at all: the integrity gate is vacuously satisfied" <| fun _ ->
            let tableRows = Map.ofList [ "manualonly", [ { Date = "2026-01-05"; Minutes = 60; Source = Manual } ] ]
            let result = plan [] tableRows Map.empty syncHour
            Expect.equal result.TableCoveredSlugs [ "manualonly" ] "no cumulative history to gate against, so it proceeds"
            Expect.equal result.IntegrityFailures [] "no integrity failure reported"

        testCase "cursor conservation: a mismatched steam_playtime_snapshot row emits one Steam_observed_total_reconciled, table-covered path" <| fun _ ->
            let cumulative = [ 509, ts 2026 1 1 12; 2282, ts 2026 6 1 12 ]
            let tableRows =
                Map.ofList [ "grounded", [
                    { Date = "2026-01-10"; Minutes = 1000; Source = SteamSync }
                    { Date = "2026-02-10"; Minutes = 1282; Source = SteamSync }
                ] ] // sums to 2282, matches t_last
            let snapshot = Map.ofList [ "grounded", 2952 ] // Steam's stale, pre-removal total
            let result = plan [ "Game-grounded", cumulative ] tableRows snapshot syncHour

            Expect.equal result.ReconciliationCount 1 "the snapshot disagrees with the derived observed total"
            match result.StreamEvents with
            | [ ("Game-grounded", evs) ] ->
                match List.last evs with
                | Steam_observed_total_reconciled 2952 -> ()
                | other -> failtest (sprintf "Expected the reconciliation to be the trailing event, got %A" other)
            | other -> failtest (sprintf "Unexpected: %A" other)

        testCase "cursor conservation: an agreeing steam_playtime_snapshot row emits no reconciliation" <| fun _ ->
            let tableRows = Map.ofList [ "agree", [ { Date = "2026-01-05"; Minutes = 400; Source = SteamSync } ] ]
            let snapshot = Map.ofList [ "agree", 400 ]
            let result = plan [] tableRows snapshot syncHour
            Expect.equal result.ReconciliationCount 0 "derived observed already equals the snapshot"

        testCase "cursor conservation: reconstruction-only path also reconciles against a mismatched snapshot" <| fun _ ->
            let events = [ 200, ts 2026 1 1 12; 350, ts 2026 1 5 12 ] // prior 200, session +150, derived observed = 350
            let snapshot = Map.ofList [ "solo2", 500 ] // Steam reports more than we derived
            let result = plan [ "Game-solo2", events ] Map.empty snapshot syncHour
            Expect.equal result.ReconciliationCount 1 "mismatch triggers reconciliation"
            match result.StreamEvents with
            | [ ("Game-solo2", evs) ] ->
                Expect.equal (List.last evs) (Steam_observed_total_reconciled 500) "reconciliation trails the reconstructed history"
            | other -> failtest (sprintf "Unexpected: %A" other)

        testCase "the Grounded fixture: full 509->570->...->2952->2282 sequence alongside its 8-row table slice and its snapshot row" <| fun _ ->
            let cumulative = [
                509, ts 2026 1 5 12
                570, ts 2026 1 12 12
                1250, ts 2026 2 1 12
                1900, ts 2026 2 15 12
                2952, ts 2026 2 19 12
                2282, ts 2026 2 20 12 // the user removed a 670-minute session; t_last = 2282
            ]
            let tableRows =
                Map.ofList [ "grounded", [
                    { Date = "2026-01-05"; Minutes = 120; Source = SteamSync }
                    { Date = "2026-01-12"; Minutes = 200; Source = SteamSync }
                    { Date = "2026-01-20"; Minutes = 400; Source = SteamSync }
                    { Date = "2026-01-28"; Minutes = 300; Source = SteamSync }
                    { Date = "2026-02-04"; Minutes = 250; Source = SteamSync }
                    { Date = "2026-02-11"; Minutes = 180; Source = SteamSync }
                    { Date = "2026-02-15"; Minutes = 410; Source = SteamSync }
                    { Date = "2026-02-19"; Minutes = 422; Source = SteamSync }
                ] ] // sums to 2282
            let snapshot = Map.ofList [ "grounded", 2952 ]

            let result = plan [ "Game-grounded", cumulative ] tableRows snapshot syncHour

            Expect.equal result.IntegrityFailures [] "the 8-row table slice sums exactly to t_last (2282) — the gate passes"
            Expect.equal result.TableCoveredSlugs [ "grounded" ] "Grounded is one of the 8 table-covered games"
            match result.StreamEvents with
            | [ ("Game-grounded", evs) ] ->
                let sessions = evs |> List.filter (function Play_session_recorded _ -> true | _ -> false)
                Expect.equal (List.length sessions) 8 "one Play_session_recorded per real table row"
                Expect.equal (evs |> List.sumBy (function Play_session_recorded d -> d.Minutes | _ -> 0)) 2282 "sessions sum to t_last"
                Expect.equal (List.last evs) (Steam_observed_total_reconciled 2952) "Steam's stale pre-removal total is carried across the cutover as the cursor"
                Expect.isFalse (evs |> List.exists (function Prior_play_time_recorded _ -> true | _ -> false)) "table-covered games get no prior-playtime lump"
            | other -> failtest (sprintf "Unexpected: %A" other)

            // Whole-plan invariants the acceptance criteria assert against.
            Expect.equal result.ExpectedRowCount 8 "8 distinct (slug, day) pairs"
            Expect.equal (result.Events |> List.length) 8 "Events excludes the reconciliation — only the 8 Play_session_recorded"
            Expect.isFalse (result.Events |> List.exists (function Steam_observed_total_reconciled _ -> true | _ -> false)) "Events never includes reconciliation events"

        testCase "row-count conservation: a slug with neither cumulative events nor table rows is never touched" <| fun _ ->
            let result = plan [ "Game-untouched", [] ] Map.empty Map.empty syncHour
            Expect.equal result.StreamEvents [] "an empty cumulative list and no table rows produce no plan entry"
            Expect.equal result.ReconstructedSlugs [] "not reconstructed"
            Expect.equal result.TableCoveredSlugs [] "not table-covered"

        testCase "every reconstructed session's date is >= the gaming day of t0" <| fun _ ->
            let t0 = ts 2026 3 1 12
            let t1 = ts 2026 3 10 12
            let events = [ 50, t0; 200, t1 ]
            let result = plan [ "Game-ordered", events ] Map.empty Map.empty syncHour
            match result.StreamEvents with
            | [ ("Game-ordered", evs) ] ->
                let today = DateTime.Now.ToString("yyyy-MM-dd")
                for day in sessionDaysOf evs do
                    Expect.isTrue (String.CompareOrdinal(day, dayOf t0) >= 0) "every session day is on or after day(t0)"
                    Expect.isTrue (String.CompareOrdinal(day, today) <= 0) "every session day is on or before today — these are 2026-03 fixture dates, always in the past"
                    let parsed, _ = DateTime.TryParseExact(day, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None)
                    Expect.isTrue parsed "every session day parses as yyyy-MM-dd"
            | other -> failtest (sprintf "Unexpected: %A" other)

        testCase "no Imported source ever appears — every emitted session is Manual or SteamSync" <| fun _ ->
            let tableRows = Map.ofList [ "onlytwo", [ { Date = "2026-01-01"; Minutes = 10; Source = Manual }; { Date = "2026-01-02"; Minutes = 20; Source = SteamSync } ] ]
            let result = plan [] tableRows Map.empty syncHour
            match result.StreamEvents with
            | [ ("Game-onlytwo", evs) ] ->
                for e in evs do
                    match e with
                    | Play_session_recorded d -> Expect.isTrue (d.Source = Manual || d.Source = SteamSync) "only the two known sources exist"
                    | _ -> ()
            | other -> failtest (sprintf "Unexpected: %A" other)
    ]
