---
id: 0019
title: Steam Family token refresh — pure mint-and-retry seam shipped, live audience/scope verification deferred to integration-hebjs
scope: integration
status: accepted
date: 2026-07-20
related_tasks: [integration-ygwsa, integration-hebjs, integration-p2hxn]
---

# ADR 0019: Steam Family token refresh — pure mint-and-retry seam shipped, live audience/scope verification deferred

## Context

The Steam Family shared-library import (`Steam.getFamilyGroupForUser`, `getSharedLibraryApps`,
`getFamilyGroup` in `src/Server/Steam.fs`) needs a web `access_token` that today the user
scrapes by hand from Chrome DevTools every ~1 hour. integration-ygwsa was a spike to prove
(or disprove) that the server can mint its own family-scope access tokens from a stored
Steam refresh token obtained via a one-time interactive login (SteamKit2 QR or
credentials+Steam Guard), eliminating the DevTools ritual — the same shape as ADR-0011
(Jellyfin: stored long-lived credential → adapter self-mints short-lived tokens, expiry
never user-facing).

This spike could not obtain a real Steam refresh token: minting one requires a human to
scan a QR code with the Steam mobile app against a live terminal session (or supply
credentials + a live Steam Guard code), and no throwaway Steam account with an active
Family Group was available in this environment. Per the spike task's own instructions,
the fallback was to build and unit-test the token-minting *seam* with injected fakes, and
document the unverified live path honestly rather than imply it was exercised.

A companion research pass (`.agentheim/knowledge/research/steam-family-api-auto-token-refresh-2026-07-20.md`)
answered the surrounding technical questions from public sources, but could **not** answer
the one decision-critical question: whether a token minted this way actually carries the
audience/scope `IFamilyGroupsService` requires, or whether that interface specifically
requires the browser's own JS-minted token. No public source documents anyone succeeding
or failing at this specific call.

## Decision

1. **Ship the pure orchestration seam now, ADR-0011-shaped.** `Steam.withTokenRefresh`
   (`src/Server/Steam.fs`) mirrors `Jellyfin.withReauthRetry` exactly: run a token-consuming
   `fetch` once; on `Error Rejected`, call an injected `mint: unit -> Async<Result<string,
   string>>` exactly once; on success, `persist` the fresh token and retry `fetch` exactly
   once; a second rejection, a failed mint, or any non-auth failure returns a clear `Error`
   and never loops. Fully unit-tested with plain lambdas
   (`tests/Server.Tests/SteamFamilyTokenTests.fs`) — no HTTP, no SteamKit2, no SQLite. This is
   what integration-hebjs should wire the real `mint`/`persist` lambdas into.

2. **Do not add a SteamKit2 dependency to `Server.fsproj` yet.** The one-time interactive
   login (QR/credentials) is the only place SteamKit2's actual value lives; the ongoing
   token *refresh* can — per research — be a plain HTTP POST to
   `IAuthenticationService/GenerateAccessTokenForApp/v1`, no different in shape from the
   rest of `Steam.fs`'s `HttpClient`-only adapter style, **provided the refresh token was
   obtained with `AuthSessionDetails.PlatformType = MobileApp` and `IsPersistentSession =
   true`** (a `SteamClient`-platform token needs an authenticated CM connection to refresh
   as of an April 2025 Steam-side change — that would force a permanent SteamKit2 + live-CM
   dependency into the server, which we want to avoid). **This platform choice is why the
   resulting login session reads as "a `MobileApp`-platform session signing in from a
   datacenter IP" — an accepted, currently-unfixable-under-this-decision risk; see ADR-0067
   before proposing to reverse it.** A throwaway harness reflecting this
   shape lives in `spikes/steam-family-token-spike/` (`login.fsx`, `refresh-and-call.fsx`) —
   **UNEXECUTED**, written to the documented API but never run against the real Steam
   network. SteamKit2 only becomes a real `Server.fsproj` dependency if integration-hebjs's
   empirical check (below) succeeds and a one-time-login UI flow is built into Settings.

3. **integration-hebjs's first ~30 minutes must be the empirical audience/scope check**,
   not implementation. Concretely: QR-login with `PlatformType=MobileApp`, mint via
   `GenerateAccessTokenForApp`, call `GetFamilyGroupForUser`, and compare against the
   known-good browser-scraped token currently in Settings. This single test fully resolves
   the open question before any further implementation investment. If it fails (401/403
   where the browser token succeeds), fall back to the semi-automated browser-retrieval
   path evaluated below.

4. **Fallback evaluated, not built:** semi-automated browser retrieval — driving a
   logged-in Chrome session (Chrome DevTools MCP, or Playwright) to repeat the exact manual
   DevTools ritual (visit the Family page, filter network requests for
   `IFamilyGroupsService`, extract `access_token=`) — remains viable if the refresh-token
   approach's audience check fails. It trades "no browser dependency" for "no unverified
   Steam-side audience assumption," and is strictly less invasive to `Server.fsproj` (no
   SteamKit2 dependency at all, still needs a browser profile signed into Steam). Community
   precedent (Chachigo's `FamilyBot`) uses exactly this approach and reports it as the only
   one that works for them — weak but real signal in its favor as a fallback. **This is now formally escalation-ladder step 2 in ADR-0067** (the accepted-risk ADR for the MobileApp-from-datacenter-IP login signature this platform choice produces) — still evaluated, not built; do not pre-spike it without a trigger named there.

## Consequences

- integration-hebjs is unblocked to *start* (the seam it needs is shipped and tested) but
  is explicitly not unblocked to *assume* the refresh-token approach works — its first
  action is the empirical test in point 3, not building UI.
- `Steam.fs` gained no new runtime dependency (`FamilyFetchError`, `TokenMinter`,
  `withTokenRefresh` are pure F# with no SteamKit2/HTTP inside them) — safe to ship
  independent of whether the live approach pans out.
- Refresh-token security: per research, a persistent-session refresh token is valid ~1
  year and Valve can invalidate it silently for undocumented reasons (suspected IP
  changes) — the production shape must plan for a "re-authentication required" surfaced
  state (mirroring `Jellyfin.withReauthRetry`'s missing-credentials case), not assume the
  token is good until its stated expiry. Storing a refresh token means storing a powerful,
  long-lived credential — it belongs in `SettingsStore`/SQLite in the data dir like the
  existing `steam_family_token`, `jellyfin_access_token`, etc.; acceptable because the app
  is single-user, loopback/self-hosted (ADR-0007).
- If integration-hebjs's empirical check fails, `Steam.withTokenRefresh` is still reusable
  for the browser-retrieval fallback (its `mint` lambda just becomes "drive Chrome DevTools
  MCP through the manual ritual" instead of "call `GenerateAccessTokenForApp`") — the seam
  doesn't assume which minting mechanism is behind it.
