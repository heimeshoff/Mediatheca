module Mediatheca.Client.Pages.StreamDetail.Types

open Mediatheca.Shared

/// Compensating-event composer state (administration-xjmda, ADR-0032): the
/// "Append corrective event" action's own little workflow, nested under the
/// stream drill-in's `Model` — pick a real event type seen under this
/// stream's bounded context, clone its most recent payload as a starting
/// point, edit it, preview the canonicalized (round-tripped) form, then
/// confirm the append. `Preview` carries the `ExpectedPosition` the eventual
/// commit must still match (optimistic concurrency).
type ComposerState = {
    IsOpen: bool
    Types: string list
    TypesLoading: bool
    TypesError: string option
    SelectedType: string option
    /// The payload editor's current (operator-edited) text.
    Payload: string
    TemplateLoading: bool
    /// Set when the pre-fill was cloned from a sibling stream rather than
    /// this stream itself (`CompensatingEventTemplate.FromOtherStream`).
    TemplateFromOtherStream: bool
    PreviewLoading: bool
    Preview: CompensatingEventPreview option
    PreviewError: string option
    AppendInFlight: bool
    AppendError: string option
}

module ComposerState =
    let empty : ComposerState = {
        IsOpen = false
        Types = []
        TypesLoading = false
        TypesError = None
        SelectedType = None
        Payload = ""
        TemplateLoading = false
        TemplateFromOtherStream = false
        PreviewLoading = false
        Preview = None
        PreviewError = None
        AppendInFlight = false
        AppendError = None
    }

// Stream drill-in (administration-v4y9g): one event stream's full history,
// rendered through the same formatters as the event browser, plus what the
// matching projection currently says about it.
type Model = {
    StreamId: string
    Detail: StreamDetailDto option
    IsLoading: bool
    Error: string option
    /// Which entry's raw-JSON view is expanded (GlobalPosition), if any.
    ExpandedEntry: int64 option
    Composer: ComposerState
}

type Msg =
    | Load
    | Detail_loaded of StreamDetailDto
    | Load_failed of string
    | Toggle_raw of int64
    // Compensating-event composer (administration-xjmda, ADR-0032)
    | Open_composer
    | Close_composer
    | Composer_types_loaded of string list
    | Composer_types_failed of string
    | Select_event_type of string
    | Composer_template_loaded of CompensatingEventTemplate option
    | Composer_template_failed of string
    | Payload_edited of string
    | Request_preview
    | Preview_result of Result<CompensatingEventPreview, string>
    | Close_preview
    | Confirm_append
    | Append_result of Result<unit, string>
