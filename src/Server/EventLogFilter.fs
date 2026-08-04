namespace Mediatheca.Server

open System.IO
open System.Text.Json

/// Offline NDJSON filter for the ADR-0038 wipe-first purge of the eleven
/// demoted Game metadata event types (administration-z6ymt). This module is
/// pure/Giraffe-decoupled (`TextReader`/`TextWriter`, the same shape
/// `EventStore.exportNdjson`/`importNdjson` established, ADR-0029) — it never
/// touches a `SqliteConnection`, so it can run standalone on a laptop against
/// an exported file, offline, with no live database in reach at all (the
/// 2026-08-02 "workers never touch the live database" rule). The runbook at
/// `docs/runbooks/purge-demoted-metadata-events.md` documents the operator
/// flow this feeds: export -> filter (this module, via the CLI entry point
/// in `Program.fs`) -> wipe-import -> Rebuild-all -> drift check.
module EventLogFilter =

    /// The exact eleven event types administration-z6ymt's purge drops — see
    /// the task file's "Why" section for the per-type justification. This is
    /// a DENY list, not an allow list: every other event type, including any
    /// not enumerated here, is kept by construction (`classifyLine` below
    /// only ever drops a line whose `eventType` is IN this set).
    ///
    /// Deliberately excludes (never drop, regardless of duplication):
    ///   - the five identity-card types (ADR-0043): `Game_rawg_id_set`,
    ///     `Game_steam_app_id_set`, `Game_family_owner_added`,
    ///     `Game_family_owner_removed`, `Game_steam_library_date_set`
    ///   - `Series_refreshed` (still a fully live event type post-series-r2xhv;
    ///     only its historical no-change rows are inert, and dropping those
    ///     needs a payload-level predicate this task explicitly defers)
    ///   - h4mrd's reconstructed play-session history (ADR-0050):
    ///     `Play_session_recorded`, `Prior_play_time_recorded`,
    ///     `Steam_observed_total_reconciled`
    let purgeEligibleEventTypes : Set<string> =
        set [
            "Game_categorized"
            "Game_hltb_hours_set"
            "Game_description_set"
            "Game_short_description_set"
            "Game_website_url_set"
            "Game_play_mode_added"
            "Game_play_mode_removed"
            "Game_steam_last_played_set"
            "Game_store_added"
            "Game_store_removed"
            "Game_play_time_set"
        ]

    /// Extracts just the `eventType` field's string value from one NDJSON
    /// line via `JsonDocument` — a read-only parse that never re-serializes
    /// anything. Returns `None` for a line that isn't valid JSON, or has no
    /// string `eventType` field (both treated identically by the caller:
    /// "couldn't classify this line, so never drop it").
    let private tryReadEventType (line: string) : string option =
        try
            use doc = JsonDocument.Parse(line: string)
            match doc.RootElement.TryGetProperty("eventType") with
            | true, prop when prop.ValueKind = JsonValueKind.String -> Some (prop.GetString())
            | _ -> None
        with _ -> None

    /// The classification of one non-blank line — never a rewrite decision;
    /// `filterNdjson` below is the only place that decides what to WRITE, and
    /// it always writes the ORIGINAL line verbatim for every outcome except
    /// `Dropped` (ADR-0029's byte-stability discipline extended to this
    /// filter: kept lines are never reparsed/re-nested/re-serialized).
    type LineOutcome =
        | Kept
        | Dropped of eventType: string
        /// The line couldn't be parsed as JSON, or had no string `eventType`
        /// field — fail-safe: always treated as KEPT by `filterNdjson`, never
        /// silently dropped, but counted separately in `FilterSummary` so an
        /// operator can investigate rather than trust a report that hides it.
        | Unparseable

    let classifyLine (denyList: Set<string>) (line: string) : LineOutcome =
        match tryReadEventType line with
        | Some eventType when denyList.Contains eventType -> Dropped eventType
        | Some _ -> Kept
        | None -> Unparseable

    /// The filter's report — the executable post-condition the task's first
    /// acceptance criterion names: `KeptLines + DroppedLines = InputLines`
    /// (blank lines and unparseable lines both count as kept, since both are
    /// passed through verbatim), and every key of `DroppedByType` is a
    /// member of the `denyList` the caller passed in.
    type FilterSummary = {
        InputLines: int
        KeptLines: int
        DroppedLines: int
        DroppedByType: Map<string, int>
        UnparseableLines: int
    }

    /// Streams `reader` line-by-line to `writer`: a line whose `eventType`
    /// is in `denyList` is dropped (not written); every other line —
    /// including blank lines and unparseable lines — is written through
    /// byte-identical to what `reader.ReadLine()` returned (never reparsed,
    /// never re-serialized). Never materializes the whole log as one
    /// in-memory string/collection, the same batching discipline
    /// `EventStore.exportNdjson`/`importNdjsonRows` established.
    let filterNdjson (denyList: Set<string>) (reader: TextReader) (writer: TextWriter) : FilterSummary =
        let mutable inputLines = 0
        let mutable kept = 0
        let mutable dropped = 0
        let mutable unparseable = 0
        let mutable droppedByType = Map.empty

        let bumpDropped (eventType: string) =
            droppedByType <-
                droppedByType
                |> Map.change eventType (function Some c -> Some (c + 1) | None -> Some 1)

        let rec loop () =
            match reader.ReadLine() with
            | null -> ()
            | line ->
                inputLines <- inputLines + 1
                if line.Trim() = "" then
                    writer.WriteLine(line: string)
                    kept <- kept + 1
                else
                    match classifyLine denyList line with
                    | Kept ->
                        writer.WriteLine(line: string)
                        kept <- kept + 1
                    | Unparseable ->
                        writer.WriteLine(line: string)
                        kept <- kept + 1
                        unparseable <- unparseable + 1
                    | Dropped eventType ->
                        dropped <- dropped + 1
                        bumpDropped eventType
                loop ()

        loop ()

        { InputLines = inputLines
          KeptLines = kept
          DroppedLines = dropped
          DroppedByType = droppedByType
          UnparseableLines = unparseable }

    /// The builder-executed CLI entry point (`Program.fs` dispatches
    /// `filter-demoted-events <input> <output>` here before starting the
    /// Giraffe host) — reads `inputPath`, writes `outputPath`, and prints the
    /// summary to stdout so the operator can compare it against the confirm
    /// modal's line counts per the runbook's write-gap guard step. `inputPath`
    /// and `outputPath` are plain file paths on the operator's own laptop —
    /// this never opens a `SqliteConnection` and never reaches `DATA_DIR`.
    let runCli (inputPath: string) (outputPath: string) : unit =
        use reader = new StreamReader(inputPath)
        use writer = new StreamWriter(outputPath, false)
        let summary = filterNdjson purgeEligibleEventTypes reader writer
        writer.Flush()

        printfn "Input lines:    %d" summary.InputLines
        printfn "Kept lines:     %d" summary.KeptLines
        printfn "Dropped lines:  %d" summary.DroppedLines
        if summary.UnparseableLines > 0 then
            printfn "WARNING: %d line(s) could not be parsed and were KEPT (fail-safe) — investigate before proceeding." summary.UnparseableLines
        if not (Map.isEmpty summary.DroppedByType) then
            printfn "Dropped by type:"
            summary.DroppedByType
            |> Map.toList
            |> List.sortBy fst
            |> List.iter (fun (eventType, count) -> printfn "  %-30s %d" eventType count)
