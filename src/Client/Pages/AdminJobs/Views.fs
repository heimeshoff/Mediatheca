module Mediatheca.Client.Pages.AdminJobs.Views

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client
open Mediatheca.Shared
open Mediatheca.Client.Pages.AdminJobs.Types

/// `startedAt`/`finishedAt` are stored ISO-8601 UTC; `nextFireAt` is a local
/// DateTime's `.ToString("o")` (carries its own UTC offset). Rather than
/// parsing and reformatting either in Fable, this trims to the
/// human-legible `yyyy-MM-dd HH:mm` prefix and labels the zone explicitly —
/// the task's explicit "timezone care" note (ADR-0026).
let private trimIso (iso: string) : string =
    let normalized = iso.Replace("T", " ")
    if normalized.Length >= 16 then normalized.Substring(0, 16) else normalized

let private statusBadge (status: JobRunStatus) =
    let variant, label =
        match status with
        | RunStatusRunning -> badge.info, "running"
        | RunStatusOk -> badge.success, "ok"
        | RunStatusError -> badge.error, "error"
        | RunStatusSkipped -> badge.warning, "skipped"
        | RunStatusInterrupted -> badge.ghost, "interrupted"
    Daisy.badge [ variant; badge.sm; prop.text label ]

let private historyTable (runs: JobRunDto list) =
    if List.isEmpty runs then
        Html.p [ prop.className DesignSystem.mutedText; prop.text "No runs recorded yet." ]
    else
        Html.div [
            prop.className "overflow-x-auto max-h-64 overflow-y-auto"
            prop.children [
                Html.table [
                    prop.className "table table-sm"
                    prop.children [
                        Html.thead [
                            Html.tr [
                                Html.th [ prop.text "Trigger" ]
                                Html.th [ prop.text "Status" ]
                                Html.th [ prop.text "Summary" ]
                                Html.th [ prop.text "Started (UTC)" ]
                                Html.th [ prop.text "Finished (UTC)" ]
                            ]
                        ]
                        Html.tbody [
                            for run in runs ->
                                Html.tr [
                                    prop.key (string run.Id)
                                    prop.children [
                                        Html.td [ prop.className DesignSystem.dataText; prop.text run.Trigger ]
                                        Html.td [ statusBadge run.Status ]
                                        Html.td [ prop.className DesignSystem.bodyText; prop.text (run.Summary |> Option.defaultValue "-") ]
                                        Html.td [ prop.className DesignSystem.dataText; prop.text (trimIso run.StartedAt) ]
                                        Html.td [
                                            prop.className DesignSystem.dataText
                                            prop.text (run.FinishedAt |> Option.map trimIso |> Option.defaultValue "-")
                                        ]
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]

let private jobCard (model: Model) (dispatch: Msg -> unit) (status: JobStatusDto) =
    let isRunning =
        Set.contains status.JobName model.RunningNow
        || (status.LastRun |> Option.map (fun r -> r.Status = RunStatusRunning) |> Option.defaultValue false)
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-4 flex flex-col gap-3")
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between gap-3"
                prop.children [
                    Html.h3 [ prop.className DesignSystem.cardTitle; prop.text status.JobName ]
                    Daisy.button.button [
                        button.outline
                        button.sm
                        prop.disabled isRunning
                        prop.onClick (fun _ -> dispatch (Run_now_clicked status.JobName))
                        prop.text (if isRunning then "Running…" else "Run now")
                    ]
                ]
            ]
            Html.div [
                prop.className "flex flex-wrap items-center gap-4"
                prop.children [
                    Html.span [
                        prop.className DesignSystem.mutedText
                        prop.text (sprintf "Next fire (local): %s" (trimIso status.NextFireAt))
                    ]
                    match status.LastRun with
                    | None ->
                        Html.span [ prop.className DesignSystem.mutedText; prop.text "No runs yet" ]
                    | Some last ->
                        Html.div [
                            prop.className "flex items-center gap-2"
                            prop.children [
                                Html.span [ prop.className DesignSystem.mutedText; prop.text "Last outcome:" ]
                                statusBadge last.Status
                                Html.span [
                                    prop.className DesignSystem.bodyText
                                    prop.text (last.Summary |> Option.defaultValue "")
                                ]
                            ]
                        ]
                ]
            ]
            match model.LastRunResult |> Map.tryFind status.JobName with
            | Some RunJobRejected ->
                Html.p [ prop.className "text-sm text-warning"; prop.text "Rejected — this job is already running." ]
            | _ -> Html.none
            historyTable status.RecentRuns
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " flex flex-col gap-4")
        prop.children [
            Html.p [
                prop.className DesignSystem.mutedText
                prop.text "Scheduled job history and manual triggers. \"Run now\" starts the job immediately and returns before it completes — this tab polls until it resolves."
            ]
            if model.IsLoading && List.isEmpty model.Statuses then
                Html.p [ prop.className DesignSystem.mutedText; prop.text "Loading..." ]
            else
                Html.div [
                    prop.className "flex flex-col gap-4"
                    prop.children [ for status in model.Statuses -> jobCard model dispatch status ]
                ]
        ]
    ]
