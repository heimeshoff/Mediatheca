module Mediatheca.Client.Components.EditableDateInput

open Feliz
open Feliz.DaisyUI

/// Inline date editor that drafts changes locally and only commits on Enter or
/// blur-outside-the-input. Escape cancels (no API call, no toast).
///
/// Behaviour matches the spec in task 047:
/// - Native `<input type="date">` only fires `change` for fully-valid dates or
///   empty values, so we never see partial drafts. The `draft.Length = 10`
///   check is enough to validate.
/// - Picking from the calendar dropdown updates the draft visually but does
///   not auto-commit — Enter or blur is required.
/// - Blur back into the picker dropdown does not fire `onBlur` because the
///   dropdown lives in the input's shadow DOM and focus stays on the input.
///
/// Caller controls styling by passing extra DaisyUI input size classes (e.g.
/// `input-xs`, `input-sm`) and any other class names through `extraClasses`.
[<ReactComponent>]
let EditableDateInput
    (initial: string)
    (extraClasses: string)
    (onCommit: string -> unit)
    (onCancel: unit -> unit) =
    let trimmed = if initial.Length > 10 then initial.Substring(0, 10) else initial
    let draft, setDraft = React.useState trimmed
    let commitOrCancel () =
        if draft.Length = 10 && draft <> trimmed then onCommit draft
        else onCancel ()
    Daisy.input [
        prop.className extraClasses
        prop.type' "date"
        prop.autoFocus true
        prop.value draft
        prop.onChange (fun (v: string) -> setDraft v)
        prop.onKeyDown (fun e ->
            match e.key with
            | "Enter" ->
                e.preventDefault ()
                commitOrCancel ()
            | "Escape" ->
                e.preventDefault ()
                onCancel ()
            | _ -> ())
        prop.onBlur (fun _ -> commitOrCancel ())
    ]
