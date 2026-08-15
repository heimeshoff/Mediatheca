---
id: 0065
title: Steam Web API key rejection gets a typed shape, degrades the owned-games supplement instead of aborting the Family import, and is attributed separately from the family token
scope: integration
status: accepted
date: 2026-08-15
supersedes: []
superseded_by: []
related_tasks: [integration-r8kwd]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
---

# ADR 0065: Steam Web API key rejection gets a typed shape, degrades the owned-games supplement instead of aborting the Family import, and is attributed separately from the family token

## Context

After one successful Steam Family import (integration-hebjs, ADR-0061), Valve flagged the
builder's account as "probably being used by another user". Connect Steam (QR, the family
**refresh token** ceremony) kept succeeding, but every subsequent import failed with:

> `Steam Family import failed: Response status code does not indicate success: 401 (Unauthorized).`

That text is the message of an `HttpRequestException` thrown by `EnsureSuccessStatusCode()`.
The family fetches never throw on 401 — `Steam.fetchJsonWithTokenRejectable` maps 401/403 to
`Error Rejected` so `withTokenRefresh` (ADR-0019/0061) can mint-and-retry. The only call
inside `runSteamFamilyImport` (`Api.fs`) that used the throwing `fetchJson` helper *and* sat
outside the per-app `try/with` was `Steam.getOwnedGames` — a best-effort supplement that
exists only to backfill the caller's own `owner_steamids` entry (Steam's
`GetSharedLibraryApps` can omit it), authenticated by the **Steam Web API key**
(`key=`, `steam_api_key`), a credential entirely independent of the family refresh/access
token pair. Valve's standard remediation for a "possibly compromised" flag includes revoking
the account's Web API key, which fits the timeline. The generic 401 message reads as "the
family token is bad — reconnect", which sent the builder into a repeated (and useless, and
plausibly self-reinforcing — each QR reconnect looks like a new device/location login to
Valve) reconnect loop while the real, different fix (regenerate the Web API key) sat
undiscovered.

Two independent defects followed from this: (1) a failure of a best-effort supplement killed
the entire import instead of degrading, breaking the fault-isolation discipline the per-app
loop and Jellyfin sync already follow (ADR-0010); (2) the error message misattributed the
credential, sending the user down the wrong remediation path entirely.

## Decision

**1. Typed failure, ADR-0011/0019-shaped, but for a different credential.**
`Steam.SteamWebApiError = KeyRejected | WebApiOtherFailure of string` plus
`Steam.tryGetOwnedGames`, a non-throwing sibling of `getOwnedGames` that returns
`Result<SteamOwnedGame list, SteamWebApiError>` — 401/403 maps to `KeyRejected`, anything
else to `WebApiOtherFailure`. `getOwnedGames` itself is untouched (its existing callers,
`testSteamApiKey` and `importSteamLibrary`, already wrap it in their own `try/with` and have
no reason to change).

**2. The supplement degrades instead of aborting.** In `runSteamFamilyImport`, a `KeyRejected`
or `WebApiOtherFailure` result from `tryGetOwnedGames` no longer propagates — the import
proceeds with `userOwnedAppIds = Set.empty` (own-ownership simply doesn't get backfilled that
run) and exactly one line is appended to the result's `Errors`, naming the Web API key and its
remedy verbatim: `"Steam Web API key rejected (401) — generate a new key at
steamcommunity.com/dev/apikey and paste it into Settings → Steam"`. The outer `with ex ->`
catch-all in `runSteamFamilyImport` is kept as a last resort but is no longer a path any known
per-credential HTTP failure takes.

**3. The two credentials' failures never share wording, and the Web API key rejection is
persisted, not just returned one-shot.** `steam_api_key_last_error` (SettingsStore) is set on
`KeyRejected` and cleared (a) the next time `tryGetOwnedGames` succeeds, (b) `setSteamApiKey`
is called (saving a — presumably fresh — key), or (c) `testSteamApiKey` succeeds. A new RPC,
`getSteamApiKeyLastError: unit -> Async<string option>`, lets Settings → Steam render a
standing "Steam Web API key rejected" alert (mirroring `steamFamilyDetail`'s
`SteamNeedsReconnect` reconnect prompt in shape, but visually and textually distinct — no
shared component, no shared message — since the two credentials require two different user
actions).

## Alternatives considered

- **Reuse the family token's `"reconnect required: ..."` prefix convention (ADR-0061 point 3)
  for the Web API key too.** Rejected: that convention exists specifically so the client can
  substring-match one marker and show one dedicated prompt (Reconnect Steam). Overloading it
  for a second, differently-remedied credential would make the substring match ambiguous (or
  require a second marker anyway) for no real code reuse — the two remedies are different UI
  actions (QR ceremony vs. paste-and-save a key), so a second, separate persisted-notice
  mechanism (mirroring `steam_family_last_sync`'s existing persisted-scalar pattern, not the
  reconnect-prompt pattern) fits better.
- **A structured `Result<_, SteamFamilyError>`-style DU spanning both credentials.** Rejected
  for the same reason ADR-0061 rejected a structured DU for the family side: every adapter
  error in this BC is a plain string surfaced through `Result<_, string>`, and the one place
  that *does* need a typed distinction (this task's owned-games supplement, to decide
  "degrade vs. abort" before it even becomes a string) gets its own narrow `SteamWebApiError`
  DU — consistent with `Jellyfin.FetchError` and `Steam.FamilyFetchError`'s existing per-adapter,
  per-failure-mode typed shapes, not a cross-cutting one.

## Consequences

- The message `"Steam Family import failed: Response status code does not indicate success:
  401"` can no longer be produced by a rejected Web API key — the only throwing `fetchJson`
  caller inside `runSteamFamilyImport`'s untried region has been replaced.
- A rejected/revoked Web API key no longer blocks a Family import at all; it costs exactly the
  own-ownership backfill for that run plus one clear, remedy-bearing error line.
- `steam_api_key_last_error` adds a fourth persisted-scalar-notice pattern to this BC
  (alongside `steam_family_last_sync`, `JellyfinSyncStatus.SyncFailed`, and the family
  reconnect flag derived client-side from message substring) rather than unifying them — the
  BC's open question "how to expose adapter failures back to the UI consistently... varies per
  adapter" remains open; this task follows the existing pattern rather than resolving it.
- **Builder gate, outstanding at merge time:** regenerating the Web API key at
  `steamcommunity.com/dev/apikey` and saving it in Settings, then running a live family import,
  is deferred to the builder — see the task file's "Builder gate — outstanding" section. Until
  that runs, the root-cause hypothesis (revoked Web API key, not a rejected family token) is
  well-grounded by code inspection (the family fetches structurally cannot produce this
  exception text) but not yet empirically confirmed against the real Steam network.
