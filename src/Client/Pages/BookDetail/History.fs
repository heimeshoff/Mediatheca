/// books-wk67x (amending ADR-0076 §2): pure, Feliz-free ordering seam for
/// the History list — the same `Progress.fs`/`Format.fs` split
/// (`SeriesDetail.NextUp`'s own precedent) keeps a page's non-trivial pure
/// logic unit-testable independently of the rendered page. History entries
/// are append-only now: several rows can share `(ObservedOn, Source)`, so
/// `Views.historySection`'s newest-first sort is real logic worth its own
/// seam and test, not a one-line inline `List.sortByDescending`.
module Mediatheca.Client.Pages.BookDetail.History

open Mediatheca.Shared

/// Newest first — by `ObservedOn`, then `EntryId` (append order) — so
/// several same-day entries render with the most-recently-recorded one
/// first, and every entry survives regardless of how many others share its
/// day and source.
let orderNewestFirst (history: ReadingProgressDto list) : ReadingProgressDto list =
    history |> List.sortByDescending (fun r -> r.ObservedOn, r.EntryId)
