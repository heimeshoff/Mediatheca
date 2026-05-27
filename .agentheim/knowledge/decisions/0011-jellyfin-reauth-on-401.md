---
id: 0011
title: Jellyfin self-heals a rejected token via a pure re-auth-and-retry orchestration
scope: integration
status: accepted
date: 2026-05-27
related_tasks: [integration-002]
---

# ADR 0011: Jellyfin self-heals a rejected token via a pure re-auth-and-retry orchestration

## Context
`jellyfin_access_token` was written only by `testJellyfinConnection`; every sync just read
it. If the stored token were ever rejected (Jellyfin restart, token rotation, password
change), every subsequent sync would fail with no self-healing — the user would have to
manually re-test the connection in Settings. This was a latent robustness gap flagged as a
disproven hypothesis during the integration-001 diagnosis (ADR 0010) and split out as
integration-002. The current token works (confirmed live), so this is preventive hardening.

Two design pulls were in tension:
1. The fetch layer (`Jellyfin.fetchJsonWithAuth`) used `EnsureSuccessStatusCode`, which
   throws on *any* non-success. A 401 was indistinguishable from a 500 or a network fault.
2. Re-auth needs effects the HTTP layer must not own: the stored username/password, and a
   way to persist the fresh token (`SettingsStore`, only reachable from `Api.fs`).

## Decision
Surface auth failure as data, then orchestrate the retry purely.

- **Typed fetch error.** `fetchJsonWithAuth` now inspects the status code and returns
  `Result<string, FetchError>` where `FetchError = Unauthorized | OtherFailure of string`.
  A 401/403 becomes `Error Unauthorized`; everything else (other HTTP codes, decode
  failures, transport exceptions) becomes `OtherFailure`. Nothing in the fetch path throws
  anymore.
- **Pure orchestration.** `Jellyfin.withReauthRetry` takes the current token, a
  token-consuming `fetch`, a `reauthenticate` thunk, and a `persist` callback — all as
  lambdas. Policy: run `fetch` once; on `Unauthorized`, re-auth exactly once; on re-auth
  success persist the fresh token and retry `fetch` exactly once; a second `Unauthorized`,
  a failed re-auth, or a re-auth thunk that reports missing credentials all return a clear
  `Error` and never loop. This mirrors the integration-001 pattern
  (`JellyfinImport.syncSeriesWatchHistory`): the effectful seams are injected so the
  exactly-once policy is unit-testable with plain lambdas — no HTTP server, no SQLite.
- **Self-healing variants.** `getMoviesWithReauth` / `getSeriesWithReauth` /
  `getEpisodesWithReauth` wire `withReauthRetry` to the real config + a `reauthThunk` that
  returns "re-authentication required" when no credentials are stored. The sync call sites
  in `Api.fs` (`runJellyfinImport`, `scanJellyfinLibrary`) pass a `persistAuth` callback
  that writes `jellyfin_access_token` + `jellyfin_user_id` back via `SettingsStore` and
  refreshes the in-flight config so later fetches in the same run use the fresh token.

## Consequences
- A future token rejection now heals itself on the next sync instead of failing forever.
- Re-auth failure / missing credentials surface as a meaningful `SyncFailed` message
  (via the ADR 0010 persisted-result machinery), not an opaque HTTP error.
- `withReauthRetry` is fully unit-tested (`tests/Server.Tests/JellyfinReauthTests.fs`):
  no-401, 401-then-success, 401-twice (no loop), failed re-auth, missing credentials,
  non-auth error passthrough.
- The legacy `getMovies`/`getSeries`/`getEpisodes` wrappers were kept (now mapping
  `FetchError` to a string) so non-sync callers and the existing API surface are unchanged.
- Retry is intentionally a flat once-per-fetch policy, not a global "re-auth once per run".
  Re-auth is idempotent and the persisted token is reused within a run, so at most one HTTP
  re-auth round-trip happens per run in practice; the simpler per-fetch guard avoids
  threading shared retry state through the import loop.
