---
id: integration-r8kwd
title: Steam Family import aborts with an opaque 401 that comes from the Web-API-key `GetOwnedGames` supplement, not the family token — make the supplement non-fatal and attribute credential failures to the right credential
status: done
type: bug
context: integration
created: 2026-08-15
completed: 2026-08-15
depends_on: []
blocks: [integration-n3vqa]
tags: [steam, steam-family, auth, token, settings, import, error-surfacing]
related_adrs: [0011, 0019, 0061, 0065]
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

- [x] With a valid family refresh token and a Web API key Steam answers with 401, "Import family
      library" **completes**: games are created/matched, family owners for *friends* are set,
      `steam_family_last_sync` is written, and the result carries exactly one error line that
      names the Web API key (not the family token) and the remedy
      (`steamcommunity.com/dev/apikey` → Settings → Steam).
      Verified by `SteamFamilyImportOwnedGamesTests.fs`'s first case (a stubbed shared app is
      still created and owned; "family owners for friends" specifically is not re-exercised here
      — that per-app loop code is untouched by this task and already covered elsewhere — but the
      owned-games supplement's 401 no longer prevents it from running at all).
- [x] The message `Steam Family import failed: Response status code does not indicate success: 401`
      can no longer be produced by a rejected Web API key — the generic outer handler is not the
      path a credential rejection takes.
- [x] A rejected/expired **family** token still yields the existing `reconnect required: …` /
      "rejected the family token again" wording — the two credentials never share a message
      (untouched code path; existing `SteamFamilyTokenTests.fs` coverage still passes unchanged,
      and the new supplement error text is asserted to never contain "reconnect required").
- [x] Settings → Steam shows a persistent "Steam Web API key rejected" notice (with the
      regenerate remedy) after such a failure, cleared once a key is saved/tested successfully.
- [x] Expecto: owned-games supplement 401 → import `Ok` with the attributed error; typed
      401/403 → `KeyRejected`. `npm test` green; `npm run build` green.
- [ ] Builder gate: after regenerating the Web API key at steamcommunity.com/dev/apikey and
      saving it in Settings, a live family import succeeds end to end (this also confirms or
      refutes the root-cause hypothesis — record the outcome in the task file when closing).

## Builder gate — outstanding

> **AMENDED 2026-08-18 — step 3 below (run a live family import) is SPLIT and DEFERRED. Do not
> run a full family import to close this gate.** After this task shipped, the builder reported
> that Valve had alerted the account *twice*, each time following a family import: *"Your
> accounts appear to be using the Steam API in the same way a certain brand of account
> hijacking does."* A full import is therefore the exact act under suspicion, and this gate
> must not be the thing that triggers a third flag. The gate is split into a safe half that can
> run now and a deferred half with a named owner. See integration-w7ktb (Adapter-owned
> storefront throttle), integration-n3vqa (incremental import) and integration-p2hxn (the
> accepted-risk ADR).

The last acceptance criterion, and the Notes' "verify the root-cause hypothesis first (Test
key)" step, are deferred to the builder — they need the builder's real Steam account and a
browser, neither of which the implementing session has. Everything else in this task is done
and tested.

**Half A — safe to run now.** This fully answers *"is the key valid again"* and
confirms/refutes this task's root-cause hypothesis. It costs a **single** `GetOwnedGames`
request, not a library sweep:

0. **First, rule out an actual compromise.** Open https://steamcommunity.com/dev/apikey and
   confirm the registered key is *yours*, or that none is registered. An unfamiliar key or
   domain there is the literal hijack pattern Valve's warning describes, and would mean the
   account is genuinely compromised — stop and deal with that instead. Also check Steam Support
   messages, review authorized devices, and confirm mobile Steam Guard is on.
1. Regenerate the key at that page and paste it into Settings → Steam → **Save**. (Saving
   clears the "Steam Web API key rejected" notice this task added, if one is showing.)
2. Click **Test Connection**. One request. Confirm it succeeds and the notice has cleared.
3. Record the outcome here: root-cause confirmed (the old key was revoked) or refuted.

