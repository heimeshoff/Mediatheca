---
id: integration-qb7tk
title: qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter (login, list torrents with ratio and seeding time, list a torrent's files, delete with files) that integration-r4vzm builds on
status: done
type: feature
context: integration
created: 2026-09-10
completed: 2026-09-10
depends_on: [design-system-001]
blocks: [integration-r4vzm]
tags: [qbittorrent, settings, adapter, storage]
related_adrs: [0011, 0070]
related_research: []
prior_art: [integration-002, integration-003]
---

## Why

Mediatheca is about to become a qBittorrent client: the "Remove local copy" flow
(integration-mqsd3) must find the torrents behind a Jellyfin item and delete them with
their files. That needs credentials, a session, and a small typed adapter — all of which
are independently valuable and independently testable against the real qBittorrent on
harbour before any destructive flow exists. Splitting them out keeps mqsd3 to one
worker session and lets the credentials + "Test connection" round-trip ship first, the
same way `testJellyfinConnection` shipped ahead of every Jellyfin sync feature.

## What

1. **Settings** — three SettingsStore keys, mirroring the Jellyfin ones:
   `qbittorrent_url`, `qbittorrent_username`, `qbittorrent_password`. Three
   `IMediathecaApi` members: a getter that returns URL + username and **never** the
   password, a setter for all three, and `testQbittorrentConnection (url, username,
   password)` that logs in and reports the app version (`GET /api/v2/app/version`) and
   torrent count. A **qBittorrent card** on the Settings page beside the Jellyfin card,
   built from the same Elmish shape (`*Input` fields, `IsTesting*`, `IsSaving*`,
   `*TestResult`, `*SaveResult`).

2. **Adapter `src/Server/Qbittorrent.fs`** — one module, typed errors, no vendor JSON
   leaking out:
   - `QbittorrentConfig = { Url; Username; Password }`
   - `QbittorrentError = AuthFailed | OtherFailure of string` (the sibling of
     `Jellyfin.FetchError`)
   - `TorrentInfo = { Hash; Name; SavePath; ContentPath; Ratio: float; SeedingTime:
     TimeSpan; State: string }` decoded from `GET /api/v2/torrents/info` (`hash`,
     `name`, `save_path`, `content_path`, `ratio`, `seeding_time` seconds, `state`)
   - `login` — `POST /api/v2/auth/login` (form `username`/`password`); qBittorrent
     answers HTTP 200 with body `Ok.` on success and `Fails.` on bad credentials, and
     403 when the client is banned — both non-`Ok.` outcomes are `AuthFailed`.
   - `listTorrents`, `listFiles hash` (`GET /api/v2/torrents/files?hash=…` → file
     names relative to the save path; integration-r4vzm counts a pack torrent's extra
     files with it), `deleteTorrents (hashes: string list) (deleteFiles: bool)` —
     `POST /api/v2/torrents/delete` with `hashes=h1|h2|…&deleteFiles=true|false`.
   - `Ratio` is decoded as-is; qBittorrent's sentinels (`-1` for infinite, the `9999`
     cap) are passed through untouched — interpreting them is the caller's job.
   - `withSession config (use: Session -> Async<Result<'a, QbittorrentError>>)` —
     login-once-use-once: the `SID` cookie lives only in the `Session` value for the
     duration of one operation and is sent as an explicit `Cookie: SID=…` request
     header, so the shared `HttpClient` stays stateless. No persisted cookie, no
     re-auth-and-retry seam — a qBittorrent session is cheap to reacquire, unlike a
     Jellyfin token (ADR-0011's seam is deliberately *not* mirrored).
   - Requests carry **no `Origin` or `Referer` header** — qBittorrent's CSRF check
     rejects cross-origin browsers, not server-to-server calls without an `Origin`.

## Acceptance criteria

- [ ] `SettingsStore` holds `qbittorrent_url`, `qbittorrent_username`,
      `qbittorrent_password` after saving from Settings; the getter API member returns
      URL + username and no password field exists on its DTO.
- [ ] `testQbittorrentConnection` against a reachable qBittorrent returns `Ok` with the
      app version and torrent count; against wrong credentials returns `Error` naming
      authentication (not a generic HTTP error).
- [ ] `Qbittorrent.login` maps HTTP 200 + body `Fails.` and HTTP 403 to `AuthFailed`,
      HTTP 200 + `Ok.` to a session, and transport faults to `OtherFailure` — unit-tested
      with injected HTTP lambdas in the shape of `JellyfinReauthTests.fs`.
- [ ] The `torrents/info` decoder is unit-tested against a fixture JSON array covering
      `ratio` as a float, `seeding_time` in seconds → `TimeSpan`, `save_path`,
      `content_path`, `state`.
- [ ] `deleteTorrents` sends `hashes` joined with `|` and `deleteFiles` as `true`/`false`
      — asserted on the captured request body in a unit test.
- [ ] `listFiles` decodes a fixture `torrents/files` response into relative file names
      (unit test).
- [ ] `withSession` logs in exactly once per operation and sends the `SID` cookie as a
      request header; no cookie survives the operation (unit test with a counting login
      lambda).
- [ ] No request built by the adapter carries an `Origin` or `Referer` header (unit
      test on the captured `HttpRequestMessage`).
- [ ] The Settings page shows a qBittorrent card with URL / username / password inputs,
      Test and Save buttons, and the last test/save result, styled like the Jellyfin card
      (StyleGuide gate: `design-system-001` is done). [human-eye]
- [ ] `npm run build` and `npm test` pass.

## Notes

**Verified on harbour 2026-09-10 (see integration-mqsd3 Notes for the full list):**
qBittorrent runs with default WebUI auth (localhost auth required, CSRF on) behind the
Tailscale sidecar, where every client looks like localhost — so "bypass auth for
localhost" is never the answer; real credentials are. It is reached from the mediatheca
container over the tailnet the same way Jellyfin is (`https://jellyfin.elver-minor.ts.net/`
today; the qBittorrent hostname is configured, not hardcoded). Torrents save to
`/downloads/movies` and `/downloads/shows/<show>`.

**Out of scope here:** anything that deletes. This task ships read + test + credentials;
integration-r4vzm (the server-side removal flow) is the first and only caller of
`deleteTorrents`, and integration-mqsd3 is the UI on top of that. Once this ships,
the harbour fleet docs get a one-line note that mediatheca holds qBittorrent credentials.

**Decisions:** ADR-0070 (projection-only removal) is referenced for context only; this
task makes no event-vs-cache call of its own.

## Outcome

Shipped the qBittorrent adapter and its Settings card exactly as scoped — read + test +
credentials, nothing that deletes wired to a UI yet.

- **`src/Server/Qbittorrent.fs`** (new): `QbittorrentConfig`, `QbittorrentError = AuthFailed
  | OtherFailure of string` (the sibling of `Jellyfin.FetchError`), `Session = { Sid: string
  }`, `TorrentInfo` (decoded from `torrents/info`: `hash`, `name`, `save_path`,
  `content_path`, `ratio` as-is, `seeding_time` seconds → `TimeSpan`, `state`). `login`
  maps HTTP 200 + `Ok.` to a session (SID parsed out of `Set-Cookie`), HTTP 200 + `Fails.`
  and HTTP 403 to `AuthFailed`, transport faults to `OtherFailure`. `withSession` is
  login-once-use-once (no persisted cookie, no ADR-0011-shaped re-auth-and-retry, per
  ADR-0070 point 7). `listTorrents`, `listFiles`, `deleteTorrents` (hashes joined `|`,
  `deleteFiles=true|false` — exists as a typed primitive; integration-r4vzm is its first
  caller) and `getAppVersion`/`testConnection` round out the adapter. No request ever
  carries an `Origin`/`Referer` header.
- **`src/Shared/Shared.fs`**: `QbittorrentSettings = { Url; Username }` (no password field)
  and three `IMediathecaApi` members — `getQbittorrentSettings`, `setQbittorrentCredentials`
  (all three fields), `testQbittorrentConnection` (logs in, reports app version + torrent
  count, never persists — persistence is the setter's job alone, unlike Jellyfin's combined
  "Test & Save").
- **`src/Server/Api.fs`**: wires the three members to `SettingsStore` keys `qbittorrent_url`
  / `qbittorrent_username` / `qbittorrent_password` and `Qbittorrent.testConnection`.
- **Settings client** (`Types.fs`/`State.fs`/`Views.fs`): a qBittorrent card mirroring the
  Jellyfin card's Elmish shape (`*Input` fields, `IsTesting*`/`IsSaving*`,
  `*TestResult`/`*SaveResult`) but with two distinct buttons ("Test connection" / "Save")
  since the API deliberately keeps test and save as separate members.
