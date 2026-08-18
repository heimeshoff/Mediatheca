---
id: 0069
title: Incremental Steam Family import diffs on findBySteamAppId, full re-enrich streams over a second SSE route, and the persisted last-result stays a separate model field from the session's fresh result
scope: integration
status: accepted
date: 2026-08-18
supersedes: []
superseded_by: []
related_tasks: [integration-n3vqa]
related_research: []
---

# ADR 0069: Incremental Steam Family import diffs on `findBySteamAppId`, full re-enrich streams over a second SSE route, and the persisted last-result stays a separate model field

## Context

integration-n3vqa turns the Steam Family import from "re-enrich every shared app on every
click" into "diff against what's known, enrich only the newcomers" — the fix for the
burst-enumerate-everything traffic shape Valve twice flagged the builder's account over
(ADR-0066 is the complementary fix for request *spacing*; this task owns request *count*).
The task's own Notes section already settled the conceptual questions (arrivals are cache,
not events, per ADR-0043; Settings-only surfacing; leave `GetSharedLibraryApps`'s flags
alone). Three wiring questions were left to the implementing worker.

## Decision

**1. `SteamFamilyImportMode = Incremental | FullReenrich`, threaded as a plain parameter
through `runSteamFamilyImport`.** The default click passes `Incremental`: an app is "known"
exactly when `GameProjection.findBySteamAppId` hits, and a known app's per-app branch skips
`Steam.getSteamStoreDetails` (and every identity-card/facet/release-date update downstream of
it) entirely, while still executing `Set_steam_library_date` and the family-owner commands.
The by-name-match sub-branch (a game with a matching title but no Steam appId link yet) is
*not* specially optimized — it falls under "new" by the task's own binary classification
(`findBySteamAppId` miss), so it keeps doing full enrichment exactly as it always has. This
is a narrow reading of an already-settled task decision, not a fresh one.

**2. Full re-enrich streams over a second SSE route (`/api/stream/reenrich-steam-family`),
not a blocking RPC.** `Api.fs`'s `steamFamilyImportHandler` and `Composition.fs`'s routing
both take the mode as a fixed parameter — two routes calling the same handler function with
different modes — rather than adding a `reEnrichSteamFamilyLibrary: unit ->
Async<Result<...>>` to `IMediathecaApi`. Rejected the blocking-RPC alternative: ADR-0066
derived a ~10-minute runtime for a full sweep of a several-hundred-title family library at
the 1500ms/request storefront ceiling; a synchronous Fable.Remoting call over that duration
has no progress feedback and risks a client/proxy timeout, while the existing SSE envelope
(`Sse.sseFrame`, `SteamFamilyImportProgress`) already solves exactly this for the default
import. Reusing it costs one more route registration and gives the rare, slow, explicit
action the same progress bar as the common, fast, default one.

**3. The persisted last-result is a separate Settings model field
(`SteamFamilyLastPersistedResult`), not a reload-time write into the same
`SteamFamilyImportResult` field a fresh completion sets.** The existing "Import Family
Library" button's visibility guard is `not IsImportingSteamFamily &&
SteamFamilyImportResult.IsNone` — pre-existing behavior this task must not regress. Writing
the persisted-on-reload result into that same field would permanently hide the primary
import button behind a stale "already done" state on every visit after the first import ever
completes, which is a materially worse regression than the feature is worth. Keeping the two
fields separate means: the primary button's visibility is unchanged: it hides only once
something completes *this session*; the "last import" panel (persisted result, arrivals
included) renders only when nothing has completed yet this session, and a fresh completion
(import or re-enrich) always supersedes it. The "Re-enrich all family games" secondary
button is deliberately *not* gated on `SteamFamilyImportResult.IsNone` at all — it stays
available even after a default import completes in the same session, since "now give me a
full refresh" is a legitimate immediate follow-up the primary button's one-shot-per-session
guard would otherwise block.

## Alternatives considered

- **A blocking `reEnrichSteamFamilyLibrary` RPC.** Rejected — see point 2: no progress
  feedback for a multi-minute operation, and duplicates infrastructure the SSE envelope
  already provides.
- **Reusing `SteamFamilyImportResult` for both the fresh-session and persisted-on-reload
  cases**, keying visibility off "has a result at all" instead of "has a result this
  session". Rejected — see point 3: this hides the primary action behind a permanently
  "already done" reload state once the family library has ever been imported once, which
  defeats the point of an *incremental*, repeatedly-clickable import.
- **Treating the by-name-match sub-branch as "known" too** (skipping enrichment for it in
  `Incremental` mode). Rejected — the task's Notes explicitly pin the classification to
  `findBySteamAppId` hits only; reopening it here would be scope creep into an
  already-settled question, not a wiring decision.

## Consequences

- Two SSE routes now call the same `steamFamilyImportHandler`/`runSteamFamilyImport` with a
  different `SteamFamilyImportMode` — a caller reading `Composition.fs`'s route table sees
  both actions' costs (cheap vs. ~10-minute) at a glance next to each other.
- `Settings/Types.fs` carries two Steam-Family-import-result-shaped fields
  (`SteamFamilyImportResult`, session-fresh; `SteamFamilyLastPersistedResult`,
  reload-persisted) rather than one — a small ongoing cost in exchange for never silently
  hiding the primary import action after a reload.
- `npm test`: 703 tests passing (7 added, `tests/Server.Tests/SteamFamilyIncrementalImportTests.fs`),
  no live Steam call in any of them. `npm run build` clean.

## References

- `.agentheim/contexts/integration/done/integration-n3vqa-incremental-family-import-whats-new.md`
- `.agentheim/knowledge/decisions/0043-event-worthiness-doctrine-observation-vs-third-party-cache.md` —
  arrivals are cache, not events.
- `.agentheim/knowledge/decisions/0066-steam-storefront-throttle-is-adapter-owned.md` — the
  complementary request-*spacing* fix, and the ~10-minute full-sweep runtime this ADR's
  point 2 reasons from.
- `src/Server/Api.fs` (`runSteamFamilyImport`, `steamFamilyImportHandler`,
  `SteamFamilyImportMode`), `src/Server/Composition.fs` (the two SSE routes),
  `src/Client/Pages/Settings/Types.fs`/`State.fs`/`Views.fs`.
