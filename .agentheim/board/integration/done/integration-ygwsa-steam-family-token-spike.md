---
id: integration-ygwsa
title: Spike — mint Steam Family access tokens from a stored refresh token (SteamKit2)
status: done
type: spike
context: integration
created: 2026-07-20
completed: 2026-07-20
depends_on: []
blocks: [integration-hebjs]
tags: [steam, steam-family, auth, token, spike]
related_adrs: [0011, 0019]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
prior_art: []
---

## Why

The Steam Family shared-library import requires a web `access_token` that the user
currently scrapes by hand: log into store.steampowered.com, open Chrome DevTools →
Network tab, visit the Family page, filter for `IFamilyGroupsService`, copy the
`access_token=...` query value, paste it into Settings. The token expires within
~1 hour, so **every** import repeats the whole ritual. The user wants a one-click
(ideally fully automatic) import.

## What

Prove (or disprove) that the Mediatheca server can mint valid family-scope access
tokens itself, eliminating the DevTools ritual.

Known mechanism to verify: Steam's `IAuthenticationService` supports a one-time
interactive login — credentials + Steam Guard confirmation, or a QR code scanned
with the Steam mobile app — that yields a long-lived **refresh token**.
`IAuthenticationService/GenerateAccessTokenForApp` (with renewal allowed) can then
mint fresh access tokens from that refresh token on demand, no browser involved.
**SteamKit2** (C#/.NET, NuGet — usable directly from the F# server) implements
this flow (`BeginAuthSessionViaQR` / `BeginAuthSessionViaCredentials`); the node
`steam-session` library (DoctorMcKay) is a good reference implementation.

**Decision-critical unknown:** whether a token minted this way carries the right
audience/scope for the `IFamilyGroupsService` endpoints we call
(`GetFamilyGroupForUser`, `GetSharedLibraryApps`, `GetFamilyGroup` — see
`src/Server/Steam.fs`). Community family-library sync tools suggest yes; this
spike confirms it against the real API.

## Acceptance criteria

- [ ] A throwaway harness (F# script / console project, not production code)
      authenticates once interactively via SteamKit2 — QR flow preferred,
      credentials + Steam Guard acceptable — and persists the resulting refresh token.
      **NOT COMPLETED LIVE** — see Outcome. `spikes/steam-family-token-spike/login.fsx`
      is written to the documented SteamKit2 API but was never run: the QR step
      requires a human with the Steam mobile app at a live terminal, unavailable to
      this worker.
- [ ] From the stored refresh token alone (no browser, no manual step), the harness
      mints an access token and successfully calls `GetFamilyGroupForUser` and
      `GetSharedLibraryApps` for the user's real family group.
      **NOT COMPLETED LIVE** — blocked on the same missing credentials as above.
      `spikes/steam-family-token-spike/refresh-and-call.fsx` is written but
      unexecuted. This is exactly the decision-critical audience/scope question the
      spike set out to answer and could not, empirically.
- [x] Findings written up in this task's Notes (or a short report in
      `knowledge/research/`): refresh-token lifetime and renewal behavior, token
      audience requirements, whether the QR flow is embeddable in the Settings UI,
      and the recommended production shape for integration-hebjs.
      → `.agentheim/knowledge/research/steam-family-api-auto-token-refresh-2026-07-20.md`
      + ADR-0019 + Outcome below.
- [x] If the refresh-token approach fails, the write-up says why and evaluates the
      fallback: semi-automated browser retrieval (Chrome DevTools MCP / Playwright
      driving the logged-in browser to harvest the token), i.e. "LLM does the
      DevTools ritual".
      → ADR-0019 point 4 evaluates the fallback proactively (the approach's
      pass/fail status is itself unknown, not just "failed", so the fallback is
      documented as a contingency for integration-hebjs rather than as a settled
      final answer).

## Outcome

**Live end-to-end verification NOT achieved — no Steam credentials or QR-scanning
device were available to this worker.** What was delivered instead, per the task's own
fallback instructions:

1. **Research** (`.agentheim/knowledge/research/steam-family-api-auto-token-refresh-2026-07-20.md`):
   answered every surrounding technical question from public sources — SteamKit2's QR
   login API shape, why `PlatformType = MobileApp` (not the sample's default
   `SteamClient`) is required to refresh tokens over plain HTTP without a live CM
   connection (an April 2025 Steam-side change closed that path for `SteamClient`-platform
   tokens), refresh-token lifetime (~1 year, access tokens ~1 day, silent Valve-side
   invalidation is possible), and — critically — that **no public source confirms or
   denies** whether a token minted this way carries the audience/scope
   `IFamilyGroupsService` requires. The one real-world family-sync bot found in the wild
   (Chachigo's `FamilyBot`) uses only browser-scraped tokens, a weak negative signal.
2. **Throwaway harness** (`spikes/steam-family-token-spike/`): `login.fsx` (QR login,
   `PlatformType=MobileApp`, `IsPersistentSession=true`, persists a refresh token) and
   `refresh-and-call.fsx` (plain-HTTP `GenerateAccessTokenForApp` mint, then calls
   `GetFamilyGroupForUser` with the result). Written to the documented SteamKit2/Steam
   Web API shape but **never executed** — this is explicitly flagged in the harness's own
   README and file headers so it can't be mistaken for a verified path.
3. **Production-shape seam, built and unit-tested** (`src/Server/Steam.fs`:
   `FamilyFetchError`, `TokenMinter`, `Steam.withTokenRefresh`; tests in
   `tests/Server.Tests/SteamFamilyTokenTests.fs`, 5 cases): the same ADR-0011-shaped pure
   mint-and-retry orchestration `Jellyfin.withReauthRetry` uses, over injected lambdas —
   no HTTP, no SteamKit2, no SQLite. This is what integration-hebjs wires the real `mint`
   (either `GenerateAccessTokenForApp`, or the browser-retrieval fallback) and `persist`
   (SettingsStore) lambdas into. It does not depend on which minting mechanism wins.
4. **ADR-0019**: records the decision to ship the seam now without a SteamKit2 runtime
   dependency, and states plainly that integration-hebjs's *first* action must be the
   ~30-minute empirical audience/scope test (QR login → mint → `GetFamilyGroupForUser`,
   compared against the known-good browser token) before any further implementation
   investment — success or failure of that single test fully determines whether hebjs
   builds the SteamKit2 QR-login UI or the browser-retrieval fallback instead.

The spike is closed as "seam + knowledge delivered, live verification explicitly
deferred" rather than bounced, per the task's own guidance that this is a legitimate
spike outcome when real Steam credentials are unavailable.

Key files: `src/Server/Steam.fs` (seam), `tests/Server.Tests/SteamFamilyTokenTests.fs`
(tests), `spikes/steam-family-token-spike/` (unexecuted harness + README),
`.agentheim/knowledge/decisions/0019-steam-family-token-refresh-seam-pending-audience-verification.md`
(ADR), `.agentheim/knowledge/research/steam-family-api-auto-token-refresh-2026-07-20.md`
(research report), `.agentheim/contexts/integration/README.md` (ubiquitous language +
open question updated).

## Notes

- Current manual flow: `src/Client/Pages/Settings/Views.fs` `steamFamilyDetail`
  (collapsible "How to get the access token" instructions); token persisted via
  `SettingsStore`; consumed by `Steam.fs` `fetchJsonWithToken`.
- Pattern precedent: ADR-0011 — the Jellyfin adapter self-heals a rejected token
  from stored credentials (re-auth-and-retry). The end state here is the same
  shape: stored long-lived credential → adapter refreshes short-lived tokens
  itself, expiry never user-facing.
- Security note for the write-up: storing a refresh token is storing a powerful
  credential; note where it lives (SettingsStore/SQLite in the data dir) and that
  the app is single-user, loopback/self-hosted (ADR-0007).

## Verifier note (iteration 1)

**VERDICT: FAIL** — broken ADR cross-reference in three places.

**Reasons:** the write-up is ADR-0019, but `src/Server/Steam.fs:298`,
`tests/Server.Tests/SteamFamilyTokenTests.fs:11`, and
`spikes/steam-family-token-spike/README.md:18` pointed readers at ADR-0017 — a real but
unrelated ADR (the admin-console Remoting decision). Load-bearing for a spike whose sole
deliverable is knowledge transfer to `integration-hebjs`: the `Steam.fs` comment is the
primary in-code signpost that the live mint path is unverified, and the `spikes/README.md`
line is the sentence that names hebjs and tells it what to verify first.

**Suggested fix:** replace `ADR-0017` with `ADR-0019` at the three cited locations.

**Iteration hint:** likely-fixable.

**Provenance — this defect was conductor-introduced, not worker error.** The worker wrote
its ADR as 0018; the conductor renumbered it to 0019 to resolve a collision with a
concurrent sibling task, but its renumbering grep searched only for the string `0018` and
so missed these three `ADR-0017` pointers (which the worker had left from an earlier
numbering of its own). The conductor applied the three-token fix directly rather than
re-dispatching a worker, since the defect and its remedy were both bookkeeping it owns.
All other verifier checks passed at iteration 1: 296/296 tests, exactly-once retry policy
genuinely pinned, credential safety clean, honesty labeling genuine, knowledge transfer
decision-grade.
