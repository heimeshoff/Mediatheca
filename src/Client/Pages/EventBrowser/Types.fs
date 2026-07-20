module Mediatheca.Client.Pages.EventBrowser.Types

open Mediatheca.Shared

type Model = {
    Events: EventDto list
    Streams: string list
    EventTypes: string list
    BoundedContexts: string list
    Search: string
    StreamFilter: string
    EventTypeFilter: string
    BoundedContextFilter: string
    /// yyyy-MM-dd, from an <input type="date">; "" = unset.
    TimestampFrom: string
    /// yyyy-MM-dd, from an <input type="date">; "" = unset.
    TimestampTo: string
    PageSize: int
    /// The "Before" cursor used to fetch the current page (None = first page).
    CurrentBefore: int64 option
    /// Cursors of ancestor pages, most-recently-visited first, so Prev_page can
    /// pop back to them. Keyset pagination going backward: the client remembers
    /// where it's been rather than the server exposing a second ("after")
    /// query direction.
    CursorStack: int64 option list
    HasMore: bool
    TotalMatches: int
    IsLoading: bool
    ExpandedEvent: int64 option
}

type Msg =
    | Load_filter_options
    | Filter_options_loaded of streams: string list * eventTypes: string list * boundedContexts: string list
    /// Fetch a page. `before`/`cursorStack` describe where this fetch lands in
    /// the pagination history; carried through to Page_loaded so the result can
    /// be applied even if filters changed no msgs are in flight concurrently.
    | Load_page of before: int64 option * cursorStack: int64 option list
    | Page_loaded of before: int64 option * cursorStack: int64 option list * page: EventPage
    | Search_changed of string
    | Stream_filter_changed of string
    | Event_type_filter_changed of string
    | Bounded_context_filter_changed of string
    | Timestamp_from_changed of string
    | Timestamp_to_changed of string
    | Page_size_changed of int
    | Next_page
    | Prev_page
    | Toggle_event_detail of int64
