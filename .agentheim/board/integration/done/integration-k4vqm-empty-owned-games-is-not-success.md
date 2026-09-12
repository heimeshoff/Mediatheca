---
id: integration-k4vqm
title: An empty `GetOwnedGames` response is treated as success everywhere — the key test probes a third party's private profile and calls a good key "may be invalid", while the import and the scheduled sync silently degrade
status: done
type: bug
context: integration
created: 2026-08-18
completed: 2026-08-18
depends_on: []
blocks: []
tags: [steam, api-key, settings, sync, import, error-surfacing, privacy]
related_adrs: [0010, 0043, 0065, 0068]
related_research: []
prior_art: [integration-r8kwd, integration-003, integration-004]
---

## Why

Found while closing integration-r8kwd's builder gate on 2026-08-18. The builder regenerated
the Steam Web API key, saved it, and clicked **Test Connection**. Steam accepted the key —
and Settings reported **"API key accepted but returned no results (may be invalid)"**.

The key is fine. The test is wrong.

`testSteamApiKey` (`src/Server/Api.fs:3653-3670`) does not test the builder's account. It
substitutes a **hardcoded third-party SteamID**:

```fsharp
let testConfig: Steam.SteamConfig = {
    ApiKey = key
    SteamId = "76561197960435530" // Robin Walker (Valve employee, public profile)
}
```

and then calls that key invalid if the response is empty. `IPlayerService/GetOwnedGames`
returns `{"response":{}}` for **any** profile whose *Game details* privacy is not public —
so the test's verdict depends entirely on a stranger's privacy setting, which this project
does not control and cannot notice changing. The comment's "public profile" is a load-bearing
assumption with nothing keeping it true. Evidently it no longer is.

The deeper defect is that **an empty owned-games list is being read as a meaningful success
signal in three places**, when it is genuinely ambiguous — it means *either* "this account
owns nothing" *or* "game details are private" *or* "the probe target changed", and those are
not the same fact:

1. **`testSteamApiKey`** collapses three outcomes into two. A 401 (key rejected) and a 200
   with an empty list (key **valid**, probe inconclusive) both surface as "may be invalid" —
   the exact ambiguity integration-r8kwd existed to remove for the *other* credential, now
   reproduced here. This one actively misleads: it tells the builder a good key is bad.
2. **The family import's owned-games supplement** (`Api.fs` ~495-520, integration-r8kwd's
   work) treats `Ok []` as success and **clears** the persisted `steam_api_key_last_error`
   notice. A privacy-restricted (or otherwise empty) response therefore silently wipes a
   standing warning and proceeds with `userOwnedAppIds = Set.empty` — own-ownership never
   set, no error line, nothing surfaced.
