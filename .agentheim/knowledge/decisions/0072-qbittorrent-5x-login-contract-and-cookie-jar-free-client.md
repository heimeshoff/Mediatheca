---
id: 0072
title: The qBittorrent adapter speaks the 5.x login contract (HTTP 204 + `QBT_SID_<port>` cookie, HTTP 401 on bad credentials) alongside the 4.x one, and runs on its own cookie-jar-free HttpClient
scope: integration
status: accepted
date: 2026-09-11
supersedes: []
superseded_by: []
amends: [0071]
related_tasks: [integration-qb7tk, integration-r4vzm, integration-mqsd3]
related_research: []
---

# ADR 0072: qBittorrent 5.x login contract + cookie-jar-free HttpClient

## Context

The first production use of the Settings "Test connection" button against the live
qBittorrent (linuxserver `5.2.3`, reached at `https://qb.elver-minor.ts.net` through its
Tailscale sidecar) reported *"qBittorrent authentication failed: check the username and
password"* although qBittorrent's own log recorded `WebAPI login success` for that very
request. Root cause, verified against the `release-5.2.3` source
(`src/webui/webapplication.cpp`, `src/webui/api/authcontroller.cpp`) and a live probe:

1. **Login response shape changed in 5.x.** integration-qb7tk implemented the wiki's 4.x
   contract: success = HTTP 200 + body `Ok.`, bad credentials = HTTP 200 + body `Fails.`,
   banned = HTTP 403. In 5.x `AuthController::loginAction` sets `APIStatus::Ok` with no
   data, which `WebApplication` renders as **HTTP 204, empty body**; bad credentials throw
   `APIErrorType::Unauthorized` → **HTTP 401 "Unauthorized"** (live probe confirmed). The
   adapter saw 204 + `"" <> "Ok."` and classified a successful login as `AuthFailed`.
2. **Session cookie renamed.** 5.x issues `QBT_SID_<webui-port>` (`SESSION_COOKIE_NAME_PREFIX
   + port`, e.g. `QBT_SID_8080`) instead of `SID`, and `cookieSessionInitialize` looks the
   session up **by that name**. Sending it back as `Cookie: SID=…` would never match.
3. **The shared `HttpClient` has a cookie jar.** A wire-level experiment showed .NET's default
   handler stores the login `Set-Cookie` and replays it on every later request to the host,
   so the second "Test connection" sent the previous session on the login call itself.
   qBittorrent answers an already-authenticated login with a bare 2xx and **no new
   `Set-Cookie`** — login-once-use-once (ADR-0071 point 7) had silently become one
   shared session that outlived the operation.

## Decision

- **`Session` carries the cookie name.** `Session = { CookieName; Sid }`; `withCookie` sends
  `Cookie: <CookieName>=<Sid>`. `extractSession` accepts `SID` (4.x) or any `QBT_SID_*`
  (5.x) from the first `name=value` segment of each `Set-Cookie` header.
- **Login success = any 2xx that carries a session cookie.** Body text is no longer the
  success signal. HTTP 401 and HTTP 403 → `AuthFailed`; a 2xx with body `Fails.` (4.x) →
  `AuthFailed`; a 2xx **without** a session cookie → `OtherFailure` naming the status, because
  that is qBittorrent's "already logged in" answer and this adapter never intends to be.
  Other non-2xx → `OtherFailure "HTTP n"`. Authenticated calls keep 403 → `AuthFailed`.
- **qBittorrent gets its own HttpClient with `UseCookies = false`.** `Qbittorrent.createHandler`
  / `createHttpClient` own that fact; `Composition` builds `qbittorrentHttpClient` from it and
  `Api.create` takes it as a separate parameter so every `Qbittorrent.*` call site in `Api.fs`
  is visibly on the cookie-jar-free client. The app-wide shared client is untouched.

## Consequences

- One code path serves 4.x and 5.x qBittorrent; the tests pin both response shapes.
- ADR-0071 point 7 ("login-once-use-once, never persisted") is now actually true at the
  transport level, not just in the adapter's intent.
- The Settings error text stays "check the username and password" for `AuthFailed`; a
  genuine 5.x bad password now reaches it via 401 instead of surfacing as `HTTP 401`.
- Not changed: 401 on an *authenticated* call still maps to `OtherFailure "HTTP 401"` —
  in 5.x that is the CSRF/Host-header check, a configuration problem, not a credentials one.
