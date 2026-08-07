---
id: integration-hebjs
title: One-click Steam Family import — automatic access-token acquisition
status: doing
type: feature
context: integration
created: 2026-07-20
completed:
depends_on: [integration-ygwsa, design-system-001]
blocks: []
tags: [steam, steam-family, auth, token, settings, import]
related_adrs: [0011, 0019]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
prior_art: []
---

## Why

Importing the Steam Family shared library currently demands a manual DevTools
ritual (log in → Network tab → Family page → copy `access_token`) every ~1 hour
of token lifetime. The import itself is one button; acquiring the token is six
manual steps. The user wants: click a button, library imports.

## What

Replace the paste-a-token flow in Settings → Steam Family with a **one-time
"Connect Steam" setup** (QR code scanned with the Steam mobile app, or
credentials + Steam Guard — final shape per the integration-ygwsa spike outcome).
The server stores the resulting long-lived refresh token (SettingsStore) and
mints short-lived access tokens on demand for family-member discovery and
shared-library import. Token expiry stops being user-facing entirely; the family
import becomes genuinely one-click and is then eligible to join the scheduled
sync cadence later.

## Builder gate — outcome: **PASS** (2026-08-07, run live by the builder)

The ADR-0019 empirical check ran against the real Steam network with the builder's own
account and Family Group:

1. `login.fsx` — QR login via SteamKit2 3.1.0 (`PlatformType = MobileApp`,
   `IsPersistentSession = true`), scanned with the Steam mobile app; refresh token
   (498 chars) persisted to the gitignored `refresh-token.local.txt`.
2. `refresh-and-call.fsx` — `IAuthenticationService/GenerateAccessTokenForApp/v1` via
   **plain HTTP POST** (no SteamKit2, no CM connection): HTTP 200; minted access token's
   JWT carries `aud: ["web","mobile"]`, expiry ≈ 24h from mint.
3. `IFamilyGroupsService/GetFamilyGroupForUser` with the minted token: **HTTP 200 with
   real family data** (family_groupid returned, role, membership history) — the minted
   token carries exactly the audience/scope the family endpoints require.

**Conclusion:** the SteamKit2 QR-login + HTTP-refresh path is confirmed end-to-end. The
acceptance criteria below stand as written; the browser-retrieval fallback (ADR-0019
point 4) is not needed.

Implementation intel from the gate run (worker: read before building):

- SteamKit2 3.1.0 API deltas vs. the harness as originally written (all fixed in
  `spikes/steam-family-token-spike/`, which is now a proven-live reference):
  `CallbackManager` is not `IDisposable`; `QrAuthSession.ChallengeURLChanged` is an
  `Action` **property** (assign with `<-`), not an event; the enum is
  `SteamKit2.Internal.EAuthTokenPlatformType`.
- `GenerateAccessTokenForApp` **requires a `steamid` parameter** alongside
  `refresh_token` — recoverable from the refresh token itself (JWT `sub` claim), so
  nothing extra needs persisting.
- The QR challenge URL rotates roughly every 30 s (`ChallengeURLChanged` fires); the
  Settings UI must re-render the QR on each rotation. The challenge URL is a QR payload
  for the mobile app's scanner — opening it in a desktop browser lands on Steam's
  install page, so it must be shown as a scannable QR image, never as a link.

## Acceptance criteria

- [ ] Settings → Steam Family offers a "Connect Steam" one-time QR flow (QR rendered
      in Settings, polled to completion); the "How to get the access token" DevTools
      instruction block is demoted to a manual-fallback footnote.
- [ ] The refresh token persists in SettingsStore; access tokens are minted on demand
      through the already-shipped `Steam.withTokenRefresh` seam
      (`src/Server/Steam.fs`, tests in `SteamFamilyTokenTests.fs`) — the task wires
      real `mint`/`persist` lambdas into it, it does not re-implement retry logic.
- [ ] After connecting once, "Discover Family Members" and "Import shared library"
      work with no manual token step — including more than an hour later
      (server auto-mints a fresh access token per call or on 401).
- [ ] An expired/revoked refresh token surfaces a clear "reconnect Steam" prompt
      in Settings (mirroring the ADR-0011 Jellyfin re-auth pattern), never a
      silent failure.
- [ ] A manually pasted access token still works as the fallback path (decided
      2026-08-04: keep it — it is also the contingency if Valve invalidates the
      mint path server-side).

## Notes

- **Refined 2026-08-07:** builder gate run live and **passed** (see above) — task
  promoted to todo. The spike harness fixes made during the gate run are committed
  under this task's trailer.
- **Refined 2026-08-04:** the two open questions are settled — (1) manual paste stays
  as a fallback footnote, not removed; (2) scheduled family-library sync is **out of
  scope** — family import stays manual-trigger-only in this task; auto-sync becomes
  its own capture once one-click import has proven itself.
- SteamKit2 is now confirmed as a server dependency for this task — but **only** for
  the one-time QR login flow (ADR-0019 point 2: the ongoing refresh stays a plain HTTP
  POST, proven live by the gate; no CM connection at refresh time).
- A live refresh token for the builder's account currently sits in
  `spikes/steam-family-token-spike/refresh-token.local.txt` (gitignored) — the worker
  can reuse it to test the refresh path without another QR ceremony.
- Frontend-bearing → gated on design-system-001 (styleguide, done) per the BC
  README's frontend gate.
- UI surface today: `steamFamilyDetail` in `src/Client/Pages/Settings/Views.fs`;
  token save path `Save_steam_family_token` → SettingsStore.
- Security note (from the spike): the refresh token is a powerful ~1-year credential;
  it lives in SettingsStore/SQLite in the data dir — acceptable for a single-user,
  self-hosted app (ADR-0007), but say so in the Settings UI copy.
