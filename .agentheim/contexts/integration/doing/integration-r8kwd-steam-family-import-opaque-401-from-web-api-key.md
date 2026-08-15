---
id: integration-r8kwd
title: Steam Family import aborts with an opaque 401 that comes from the Web-API-key `GetOwnedGames` supplement, not the family token — make the supplement non-fatal and attribute credential failures to the right credential
status: doing
type: bug
context: integration
created: 2026-08-15
completed:
depends_on: []
blocks: [integration-n3vqa]
tags: [steam, steam-family, auth, token, settings, import, error-surfacing]
related_adrs: [0011, 0019, 0061]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
prior_art: [integration-hebjs, integration-ygwsa, integration-002, integration-004]
---

## Why

The one-click Steam Family import (integration-hebjs, ADR-0061) worked once. Shortly after,
Valve flagged the builder's Steam account as "probably being used by another user" (the
don't-get-scammed warning). Since then, **Connect Steam (QR) still succeeds**, but every
family import fails with:

> `Steam Family import failed: Response status code does not indicate success: 401 (Unauthorized).`

That message is diagnostic. It is the text of an `HttpRequestException` thrown by
`EnsureSuccessStatusCode()`, and the *family* fetches never throw on 401 — `Steam.fetchJsonWithTokenRejectable`
(`src/Server/Steam.fs:396-411`) maps 401/403 to `Error Rejected` so `withTokenRefresh` can
mint-and-retry, and a still-rejected family token would read *"Steam rejected the family token
again after minting a fresh one"* or *"reconnect required: …"*. The only call inside
`runSteamFamilyImport` (`src/Server/Api.fs:422-725`) that uses the throwing `fetchJson`
helper *and* sits outside the per-app `try/with` is the **`Steam.getOwnedGames` supplement at
`Api.fs:491`** — the Steam *Web API key* + `steamid` call (`IPlayerService/GetOwnedGames`) that
adds the user's own ownership to `owner_steamids` because `GetSharedLibraryApps` may omit it.
The exception escapes to the outer `with ex -> Error $"Steam Family import failed: {ex.Message}"`.

