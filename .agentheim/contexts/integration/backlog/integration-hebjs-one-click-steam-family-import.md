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

## Acceptance criteria

_Provisional — refine after the integration-ygwsa spike lands its findings._

- [ ] Settings → Steam Family offers a "Connect Steam" one-time flow; the
      "How to get the access token" DevTools instruction block is gone (or demoted
      to a manual-fallback footnote).
- [ ] After connecting once, "Discover Family Members" and "Import shared library"
      work with no manual token step — including more than an hour later
      (server auto-mints a fresh access token per call or on 401).
- [ ] An expired/revoked refresh token surfaces a clear "reconnect Steam" prompt
      in Settings (mirroring the ADR-0011 Jellyfin re-auth pattern), never a
      silent failure.
- [ ] Existing saved-token behavior degrades gracefully: a manually pasted token
      still works as fallback, or is cleanly migrated/removed — decide during
      refinement.

## Notes

- Depends on integration-ygwsa proving the refresh-token → family-scope
  access-token mint (SteamKit2). If the spike lands on the fallback instead
  (browser-driven token harvest), this task's What/AC get rewritten around that.
- Frontend-bearing → gated on design-system-001 (styleguide, done) per the BC
  README's frontend gate.
- UI surface today: `steamFamilyDetail` in `src/Client/Pages/Settings/Views.fs`;
  token save path `Save_steam_family_token` → SettingsStore.
- Open question for refinement: should the scheduled Steam job
  (`ScheduledJobs.fs`) also refresh the family library once tokens are free, or
  does family import stay manual-trigger-only?
