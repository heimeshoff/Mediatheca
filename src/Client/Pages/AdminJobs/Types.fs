module Mediatheca.Client.Pages.AdminJobs.Types

open Mediatheca.Shared

/// The Jobs tab (administration-yamm5, ADR-0026): per-job next-fire time,
/// last outcome + summary, recent-run history, and a fire-and-forget
/// "Run now" whose row is polled until it resolves. Reuses ADR-0023's
/// epoch-guarded polling `Cmd` shape (not SSE — run-now is a plain Remoting
/// call returning a row id).
type Model = {
    Statuses: JobStatusDto list
    IsLoading: bool
    /// Job names with a client-initiated "Run now" in flight — drives the
    /// button's disabled state and whether polling keeps going. The server's
    /// own name-keyed guard (ADR-0026) is the real single source of truth for
    /// concurrency; this is purely this tab's UI state.
    RunningNow: Set<string>
    /// Bumped whenever a new "Run now" starts polling, so a poll scheduled
    /// before a newer run started (or a stale poll from before navigating
    /// away and back) is inert once it fires.
    PollEpoch: int
    /// Most recent `runJobNow` result per job — used to surface a rejection
    /// (e.g. a stale double-click) distinctly from a started run.
    LastRunResult: Map<string, RunJobResult>
}

type Msg =
    | Load
    | Statuses_loaded of JobStatusDto list
    | Run_now_clicked of jobName: string
    | Run_now_response of jobName: string * RunJobResult
    | Poll_tick of epoch: int
    | Poll_loaded of epoch: int * JobStatusDto list
