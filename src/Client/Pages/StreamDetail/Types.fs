module Mediatheca.Client.Pages.StreamDetail.Types

open Mediatheca.Shared

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
}

type Msg =
    | Load
    | Detail_loaded of StreamDetailDto
    | Load_failed of string
    | Toggle_raw of int64
