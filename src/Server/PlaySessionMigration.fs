namespace Mediatheca.Server

open System
open Mediatheca.Shared
open Mediatheca.Server.Games

/// Reconstructs play-session history from the 204 cumulative
/// `Game_play_time_set` totals games-p6vkz retired (games-h4mrd). Every
/// stream's *earliest* observation becomes prior playtime — a dateless
/// fact — never a fabricated session; every later positive delta becomes a
/// genuinely dated `Play_session_recorded`. Pure: reads no database, writes
/// nothing, decides nothing about transport, guards or idempotency —
/// `Administration.fs`'s SSE handler is the thin, DB-touching shell around
/// this module's `plan`, in the `decideAndClaimWipeImportGuard` extraction
/// shape.
module PlaySessionMigration =

    /// One pre-migration `game_play_session` row, from the OLD,
    /// non-event-sourced schema (`id, game_slug, steam_app_id, date,
    /// minutes_played, created_at`) games-p6vkz's `PlaySessionProjection`
    /// superseded. Already classified into `PlaySessionSource` by the
    /// caller — the old table's own sentinel convention was
    /// `steam_app_id = 0` means `Manual`, anything else `SteamSync` — so
    /// this module stays schema-agnostic and never touches SQL.
    type TableRow = {
        Date: string
        Minutes: int
        Source: PlaySessionSource
    }

    /// A table-covered slug refused outright — the `Σ table rows = t_last`
    /// integrity gate (exploiting `recomputeAndPublishTotal`'s structural
    /// identity) failed, so NO events are emitted for it; the discrepancy is
    /// reported instead of guessed at.
    type IntegrityFailure = {
        Slug: string
        TableTotal: int
        LastEventTotal: int
    }

    type MigrationPlan = {
        /// Every event to append, grouped by the stream it belongs to, each
        /// stream's list in append order — the shape the executor loops
        /// over, one `appendToStream` call per stream.
        StreamEvents: (string * GameEvent list) list
        /// Flattened `Prior_play_time_recorded` + `Play_session_recorded`
        /// events only (excludes `Steam_observed_total_reconciled`) — the
        /// exact set `event_type LIKE 'Play_session_%' OR event_type =
        /// 'Prior_play_time_recorded'` counts against in the live log.
        Events: GameEvent list
        /// Distinct `(slug, day)` pairs across every planned session — what
        /// `game_play_session`'s row count will be after Rebuild-all,
        /// independent of same-day deltas merging into one row.
        ExpectedRowCount: int
        ReconstructedSlugs: string list
        TableCoveredSlugs: string list
        PriorPlayTimeLumpCount: int
        ReconciliationCount: int
        NegativeDeltasSkipped: int
        IntegrityFailures: IntegrityFailure list
    }

    let private slugFromStreamId (streamId: string) : string =
        if streamId.StartsWith("Game-") then streamId.Substring(5) else streamId

    /// Per-slug outcome before the whole-plan fold-up below.
    type private SlugOutcome =
        | Refused of IntegrityFailure
        | Reconstructed of streamId: string * events: GameEvent list * sessionDays: (string * string) list * negativeDeltas: int * priorLump: bool
        | TableCovered of streamId: string * events: GameEvent list * sessionDays: (string * string) list * reconciled: bool

    /// Walks one reconstruction-only stream's cumulative totals, oldest
    /// first (`t0` already split off by the caller): `prevTotal` is the
    /// running high-water mark a positive delta advances and a
    /// zero-or-negative delta leaves untouched — "emit nothing, adjust
    /// nothing", the exact rule `Games.decide`'s `Record_steam_observed_total`
    /// uses for live syncs (games-p6vkz), applied here to history instead of
    /// the present.
    let rec private walkDeltas
        (gamingDay: DateTimeOffset -> string)
        (prevTotal: int)
        (negativeCount: int)
        (events: (int * DateTimeOffset) list)
        (acc: GameEvent list)
        : GameEvent list * int * int =
        match events with
        | [] -> List.rev acc, negativeCount, prevTotal
        | (total, ts) :: rest ->
            let delta = total - prevTotal
            if delta > 0 then
                let event = Play_session_recorded { Day = gamingDay ts; Minutes = delta; Source = SteamSync }
                walkDeltas gamingDay total negativeCount rest (event :: acc)
            else
                let negativeCount = if delta < 0 then negativeCount + 1 else negativeCount
                walkDeltas gamingDay prevTotal negativeCount rest acc

    /// `plan` — the whole migration policy as one pure function.
    ///
    /// - `cumulativeByStream`: every `Game_play_time_set` event's
    ///   `(totalMinutes, timestamp)`, per stream, in any order (sorted here).
    /// - `tableRowsBySlug`: pre-migration `game_play_session` rows (the old,
    ///   non-projection table), read before Rebuild-all drops it.
    /// - `snapshotBySlug`: pre-migration `steam_playtime_snapshot.total_minutes`,
    ///   read before that orphaned table is dropped — carries the Steam-sync
    ///   cursor across the cutover for the handful of games where the user's
    ///   edits diverged from what Steam last reported.
    /// - `syncHour`: `PlaytimeTracker`'s configured sync hour, threaded
    ///   through to `PlaytimeTracker.toGamingDay` so reconstructed and live
    ///   sessions land in the same gaming-day buckets.
    let plan
        (cumulativeByStream: (string * (int * DateTimeOffset) list) list)
        (tableRowsBySlug: Map<string, TableRow list>)
        (snapshotBySlug: Map<string, int>)
        (syncHour: int)
        : MigrationPlan =

        let gamingDay (ts: DateTimeOffset) = PlaytimeTracker.toGamingDay syncHour ts.LocalDateTime

        let cumulativeBySlug =
            cumulativeByStream
            |> List.map (fun (streamId, events) -> slugFromStreamId streamId, events |> List.sortBy snd)
            |> Map.ofList

        let allSlugs =
            (cumulativeBySlug |> Map.toList |> List.map fst)
            @ (tableRowsBySlug |> Map.toList |> List.map fst)
            |> List.distinct

        let outcomes =
            allSlugs
            |> List.choose (fun slug ->
                let streamId = Games.streamId slug
                let cumulative = cumulativeBySlug |> Map.tryFind slug |> Option.defaultValue []

                match tableRowsBySlug |> Map.tryFind slug with
                | Some rows when not (List.isEmpty rows) ->
                    // Table wins where it exists, all-or-nothing (games-h4mrd):
                    // the reconstruction (including its prior-playtime lump)
                    // is discarded entirely for this slug.
                    let tableTotal = rows |> List.sumBy (fun r -> r.Minutes)
                    let lastEventTotal = cumulative |> List.tryLast |> Option.map fst
                    let integrityOk =
                        match lastEventTotal with
                        | None -> true // no cumulative history to gate against
                        | Some lastTotal -> tableTotal = lastTotal
                    if not integrityOk then
                        Some (Refused { Slug = slug; TableTotal = tableTotal; LastEventTotal = lastEventTotal |> Option.defaultValue 0 })
                    else
                        let sessionEvents =
                            rows |> List.map (fun r -> Play_session_recorded { Day = r.Date; Minutes = r.Minutes; Source = r.Source })
                        let derivedObserved =
                            rows |> List.filter (fun r -> r.Source = SteamSync) |> List.sumBy (fun r -> r.Minutes)
                        let reconciliation =
                            snapshotBySlug
                            |> Map.tryFind slug
                            |> Option.filter (fun snap -> snap <> derivedObserved)
                            |> Option.map Steam_observed_total_reconciled
                        let sessionDays = rows |> List.map (fun r -> slug, r.Date)
                        let events = sessionEvents @ (reconciliation |> Option.toList)
                        Some (TableCovered(streamId, events, sessionDays, reconciliation.IsSome))

                | _ ->
                    match cumulative with
                    | [] -> None // neither table rows nor cumulative history — never touched
                    | (m0, _t0) :: rest ->
                        let priorEvent = if m0 > 0 then [ Prior_play_time_recorded m0 ] else []
                        let sessionEvents, negativeCount, finalObserved = walkDeltas gamingDay m0 0 rest []
                        let reconciliation =
                            snapshotBySlug
                            |> Map.tryFind slug
                            |> Option.filter (fun snap -> snap <> finalObserved)
                            |> Option.map Steam_observed_total_reconciled
                        let sessionDays =
                            sessionEvents
                            |> List.choose (function Play_session_recorded d -> Some (slug, d.Day) | _ -> None)
                        let events = priorEvent @ sessionEvents @ (reconciliation |> Option.toList)
                        Some (Reconstructed(streamId, events, sessionDays, negativeCount, not (List.isEmpty priorEvent))))

        let streamEvents =
            outcomes
            |> List.choose (function
                | Refused _ -> None
                | Reconstructed(streamId, events, _, _, _) -> Some (streamId, events)
                | TableCovered(streamId, events, _, _) -> Some (streamId, events))

        let historyEvents =
            streamEvents
            |> List.collect snd
            |> List.filter (function Steam_observed_total_reconciled _ -> false | _ -> true)

        let allSessionDays =
            outcomes
            |> List.collect (function
                | Refused _ -> []
                | Reconstructed(_, _, days, _, _) -> days
                | TableCovered(_, _, days, _) -> days)
            |> List.distinct

        let reconstructedSlugs =
            outcomes |> List.choose (function Reconstructed(streamId, _, _, _, _) -> Some (slugFromStreamId streamId) | _ -> None)

        let tableCoveredSlugs =
            outcomes |> List.choose (function TableCovered(streamId, _, _, _) -> Some (slugFromStreamId streamId) | _ -> None)

        let priorPlayTimeLumpCount =
            outcomes |> List.sumBy (function Reconstructed(_, _, _, _, hadPrior) -> (if hadPrior then 1 else 0) | _ -> 0)

        let reconciliationCount =
            outcomes
            |> List.sumBy (function
                | Reconstructed(_, events, _, _, _) -> events |> List.filter (function Steam_observed_total_reconciled _ -> true | _ -> false) |> List.length
                | TableCovered(_, _, _, reconciled) -> if reconciled then 1 else 0
                | Refused _ -> 0)

        let negativeDeltasSkipped =
            outcomes |> List.sumBy (function Reconstructed(_, _, _, negCount, _) -> negCount | _ -> 0)

        let integrityFailures =
            outcomes |> List.choose (function Refused f -> Some f | _ -> None)

        { StreamEvents = streamEvents
          Events = historyEvents
          ExpectedRowCount = List.length allSessionDays
          ReconstructedSlugs = reconstructedSlugs
          TableCoveredSlugs = tableCoveredSlugs
          PriorPlayTimeLumpCount = priorPlayTimeLumpCount
          ReconciliationCount = reconciliationCount
          NegativeDeltasSkipped = negativeDeltasSkipped
          IntegrityFailures = integrityFailures }
