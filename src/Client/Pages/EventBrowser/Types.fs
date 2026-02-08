module Mediatheca.Client.Pages.EventBrowser.Types

open Mediatheca.Shared

type Model = {
    Events: EventDto list
    Streams: string list
    EventTypes: string list
    StreamFilter: string
    EventTypeFilter: string
    IsLoading: bool
    ExpandedEvent: int64 option
}

type Msg =
    | Load_events
    | Events_loaded of EventDto list
    | Streams_loaded of string list
    | Event_types_loaded of string list
    | Stream_filter_changed of string
    | Event_type_filter_changed of string
    | Toggle_event_detail of int64
