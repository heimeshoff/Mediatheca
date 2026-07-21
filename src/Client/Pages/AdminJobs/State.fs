module Mediatheca.Client.Pages.AdminJobs.State

open Elmish
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminJobs.Types

/// Run-now poll interval (ADR-0026, reusing ADR-0023's epoch-guarded Cmd
/// shape). A client-side constant — fine for a single-user app.
let pollIntervalMs = 2000

let init () : Model * Cmd<Msg> =
    { Statuses = []
      IsLoading = true
      RunningNow = Set.empty
      PollEpoch = 0
      LastRunResult = Map.empty },
    Cmd.ofMsg Load

/// True while any name in `runningNow` still shows `RunStatusRunning` as its
/// most recent run in the freshly-fetched `statuses`. The server's
/// name-keyed guard (ADR-0026) means at most one row per job can ever be
/// `running` at a time, so "the newest row is still running" is exactly
/// "our run-now hasn't resolved yet".
let private stillRunning (statuses: JobStatusDto list) (runningNow: Set<string>) : Set<string> =
    runningNow
    |> Set.filter (fun name ->
        statuses
        |> List.tryFind (fun s -> s.JobName = name)
        |> Option.bind (fun s -> s.LastRun)
        |> Option.map (fun r -> r.Status = RunStatusRunning)
        |> Option.defaultValue false)

/// Reschedule the next poll after `pollIntervalMs`, tagged with the epoch
/// current when it was scheduled — same shape as EventBrowser's Follow-mode
/// `delayedPoll` (administration-mtf1f / ADR-0023).
let private delayedPoll (epoch: int) : Cmd<Msg> =
    Cmd.OfAsync.perform
        (fun () -> async {
            do! Async.Sleep pollIntervalMs
            return epoch
        })
        ()
        Poll_tick

let update (api: IAdminApi) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | Load ->
        { model with IsLoading = true }, Cmd.OfAsync.perform api.getJobStatuses () Statuses_loaded

    | Statuses_loaded statuses ->
        { model with Statuses = statuses; IsLoading = false }, Cmd.none

    | Run_now_clicked jobName ->
        { model with RunningNow = Set.add jobName model.RunningNow },
        Cmd.OfAsync.perform api.runJobNow jobName (fun result -> Run_now_response (jobName, result))

    | Run_now_response (jobName, result) ->
        let model = { model with LastRunResult = Map.add jobName result model.LastRunResult }
        match result with
        | RunJobRejected ->
            { model with RunningNow = Set.remove jobName model.RunningNow }, Cmd.none
        | RunJobStarted _ ->
            // Fire-and-forget: the row already exists (running) by the time
            // this response lands, so poll immediately (no initial sleep),
            // then keep polling every `pollIntervalMs` until it resolves.
            let newEpoch = model.PollEpoch + 1
            { model with PollEpoch = newEpoch }, Cmd.ofMsg (Poll_tick newEpoch)

    | Poll_tick epoch ->
        if epoch <> model.PollEpoch then
            // Stale: a newer "Run now" (or none) has superseded this poll loop.
            model, Cmd.none
        else
            model, Cmd.OfAsync.perform api.getJobStatuses () (fun statuses -> Poll_loaded (epoch, statuses))

    | Poll_loaded (epoch, statuses) ->
        if epoch <> model.PollEpoch then
            model, Cmd.none
        else
            let runningNow = stillRunning statuses model.RunningNow
            let model = { model with Statuses = statuses; RunningNow = runningNow }
            model, (if Set.isEmpty runningNow then Cmd.none else delayedPoll epoch)
