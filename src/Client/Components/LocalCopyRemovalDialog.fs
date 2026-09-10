/// integration-mqsd3: the shared "Remove local copy" confirmation + outcome
/// dialog, UI over integration-r4vzm's plan/execute API (ADR-0071). Composed
/// into both `MovieDetail` and `SeriesDetail` — the confirmation and outcome
/// UI is identical for both targets, only the resolved `LocalCopyTarget`
/// differs.
module Mediatheca.Client.Components.LocalCopyRemovalDialog

open Feliz
open Feliz.DaisyUI
open Elmish
open Mediatheca.Shared
open Mediatheca.Client
open Mediatheca.Client.Components

/// The effects `update` needs, not the whole `IMediathecaApi` — client tests
/// (ADR-0064) pass `Unchecked.defaultof<IMediathecaApi>`, which is `null` in
/// Fable and throws the moment `update` reads a member off it via
/// `Cmd.OfAsync.perform api.x`. Pages build this record from the real `api`;
/// tests build it from plain lambdas.
type Effects = {
    Plan: LocalCopyTarget -> Async<Result<RemovalPlan, string>>
    Remove: LocalCopyTarget * string list -> Async<RemovalOutcome>
}

/// Whether the "Remove local copy" action shows in a detail page's action
/// row — shown only when the detail DTO's `JellyfinId` is `Some` (mirrors
/// "Play in Jellyfin"'s condition; integration-mqsd3 What). Shared by both
/// `MovieDetail.Views` and `SeriesDetail.Views`.
let showRemoveLocalCopy (jellyfinId: string option) : bool = jellyfinId.IsSome

/// A phase machine so every decision the dialog has made is visible on the
/// model and testable without inspecting a `Cmd` (integration-mqsd3 Notes —
/// no existing client test inspects one).
type Phase =
    | Planning
    | PlanFailed of string
    | Confirming of RemovalPlan * ticked: Set<string>
    | Removing of acknowledged: string list
    | Finished of RemovalOutcome

type Model = {
    Target: LocalCopyTarget
    Phase: Phase
}

type Msg =
    | Plan_result of Result<RemovalPlan, string>
    | Toggle of hash: string
    | Confirm
    | Removal_result of RemovalOutcome
    | Retry

/// The ticked rows' hashes, in plan order — exactly what `Confirm` hands to
/// `Remove`.
let acknowledgedHashes (plan: RemovalPlan) (ticked: Set<string>) : string list =
    plan.Torrents
    |> List.filter (fun t -> ticked.Contains t.Hash)
    |> List.map (fun t -> t.Hash)

/// Whether `Confirm` may fire. The tick is an acknowledgement of what the
/// server will delete, not a selection (ADR-0071 point 6) — every
/// `TorrentsPresent` row must be ticked; `FilesOnly` has nothing to
/// acknowledge; the two dead-end cases never allow removal.
let canRemove (plan: RemovalPlan) (ticked: Set<string>) : bool =
    match plan.Case with
    | TorrentsPresent -> plan.Torrents |> List.forall (fun t -> ticked.Contains t.Hash)
    | FilesOnly -> true
    | AlreadyGoneInJellyfin
    | PathOutsideMountMap -> false

let private preTickedHashes (plan: RemovalPlan) : Set<string> =
    plan.Torrents
    |> List.filter (fun t -> t.PreTicked)
    |> List.map (fun t -> t.Hash)
    |> Set.ofList

/// Human labels for the outcome view's step list (integration-mqsd3 What).
let stepLabel (step: RemovalStep) : string =
    match step with
    | PreserveWatchHistory -> "Play state saved"
    | ResolvePath -> "Path resolved"
    | MatchTorrents -> "Torrents matched"
    | DeleteTorrents -> "Torrents deleted (with files)"
    | DeleteJellyfinItem -> "Jellyfin item deleted"
    | Verify -> "Verified gone"
    | ClearLinks -> "Jellyfin link cleared"

type OutcomeRowStatus =
    | Row_completed
    | Row_failed of reason: string

type OutcomeRow = {
    Step: RemovalStep
    Label: string
    Status: OutcomeRowStatus
}

