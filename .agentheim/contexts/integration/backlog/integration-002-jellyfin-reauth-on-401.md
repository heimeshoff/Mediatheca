---
id: integration-002
title: Re-authenticate Jellyfin and retry once on a 401/403 during sync
status: backlog
type: bug
context: integration
created: 2026-05-27
completed:
commit:
depends_on: []
blocks: []
tags: [jellyfin, sync, auth, robustness]
related_adrs: [0010]
related_research: []
prior_art: [integration-001]
---

## Why

`jellyfin_access_token` is written only by `testJellyfinConnection`
(`Api.fs` ~3944); auto-sync just reads it (`Program.fs` ~123). If the stored
token is ever rejected (Jellyfin restart, token rotation, password change),
the sync fails every run with no self-healing — the user has to manually
re-test the connection in Settings.

This was a hypothesis during the `integration-001` diagnosis but was disproven
as that breakage's trigger (the token was valid). It remains a real latent
robustness gap, split out per the integration-001 notes.

## What

When a Jellyfin fetch (`getMovies` / `getSeries` / `getEpisodes` /
`getLibraryItems`) returns a 401/403, re-authenticate once using the stored
`jellyfin_username` / `jellyfin_password`, persist the fresh
`jellyfin_access_token` (+ user id), and retry the failed request once. If the
re-auth itself fails, surface that as a `SyncFailed` with a clear message (the
observability from integration-001 / ADR 0010 already persists the result).

## Acceptance criteria

- [ ] A 401/403 on any Jellyfin fetch during sync triggers exactly one re-auth
      attempt with the stored username/password.
- [ ] On successful re-auth the new token is persisted and the original request
      is retried once; a second failure is reported, not looped.
- [ ] If username/password are not stored, the run fails with a clear
      "re-authentication required" message rather than an opaque HTTP error.
- [ ] Regression test: stubbed Jellyfin returns 401 then 200 after re-auth ->
      sync succeeds and the new token is persisted.
- [ ] `npm run build` clean and `npm test` green.

## Notes

- Builds on ADR 0010 (Jellyfin sync observability + fault isolation).
- `Jellyfin.authenticate` already exists and returns the token + user id.
- Consider threading the re-auth through `fetchJsonWithAuth` (currently uses
  `EnsureSuccessStatusCode`, which throws on 401 — inspect the status code
  instead so the caller can decide to re-auth).