So the family token path is almost certainly fine (the QR reconnect proves the refresh token
works, and `GetFamilyGroupForUser` + `GetSharedLibraryApps` had already succeeded when the
crash happened) — what is being rejected is the **Steam Web API key** stored in
`steam_api_key`. Steam answers a revoked/invalid `key=` with exactly HTTP 401 ("Access is
denied. Retrying will not help. Please verify your key= parameter"). Valve's standard
remediation when it flags an account as possibly compromised includes **revoking the
account's Web API key** — which fits the timeline precisely.

Two defects follow, independent of whether that root-cause hypothesis is confirmed:

1. **A failure of a best-effort supplement kills the whole import.** `getOwnedGames` exists only
   to add the user's own steamid to `OwnerSteamids`; it is not what the import is *for*. It
   should degrade (import continues, own-ownership not set, one clear error line) — the same
   fault-isolation discipline the per-app loop and the Jellyfin sync already follow (ADR-0010).
2. **The error misattributes the credential.** "Steam Family import failed … 401" reads as
   *the family token is bad — reconnect*, so the builder reconnected repeatedly to no effect.
   The two credentials must fail with two distinguishable, remedy-bearing messages:
   family token → *"reconnect required — Connect Steam in Settings"* (already exists);
   Web API key → *"Steam Web API key rejected (401) — generate a new key at
   steamcommunity.com/dev/apikey and paste it into Settings → Steam"*.

The same revoked key also silently breaks the scheduled Steam playtime sync
(`Api.fs:3669`, `getOwnedGames` again) — that path is *not* this task's scope but is why the
key rejection should be surfaced somewhere persistent, not just in a one-shot import toast.

## What

- In `runSteamFamilyImport`, wrap the `Steam.getOwnedGames` supplement so an HTTP failure
  (any exception, 401 included) becomes: import proceeds with `userOwnedAppIds = Set.empty`,
  and one entry is appended to `errors` naming the cause and the remedy (see criteria).
- Give `Steam.getOwnedGames` failure a typed shape callers can distinguish (e.g. return
  `Result<SteamOwnedGame list, SteamWebApiError>` where a 401/403 is `KeyRejected` and anything
  else is `Other of string`) — or, minimally, a sibling `tryGetOwnedGames` used by the import
  and the scheduled sync — so "key rejected" is a first-class outcome, not a string match on
  `ex.Message`. Prefer keeping the existing `getOwnedGames` signature for callers you don't touch.
- Persist the last Web-API-key rejection (e.g. `steam_api_key_last_error` in `SettingsStore`,
  cleared on the next success or on `setSteamApiKey`) so the Settings → Steam section can show
  a standing "API key rejected — regenerate" notice with the remedy link, next to the existing
  "Test key" affordance (mirror `steam_family_*` / Jellyfin `SyncFailed` surfacing,
  integration-003).
- Keep the outer `with ex ->` in `runSteamFamilyImport` as a last resort, but no known
  per-credential HTTP failure should reach it any more.
- Add an Expecto test that a 401 from the owned-games supplement does **not** fail the import
  and does surface the attributed error line; add a unit test for the typed 401 → `KeyRejected`
  mapping. (Family-fetch tests from integration-hebjs already pin the mint-and-retry side.)

## Acceptance criteria

- [ ] With a valid family refresh token and a Web API key Steam answers with 401, "Import family
      library" **completes**: games are created/matched, family owners for *friends* are set,
      `steam_family_last_sync` is written, and the result carries exactly one error line that
      names the Web API key (not the family token) and the remedy
      (`steamcommunity.com/dev/apikey` → Settings → Steam).
- [ ] The message `Steam Family import failed: Response status code does not indicate success: 401`
      can no longer be produced by a rejected Web API key — the generic outer handler is not the
      path a credential rejection takes.
- [ ] A rejected/expired **family** token still yields the existing `reconnect required: …` /
      "rejected the family token again" wording — the two credentials never share a message.
- [ ] Settings → Steam shows a persistent "Steam Web API key rejected" notice (with the
      regenerate remedy) after such a failure, cleared once a key is saved/tested successfully.
- [ ] Expecto: owned-games supplement 401 → import `Ok` with the attributed error; typed
      401/403 → `KeyRejected`. `npm test` green; `npm run build` green.
- [ ] Builder gate: after regenerating the Web API key at steamcommunity.com/dev/apikey and
      saving it in Settings, a live family import succeeds end to end (this also confirms or
      refutes the root-cause hypothesis — record the outcome in the task file when closing).

## Notes

- **Root-cause hypothesis to verify first (5 minutes):** in Settings → Steam, run "Test key"
  with the stored key; if it fails, open https://steamcommunity.com/dev/apikey — a revoked key
  shows as no key registered. Regenerating and saving it should make the import work again
  *today*, before this task ships. If "Test key" passes, the hypothesis is wrong: re-check
  which `fetchJson` caller threw (add the failing URL host to the exception surface as a first
  step) — but do the resilience + attribution work regardless.
- Why the account got flagged is outside this repo's control, but note for the builder: the QR
  Connect logs the server in as a **new `MobileApp`-platform persistent session** (ADR-0061),
  from the Docker host's IP — from Valve's side that looks like a new device signing in from a
  new location. Repeated reconnect attempts while the import kept failing would only have
  reinforced that signal. Reducing the number of reconnects is a side benefit of fixing the
  misattribution.
- Do **not** touch the family-token mint-and-retry seam (`withTokenRefresh`, ADR-0019/0061) —
  it behaved correctly here; the bug is entirely in the API-key call and its error surfacing.
- `getPlayerSummaries` (also API-key based, used by `fetchSteamFamilyMembers`) already
  returns `Result` and is best-effort — leave it, but its 401 could reuse the same `KeyRejected`
  shape if cheap.
- Follow-on: integration-n3vqa (incremental "what's new in the family library") depends on
  this — an import that aborts can't report arrivals.
