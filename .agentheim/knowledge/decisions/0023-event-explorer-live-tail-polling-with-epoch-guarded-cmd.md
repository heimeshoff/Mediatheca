---
id: 0023
title: Event explorer live tail polls via an epoch-guarded self-rescheduling Cmd, capped and filter-aware
scope: administration
status: accepted
date: 2026-07-20
supersedes: []
superseded_by: []
related_tasks: [administration-mtf1f]
related_research: []
---

# ADR 0023: Event explorer live tail polls via an epoch-guarded self-rescheduling Cmd, capped and filter-aware

## Context

administration-g5dfy (ADR-0020) shaped `EventFilter`/`EventPageQuery`/`EventPage`
and `EventStore.queryEventPage` explicitly so a live-tail "everything after
global position N matching these filters" query could extend the same
machinery rather than duplicate it. administration-mtf1f adds a Follow toggle
to the Events tab: while on, the client polls for new events every ~2s and
prepends them, respecting the active filters, with a subtle arrival highlight.

Two things needed a deliberate answer, not a default:

1. **Where does the polling loop live, and how does it stop?** The client is
   Elmish MVU (ADR-0005), which has no `clearInterval` primitive — a `setTimeout`
   started in a view or a `Cmd` loop with no cancellation guard leaks a timer
   that keeps polling after the toggle goes off or the user navigates away.
   This is exactly the failure the task's third acceptance criterion exists to
   catch.
2. **What happens to a user who has paged into history while Follow is on?**
   Prepending live rows onto a page someone is actively reading is a
   surprising UX trap.

## Decision

### Query: extend, don't duplicate

Verified the README's claim: `EventFilter` was indeed shaped so it's reusable
as-is. Added, without touching `EventFilter`:

- `EventTailQuery = { Filter: EventFilter; After: int64; Limit: int }` (Shared.fs).
- `EventStore.queryEventsAfter conn filter after limit` — ascending
  `global_position > @after`, capped at `limit`, ordered ascending.
- `IAdminApi.getEventsAfter: EventTailQuery -> Async<EventDto list>`, implemented
  in `Administration.fs` by resolving `BoundedContext` to a stream prefix
  exactly as `getEventPage` does.

`queryEventPage`'s inline filter-condition building (search/stream/event-type/
BC-prefix/timestamp-range) was extracted into a private
`EventStore.buildFilterConditions`, used by both `queryEventPage` and
`queryEventsAfter`. This is the one refactor made to existing code, and it's
the point of the exercise: without it, "reuse the filter" would mean copying
six `match` arms into a second function, silently forking behavior the first
time someone edits one copy and not the other.

`Limit` (client passes a constant 200) bounds a single poll response — the
caution in the task about a growing log under repeated polling is answered by
never asking for more than "since I last looked, capped," never a full
re-query of history.

### Client loop: epoch-guarded self-rescheduling `Cmd`, not `Elmish.Sub`

Chose a self-rescheduling `Cmd` (`Async.Sleep` + `Cmd.OfAsync`, then dispatch
the next `Poll_tail`) over `Elmish.Sub`/`Program.withSubscription`. This
codebase's `Program.mkProgram` wiring (`App.fs`) does not currently use
`withSubscription` anywhere, and per-page MVU modules are composed purely via
`Cmd.map` (see `Admin.State.init`) with no subscription plumbing threaded
through — introducing subscription wiring for one toggle on one page would be
a bigger structural change than the feature warrants. The `Cmd` primitives
used (`Async.Sleep`, `Cmd.OfAsync.perform`/`.either`) are the same ones every
other async flow in `EventBrowser.State` already uses.

The cancellation problem — "how do you stop a timer that already fired" — is
solved without a disposal handle at all: every scheduled poll and its response
carries the `FollowEpoch` that was current when it was scheduled (`Poll_tail
of epoch: int`, `Tail_loaded of epoch: int * events`). `Toggle_follow` bumps
`Model.FollowEpoch`. `update` checks the epoch on both `Poll_tail` (before
firing the fetch) and `Tail_loaded` (before applying the result and
rescheduling); a mismatch is a no-op. A timer or in-flight request from a
superseded epoch produces a message that lands in `update` and does nothing —
not a message that never arrives. This is simpler to reason about than a
mutable disposal token stored in the model (which Elmish's value-model
discourages) and needs no JS interop beyond what `Async.Sleep` already
compiles to.