3. **The scheduled Steam playtime sync** (`Api.fs` ~3669 area, and `PlaytimeTracker.fs`)
   uses the same call. If it returns empty, the sync silently records nothing — no games,
   no playtime, no failure. integration-r8kwd flagged this exact risk as out of its scope
   ("the same revoked key also silently breaks the scheduled Steam playtime sync … which is
   why the key rejection should be surfaced somewhere persistent"); this task is its home.

The BC already has the pattern for this: ADR-0010's persisted-result / fault-isolation
discipline, and integration-003's "surface the persisted failure in Settings".

## What

- **Fix the probe.** `testSteamApiKey` must test something this project controls. Preferred:
  the builder's own stored `steam_id` (already in `SettingsStore`, exposed via `getSteamId`).
  If no `steam_id` is stored, fall back to an endpoint whose success does not depend on any
  profile's privacy — validating the key itself rather than a profile's visibility. Remove the
  hardcoded third-party SteamID and its "public profile" assumption entirely.
- **Distinguish the three outcomes** in the test's result, in the BC's existing plain-string
  convention: *key rejected* (401/403 → reuse `Steam.KeyRejected` from ADR-0065), *key valid*
  (data returned), and *key valid but the probe returned nothing* — which must read as
  **inconclusive with a cause hint** ("key accepted; the profile's game details are private or
  the account owns no games"), never as "may be invalid".
- **Stop treating `Ok []` as an all-clear in the family import.** An empty supplement response
  must not clear `steam_api_key_last_error`; clear it only on a genuinely informative success.
  Decide (and record) whether an empty response also deserves its own non-fatal error line —
  it is a real degradation of own-ownership enrichment that is currently invisible.
- **Surface the same condition for the scheduled playtime sync** rather than letting it no-op
  silently, following ADR-0010's persisted-result pattern and integration-003's Settings
  surfacing.
- Document in the BC README that an empty `GetOwnedGames` is ambiguous and why (profile
  privacy), so the next caller does not re-derive it.

## Acceptance criteria

- [ ] `testSteamApiKey` no longer references any hardcoded third-party SteamID.
- [ ] Expecto (fake `HttpMessageHandler`, per `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs`'s
      established `TestDb.withTempDbFactory` + `createApi` shape): a **401** from the test's
      probe yields a *key rejected* result naming the regenerate remedy; a **200 with a
      non-empty list** yields success; a **200 with an empty list** yields a **distinct,
      inconclusive** result whose message does **not** claim the key may be invalid and does
      mention profile privacy as the likely cause.
- [ ] Expecto: a family import whose owned-games supplement returns `Ok []` does **not** clear
      a pre-seeded `steam_api_key_last_error` (contrast with the existing test where a genuinely
      populated success does clear it — that test must still pass unchanged).
- [ ] Expecto: a scheduled playtime sync whose owned-games call returns empty surfaces a
      persisted, user-visible indication rather than completing silently.
- [ ] BC README records the empty-response ambiguity.
- [ ] `npm test` and `npm run build` green. **No test makes a live Steam call.**

## Notes

- **The key is valid** — this task must not be read as "the key is broken". Confirmed
  2026-08-18: Steam returned 200 for the newly generated key; only the probe target was
  unhelpful. See integration-r8kwd's "Half A outcome" section.
- **Verifiable without a live import** — every criterion runs against a fake `HttpClient`. Do
  not schedule live Steam traffic to check this; see integration-w7ktb and integration-p2hxn
  for why that matters right now.
- Worth checking while in here: `getPlayerSummaries` (also Web-API-key based, used by
  `fetchSteamFamilyMembers`) already returns `Result` and is best-effort — integration-r8kwd
  left it alone but noted its 401 could reuse the same `KeyRejected` shape if cheap. Same
  question applies to its empty-response case.
- **Splittable if it grows**: the `testSteamApiKey` probe fix is the urgent, user-visible half
  (it is actively lying to the builder today); the family-import and scheduled-sync
  empty-response handling could become a follow-on if this gets large. Keep them together if
  the shared "empty is not success" shape makes one coherent change.
- Independent of integration-w7ktb, integration-p2hxn and integration-n3vqa — no dependency
  in any direction, though it touches the same `Steam.fs` / `Api.fs` neighbourhood as w7ktb
  and n3vqa, so expect merge overlap if worked concurrently.

## Outcome

Fixed all three call sites and added the doctrine ADR (0068, amends 0065).

- **`testSteamApiKey` (`Api.fs`)**: no longer references the hardcoded third-party SteamID.
  It now probes the builder's own stored `steam_id` via `Steam.tryGetOwnedGames`; if none is
  stored, it falls back to `Steam.tryValidateApiKeyOnly` (`ISteamUserStats/GetSchemaForGame`,
  appId 440 — takes no `steamid` parameter, so its success is independent of any profile's
  privacy). Three distinguishable `Result<unit, string>` outcomes: `Error
  Steam.webApiKeyRejectedMessage` (401/403, names the regenerate remedy), `Ok ()` (non-empty
  owned-games list — genuine success), and a third, distinctly-worded `Error` for an empty-
  but-200 response ("...this does not indicate a problem with the key" — never "may be
  invalid", names profile privacy as the likely cause).
- **Family import owned-games supplement (`Api.fs`'s `runSteamFamilyImport`)**: `Ok []` no
  longer clears `steam_api_key_last_error` (only a genuinely populated `Ok games` does) and
  now appends its own non-fatal `Errors` line, worded distinctly from `KeyRejected`'s.
- **Scheduled playtime sync (`PlaytimeTracker.runSync`)**: switched from the throwing
  `Steam.getRecentlyPlayedGames` to the new non-throwing `Steam.tryGetRecentlyPlayedGames`. A
  `KeyRejected` result now persists `steam_api_key_last_error` (Settings → Steam's existing
  notice picks it up with no new UI work) and fails the run with an attributed message,
  instead of an opaque, unattributed `HttpRequestException` escaping to a generic catch-all. A
  genuinely empty (`Ok []`, "nothing played recently") result is deliberately left alone — see
  ADR-0068's "alternatives considered" for why this endpoint's emptiness is not flagged the
  same way `GetOwnedGames`'s is (it's the routine, frequent case, not evidence of anything
  wrong).
- **`Steam.fs`**: added `webApiKeyRejectedMessage` (one shared remedy string, replacing three
  independently-worded copies), `tryValidateApiKeyOnly`, and `tryGetRecentlyPlayedGames`.
- **BC README**: extended the "Web API key" ubiquitous-language entry with the empty-response
  ambiguity and how each of the three call sites now handles it.
- **ADR 0068** (amends ADR-0065) records the three-outcome design, the "don't flag routine
  emptiness" alternative considered and rejected for `GetOwnedGames` but accepted for
  `GetRecentlyPlayedGames`, and the shared remedy-string decision.
- **Tests** (all against a fake `HttpMessageHandler` — no live Steam call in any test):
  `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs` — updated the pre-existing
  "successful owned-games call clears the notice" test to use a genuinely non-empty stub (it
  previously stubbed an empty response while asserting a clear, which was itself the bug this
  task fixes), added a new test asserting `Ok []` does NOT clear a pre-seeded notice, seeded
  `steam_id` in the pre-existing `testSteamApiKey` success test (now required by the fixed
  probe), and added a new `testSteamApiKeyThreeOutcomesTests` list (5 cases: no hardcoded
  SteamID referenced, 401 → key-rejected naming the remedy, 200 non-empty → success, 200 empty
  → inconclusive/privacy-worded, no-stored-steam_id → key-only fallback probe).
  `tests/Server.Tests/PlaytimeSyncKeyRejectionTests.fs` (new file, 2 cases): a rejected key
  surfaces the persisted, attributed notice instead of completing silently; a genuinely empty
  response completes normally without touching the notice.
- `npm test`: 704/704 Expecto tests passing (run twice, consistent). `npm run build`: clean.

Key files: `src/Server/Steam.fs`, `src/Server/Api.fs`, `src/Server/PlaytimeTracker.fs`,
`tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs`,
`tests/Server.Tests/PlaytimeSyncKeyRejectionTests.fs`,
`tests/Server.Tests/Server.Tests.fsproj`,
`.agentheim/knowledge/decisions/0068-steam-empty-owned-games-is-inconclusive-not-failure.md`,
`.agentheim/contexts/integration/README.md`.
