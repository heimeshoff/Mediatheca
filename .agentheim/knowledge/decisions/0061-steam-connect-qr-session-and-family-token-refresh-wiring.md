---
id: 0061
title: Steam Connect QR session (in-memory + SSE) and the production wiring of Steam.withTokenRefresh into the Family adapter
scope: integration
status: accepted
date: 2026-08-07
supersedes: []
superseded_by: []
related_tasks: [integration-hebjs, integration-ygwsa]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
---

# ADR 0061: Steam Connect QR session (in-memory + SSE) and the production wiring of Steam.withTokenRefresh into the Family adapter

## Context

ADR-0019 shipped the pure `Steam.withTokenRefresh` mint-and-retry seam
(ADR-0011-shaped) but deferred two things: (1) the live audience/scope
verification of a SteamKit2-minted access token against
`IFamilyGroupsService` (now **passed**, see the integration-hebjs task's
builder-gate section), and (2) the actual production `mint`/`persist` wiring
and the one-time QR login that produces the refresh token in the first
place. This task builds both.

Three shapes needed deciding:

1. **Where does the QR login session live while the user fetches their
   phone and scans?** SteamKit2's `QrAuthSession` is a live object over an
   open `SteamClient` connection — it can't be serialized to SQLite or
   handed across a stateless request/response cycle the way `Steam.fs`'s
   other HTTP calls are.
2. **How does the family adapter's existing two-call shape (basic
   `GetFamilyGroupForUser` → detailed `GetFamilyGroup`, `Api.fs`'s
   `fetchSteamFamilyMembers`) wire into the mint-and-retry seam without
   duplicating a mint on every call once the first one already refreshed?**
3. **What distinguishes "reconnect Steam" (no/rejected refresh token, a
   ceremony only the user can redo) from any other family-fetch failure** at
   both the server error-message level and the Settings UI?

## Decision

**1. In-memory session store + SSE polling, not a persisted session.**
`SteamConnect.fs` holds `ConnectStatus` (`AwaitingScan of qrImageDataUrl |
Connected of refreshToken | ConnectFailed of message`) in a
`ConcurrentDictionary<string, ConnectStatus ref>` keyed by a session id,
mutated from a single background `Task.Run` that owns the entire
SteamClient-connect → QR-begin → poll-for-result lifecycle. `Api.fs`'s
`steamConnectStreamHandler` (same SSE envelope as the existing
`steamFamilyImportHandler`, `Sse.sseFrame`) starts a session, polls
`SteamConnect.status` every 400ms, and streams a `qr` event each time the
QR image changes (the challenge URL rotates roughly every 30s) followed by a
terminal `complete`/`error`. On `Connected`, the SSE handler — not
`SteamConnect.fs` — persists the refresh token to SettingsStore
(`steam_family_refresh_token`), keeping `SteamConnect.fs` free of any
database dependency, consistent with every other adapter module in this BC.
A server restart mid-ceremony silently orphans the in-memory session; the
client's SSE stream just errors out (or times out) and the user clicks
Connect Steam again. Accepted for a single-user, self-hosted app with a
short interactive ceremony — see the README's open questions if this ever
needs to survive a mid-flight deploy.

**2. Re-read the persisted access token between sequential family calls.**
`getFamilyGroupForUserWithRefresh`/`getSharedLibraryAppsWithRefresh`/
`getFamilyGroupWithRefresh` each independently wrap `Steam.withTokenRefresh`
around one HTTP call. `Api.fs`'s two multi-call call sites
(`runSteamFamilyImport`, `fetchSteamFamilyMembers`) re-read
`SettingsStore.getSetting conn "steam_family_token"` before their second
call rather than reusing the access token variable captured before the
first call — so if the first call already minted and persisted a fresh
token, the second call starts from that fresh token instead of immediately
hitting `Rejected` again and triggering a second, redundant mint round trip.

