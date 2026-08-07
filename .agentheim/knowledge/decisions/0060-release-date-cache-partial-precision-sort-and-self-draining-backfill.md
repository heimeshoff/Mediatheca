---
id: 0060
title: Release dates are cache-tier, own-cursor Steam facts — partial-precision dates sort by first-of-period, and the backfill re-polls only until a game is confirmed released
scope: games
status: accepted
date: 2026-08-07
supersedes: []
superseded_by: []
amends: []
related_tasks: [games-ev65k]
related_research: []
---

# ADR 0060: Release dates are cache-tier, own-cursor Steam facts — partial-precision dates sort by first-of-period, and the backfill re-polls only until a game is confirmed released

## Context

`games-ev65k` adds Steam release-date facts (`game_metadata_cache.release_date_raw`/
`release_date_parsed`/`coming_soon`) following the ADR-0043 (event-worthiness doctrine) and
ADR-0045 (typed per-BC cache tier) precedent `games-a7dqx`/`games-b8xnw` already established for
play facets and Deck-compatibility. The tier assignment itself was not a live question — a release
date is squarely a third party's re-fetchable description of the work, the same shape as the two
prior cutovers. Three judgment calls the task explicitly left to the worker, though, do warrant a
record, because a future maintainer extending this shape (or auditing why the Upcoming section
sorts the way it does) needs the reasoning, not just the code:

1. **Sorting semantics for partial-precision dates.** Steam sends four shapes: an exact date, a
   month-year, a bare year, and TBA phrasing. Only the first is unambiguous. A month-year or bare
   year still needs *some* day-of-month to become a sortable ISO string.
2. **The backfill's steady-state candidate query.** `games-a7dqx`/`games-b8xnw`'s backfills use a
   simple, permanent `WHERE fetched_at IS NULL` cursor — once fetched, a game never revisits that
   job again. A release date is explicitly the one fact in this family that *changes* while a game
   is upcoming (delays are common), so re-using that shape verbatim would freeze every upcoming
   game's date at whatever Steam said on first fetch.
3. **The definition of "unreleased" (`IsUnreleased`)**, shared verbatim by the list-card badge, the
   detail-page treatment, and the Upcoming section's filter — the task calls out that all three must
   agree, since divergence here would look like a bug ("why does the card say upcoming but the
   section doesn't list it?").

## Decision

### Partial-precision dates sort as the first of the period

A month-year string ("October 2026") parses to the **first day of that month** (`2026-10-01`); a
bare year ("2026") parses to **1 January** of that year (`2026-01-01`). Both choices are invisible
to the user — display always uses the raw Steam string, never the parsed value — and only affect
where a partial-precision date lands relative to same-month/same-year exact dates in the Upcoming
section's ascending sort. First-of-period was chosen over last-of-period (e.g. end of month) because
it is the more conservative "soonest it could plausibly be" reading, consistent with how a person
skimming "October 2026" would mentally slot it against a same-month exact date like "5 Oct, 2026" —
both belong in the same rough window, and first-of-period never sorts a fuzzy date *after* an exact
date in the same period by construction.

### The release-date backfill's cursor is not a permanent `fetched_at IS NULL` drain

`MetadataCache.findGamesNeedingReleaseDateBackfill`'s candidate query is:

```sql
WHERE gd.steam_app_id IS NOT NULL
  AND (
    mc.release_date_fetched_at IS NULL   -- initial pass, same shape as the other two backfills
    OR mc.coming_soon = 1                -- Steam still says "not out yet"
    OR mc.release_date_parsed IS NULL    -- unparseable/TBA — worth another try
    OR mc.release_date_parsed > date('now')  -- parsed date still in the future
  )
```

A game drops out of this cursor **permanently** only once it has been fetched at least once, is no
longer flagged `coming_soon`, and has a parsed date at or before today — the self-draining property
the task calls for. This deliberately diverges from `findGamesNeedingFacetBackfill`/
`findGamesNeedingDeckCompatBackfill`'s simpler "never fetched" shape: those two facts (play facets,
Deck-compat verdict) don't have a "this will change soon" window the way an unreleased game's
release date does. Re-fetching an unparseable-but-not-coming-soon row indefinitely was accepted as a
cheap cost (still throttled at 300ms/request, same as every other Steam-fetch backfill) rather than
adding a retry-limit column — if Steam's string for a given game is durably unparseable, the raw
string is still shown correctly on every surface (display never depends on the parsed value), so the
only cost of an indefinite retry is one extra HTTP call per scheduled run, not a user-visible defect.

