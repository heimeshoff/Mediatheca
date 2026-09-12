---
id: integration-002
title: Re-authenticate Jellyfin and retry once on a 401/403 during sync
status: done
type: bug
context: integration
created: 2026-05-27
completed: 2026-05-27
commit: 72bb9a5
depends_on: []
blocks: []
tags: [jellyfin, sync, auth, robustness]
related_adrs: [0010, 0011]
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
- Decision recorded in ADR 0011 (self-heal via a pure re-auth-and-retry orchestration).

## Outcome

Jellyfin now self-heals a rejected token. Implemented in `src/Server/Jellyfin.fs`:
- `fetchJsonWithAuth` no longer throws via `EnsureSuccessStatusCode`; it inspects the
  status code and returns `Result<string, FetchError>` where
  `FetchError = Unauthorized | OtherFailure of string` (401/403 -> `Unauthorized`).
- `withReauthRetry` (pure, injectable) implements the exactly-once policy: run fetch ->
  on `Unauthorized` re-auth once -> on success persist the fresh token + retry once;
  a second 401, a failed re-auth, or missing credentials return a clear "re-authentication"
  error and never loop. Tested with plain lambdas (no HTTP/SQLite), mirroring the
  integration-001 `syncSeriesWatchHistory` pattern.
- Self-healing variants `getMoviesWithReauth` / `getSeriesWithReauth` /
  `getEpisodesWithReauth` wire the policy to the real config + a `reauthThunk` that reports
  "re-authentication required" when no credentials are stored.

Wired into the sync call sites in `src/Server/Api.fs` (`runJellyfinImport`,
`scanJellyfinLibrary`): a `persistAuth` callback writes the fresh
`jellyfin_access_token` + `jellyfin_user_id` via `SettingsStore` and refreshes the
in-flight config so later fetches in the same run use the new token.

Regression coverage in `tests/Server.Tests/JellyfinReauthTests.fs` (6 cases: no-401,
401-then-success persists + retries, 401-twice no-loop, failed re-auth, missing
credentials, non-auth passthrough). `npm test` green (265 tests, +6). `npm run build`
clean.

Key files: src/Server/Jellyfin.fs (FetchError, withReauthRetry, *WithReauth variants),
src/Server/Api.fs (persistAuth wiring at both sync call sites),
tests/Server.Tests/JellyfinReauthTests.fs (new), Integration README, ADR 0011.
