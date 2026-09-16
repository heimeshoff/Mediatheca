---
id: 0077
title: Book status changes carry an effective-on date — `Book_status_changed` / `Change_status` gain `effectiveOn: string option` so a Finished status can be backdated to the day a source says it became true (Goodreads `user_read_at`, an observation's own day); `finished_at` is a `yyyy-MM-dd` date string, and re-dating an already-Finished book is a legitimate event
scope: books
status: accepted
date: 2026-09-16
supersedes: []
superseded_by: []
amends: [0076]
related_tasks: [books-y9kxy, integration-wmqn3, integration-jjvg2, integration-y2ak4, intelligence-dnv2y]
related_research: [goodreads-reading-progress-and-book-metadata-sources-2026-09-16]
---

# ADR 0077: Book status changes carry an effective-on date (amends ADR-0076 §5)

## Context

ADR-0076 §5 keys a book's `finished_at` on the `Book_status_changed Finished` event's own timestamp,
the way `game_list.retired_at` works. The refinement pass (tactical-modeler, 2026-09-16) found that
this cannot serve the Goodreads shelf sync (`integration-wmqn3`): a book on the `read` shelf carries
`user_read_at`, the day the user actually finished it — often months before the sync first sees it.
Recording that book as "finished today" would put a book read in March on the dashboard's 7-day
"just finished" linger in September and mis-date every reading statistic. A command-only date would
not survive a projection rebuild, so the date has to ride the event.

The same pass found a second gap: under ADR-0076's literal "unless already Finished" rule a
correction of a wrong read date, or the close of a re-read, could never be recorded.

## Decision

1. **`Book_status_changed of status: BookStatus * effectiveOn: string option`** and
   **`Change_status of status: BookStatus * effectiveOn: string option`.** `effectiveOn` is the
   `yyyy-MM-dd` day the status became true according to a source that knows it — Goodreads'
   `user_read_at`, or a progress observation's own `ObservedOn` when the aggregate emits the status
   change itself. `None` means "the day this event was appended" (the event's local date).
2. **Only `Finished` is read.** The projection writes `book_list.finished_at =
   effectiveOn |> Option.defaultValue (event's local date)` on a `Finished` change and clears it when
   the status leaves `Finished`. `effectiveOn` is accepted for every status (a uniform shape) but no
   `abandoned_at` / `backlogged_at` column exists and none is to be added.
3. **`finished_at` is a date string, not a timestamp** (`yyyy-MM-dd`). The dashboard's 7-day linger
   and the Books tab's 90-day "recently finished" window compare dates
   (`finished_at >= date('now','-7 days')`), never parse a timestamp.
4. **Re-dating is a legitimate event.** `Change_status (status, effectiveOn)` is a no-op only when
   the status is unchanged **and** the effective date would not change:
   `status = current && (status <> Finished || effectiveOn = None || effectiveOn = currentFinishedOn)`.
   A `Change_status (Finished, Some d)` on an already-Finished book with a different `d` is recorded —
   this is how a corrected Goodreads read date, or a closed re-read, reaches the log.
5. **Aggregate-emitted status changes date themselves from the observation.** When `decide`
   promotes to `InFocus` or finishes on a `Reading_progress_observed`, the accompanying
   `Book_status_changed` carries `Some data.ObservedOn`, so an Audible sync that runs the morning
   after a late-night finish still dates the finish to the observation day the sync reports.
6. **`decide` validates the format only.** Rejecting a future date is an edge concern
   (`Api.fs` and the adapters), the same rule `ObservedOn` follows.

## Consequences

- `integration-wmqn3` maps `read` shelf items to `Change_status (Finished, Some user_read_at)` and
  can later re-date a book the Audible sync finished "today" (`integration-jjvg2` accepts that
  approximation and says so).
- `intelligence-dnv2y`'s linger and windows are date comparisons; no `DateTimeOffset` parsing.
- Event payloads gain one optional field; `Games`' `retired_at` timestamp precedent is deliberately
  not followed for books because books have an external source of truth for the finish day and
  games do not.