**Revised at iteration 2** — the first cut of this ADR asserted that leaving
the page unmounts the Admin/EventBrowser component tree, so React stops
calling `dispatch` and a stale scheduled `Poll_tail` goes inert on its own.
That's wrong for this codebase's root MVU: `dispatch` is wired once at
`Program.mkProgram` and is never torn down per-page: it's the *view* that's
conditional on `Model.CurrentPage`, not the message loop. `Url_changed`
(`State.fs`) replaces only the destination page's own child model in the root
`Model`; every other page's branch — including the one the user is leaving —
is left completely untouched. `AdminModel`, and the `Following`/`FollowEpoch`
it carries, survives navigating away verbatim. Root routes `Admin_msg` to
`Pages.Admin.State.update` unconditionally (no `CurrentPage` guard), which
forwards to `EventBrowser.update` equally unconditionally, so a `Poll_tail`
scheduled before navigation still matches the retained epoch after the user
has left, still fires `getEventsAfter`, and still reschedules — an orphan 2s
poll with no natural death. This was caught by verification, not written
correctly the first time; see the **Navigation teardown** section below for
the actual fix landed at iteration 2.

The epoch check is still what stops the *loop* while the component is still
mounted (toggle off, or an in-flight response arriving after the toggle
flips) — that half was and remains correct.

### Navigation teardown (added at iteration 2)

`EventBrowser.State.stopFollowing` was `private`; the fix is to make it
reachable from outside the module and call it from the one place that
actually observes a page transition: root `Url_changed`. `Admin.State` gained
a one-line wrapper, `stopFollowing: Model -> Model`, that applies
`EventBrowser.State.stopFollowing` to `Model.AdminModel.EventBrowserModel`.
Root `Url_changed` (`State.fs`), right after computing the destination `page`
and before dispatching into the per-page `match`, checks `prevPage, page`:
leaving `Admin _` for anything else applies `Pages.Admin.State.stopFollowing`
to the retained `AdminModel`; staying within `Admin _` (tab switch) or
navigating between two non-Admin pages is a no-op.

This reuses the exact mechanism already proven correct for toggle-off and
pagination-away — bump `FollowEpoch`, turn `Following` off — rather than
inventing a second teardown path, and it covers the in-flight-request case
for free: bumping the epoch *immediately* on navigation means a
`getEventsAfter` response that was already in flight when the user left
arrives later carrying the *old* epoch, fails the existing `Tail_loaded`
guard, and is dropped without rescheduling — identical to how a toggle-off
mid-request already behaved. No new guard, no root-level message-dropping,
no second source of truth for "is Follow still allowed to run."