/// The outcome view's step rows, in order: every `Completed` step first,
/// then — on failure — the failing step appended with its reason verbatim.
/// Pure so the view's row-building is unit-testable without rendering
/// (ADR-0064).
let outcomeRows (outcome: RemovalOutcome) : OutcomeRow list =
    let completed =
        outcome.Completed
        |> List.map (fun step -> { Step = step; Label = stepLabel step; Status = Row_completed })
    match outcome.Failure with
    | Some (step, reason) ->
        completed @ [ { Step = step; Label = stepLabel step; Status = Row_failed reason } ]
    | None -> completed

let init (effects: Effects) (target: LocalCopyTarget) : Model * Cmd<Msg> =
    { Target = target; Phase = Planning },
    Cmd.OfAsync.perform effects.Plan target Plan_result

let update (effects: Effects) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg, model.Phase with
    | Plan_result (Ok plan), Planning ->
        { model with Phase = Confirming (plan, preTickedHashes plan) }, Cmd.none

    | Plan_result (Error e), Planning ->
        { model with Phase = PlanFailed e }, Cmd.none

    | Toggle hash, Confirming (plan, ticked) ->
        let ticked' =
            if ticked.Contains hash then Set.remove hash ticked else Set.add hash ticked
        { model with Phase = Confirming (plan, ticked') }, Cmd.none

    | Confirm, Confirming (plan, ticked) ->
        let acknowledged = acknowledgedHashes plan ticked
        { model with Phase = Removing acknowledged },
        Cmd.OfAsync.perform effects.Remove (model.Target, acknowledged) Removal_result

    | Removal_result outcome, Removing _ ->
        { model with Phase = Finished outcome }, Cmd.none

    | Retry, Finished outcome when outcome.Failure.IsSome ->
        { model with Phase = Planning }, Cmd.OfAsync.perform effects.Plan model.Target Plan_result

    | _ -> model, Cmd.none

// ── View ──

let private caseBanner (plan: RemovalPlan) : string =
    match plan.Case with
    | TorrentsPresent ->
        let n = List.length plan.Torrents
        let torrentWord = if n = 1 then "torrent" else "torrents"
        let seedWord = if n = 1 then "seeds" else "seed"
        $"{n} {torrentWord} still {seedWord} these files."
    | FilesOnly ->
        "No torrent seeds these files any more — only the Jellyfin item and its files will be deleted."
    | AlreadyGoneInJellyfin ->
        "This item is already gone from Jellyfin — nothing left to remove."
    | PathOutsideMountMap ->
        $"{plan.Path} is outside the configured mount map — Mediatheca can't resolve it to a local path."

let private torrentRow (dispatch: Msg -> unit) (ticked: Set<string>) (t: PlannedTorrent) : ReactElement =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-3 flex items-start gap-3")
        prop.children [
            Daisy.checkbox [
                checkbox.sm
                prop.isChecked (ticked.Contains t.Hash)
                prop.onChange (fun (_: bool) -> dispatch (Toggle t.Hash))
            ]
            Html.div [
                prop.className "flex-1 min-w-0 space-y-1.5"
                prop.children [
                    Html.p [ prop.className "font-semibold text-sm truncate"; prop.text t.Name ]
                    Html.div [
                        prop.className "flex flex-wrap items-center gap-3 text-xs text-base-content/70 font-mono"
                        prop.children [
                            Html.span [ prop.text $"ratio %.2f{t.Ratio}" ]
                            Html.span [ prop.text $"seeding %.1f{t.SeedingTimeDays} d" ]
                        ]
                    ]
                    match t.Risk with
                    | HitAndRun reason ->
                        Html.p [ prop.className "text-xs text-warning"; prop.text reason ]
                    | Safe -> ()
                    match t.Match with
                    | PackAncestor extra ->
                        let plural = if extra = 1 then "" else "s"
                        Daisy.alert [
                            alert.warning
                            prop.className "py-1.5 px-3 text-xs font-bold mt-1"
                            prop.text $"Also contains {extra} other file{plural} — deleting this torrent removes them too."
                        ]
                    | ExactOrInside -> ()
                ]
            ]
        ]
    ]

let private confirmingContent (dispatch: Msg -> unit) (plan: RemovalPlan) (ticked: Set<string>) : ReactElement list =
    [
        if plan.Path <> "" then
            Html.p [ prop.className "text-xs font-mono text-base-content/70 break-all"; prop.text plan.Path ]
        Html.p [ prop.className "text-sm text-base-content/70"; prop.text (caseBanner plan) ]
        match plan.Case with
        | TorrentsPresent ->
            Html.div [
                prop.className "space-y-2 mt-3"
                prop.children (plan.Torrents |> List.map (torrentRow dispatch ticked))
            ]
        | FilesOnly
        | AlreadyGoneInJellyfin
        | PathOutsideMountMap -> ()
    ]

let private confirmingFooter
    (dispatch: Msg -> unit)
    (onClose: unit -> unit)
    (plan: RemovalPlan)
    (ticked: Set<string>)
    : ReactElement list =
    [
        Daisy.button.button [ button.ghost; prop.onClick (fun _ -> onClose ()); prop.text "Cancel" ]
        match plan.Case with
        | AlreadyGoneInJellyfin
        | PathOutsideMountMap -> ()
        | TorrentsPresent
        | FilesOnly ->
            Daisy.button.button [
                button.error
                prop.disabled (not (canRemove plan ticked))
                prop.onClick (fun _ -> dispatch Confirm)
                prop.text "Remove"
            ]
    ]

let private planningContent () : ReactElement list =
    [
        Html.div [
            prop.className "flex items-center justify-center py-8"
            prop.children [ Daisy.loading [ loading.spinner; loading.lg ] ]
        ]
    ]

let private planFailedContent (message: string) : ReactElement list =
    [ Daisy.alert [ alert.error; prop.text message ] ]

let private planFailedFooter (onClose: unit -> unit) : ReactElement list =
    [ Daisy.button.button [ button.ghost; prop.onClick (fun _ -> onClose ()); prop.text "Close" ] ]

let private outcomeRowView (row: OutcomeRow) : ReactElement =
    match row.Status with
    | Row_completed ->
        Html.div [
            prop.className "flex items-center gap-2 text-sm"
            prop.children [
                Html.span [ prop.className "text-success"; prop.text "✓" ]
                Html.span [ prop.text row.Label ]
            ]
        ]
    | Row_failed reason ->
        Html.div [
            prop.className "space-y-1"
            prop.children [
                Html.div [
                    prop.className "flex items-center gap-2 text-sm"
                    prop.children [
                        Html.span [ prop.className "text-error"; prop.text "✕" ]
                        Html.span [ prop.className "font-semibold"; prop.text row.Label ]
                    ]
                ]
                Html.p [ prop.className "text-xs text-base-content/70 pl-6"; prop.text reason ]
            ]
        ]

let private finishedContent (outcome: RemovalOutcome) : ReactElement list =
    [
        Html.div [
            prop.className "space-y-2"
            prop.children (outcomeRows outcome |> List.map outcomeRowView)
        ]
    ]

let private finishedFooter
    (dispatch: Msg -> unit)
    (onDone: unit -> unit)
    (outcome: RemovalOutcome)
    : ReactElement list =
    match outcome.Failure with
    | Some _ ->
        [ Daisy.button.button [ button.error; prop.onClick (fun _ -> dispatch Retry); prop.text "Try again" ] ]
    | None ->
        [ Daisy.button.button [ button.primary; prop.onClick (fun _ -> onDone ()); prop.text "Done" ] ]

/// `onClose` cancels/dismisses without acting; `onDone` fires only from the
/// success outcome's Done button and reloads the page's detail DTO
/// (integration-mqsd3 What — "reload, don't patch").
let view (model: Model) (dispatch: Msg -> unit) (onClose: unit -> unit) (onDone: unit -> unit) : ReactElement =
    match model.Phase with
    | Planning
    | Removing _ ->
        ModalPanel.view "Remove local copy" onClose (planningContent ())
    | PlanFailed message ->
        ModalPanel.viewWithFooter "Remove local copy" onClose (planFailedContent message) (planFailedFooter onClose)
    | Confirming (plan, ticked) ->
        ModalPanel.viewWithFooter
            "Remove local copy"
            onClose
            (confirmingContent dispatch plan ticked)
            (confirmingFooter dispatch onClose plan ticked)
    | Finished outcome ->
        ModalPanel.viewWithFooter
            "Remove local copy"
            onClose
            (finishedContent outcome)
            (finishedFooter dispatch onDone outcome)
