---
id: 0054
title: The Steam category-id -> PlayFacets derivation table is fixed from 13 live-verified fixtures, with bare "Multi-player" resolving to CoopOnline
scope: games
status: accepted
date: 2026-08-04
supersedes: []
superseded_by: []
related_tasks: [games-a7dqx]
related_research: []
---

# ADR 0054: The Steam category-id -> PlayFacets derivation table is fixed from 13 live-verified fixtures, with bare "Multi-player" resolving to CoopOnline

## Context

ADR-0053 decided the shape of `PlayFacets`/`PlayFacetsOverride` and the merge
semantics, and the games-a7dqx task decision log fixed most of the id-to-facet
mapping in prose (decision 1's field comments, decision 2's umbrella-resolves-
to-online rule). Neither document pins down the literal numeric Steam category
ids or resolves one genuinely ambiguous case the prose didn't anticipate: what
does a bare "Multi-player" tag (Steam category id `1`) mean when it appears
*alone*, with no `Co-op`(`9`)/`PvP`(`49`)/`Cross-Platform Multiplayer`(`27`)/
`MMO`(`20`) signal at all? Decision 2 says "Co-op"/"Multi-player"/"PvP" all
resolve to "the online facet" when bare, but `PlayFacets` has no single
generic "online" field — only `CoopOnline` and `VersusOnline`, one of which
must be chosen.

`games-a7dqx`'s acceptance criteria required the table to be verified against
a live sample fetch, not shipped from an unverified guess (rule 9 in the
worker's task instructions). 13 well-known Steam appIds were fetched via
`https://store.steampowered.com/api/appdetails?appids=<id>&l=english` during
implementation and their `categories[].id` lists recorded and cross-checked
against how each title is actually known to play.

## Decision

`FacetDerivation.deriveFacets` (`src/Server/FacetDerivation.fs`) implements
the id table below, verified against the 13 live fixtures (full appId list
and category-id dumps live in that file's module doc comment and in
`FacetDerivationTests.fs`'s corresponding test cases):

- `2` -> `Solo`
- `9` (Co-op) + `39` (Shared/Split Screen Co-op), or `9` + `24` (Shared/Split
  Screen) -> `CoopCouch`
- `38` (Online Co-op) or `48` (LAN Co-op) -> `CoopOnline`
- `49` (PvP) + `37` (Shared/Split Screen PvP), or `49` + `24` -> `VersusCouch`
- `36` (Online PvP), `47` (LAN PvP), `27` (Cross-Platform Multiplayer), or
  `20` (MMO) -> `VersusOnline`
- `44` (Remote Play Together) -> `RemotePlayTogether` (ids `41`/`42`/`43` —
  Remote Play on Phone/Tablet/TV — are distinct and discarded per decision 3)
- `54` (VR Only) -> `VrOnly`; else `53` (VR Supported) or `31` (a broader "VR
  Support" tag, observed co-occurring with both `54` and `53` across
  different titles, never alone on a non-VR title in the sample) -> `VrSupported`;
  else `NoVr`
- Bare `9` (no `38`/`24`/`39`) -> `CoopOnline`; bare `49` (no `36`/`37`/`24`)
  -> `VersusOnline` — decision 2's umbrella-resolves-to-online rule
- **Bare `1` (Multi-player) alone — no `9`/`49`/`27`/`20`/`38`/`36` at all —
  resolves to `CoopOnline`.** This is the one case the source decision left
  unpinned. Chosen over `VersusOnline` or splitting the difference because:
  the Counter-Strike 2 fixture proves a genuinely competitive-only title
  still needs its own explicit versus signal (`27` in that case) to resolve
  `VersusOnline` — id `1` never appeared as the *only* multiplayer signal on
  any competitive title in the sample, whereas titles Steam only vaguely
  tags "Multi-player" trend cooperative/sandbox in practice. This is a
  judgment call on a small residual cohort (decision 2 estimated ~44 games
  total across all three bare-tag cases combined) — revisit if the live
  library shows it guessing wrong more often than not once games-j6wkr's UI
  makes it visible.

## Alternatives considered

- **Bare Multi-player resolves to VersusOnline instead.** Rejected: no
  fixture in the live sample showed a competitive-only title relying on the
  bare tag alone (Counter-Strike 2 has its own explicit `27`); defaulting to
  the less presumptuous "probably cooperative/sandbox" reading seemed safer
  for the ~44-game residual cohort.
- **A hardcoded id table copied from third-party Steam-category
  documentation, without a live fetch.** Rejected outright by the task's own
  acceptance criteria — several ids in circulation (e.g. two different VR
  ids, `31` and `53`, both observed live) are exactly the kind of detail an
  unverified table would get wrong.

## Consequences

### Positive

- The table is transcribed into `FacetDerivation.fs`'s doc comment
  (appId-by-appId, with the real-world behavior each fixture is checked
  against) and into `FacetDerivationTests.fs` as executable fixtures — so a
  future correction is a one-line table edit plus a fixture update, not an
  archaeology exercise.
- `steam_category_ids` is persisted alongside the derived facets
  (`game_metadata_cache.steam_category_ids`, JSON int array) specifically so
  a future fix to this table can re-derive every game's facets without a
  second Steam fetch.

### Negative / accepted tradeoff

- The bare-`Multi-player`-resolves-`CoopOnline` call is the one part of this
  table not directly traceable to a decision-log sentence — flagged here so
  it isn't mistaken for settled doctrine if it turns out wrong in practice.
