---
id: integration-hebjs
title: One-click Steam Family import — automatic access-token acquisition
status: done
type: feature
context: integration
created: 2026-07-20
completed: 2026-08-07
depends_on: [integration-ygwsa, design-system-001]
blocks: []
tags: [steam, steam-family, auth, token, settings, import]
related_adrs: [0011, 0019, 0061]
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

- [x] Settings → Steam Family offers a "Connect Steam" one-time QR flow (QR rendered
      in Settings, polled to completion); the "How to get the access token" DevTools
      instruction block is demoted to a manual-fallback footnote.
- [x] The refresh token persists in SettingsStore; access tokens are minted on demand
      through the already-shipped `Steam.withTokenRefresh` seam
      (`src/Server/Steam.fs`, tests in `SteamFamilyTokenTests.fs`) — the task wires
      real `mint`/`persist` lambdas into it, it does not re-implement retry logic.
- [x] After connecting once, "Discover Family Members" and "Import shared library"
      work with no manual token step — including more than an hour later
      (server auto-mints a fresh access token per call or on 401).
- [x] An expired/revoked refresh token surfaces a clear "reconnect Steam" prompt
      in Settings (mirroring the ADR-0011 Jellyfin re-auth pattern), never a
      silent failure.
- [x] A manually pasted access token still works as the fallback path (decided
      2026-08-04: keep it — it is also the contingency if Valve invalidates the
      mint path server-side).

## Outcome

Shipped the production one-time "Connect Steam" QR login and wired
`Steam.withTokenRefresh` into the Family adapter's real code paths.

**Server:**
- `src/Server/Steam.fs` — `steamIdFromRefreshToken` (pure, decodes the JWT
  `sub` claim), `mintFamilyAccessToken` (plain HTTP POST to
  `IAuthenticationService/GenerateAccessTokenForApp`, no SteamKit2/CM
  connection), `fetchFamilyGroupForUser`/`fetchSharedLibraryApps`/
  `fetchFamilyGroupDetail` (401/403 → `Error Rejected` instead of throwing),
  and the wired self-healing `getFamilyGroupForUserWithRefresh`/
  `getSharedLibraryAppsWithRefresh`/`getFamilyGroupWithRefresh`.
- `src/Server/SteamConnect.fs` (new) — the SteamKit2 QR login ceremony
  (`startConnect`/`status`), in-memory session state only.
- `src/Server/Api.fs` — `runSteamFamilyImport` and `fetchSteamFamilyMembers`
  now use the refresh-aware fetchers with real `persist`
  (`SettingsStore` `steam_family_token`) wiring; new `steamConnectStreamHandler`
  SSE endpoint (`/api/stream/steam-connect`, registered in `Composition.fs`)
  and `getSteamConnectionStatus` RPC.
- `src/Server/Server.fsproj` — added `SteamKit2` 3.1.0 and `QRCoder` 1.6.0
  package references (SteamKit2 confined to `SteamConnect.fs`).
- Live-verified (not just unit-tested) against the real Steam network with
  the builder's refresh token: mint → family-group fetch, and the full
  invalid-token → `Rejected` → mint → persist → retry → success self-heal
  cycle both confirmed working end-to-end.

**Client:**
- `src/Client/Pages/Settings/Types.fs`/`State.fs` — `SteamConnected`/
  `IsConnectingSteam`/`SteamConnectQrDataUrl`/`SteamConnectError`/
  `SteamNeedsReconnect` state, SSE-stream-driven `Start_steam_connect` (same
  pattern as the existing Steam Family import SSE consumption), and
  `isReconnectRequired` substring-detection wired into both family-fetch and
  family-import result handlers.
- `src/Client/Pages/Settings/Views.fs` — "Connect Steam" QR UI (primary),
  a "Reconnect Steam" banner, and the DevTools instructions + manual paste
  input demoted into a collapsed "Manual token entry (fallback)" section.

**Decisions:** ADR-0061 (in-memory QR session + SSE polling, re-read-token-
between-calls to avoid redundant mints, `"reconnect required: ..."` string
convention mirroring ADR-0011).

**Tests:** `tests/Server.Tests/SteamFamilyTokenTests.fs` gained
`steamIdFromRefreshTokenTests` (2) and `mintFamilyAccessTokenTests` (2) — the
pure/degenerate paths. The interactive SteamKit2 QR ceremony itself
(`SteamConnect.fs`) is not unit-tested — a legitimate TDD skip (interactive
external dependency, no decision logic of its own to assert against); it was
instead live-verified per the builder gate and via ad hoc scratch scripts
exercising the real mint/refresh/fetch path against the real Steam network
with the builder's stored refresh token (not committed). Full suite: 676
tests passing.

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