### Half A outcome — 2026-08-18: ROOT CAUSE CONFIRMED

The builder regenerated the Web API key at steamcommunity.com/dev/apikey, saved it in
Settings → Steam, and ran **Test Connection**. Result: `API key accepted but returned no
results (may be invalid)`.

**That message confirms the hypothesis rather than undermining it.** It is
`testSteamApiKey`'s empty-list branch (`Api.fs:3660`), which is only reachable **after a
2xx** — a rejected key throws in `fetchJson`'s `EnsureSuccessStatusCode` and lands in the
`with ex ->` branch as `Steam API key validation failed: … 401`. So Steam **accepted** the
new key. The same credential path that threw 401 in production now returns 200 with only
the key changed, which is the decisive evidence: the old key had been revoked, exactly as
this task's Why predicted for Valve's compromise-flag remediation, and that revoked key is
what produced the opaque `Steam Family import failed: … 401`.

**Honest caveat:** Test Connection was never run against the *old* key (the builder
regenerated first), so revocation is inferred from the production 401 plus the
new-key-returns-200 contrast rather than observed directly at the moment of test. The chain
is strong but one step short of airtight. If the builder noted whether
steamcommunity.com/dev/apikey showed *no key registered* before regenerating, that would
settle it outright — record it here if so.

**The "returned no results" half is a separate, newly-found defect, NOT a key problem** —
`testSteamApiKey` probes a hardcoded third-party SteamID (`76561197960435530`, commented
"Robin Walker (Valve employee, public profile)") whose game-details privacy is outside this
project's control, and `GetOwnedGames` answers `{"response":{}}` for any profile whose game
details are not public. Captured as **integration-k4vqm**. The key itself is fine.

**Half A is therefore closed and this task's diagnosis is settled.** Half B (a live family
import) remains deferred to integration-n3vqa per below.

**Half B — deferred, owner named.** *"A live family import succeeds end to end"* is **not**
run now. It is discharged by **integration-n3vqa's** own builder gate: that task's first live
import will be a small incremental one (a handful of requests) rather than today's
several-hundred-request sweep, and integration-w7ktb will have paced whatever remains. Closing
Half A is sufficient to consider this task's own diagnosis settled; Half B rides with n3vqa.

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

## Outcome

Root-caused and fixed per the task's diagnosis: `Steam.getOwnedGames`'s throwing `fetchJson`
was the only call inside `runSteamFamilyImport` outside a `try/with` that could produce the
opaque `EnsureSuccessStatusCode` 401 text, and it authenticates with the Web API key, not the
family token.

- `Steam.fs`: added `SteamWebApiError = KeyRejected | WebApiOtherFailure of string` and
  `Steam.tryGetOwnedGames`, a non-throwing sibling of `getOwnedGames` (which is untouched,
  along with its existing callers `testSteamApiKey`/`importSteamLibrary`).
- `Api.fs`'s `runSteamFamilyImport`: the owned-games supplement now degrades on any failure
  (`userOwnedAppIds = Set.empty`, one attributed error line appended to `Errors`) instead of
  aborting the import; `KeyRejected` persists `steam_api_key_last_error` (SettingsStore),
  cleared on the next successful call, on `setSteamApiKey`, or on a successful
  `testSteamApiKey`. Added RPC `getSteamApiKeyLastError`.
- Settings → Steam (`Client/Pages/Settings/{Types,State,Views}.fs`): a standing "Steam Web API
  key rejected" alert, distinct in wording and component from the existing family-token
  "Reconnect Steam" prompt, sourced from the new RPC and refreshed after a save, a successful
  test, or a family import completes.
- ADR 0065 records the decision (typed shape, degrade-not-abort, separate persisted-notice
  convention, alternatives considered).