- **Tests**: `tests/Server.Tests/QbittorrentTests.fs` (9 new Expecto cases, same
  recording-`HttpMessageHandler` pattern as `PlaytimeSyncKeyRejectionTests.fs`) covering
  `login`'s three response shapes + a transport fault, the `torrents/info` decoder fixture,
  the `listFiles` fixture, `deleteTorrents`'s captured request body, `withSession`'s
  exactly-once-login/no-cookie-survives behaviour, and the missing-Origin/Referer
  assertion across a full request sequence.
- BC README updated: `Qbittorrent.fs` added to the Adapter list; a new "Session
  (qBittorrent)" ubiquitous-language entry contrasts its login-once-use-once shape with
  Jellyfin's persisted-token re-auth.
- Verified: `npm run build` (196 modules, clean) and `npm test` (702 Expecto tests, all
  green, including the 9 new ones). The qBittorrent card's rendering itself is
  [human-eye] per the task's own acceptance criterion — not separately screenshotted here.
- No new ADR: ADR-0070 point 7 already records the no-persisted-session decision this task
  implements; nothing here rose to a fresh "why this, not the obvious alternative" call.

**Iteration 2 (2026-09-10):** closed the verifier's gap on acceptance criterion 2. Added a
`testConnection` test list to `tests/Server.Tests/QbittorrentTests.fs` with two Expecto cases
against the existing `RecordingHandler` — a reachable-qBittorrent case (`login` → `Ok.` +
`Set-Cookie`, `/app/version` → `v4.6.0`, `/torrents/info` → a two-torrent fixture) asserting
`testConnection` yields `Ok ("v4.6.0", 2)`, and a wrong-credentials case (`login` → HTTP 200
`Fails.`) asserting `Error AuthFailed`. Also added a cheap SettingsStore round-trip test (a
second `[<Tests>]` list in the same file, using `TestDb.withTempDbFactory`) covering the three
`qbittorrent_url`/`qbittorrent_username`/`qbittorrent_password` keys integration-r4vzm depends
on by name. `npm test` now runs 705 Expecto tests, all green (702 + 3 new); `npm run build`
stays clean. No production code changed — this iteration is test-only.

