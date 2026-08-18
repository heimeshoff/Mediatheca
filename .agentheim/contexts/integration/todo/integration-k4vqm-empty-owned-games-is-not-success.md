---
id: integration-k4vqm
title: An empty `GetOwnedGames` response is treated as success everywhere — the key test probes a third party's private profile and calls a good key "may be invalid", while the import and the scheduled sync silently degrade
status: todo
type: bug
context: integration
created: 2026-08-18
completed:
depends_on: []
blocks: []
tags: [steam, api-key, settings, sync, import, error-surfacing, privacy]
related_adrs: [0010, 0043, 0065]
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