### `IsUnreleased` is `ComingSoon OR (Parsed date is in the future)` — not "unparseable implies unreleased"

Computed once, server-side, in `GameProjection.readReleaseDateInfo` (never duplicated per-surface):

```fsharp
let isUnreleased =
    comingSoon || (parsed |> Option.exists (fun d -> d > today))
```

A game with an unparseable date (`Parsed = None`) and `ComingSoon = false` is **not** treated as
unreleased. This was a deliberate rejection of the more inclusive `comingSoon || parsed.IsNone ||
future`: in practice, Steam's `coming_soon` flag is the authoritative "not out yet" signal, and a
released game's exact-date string is reliably parseable by the four shapes `ReleaseDateParsing`
recognizes — an unparseable string on a released game is far more likely a shape the parser doesn't
yet handle than a genuine signal of upcoming status. Treating "unparseable" as "unreleased" would
have made every such parser gap silently mislabel an already-released game as upcoming across all
three surfaces (list-card badge, detail-page treatment, Upcoming section) — a much more visible and
confusing failure mode than a released game simply not showing a precise parsed date. The backfill's
candidate query (above) still treats an unparseable date as worth retrying, so the distinction is
deliberate: "worth re-fetching" and "should display as upcoming" are different questions with
different answers for this one case.

## Consequences

### Positive
- Gives the Upcoming section a stable, explainable sort order without needing day-level precision
  from Steam for every game.
- The backfill self-drains for the common case (a release date lands and stays in the past) while
  correctly never abandoning a genuinely upcoming game to a stale, possibly-delayed date.
- `IsUnreleased`'s single definition, computed once in `GameProjection`, keeps the badge/hero/section
  surfaces from ever disagreeing about which games count as upcoming.

### Negative / accepted tradeoff
- A durably-unparseable-but-released game's row gets re-fetched by the backfill indefinitely — an
  accepted, low-cost gap (see above) rather than added retry-limit machinery.
- First-of-period sorting means two games both displaying as "2026" (bare year) but with genuinely
  different actual release months will sort identically until a more precise date is fetched — an
  accepted precision loss inherent to Steam sending an imprecise string in the first place.

## Alternatives considered

- **Last-of-period sorting (end of month / 31 December)** — rejected: would sort a fuzzy date after
  same-period exact dates, the less intuitive reading of "this game could release any time in this
  window."
- **Reusing the simple permanent `fetched_at IS NULL` cursor shape from the other two backfills** —
  rejected: would freeze every upcoming game's release date at its first-ever fetch, defeating the
  task's entire "slipped dates correct themselves" purpose.
- **`IsUnreleased = comingSoon || parsed.IsNone || future`** — rejected: makes every parser gap on a
  released game silently mislabel it as upcoming across all three display surfaces; the backfill
  already treats unparseable dates as worth retrying without needing the display-facing flag to
  agree.

## References

- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md`
  — the doctrine placing release dates on the cache tier.
- `.agentheim/knowledge/decisions/0045-metadata-cache-tier-typed-per-bc-tables.md` — the cache tier
  and its hard constraint (no `ProjectionHandler` touches it), unchanged by this ADR.
- `.agentheim/knowledge/decisions/0053-game-play-facets-cache-derived-event-sourced-override.md`,
  `.agentheim/contexts/games/done/games-b8xnw-steam-deck-compat-readiness.md` — the two prior
  resumable-backfill precedents this task's job shape follows, and diverges from on the cursor
  question above.
- `src/Server/ReleaseDateParsing.fs`, `src/Server/MetadataCache.fs`
  (`findGamesNeedingReleaseDateBackfill`), `src/Server/GameProjection.fs` (`readReleaseDateInfo`),
  `src/Server/GameReleaseDateBackfill.fs` — the code this ADR describes.
