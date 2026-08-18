---
id: 0068
title: An empty Steam owned/recently-played-games response is inconclusive, not success or failure — three call sites stop treating it as either
scope: integration
status: accepted
date: 2026-08-18
supersedes: []
superseded_by: []
amends: [0065]
related_tasks: [integration-k4vqm]
related_research: []
---

# ADR 0068: An empty Steam owned/recently-played-games response is inconclusive, not success or failure — three call sites stop treating it as either

## Context

Closing integration-r8kwd's builder gate (regenerate the Steam Web API key, click **Test
Connection**) surfaced a second, independent defect: Steam accepted the freshly regenerated
key, and Settings still reported **"API key accepted but returned no results (may be
invalid)"**. `testSteamApiKey` tested a hardcoded third-party SteamID (a Valve employee's
"public profile") instead of anything this project controls, and Steam's
`IPlayerService/GetOwnedGames` returns the identical `{"response":{}}` shape whether an
account owns nothing or its **Game Details** privacy is not Public — a fact this project
neither controls nor can observe changing. The comment's "public profile" assumption was
already stale by the time it was read.

The same anti-pattern — reading an empty owned-games-shaped response as a meaningful success
or failure signal — recurred in two more places: the Family import's owned-games supplement
(`Api.fs`'s `runSteamFamilyImport`, integration-r8kwd's own work) unconditionally cleared the
persisted `steam_api_key_last_error` notice on `Ok []`, treating "no data" as "the key
definitely works"; and the scheduled playtime sync (`PlaytimeTracker.runSync`) called the
throwing `Steam.getRecentlyPlayedGames`, which let a rejected key (401) escape as an opaque,
unattributed `HttpRequestException` instead of the same typed, persisted outcome the Family
import already has for the sibling credential failure.

## Decision

**1. `testSteamApiKey` tests a profile this project actually controls.** It probes the
builder's own stored `steam_id` (`SettingsStore`) via `Steam.tryGetOwnedGames`. If no
`steam_id` is stored yet, it falls back to `Steam.tryValidateApiKeyOnly` —
`ISteamUserStats/GetSchemaForGame` against a fixed appId (440, Team Fortress 2), which takes
no `steamid` parameter at all, so its success depends solely on the key. The hardcoded
third-party SteamID is removed entirely.

**2. Three distinguishable outcomes, in the BC's existing `Result<_, string>` convention.**
`Error` carrying `Steam.webApiKeyRejectedMessage` (a 401/403, naming the regenerate remedy) is
distinct in wording from `Ok ()` (a non-empty result — genuine success) and from a third,
new `Error` message for the empty-but-200 case: *"Steam API key accepted; the profile's Game
Details privacy is not Public, or the account owns no games — this does not indicate a
problem with the key."* No new type is introduced — a fourth Result case would fit the
semantics better, but the BC's plain-string convention (ADR-0065's own alternatives-considered
section rejected a structured DU for exactly this reason) already extends to three outcomes by
message content, and a real UI difference (e.g. a neutral "inconclusive" banner rather than a
red error) is left as a follow-up, not blocking this fix.

**3. `Ok []` is never treated as an all-clear, and never silently proceeds unremarked.** The
Family import's owned-games supplement no longer clears `steam_api_key_last_error` on an
empty result — only a genuinely populated (`Ok games` with `games <> []`) result does. An
empty result now also appends its own non-fatal `Errors` line, worded distinctly from
`KeyRejected`'s (mentions privacy, never "rejected"), so the real degradation (own-ownership
not backfilled this run) is no longer invisible.

