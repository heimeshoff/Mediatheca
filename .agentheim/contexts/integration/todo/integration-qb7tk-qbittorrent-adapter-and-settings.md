---
id: integration-qb7tk
title: qBittorrent adapter and Settings card — URL, username and password stored and tested from Settings exactly like Jellyfin's, plus a typed-error `Qbittorrent.fs` adapter (login, list torrents with ratio and seeding time, list a torrent's files, delete with files) that integration-r4vzm builds on
status: todo
type: feature
context: integration
created: 2026-09-10
completed:
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
