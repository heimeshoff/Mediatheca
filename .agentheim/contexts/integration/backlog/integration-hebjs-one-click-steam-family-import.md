---
id: integration-hebjs
title: One-click Steam Family import — automatic access-token acquisition
status: backlog
type: feature
context: integration
created: 2026-07-20
completed:
depends_on: [integration-ygwsa, design-system-001]
blocks: []
tags: [steam, steam-family, auth, token, settings, import]
related_adrs: [0011]
related_research: []
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

## Builder gate — do this first, before any promotion (~30 min, Marco only)

The integration-ygwsa spike could not verify the decision-critical question — whether a
SteamKit2-minted token carries the audience/scope `IFamilyGroupsService` requires — because
the QR login needs a human with the Steam mobile app at a live terminal (ADR-0019 records
this, and mandates the empirical test as this task's *first* action, before any further
implementation investment).

1. `dotnet fsi spikes/steam-family-token-spike/login.fsx` — scan the QR with the Steam
   mobile app; persists a refresh token.
2. `dotnet fsi spikes/steam-family-token-spike/refresh-and-call.fsx` — mints an access
   token and calls `GetFamilyGroupForUser`.
3. Report the outcome here. **PASS** → this task builds the SteamKit2 QR-login flow
   (acceptance criteria below stand as written). **FAIL** → rewrite What/AC around the
   browser-retrieval fallback (ADR-0019 point 4: Chrome DevTools MCP / Playwright drives
   the logged-in browser to harvest the token on a schedule).

The task stays in `backlog/` until the gate outcome is recorded — promoting before it
would send a worker into exactly the investment ADR-0019 forbids.

## Acceptance criteria

_Conditional on the builder gate passing (SteamKit2 mint path). Rewrite on FAIL._

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

- **Refined 2026-08-04:** the two open questions are settled — (1) manual paste stays
  as a fallback footnote, not removed; (2) scheduled family-library sync is **out of
  scope** — family import stays manual-trigger-only in this task; auto-sync becomes
  its own capture once one-click import has proven itself.
- SteamKit2 becomes a server dependency only on the gate's PASS branch (ADR-0019
  deliberately shipped the seam without it).
- Frontend-bearing → gated on design-system-001 (styleguide, done) per the BC
  README's frontend gate.
- UI surface today: `steamFamilyDetail` in `src/Client/Pages/Settings/Views.fs`;
  token save path `Save_steam_family_token` → SettingsStore.
- Security note (from the spike): the refresh token is a powerful ~1-year credential;
  it lives in SettingsStore/SQLite in the data dir — acceptable for a single-user,
  self-hosted app (ADR-0007), but say so in the Settings UI copy.
