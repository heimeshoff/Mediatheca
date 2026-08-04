---
id: 0055
title: Game genres stays an event-carried identity-card projection column — the games-v4nqe cache-cutover attempt is reverted
scope: games
status: accepted
date: 2026-08-04
supersedes: []
superseded_by: []
amends: [0043]
related_tasks: [games-v4nqe, games-a7dqx]
related_research: []
---

# ADR 0055: Game genres stays an event-carried identity-card projection column — the games-v4nqe cache-cutover attempt is reverted

## Context

`games-v4nqe`'s task file (its "Event disposition table", `Game_categorized` row) instructed dropping
`game_list.genres`/`game_detail.genres` and re-sourcing `Genres` from `game_metadata_cache.genres` —
reasoning that `Game_categorized`/`Categorize_game` is dead code (zero live call sites) and that
`game_metadata_cache.genres` already shipped, unpopulated, in `games-a7dqx`. Iteration 1 of the task
executed exactly that: seeded the cache column from the projection once, dropped both projection
columns, and switched `getAll`/`getBySlug`/`getRecentlyAddedGames`/`getGameGenreDistribution` to read
`mc.genres`.

A fresh-eyes verifier failed that iteration. `related_adrs: [0043]` was in the task's own frontmatter,
and ADR-0043's classification table states, verbatim: "`name`, `year`, `poster_ref`/`cover_ref`,
**`genres`** on Movie/Series/**Game** | **Cache — projection column, event-carried** | Rides in the
`*_added_to_library` snapshot event; replay reproduces it deterministically. Passes the identity-card
clause." The diff contradicted its own related ADR with no amending ADR written. Two further facts
made the contradiction substantive, not merely procedural:

1. **No repopulation path survives the drop.** The one-time copy migration's source column
   (`game_detail.genres`) is deleted by the very migration sequence that runs it — a projection
   rebuild after that point has nothing to seed the cache from, and a wipe-first event-log import
   (ADR-0038, `administration-z6ymt`'s job, which `depends_on` this task) discards and replays the
   event log without ever touching `game_metadata_cache` at all.
2. **Genres genuinely has no ongoing refresh path in this codebase.** Every one of the 18 converted
   Steam emission sites calls `updateGameIdentityCache conn slug ... None` for the genres slot — RAWG
   genre search (the only source `GameAddedData.Genres` is ever populated from) runs exactly once, at
   creation time, in the "no existing/no-name-match" branch of each import flow. No Steam refresh, no
   `attachSteamToGameCore` fill-if-empty pass, and no enrichment loop ever re-derives it. This is
   confirmed by reading `Api.fs`'s and `PlaytimeTracker.fs`'s creation flows directly (`r.Genres` from
   `Rawg.searchGames`, consumed once into `GameAddedData.Genres`) against every converted refresh call
   site (`updateGameIdentityCache`'s genres parameter, always `None`).

Fact 2 is the one that actually decides this ADR. ADR-0043's core test is re-derivability: "if the
fact can be re-fetched from its source at any time without loss, it is cache." Genres could
*hypothetically* be re-derived by calling RAWG's search API again, but nothing in this codebase does
that, and building a new ongoing RAWG-genre-refresh mechanism is exactly the kind of "while I'm here"
scope expansion a fix-up iteration must not invent just to make a cache move technically defensible.
Without a real refresh path, genres is indistinguishable from `name`/`year`/`poster_ref` — precisely
the identity-card exception ADR-0043 already carves out: "An externally-sourced field may remain a
projection column only if it is written exclusively by an event that carries it, and never by a
refresh path." Game genres satisfies that clause exactly as it always has.

The house precedent runs the same way: ADR-0048 lists Series `Genres` as an identity-card field that
"stays on `series_list`/`series_detail`, read directly, never joined" — the one difference from Games
is that Series' `Series_categorized` is a still-callable, live command (an operator can genuinely
re-categorize a series), while Games' `Categorize_game` was zero-call-site dead code even before this
task. That difference changes *how* the command gets demoted (Series never demoted `Series_categorized`
at all; Games' `Categorize_game` command is deleted, but the event stays on the codec with a no-op
`evolve`/`handleEvent` arm — the same four-part-rule treatment `Game_store_added`/`Game_store_removed`
already established as this codebase's precedent for genuinely dead commands). It does not change
where genres itself lives: identity-card, event-carried, on both BCs.

## Decision

**Game genres is reverted to an event-carried `game_list.genres`/`game_detail.genres` projection
column**, undoing games-v4nqe iteration 1's cache cutover for this one field only. Every other part of
that iteration's diff (description/short_description/website_url → cache, the seven other demoted
event groups, the `PlayFacets`/`PlayFacetsOverride` DTO cutover, the client's forced `PlayModePicker`
deletion) is unaffected and stays exactly as iteration 1 shipped it — this ADR narrows one column,
nothing else.

### What changed back

- `game_list`/`game_detail` regain a `genres TEXT NOT NULL DEFAULT '[]'` column (both in
  `CREATE TABLE IF NOT EXISTS` for a fresh install, and via a defensive `ALTER TABLE ... ADD COLUMN`
  for any database that already ran iteration 1's drop).
- `GameProjection.handleEvent`'s `Game_added_to_library` arm writes `genres` into both tables again,
  exactly as it did before `games-v4nqe` — the same JSON-encode-then-`INSERT OR REPLACE` shape every
  other identity-card field on this event already uses.
- `GameProjection.dropDeprecatedColumns` no longer names `genres` in either column list.
  `GameProjection.copyGenresToMetadataCache` (the now-pointless one-time copy) and its
  `Composition.buildApp` call site are deleted outright, not merely left unused.
- `getAll`/`getBySlug`/`getRecentlyAddedGames`/`getGameGenreDistribution` read `genres` straight off
  `game_list`/`game_detail` again, with no join to `game_metadata_cache` for this field.
- `MetadataCache.GameIdentityCard` drops its `Genres` field — the record now carries exactly
  `Description`/`ShortDescription`/`WebsiteUrl`, the three fields that *do* have a genuine ongoing
  refresh path (Steam's `AboutTheGame`/`ShortDescription`/`WebsiteUrl`, re-fetched on every sync).
  `tryGetGameIdentityCard`/`upsertGameIdentityCard` and every one of the (now four fewer) call sites in
  `Api.fs`/`PlaytimeTracker.fs` are narrowed to match.
- `game_metadata_cache.genres` (the column `games-a7dqx` shipped, unpopulated, anticipating this
  cutover) is **kept, not dropped** — dropping it needs its own migration and buys nothing a fresh ADR
  wouldn't immediately have to re-litigate if some future task ever does build a real genre-refresh
  mechanism. It is simply, permanently, unused: nothing reads or writes it as of this ADR.
- `Categorize_game`/`Game_categorized`'s disposition from the original task is otherwise unchanged:
  the command stays deleted, the event stays on the codec with a no-op `evolve`/`handleEvent` arm
  (four-part rule, `Game_store_added` precedent) — only the *comment* explaining why changes, from
  "genres now cache-derived" to "genres stays sourced exclusively from `Game_added_to_library`'s
  payload."

### Why not build a genuine genre-refresh mechanism instead (the other branch this fix-up considered)

Adding a routine RAWG-genre re-fetch to the 18 converted call sites — so that genres would genuinely
pass the re-derivability test and the original cache cutover could stand — was considered and rejected
for this fix-up. It would be new, non-trivial scope (a RAWG search call, rate-limiting, and a merge
policy against the identity-card writer, on every one of five structurally distinct import/refresh
flows) invented solely to retroactively justify a plan the codebase's own precedent (ADR-0043/0048)
already says isn't necessary. If a future task has an independent reason to keep Game genres fresh
against RAWG on an ongoing basis, it can propose that mechanism and amend this ADR then, with its own
acceptance criteria and tests — not as a byproduct of a verification fix-up.

## Alternatives considered

- **Keep the cache cutover, write an ADR asserting genres passes re-derivability anyway.** Rejected —
  the assertion would be false on inspection of this codebase's actual call sites (fact 2 above); an
  ADR that asserts a durability story without a real mechanism is exactly what the verifier's
  suggested-fix note warned against, and would fail re-verification for the same underlying reason.
- **Build a real ongoing RAWG-genre refresh, then keep the cache cutover.** Rejected as out of scope
  for a verification fix-up — see "Why not build a genuine genre-refresh mechanism instead" above.
- **Drop `game_metadata_cache.genres` now that nothing uses it.** Rejected — it is additive, inert,
  and harmless; dropping it is its own small migration for zero present benefit, and would need
  re-adding if a future task ever does build the refresh mechanism the previous alternative describes.
- **Leave `MetadataCache.GameIdentityCard.Genres` in place but always empty/ignored.** Rejected —
  a field nothing ever legitimately populates is a trap for the next reader of this code, who would
  reasonably assume writing it does something. Removing it is a more honest signal than deprecating it
  in place.

## Consequences

### Positive

- Restores compliance with ADR-0043's own classification table and the identity-card clause it
  defines, for the one field (`related_adrs: [0043]`) the original task's diff had drifted from.
- Genres is provably lossless across both failure modes ADR-0043's ADR-0012 retraction names: a
  projection rebuild (`Drop; Init; replay`) reproduces it from `Game_added_to_library`'s payload (a
  test added to `GameFacetProjectionTests.fs` exercises this directly), and a wipe-first event-log
  import (ADR-0038) reproduces it too, since the reimported log still carries the same
  `Game_added_to_library` events.
- `MetadataCache.GameIdentityCard` now names exactly the three fields (`Description`,
  `ShortDescription`, `WebsiteUrl`) that have a real, exercised refresh path — no field in that record
  is ever silently unwritten by every call site that constructs one.
- Matches the house precedent (ADR-0048's identity-card list) instead of contradicting it.

### Negative / accepted tradeoffs

- `game_metadata_cache.genres` is now permanently dead weight — an unpopulated, unread column that
  will sit there until some future task either uses it for a real purpose or drops it. Judged
  cheaper than a second migration to remove it now.
- This is the second iteration of `games-v4nqe`'s emission cutover; the task's own diff briefly
  contradicted itself on this one field for one verification cycle. Recorded here rather than papered
  over, per this project's standing preference for an honest trail over a clean-looking history.

### Neutral

- Every other part of `games-v4nqe`'s diff (description/short_description/website_url cache cutover,
  the seven other demoted event groups, `PlayFacets`/`PlayFacetsOverride`, the client picker deletion)
  is untouched by this ADR and needed no re-justification — the verifier's own note confirmed those
  parts "verified clean."

## References

- `src/Server/GameProjection.fs` — `createTables` (genres column + defensive `ALTER TABLE`),
  `dropDeprecatedColumns` (genres removed from both column lists), `handleEvent`'s
  `Game_added_to_library`/`Game_categorized` arms, `getAll`/`getBySlug`/`getRecentlyAddedGames`/
  `getGameGenreDistribution`.
- `src/Server/MetadataCache.fs` — `GameIdentityCard`, `tryGetGameIdentityCard`,
  `upsertGameIdentityCard` (all narrowed to three fields); the `genres` `ALTER TABLE ... ADD COLUMN`
  doc comment (kept, marked permanently unused).
- `src/Server/Api.fs` — `updateGameIdentityCache` (narrowed signature, four call sites updated), the
  three creation-path `MetadataCache.upsertGameIdentityCard` calls (`Genres` field removed).
- `src/Server/PlaytimeTracker.fs` — `createGameFromSteam`'s identity-card write (`Genres` field
  removed).
- `src/Server/Composition.fs` — `GameProjection.copyGenresToMetadataCache` call site deleted.
- `tests/Server.Tests/GameFacetProjectionTests.fs` — genres-stays-event-carried test (including a
  `Projection.rebuildProjection` round-trip), the narrowed identity-card writer tests, the narrowed
  column-drop migration test, the corrected demoted-events-replay assertion.
- `tests/Server.Tests/MetadataCacheTests.fs` — narrowed `upsertGameIdentityCard` call in the
  facet-writer slice-discipline test.
- ADR-0043 — the event-worthiness doctrine and identity-card clause this ADR amends the Game-genres
  application of, without changing the doctrine itself.
- ADR-0045 — the cache tier `game_metadata_cache` belongs to; unaffected by this ADR.
- ADR-0048 — the Series precedent this ADR now matches (`Genres` as an identity-card field).
- ADR-0053/0054 — the `PlayFacets` cache-derivation work this ADR does not touch.
- ADR-0038 — the wipe-first event-log import (`administration-z6ymt`) whose durability this ADR's
  Consequences section addresses for genres specifically.
- `games-v4nqe` — the task this ADR amends the diff of (iteration 2).
- `games-a7dqx` — shipped `game_metadata_cache.genres`, unpopulated, anticipating the cutover this
  ADR reverts.