- Tests: `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs` (originally 5 Expecto cases,
  now 7 — see iteration 2 below) — the 401→`KeyRejected`/403→`KeyRejected`/500→`WebApiOtherFailure`
  typed mapping, and two `IMediathecaApi.importSteamFamily` end-to-end cases (a 401 from the
  supplement still creates and owns a stubbed shared app, appends exactly one attributed
  non-"reconnect required" error line, and still writes `steam_family_last_sync`; a subsequent
  successful owned-games call clears a previously-persisted notice).
- Builder gate (regenerate the Web API key, save, run a live import) is **not yet run** — see
  the "Builder gate — outstanding" section above for exactly what to do and record when closing.

### Iteration 2 (addressing the verifier's FAIL above)

Added the two Expecto cases the verifier named, directly pinning acceptance criterion 4's
"cleared once a key is saved/tested successfully" clause, which iteration 1 had left implied by
production code but untested:

- `setSteamApiKey clears a stale steam_api_key_last_error notice` — seeds the setting, calls
  `api.setSteamApiKey "fresh-key"`, asserts it's gone. Pins `Api.fs:3647`.
- `testSteamApiKey clears a stale steam_api_key_last_error notice on a successful test` — seeds
  the setting, stubs `GetOwnedGames` with a 200 and one non-empty game (required — an empty list
  hits the `"API key accepted but returned no results"` `Error` branch before the clear, per the
  verifier's own caveat), calls `api.testSteamApiKey "fresh-key"`, asserts the setting is gone.
  Pins `Api.fs:3666`.

No production code changed — both clear points already behaved correctly; they were simply
untested. `npm test`: 694 passing (+2 over iteration 1's 692, +7 total from this task).
`tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs` is the only file touched in this
iteration.

Key files: `src/Server/Steam.fs`, `src/Server/Api.fs`, `src/Shared/Shared.fs`,
`src/Client/Pages/Settings/Types.fs`, `src/Client/Pages/Settings/State.fs`,
`src/Client/Pages/Settings/Views.fs`, `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs`,
`.agentheim/knowledge/decisions/0065-steam-web-api-key-typed-rejection-and-fault-isolation.md`,
`.agentheim/contexts/integration/README.md`.

## Verifier note (iteration 1)

**VERDICT: FAIL**

**REASONS:**
- Acceptance criterion 4 ("Settings → Steam shows a persistent 'Steam Web API key rejected' notice
  ..., **cleared once a key is saved/tested successfully**") has no executable coverage for its
  clearing half. The two production lines that implement it —
  `SettingsStore.deleteSetting conn "steam_api_key_last_error"` at `src/Server/Api.fs:3647`
  (`setSteamApiKey`) and `src/Server/Api.fs:3666` (`testSteamApiKey`) — can both be deleted and the
  full suite still reports 692/692 passing. The only clearing path pinned by a test is a *different*
  trigger the criterion does not name:
  `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs:149` "A subsequent successful owned-games
  call clears the persisted last-error notice" (the `Ok games` branch at `Api.fs:510`). This is
  server-side, non-visual behavior fully testable with the harness already present in the new test
  file (`createApi` + `TestDb.withTempDbFactory`), so the UI manual-exercise carve-out does not apply
  to it, and it is the exact behavior the deferred builder gate leans on (task file step 2: "Saving
  now also clears the ... notice").

**SUGGESTED_FIX:** Add two Expecto cases to `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs`
using the existing `createApi`/`TestDb` harness: seed `steam_api_key_last_error`, call
`api.setSteamApiKey "fresh-key"` and assert the setting is gone; and with a stub `HttpClient`
answering `GetOwnedGames` 200 with a non-empty game list, call `api.testSteamApiKey "fresh-key"` and
assert the same. Nothing else needs to change — criteria 1, 2, 3 and 5 verified (692 Expecto tests
pass, `npm run build` clean, family-token `"reconnect required: ..."` path in
`src/Server/Steam.fs:596,614` untouched, no other Web-API-key call sits outside a `try/with` inside
`runSteamFamilyImport`, `deleteSetting` exists at `src/Server/SettingsStore.fs:43`,
scope/README/ADR-0065/related-ADR checks all clean, builder-gate criterion correctly deferred).

**ITERATION_HINT:** likely-fixable
