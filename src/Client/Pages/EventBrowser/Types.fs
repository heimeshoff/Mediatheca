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
    /// Follow mode (administration-mtf1f) — polls for events after
    /// `TailPosition` on a ~2s interval while true. Only meaningful (and only
    /// exposed in the UI) on the first/newest page: `CurrentBefore = None`.
    /// Navigating to an older page turns Follow off — prepending live rows
    /// while someone is reading page 3 of history would be a UX trap.
    Following: bool
    /// Bumped every time Follow is turned on or off. Each scheduled poll and
    /// its response carry the epoch that was current when they were
    /// scheduled; `update` discards any that don't match the current epoch.
    /// This is what stops a stale timer from resurrecting a cancelled follow
    /// loop after the toggle goes off or the user navigates away (ADR-0005,
    /// ADR-0023) — Elmish has no built-in `clearInterval`, so the guard has to
    /// live in the message itself.
    FollowEpoch: int
    /// Highest global_position seen for the current filter set on the first
    /// page — the "after" cursor for the next tail poll.
    TailPosition: int64 option
    /// Positions from the most recent tail batch, so their rows render with
    /// the arrival highlight. Replaced wholesale on every Tail_loaded — safe
    /// because the highlight animation is much shorter than the poll
    /// interval, so a stale mark is never visibly wrong.
    NewlyArrived: Set<int64>
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
    /// Turn Follow on or off.
    | Toggle_follow
    /// Self-rescheduling poll tick, guarded by `epoch` (see FollowEpoch doc).
    | Poll_tail of epoch: int
    | Tail_loaded of epoch: int * events: EventDto list