**4. The scheduled playtime sync gets the same typed-and-persisted treatment as the Family
import, for the SAME reason and the SAME credential.** `Steam.tryGetRecentlyPlayedGames`
(non-throwing, mirrors `tryGetOwnedGames`) replaces the throwing `getRecentlyPlayedGames`
inside `PlaytimeTracker.runSync`. A `KeyRejected` result persists `steam_api_key_last_error`
(the exact same setting the Family import and `testSteamApiKey` already read/write — Settings
→ Steam's existing notice UI picks it up automatically, no new UI work) and fails the run with
an attributed message, rather than the sync's own generic catch-all producing an opaque
"Playtime sync failed: ...401..." with no remedy. A genuinely empty (`Ok []`,
"nothing played in the last two weeks") result is deliberately left alone — unlike
`GetOwnedGames`, an empty `GetRecentlyPlayedGames` result is the ordinary, frequent steady
state (any day the user didn't play recently), not itself evidence of anything wrong; flagging
it every time would be false-alarm noise, not a fix.

**5. One shared remedy string.** `Steam.webApiKeyRejectedMessage` replaces three
independently-worded copies of the same "Steam Web API key rejected (401) — generate a new
key..." message (`testSteamApiKey`, the Family import, and now the scheduled sync), so a
future edit to the remedy text only has to happen once.

## Alternatives considered

- **Give the empty-but-200 case its own type (e.g. a three-case DU) instead of a differently-
  worded `Error`.** Rejected for the same reason ADR-0065 rejected a cross-cutting structured
  DU: the BC's plain-string convention already carries this distinction by message content, and
  a real type change would touch `Shared.fs`'s `IMediathecaApi` contract and the client's
  `Result<string, string>` handling for no behavioral gain this task needs.
- **Flag every empty `GetRecentlyPlayedGames` result as a degraded sync, matching the Family
  import's non-fatal-error-line treatment of empty `GetOwnedGames`.** Rejected: the two
  endpoints' emptiness has different base rates and different meanings — `GetOwnedGames`
  empty is unusual (an account that owns nothing, or a privacy flip) and worth a one-line flag
  every time; `GetRecentlyPlayedGames` empty is the routine, expected result on any day the
  user hasn't played anything in the trailing two weeks. Treating them identically would make
  the scheduled sync noisy on a near-daily basis for no diagnostic benefit — only a genuine key
  rejection (401/403) is flagged for this call site.
- **Add a live-Steam builder gate to confirm the fix.** Rejected outright for this task: Valve
  has twice flagged the builder's account over the Family import's traffic pattern
  (integration-w7ktb, integration-p2hxn); this task is verified entirely against a fake
  `HttpMessageHandler`, with no live Steam call in any test or verification step.

## Consequences

- `testSteamApiKey` can no longer report a genuinely valid key as "may be invalid" due to a
  stranger's privacy setting — it either tests the builder's own account or a
  privacy-independent key-only probe.
- `steam_api_key_last_error` only ever gets cleared by an unambiguous, genuinely informative
  success, matching the same standard already applied to setting it.
- The scheduled playtime sync's rejected-key failure is now attributable and persisted exactly
  like the Family import's, without any new Settings UI — it reuses the existing notice.
- `Steam.SteamWebApiError`/`tryGetOwnedGames`'s typed-rejection shape (ADR-0065) now has three
  call sites (`testSteamApiKey`, the Family import, the scheduled sync) instead of one,
  reinforcing it as the BC's standard shape for this credential's failures rather than a
  one-off.

## References

- `.agentheim/knowledge/decisions/0065-steam-web-api-key-typed-rejection-and-fault-isolation.md` —
  the typed-rejection/degrade-not-abort pattern this ADR extends to two more call sites.
- `.agentheim/knowledge/decisions/0010-jellyfin-sync-observability-fault-isolation.md` —
  the persisted-result / fault-isolation discipline the scheduled-sync half follows.
- `src/Server/Steam.fs` (`webApiKeyRejectedMessage`, `tryValidateApiKeyOnly`,
  `tryGetRecentlyPlayedGames`), `src/Server/Api.fs` (`testSteamApiKey`,
  `runSteamFamilyImport`'s owned-games supplement), `src/Server/PlaytimeTracker.fs` (`runSync`).