**3. `"reconnect required: ..."`-prefixed error strings, checked by
substring on the client.** `Steam.mintFamilyAccessToken` returns this
exact prefix for both "no refresh token stored" and "Steam rejected the
stored refresh token" — mirroring ADR-0011's
`Jellyfin.reauthThunk`'s `"re-authentication required: ..."` convention
exactly, one BC, one pattern. `Settings/State.fs`'s `isReconnectRequired`
substring-checks family-fetch/import error messages for this marker and
sets `SteamNeedsReconnect`, which the view renders as a dedicated
"Reconnect Steam" prompt (a button that re-runs the QR ceremony) distinct
from the generic error alert every other adapter failure gets. No new
`Result` variant or shared DU was introduced for this — every other
adapter error in this BC is already a plain `string`, and a prefix
convention keeps `mintFamilyAccessToken`'s signature identical to every
other `TokenMinter`.

**4. SteamKit2 stays confined to `SteamConnect.fs`.** Everything downstream
of a stored refresh token — `mintFamilyAccessToken`,
`steamIdFromRefreshToken`, the three family GET calls — is plain
`HttpClient`, matching every other adapter call in `Steam.fs` and per
ADR-0019 point 2 (no CM connection for ongoing refresh). `Server.fsproj`
takes `SteamKit2`/`QRCoder` as dependencies pinned to the exact versions the
builder gate proved live.

## Alternatives considered

- **Persist the in-progress QR session to SQLite** (so it survives a
  restart). Rejected: `QrAuthSession` is a live SteamKit2 object over an
  open connection, not serializable data — persisting would mean
  reconnecting to Steam and re-requesting a *new* QR challenge on every
  poll anyway, which is what a fresh "Connect Steam" click already does at
  no extra cost, for a failure mode (mid-ceremony restart) that's rare and
  cheap to recover from by hand.
- **A structured `Result<'a, SteamFamilyError>` (with a dedicated
  `NeedsReconnect` case) instead of a string-prefix convention.** Rejected:
  every other cross-cutting adapter failure in this BC — Jellyfin's
  re-auth included — is already a plain string surfaced through
  `Result<_, string>` RPC contracts; introducing one new DU for Steam alone
  would be an inconsistent one-off rather than following the BC's existing
  convention.
- **Have `SteamConnect.fs` persist the refresh token itself** (taking a
  `SqliteConnection` or a `persist` callback parameter). Rejected: keeps
  `SteamConnect.fs`'s job purely "run the SteamKit2 ceremony, report
  status" — identical separation of concerns to how `Jellyfin.fs`/`Steam.fs`
  never touch `SettingsStore` directly, only `Api.fs` does.

## Consequences

### Positive

- Live-verified end-to-end (not just unit-tested): a scratch script driving
  the real `mintFamilyAccessToken` → `getFamilyGroupForUserWithRefresh` →
  `getFamilyGroupWithRefresh` path against the builder's real refresh token
  confirmed the full self-heal cycle (deliberately-invalid access token →
  `Rejected` → mint → persist → retry → success, then a second call
  reusing the persisted token) works against the real Steam network, not
  just the pure `withTokenRefresh` unit tests.
- `SteamConnect.fs` and `Steam.fs`'s HTTP-only mint/fetch functions stay
  independently testable: the pure parts (`steamIdFromRefreshToken`,
  `mintFamilyAccessToken`'s degenerate empty-token case) have unit tests;
  the interactive SteamKit2 ceremony does not (see the task's
  `TDD_SKIPPED` note) and never needs to, since none of its logic is
  decision logic — it's an I/O ceremony driven entirely by SteamKit2's own
  polling.

### Negative / accepted tradeoff

- A mid-ceremony server restart orphans the in-memory QR session with no
  explicit cleanup — acceptable (single-user, short ceremony, cheap manual
  retry) but would need revisiting if Connect Steam ever needs to survive a
  deploy mid-flight.
- The `"reconnect required: ..."` string-prefix convention is stringly
  typed, matching the BC's existing string-error convention but meaning a
  typo in the prefix on either side (server or client) would silently
  degrade the reconnect prompt to a generic error banner rather than a
  compile error — a small tradeoff over the DU alternative, made because
  the DU-per-adapter-error path the BC doesn't otherwise follow at all.