Considered and rejected: gating the `Admin_msg` branch in root `update` to
drop child messages whenever `CurrentPage` isn't `Admin _`. That also closes
the leak (a dropped `Tail_loaded` can't reschedule either), but it's a
coarser instrument — it would silently swallow *any* Admin-page message that
outlives navigation, not just Follow's, which matters once
administration-qjcp4 (rebuild-progress SSE) or another background Admin
process lands and legitimately wants to keep updating state the user will see
on return. Bumping the epoch at the source keeps the "what's allowed to keep
running after I leave" decision local to the one feature that actually needs
it.

### Follow is only available on the newest page

`Toggle_follow`'s UI (`followToggle` in `EventBrowser.Views`) is disabled
whenever `Model.CurrentBefore <> None` (i.e. the user has paged back into
history). `Next_page`/`Prev_page` unconditionally call `stopFollowing`,
bumping the epoch and turning `Following` off — so following never survives a
pagination action, in either direction, regardless of which page it lands on.
This directly answers the "prepending rows while someone reads page 3" trap:
it can't happen, because Follow cannot be on anywhere but page 1.

Filter changes are treated differently: they already reload the first page
(`Load_page (None, [])`), and Follow — if on — is left on, since a filter
change doesn't move the user away from the live edge, only changes what "live"
means. The next `Poll_tail` for that (unchanged) epoch reads the model's
filters fresh at fetch time, so the tail seamlessly follows the new filter.

### Bounded client-side state, not just a bounded query

Two additional caps beyond the per-request `Limit`:

- `TailPosition` (the "after" cursor) resets to the first page's newest
  `GlobalPosition` on every first-page load (filter change or explicit
  refresh), so restarting Follow after a filter edit never re-fetches events
  the new filter already showed.
- `Events` is truncated to `maxFollowedRows` (200) after each tail batch is
  prepended, so an hours-long Follow session doesn't grow the in-memory list
  without bound. `TotalMatches` is incremented by the count of genuinely new
  (deduped) events per batch rather than re-queried, since a live-tail poll
  intentionally never re-runs a full `COUNT(*)` — see "why not re-run the page
  query," below.

### Highlight animation reuses the existing keyframe vocabulary

Added `.animate-highlight` (`fade-in-up` entrance + a new `highlight-flash`
keyframe — a brief primary-tinted background fading to transparent) to
`index.css`, and `DesignSystem.animateHighlight`, alongside the existing
`animate-fade-in`/`animate-fade-in-up`/`animate-scale-in` trio rather than a
one-off bespoke keyframe. The class is applied via `Set.contains` against
`Model.NewlyArrived`, which is replaced wholesale (not accumulated) on every
`Tail_loaded` — safe because the animation (1.6s) is well under the poll
interval (2s), so a stale mark is never visibly wrong, and this avoids
needing a "clear highlight after N ms" message just to bound memory.

## Consequences

### Positive
- No new client-server query shape duplicated: `EventFilter` is reused
  verbatim, and the only new server-side code is the ascending counterpart to
  an existing, already-tested condition-builder.
- The stop-polling guarantee (acceptance criterion 3) has no failure mode that
  depends on remembering to call a disposal function — the epoch check is
  unconditional in `update`.
- No SSE/WebSocket infrastructure introduced (explicitly out of scope per the
  task's Notes; reserved for administration-qjcp4's rebuild-progress stream).

### Negative
- Polling still issues an HTTP request every ~2s while Follow is on, even
  when nothing has changed — cheaper than the old `queryEventPage` (bounded,
  indexed `global_position > @after` scan vs. a full filtered page query plus
  a `COUNT(*)`), but not free. Acceptable for a single-user admin tool.
- `TotalMatches` after tail events arrive is an estimate (`server total at
  last full page load` + `deduped tail arrivals since`), not a fresh
  server-side count — a filter that matches events indirectly affected by
  ones already counted (there are none today, since filters are all evaluated
  per-event) would not go stale, but this is a shortcut worth remembering if
  filter semantics ever become cross-event.

### Neutral
- `EventStore.buildFilterConditions` is now the single seam both queries share;
  a new filter dimension added to `EventFilter`/`QueryFilter` in the future
  needs exactly one new `match` arm, not two.

## Alternatives considered

- **`Elmish.Sub`/`Program.withSubscription`** — the textbook answer for a
  ticking background process in Elmish, and the ADR-0005 guidance explicitly
  flags it as an option. Rejected for this task specifically because no
  subscription wiring exists anywhere in this codebase's `Program.mkProgram`
  call or its per-page composition today; wiring it through `Admin.State` and
  `App.fs` for one toggle would be a larger structural change than a
  self-contained epoch-guarded `Cmd` loop, which uses primitives already
  idiomatic in this file. If a second live-updating surface appears
  (rebuild-progress SSE, administration-qjcp4, is the more likely candidate
  for that), revisit whether a shared `Sub`-based pattern is worth
  introducing then.
- **Raw `setTimeout`/`clearInterval` via JS interop** — rejected outright per
  the pre-loaded ADR-0005 guidance: this is precisely the shape of leak the
  acceptance criteria exist to prevent, and Elmish already gives a
  functional-state alternative (the epoch guard) that needs no interop.
- **Re-run `queryEventPage` (full filtered page + `COUNT`) on every tick
  instead of a dedicated ascending query** — rejected: doing so on every poll
  would re-scan/re-count the filtered set every ~2s regardless of whether
  anything changed, exactly the cost the task's caution calls out. The
  bounded `global_position > @after` query only touches rows that are
  actually new.
- **Keep Follow available on any page, and prepend/merge onto whatever page is
  showing** — rejected: this is the UX trap the task's caution names
  explicitly. Disabling Follow off the first page, and force-stopping it on
  pagination, is a small, explicit rule instead of a coordinate-merging
  problem (what does "prepend" even mean on page 3 of a `before`-cursor
  history?).

## References

- `src/Shared/Shared.fs` — `EventTailQuery`, `IAdminApi.getEventsAfter`.
- `src/Server/EventStore.fs` — `buildFilterConditions`, `queryEventsAfter`.
- `src/Server/Administration.fs` — `getEventsAfter`.
- `src/Client/Pages/EventBrowser/Types.fs` — `Model.Following`/`FollowEpoch`/
  `TailPosition`/`NewlyArrived`, `Msg.Toggle_follow`/`Poll_tail`/`Tail_loaded`.
- `src/Client/Pages/EventBrowser/State.fs` — `stopFollowing` (now public),
  `delayedPoll`, the `Toggle_follow`/`Poll_tail`/`Tail_loaded`/`Next_page`/
  `Prev_page` cases.
- `src/Client/Pages/EventBrowser/Views.fs` — `followToggle`, `eventRow`'s
  `isNewlyArrived` highlight.
- `src/Client/Pages/Admin/State.fs` — `stopFollowing` wrapper (iteration 2).
- `src/Client/State.fs` — `Url_changed`'s `prevPage, page` teardown check
  (iteration 2).
- `src/Client/index.css` — `highlight-flash` keyframe, `.animate-highlight`.
- `src/Client/DesignSystem.fs` — `animateHighlight`.
- `tests/Server.Tests/EventStoreTests.fs`, `tests/Server.Tests/AdministrationTests.fs`
  — `queryEventsAfter`/`getEventsAfter` coverage.
- ADR-0020 — the filter/pagination design this task extends.
- ADR-0005 — Elmish MVU, the subscription-vs-Cmd tradeoff this ADR resolves.