## Verifier note (iteration 1)

**REASONS:**
- Acceptance criterion 2 ("`testQbittorrentConnection` against a reachable qBittorrent returns `Ok` with the app version and torrent count; against wrong credentials returns `Error` naming authentication (not a generic HTTP error)") has no coverage of any kind. `Qbittorrent.testConnection` (`src/Server/Qbittorrent.fs:218`) and its API wrapper (`src/Server/Api.fs:4635`) are never called by any test — a repo-wide grep for `testConnection`/`testQbittorrent` in `tests/` returns nothing, and none of the 9 new cases in `tests/Server.Tests/QbittorrentTests.fs` exercise it. The success half (version string + torrent *count* tuple) and the "authentication, not generic HTTP" error wording are both entirely unasserted, even though the file's own `RecordingHandler` already serves `/auth/login` + `/app/version` + `/torrents/info` in the Origin/Referer case (`QbittorrentTests.fs:875-894`) and would make this a ~10-line test. The task's `## Outcome` claims no live/harbour verification of this round-trip either, so there is neither a test nor an inspectable artifact for this criterion.
- Secondary (not on its own a FAIL): criterion 1's first half ("`SettingsStore` holds `qbittorrent_url`/`qbittorrent_username`/`qbittorrent_password` after saving from Settings") rests only on reading the three literal `SettingsStore.setSetting` calls at `src/Server/Api.fs:4627-4629`; accepted here as inspection evidence, but integration-r4vzm depends on those exact key names, so a round-trip test would be cheap insurance while the next worker is in this file.
- Everything else checked out and is not the reason for this verdict: `npm test` → 702 passed / 0 failed, exit 0; `npm run build` → clean; scope is confined to the task's own files; the BC README's Adapter + new "Session (qBittorrent)" entries match the diff; the diff honors ADR-0070 point 7 (login-once-use-once, no persisted SID) and correctly does *not* mirror ADR-0011's re-auth seam, so no new ADR is owed; no protocol/INDEX/git tampering; check 8 skipped (integration declares no `## Runtime surface`); the `[human-eye]` criterion 9 is builder eye-check pending (structural parity with `jellyfinDetail` via the same `integrationCard`/`statusBadge`/`feedbackAlert`/`Daisy.input` compositions is present).

**SUGGESTED_FIX:** Add two Expecto cases to `tests/Server.Tests/QbittorrentTests.fs` using the existing `RecordingHandler`: one where login returns `Ok.` + `Set-Cookie`, `/app/version` returns e.g. `v4.6.0` and `/torrents/info` returns a two-element fixture, asserting `testConnection` yields `Ok ("v4.6.0", 2)`; and one where login returns HTTP 200 + `Fails.`, asserting `testConnection` yields `Error AuthFailed` (the shape `Api.fs` maps to the "authentication failed: check the username and password" wording). Optionally add a SettingsStore round-trip test for the three `qbittorrent_*` keys.

**ITERATION_HINT:** likely-fixable
