---
id: integration-ygwsa
title: Spike — mint Steam Family access tokens from a stored refresh token (SteamKit2)
status: doing
type: spike
context: integration
created: 2026-07-20
completed:
depends_on: []
blocks: [integration-hebjs]
tags: [steam, steam-family, auth, token, spike]
related_adrs: [0011]
related_research: []
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
- [ ] From the stored refresh token alone (no browser, no manual step), the harness
      mints an access token and successfully calls `GetFamilyGroupForUser` and
      `GetSharedLibraryApps` for the user's real family group.
- [ ] Findings written up in this task's Notes (or a short report in
      `knowledge/research/`): refresh-token lifetime and renewal behavior, token
      audience requirements, whether the QR flow is embeddable in the Settings UI,
      and the recommended production shape for integration-hebjs.
- [ ] If the refresh-token approach fails, the write-up says why and evaluates the
      fallback: semi-automated browser retrieval (Chrome DevTools MCP / Playwright
      driving the logged-in browser to harvest the token), i.e. "LLM does the
      DevTools ritual".

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
