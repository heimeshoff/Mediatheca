---
id: integration-v0xmv
title: Remove the Steam Connect QR login and the refresh-token mint path — the Steam Family import runs only on a browser-obtained access token pasted in Settings, and Mediatheca never performs a Steam login or token mint again
status: done
type: refactor
context: integration
created: 2026-09-05
completed: 2026-09-05
depends_on: [design-system-001]
blocks: []
tags: [steam, steam-family, auth, token, settings, import, account-safety, removal]
related_adrs: [0019, 0061, 0065, 0067, 0070]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
prior_art: [integration-hebjs, integration-zwnh4, integration-p2hxn, integration-ygwsa, integration-r8kwd]
---

## Why

Valve has now escalated from three "this account may have been accessed by someone else"
alerts to an explicit threat: **the account will be permanently banned if the login API is
used against Steam's rules again** (builder, 2026-09-05). This ends ADR-0067's "accepted
risk + escalation ladder" framing. The ladder assumed we could keep the login-shaped traffic
and pay for each further alert with a cheaper mitigation; a ban threat makes any further
server-side Steam login — even one — unaffordable.

The evidence points at exactly one thing. ADR-0067's 2026-09-04 amendment traced the third
alert, to the minute, to `GET /api/stream/steam-connect` — the SteamKit2 QR ceremony
(`SteamConnect.fs`, integration-hebjs) that pretends to be the Steam mobile app
(`PlatformType = MobileApp`, persistent session) to obtain a refresh token, from which the
server then mints access tokens via `IAuthenticationService/GenerateAccessTokenForApp`. Both
of those are Steam's **login API**, driven by a third-party program. The enumeration half of
the signature was already fixed and live at the time (ADR-0066 spacing, ADR-0069 count) and
did not stop the alert. The manual mode that preceded integration-hebjs — the builder copies
their own browser session's `access_token` and pastes it into Settings — ran without any
alert, and touches no login API at all: the app never authenticates, it only *uses* a token
the builder's own logged-in browser already holds.

So the decision is made, not modeled: **remove the QR login and every code path that can
mint a Steam token, and make the paste-a-token flow the one and only way in.** This reverses
integration-hebjs's product goal (one-click family import) deliberately; token lifetime
(~24h for a `webapi_token` from `ajaxgetasyncconfig`) becomes user-facing again, and that is
the price of keeping the account.

## Compliance check of the remaining import shape (recorded here, restated in the ADR)

What the Steam Web API Terms of Use (steamcommunity.com/dev/apiterms) actually bind us to:
≤100,000 calls/day; never intercept or store the end user's Steam password; abide by the
documentation; don't violate the Subscriber Agreement; don't degrade Steam. Valve's own
alert wording — "using the Steam API in the same way a certain brand of account hijacking
does" — is about the *login* shape, not volume.

After this task the family import consists of, per click:
- **`IFamilyGroupsService/GetFamilyGroupForUser` + `GetSharedLibraryApps` (+ `GetFamilyGroup`
  for member discovery)** — 2–3 GETs, authenticated with the builder's *own* browser
  `access_token` (`webapi_token`), i.e. the exact calls the Steam store's own family page
  makes. Undocumented interface (ADR-0019 already records this), read-only, and no login
  performed by the app. This is the residual gray area and it is the same one the pre-hebjs
  mode lived in without incident; it cannot be made "documented" from our side. Every other
  known family-library tool (research report [9], FamilyBot) works this way too.
- **`IPlayerService/GetOwnedGames`** via the Web API key — documented, key-authenticated,
  best-effort supplement (ADR-0065/0068 unchanged).
- **`store.steampowered.com/api/appdetails`** — public, unauthenticated, Adapter-throttled
  at 1500ms (ADR-0066), only for genuinely *new* apps (ADR-0069).
- **No call to `api.steampowered.com/IAuthenticationService/*` of any kind**, no SteamKit2,
  no CM connection, no stored refresh token, no device registered on the account.

Verdict: compliant with every explicit clause; the one gray area (undocumented interface) is
inherent to the feature and predates the incidents. **What is removed is the only part that
plausibly qualifies as "using the login API against the rules".** Import stays a manual click
— never scheduled (concept-page open question is thereby closed: no).

## What

Delete the QR ceremony, the refresh-token storage and the token-mint path end to end, then
re-promote manual token entry from a demoted fallback to the primary (and only) flow, with
the token-retrieval instructions rewritten around the easier `ajaxgetasyncconfig` route.

**Server**
- Delete `src/Server/SteamConnect.fs`; remove its `<Compile>` entry and the `SteamKit2` and
  `QRCoder` `PackageReference`s (with their integration-hebjs comment) from `Server.fsproj`.
  Nothing else references either package (verified by grep).
- `Composition.fs`: remove the `/api/stream/steam-connect` route. Add a one-time startup
  cleanup that `SettingsStore.deleteSetting conn "steam_family_refresh_token"` — the stored
  refresh token is a live Steam credential the app must no longer hold.
- `Api.fs`: remove `steamConnectStreamHandler` and the `getSteamConnectionStatus`
  implementation. `runSteamFamilyImport` and `fetchSteamFamilyMembers` read only
  `steam_family_token` and call the plain `Steam.getFamilyGroupForUser` /
  `getSharedLibraryApps` / `getFamilyGroup` — no `*WithRefresh`, no `persistAccessToken`
  lambda, no re-reading the token between steps.
- `Steam.fs`: delete `mintFamilyAccessToken`, `steamIdFromRefreshToken`,
  `GenerateAccessTokenResponse`, `TokenMinter`, `withTokenRefresh`, and the three
  `*WithRefresh` wrappers. Keep `FamilyFetchError` (`Rejected` vs `FamilyOtherFailure`) and
  the `fetch*` functions — the 401/403 distinction is still needed for attribution.
  `mapFamilyFetchError` maps `Rejected` to a fixed, typed message prefixed
  **`"family token rejected: "`** (e.g. `family token rejected: your Steam Family access
  token has expired or was rejected — paste a fresh one in Settings → Steam Family`). It must
  stay wording-distinct from the Web API key rejection message (ADR-0065's attribution rule
  is unchanged). The `"reconnect required: "` prefix disappears from the codebase.
- Whole-server invariant: **no source file contains `IAuthenticationService`** (grep-clean),
  and no code path opens a Steam login session or mints a Steam token.

**Shared**
- Remove `getSteamConnectionStatus` from `IMediathecaApi`.

**Client — Settings → Steam Family (`Pages/Settings/{Types,State,Views}.fs`)**
- Remove `SteamConnected`, `IsConnectingSteam`, `SteamConnectQrDataUrl`, `SteamConnectError`
  and the messages `Load_steam_connect_status`, `Steam_connect_status_loaded`,
  `Start_steam_connect`, `Steam_connect_qr_received`, `Steam_connect_completed`, plus the
  SSE reader for `/api/stream/steam-connect`. Remove `Load_steam_connect_status` from the
  init batch.
- Rename `SteamNeedsReconnect` → `SteamFamilyTokenRejected`; `isReconnectRequired` →
  `isFamilyTokenRejected`, detecting the new `"family token rejected"` prefix. Set from the
  same three places as today (fetch members, import, re-enrich); cleared on a successful
  token save.
- Views: delete the Connect/Reconnect block and the QR image. The token entry (input + Save)
  moves out of the "Manual token entry (fallback)" collapse into the card body as the primary
  step, preceded by the how-to. When `SteamFamilyTokenRejected` is set, show a warning alert
  "Your Steam Family token has expired — paste a fresh one below" (no button that performs
  any Steam call). Keep the "credential stays in your local database" footnote.
- Rewrite the how-to (paper/velvet styling unchanged; sub-collapse may stay):
  1. In the browser where you are logged into Steam, open
     `https://store.steampowered.com/pointssummary/ajaxgetasyncconfig`.
  2. Copy the value of `webapi_token` from the JSON shown.
  3. Paste it here and Save. The token is valid for roughly a day; when an import reports it
     rejected, repeat these steps.
  Drop the DevTools Network-tab ritual entirely (it yields the same token, six steps later).
- Settings overview badge: `"Token set"` logic already keys on `SteamFamilyToken`; verify it
  still reads correctly with the Connect state gone.

**Tests**
- Delete `tests/Server.Tests/SteamConnectDeviceIdentityTests.fs` (and its `<Compile>`).
- `SteamFamilyTokenTests.fs`: delete the `withTokenRefresh`, `steamIdFromRefreshToken` and
  `mintFamilyAccessToken` lists (they test deleted code). Add: a 401/403 from a family
  endpoint maps to the `"family token rejected: "`-prefixed message; that message and the
  Web API key rejection message never share a prefix; the family import with an empty
  `steam_family_token` returns the "not configured" error without any HTTP call.
- Client (Vitest, ADR-0064): `isFamilyTokenRejected` recognises the new prefix and rejects
  the old `"reconnect required"` wording.

**Spike**
- Delete `spikes/steam-family-token-spike/` (`login.fsx`, `refresh-and-call.fsx`, README):
  it is a working SteamKit2 QR-login + token-mint script against the builder's account — the
  precise thing the project is now committing never to run. Git history and ADR-0019/0061
  keep the record.

**Knowledge**
- New ADR (worker writes): *"Mediatheca never performs a Steam login or mints a Steam token;
  the Steam Family import runs only on a browser-obtained access token pasted by the user"*.
  Supersedes ADR-0061 (Connect QR session + refresh wiring). Amends ADR-0019 in place: point
  2 (no SteamKit2 dependency) becomes permanent and absolute — the "yet" is gone; points 3
  and 4 (audience check, browser-retrieval fallback) are moot, and the fallback must **not**
  be built either, since a scripted browser driving Steam's login is the same class of act.
  Amends ADR-0067 in place: the login half is *removed*, not accepted; the escalation ladder,
  the no-speculative-reconnect rule and the device-identity rung are retired with the code
  they governed; the compliance check above is recorded as the ADR's Context. Include the
  ban-threat date (2026-09-05) as the trigger. Also fix the verifier-noted stale wording in
  ADR-0019 points 2/4 and ADR-0061 Consequences (datacenter-IP framing) while there.
- `contexts/integration/README.md`: rewrite the **Adapter** line (no `SteamConnect.fs`
  satellite), **Refresh token** and **Connect** entries (Steam Family has neither anymore —
  keep the *term* Refresh token for Jellyfin, drop the Steam half), the "Connect Steam" note
  under commands, and the two Open-questions bullets on QR session state and accepted risk
  (replace with one settled line pointing at the new ADR).
- `contexts/integration/concepts/steam-account-flag-risk-surface.md`: update to the settled
  state — two surfaces (Web API key, storefront) plus a *user-supplied* access token; the
  login half is gone; the open question "scheduled family import?" is answered no; add
  integration-v0xmv and the new ADR to `derived_from`.

## Acceptance criteria

- [x] `src/Server/SteamConnect.fs`, `tests/Server.Tests/SteamConnectDeviceIdentityTests.fs`
      and `spikes/steam-family-token-spike/` no longer exist; `Server.fsproj` has no
      `SteamKit2` or `QRCoder` package reference; `dotnet build` and `npm run build` pass.
- [x] `grep -rn "IAuthenticationService\|SteamKit2\|steam_family_refresh_token\|reconnect required\|steam-connect" src/ tests/` returns only the startup cleanup that deletes the `steam_family_refresh_token` setting.
- [x] Startup deletes any stored `steam_family_refresh_token` (test: seed the setting in an
      in-memory DB, run composition/init, assert it is gone). *(The startup cleanup is
      extracted as `Composition.deleteRetiredSteamRefreshToken` — directly unit-tested in
      `SteamFamilyTokenTests.fs` without spinning up a full `WebApplication`; see Outcome.)*
- [x] `IMediathecaApi` has no `getSteamConnectionStatus`; the client compiles without any
      Connect/QR state or messages.
- [x] Family member discovery and family import use only `steam_family_token`; an empty
      token yields "Steam Family access token not configured" with no HTTP call; a 401/403
      from `IFamilyGroupsService` yields the `"family token rejected: "`-prefixed message
      and the Settings card shows the "paste a fresh token" warning — with no button that
      performs any Steam call.
- [x] Web API key rejection wording (ADR-0065) is unchanged and shares no prefix with the
      family-token rejection message (test).
- [x] Settings → Steam Family shows the token how-to (`ajaxgetasyncconfig` → `webapi_token`)
      and the token input/Save as the primary step; the "Manual token entry (fallback)" and
      DevTools Network-tab instructions are gone; existing tokens still show as `****xxxx`.
- [x] Expecto and Vitest suites pass; no test references deleted symbols.
- [x] New ADR written and linked (`related_adrs` here, `related_tasks` there); ADR-0061
      marked superseded by it; ADR-0019 and ADR-0067 amended in place as described; README
      and concept page updated; INDEX adr-local list updated. *(INDEX update is the
      conductor's, per the task-file protocol — not done by this worker.)*

## Notes

- **Builder-side, outside the task (do these by hand):** (1) at
  `store.steampowered.com/twofactor/manage` remove the "Mediatheca" / "… (SteamKit2)"
  authorized devices — with the QR mode gone there is no longer any cost to doing so;
  (2) deploy this change **before** the next family import; (3) fetch a fresh `webapi_token`
  from your own logged-in browser and paste it — never let anything script that step;
  (4) do not press any Steam-related button on the old deployed build in the meantime.
- **Out of scope, on purpose:** any automated token retrieval — SteamKit2, a headless/driven
  browser (Chrome DevTools MCP, Playwright, Selenium), or a browser extension that posts the
  token to the server. All of them either perform or automate a Steam login. ADR-0019 pt 4's
  "browser-retrieval fallback" is closed as *will not build*, not merely "not yet".
- Token facts to keep honest in the UI: `webapi_token` from `ajaxgetasyncconfig` lasts
  roughly 24h (research report [9]; integration-hebjs builder gate measured ≈24h for the
  minted variant); the current UI's "~1 hour" claim came from the DevTools-scraped
  store-page token and is stale either way — say "roughly a day".
- The `FamilyFetchError.Rejected` distinction stays: it is what lets the client tell "paste a
  new token" apart from "Steam is down" and from "your Web API key is bad".
- Pasted-chat source (builder, 2026-09-05 midday): the `GetFamilyGroupForUser` →
  `GetSharedLibraryApps` (`include_own`, `include_excluded`, `owner_steamids[]`,
  `exclude_reason`, `rt_time_acquired`) shape it describes is already what
  `Steam.fs`/`runSteamFamilyImport` implement; nothing new to add on the query side.

## Outcome

Deleted `src/Server/SteamConnect.fs`, the `/api/stream/steam-connect` route, the
`SteamKit2`/`QRCoder` package references, `spikes/steam-family-token-spike/`, and
`tests/Server.Tests/SteamConnectDeviceIdentityTests.fs`. In `src/Server/Steam.fs`, deleted
`withTokenRefresh`, `TokenMinter`, `mintFamilyAccessToken`, `steamIdFromRefreshToken`,
`GenerateAccessTokenResponse`, and the three `*WithRefresh` wrappers; kept `FamilyFetchError`
and the plain `fetch*`/`get*` family functions, and added `familyTokenRejectedMessage`
(`"family token rejected: ..."`, textually distinct from ADR-0065's Web API key wording) as
`mapFamilyFetchError`'s new `Rejected` mapping. `src/Server/Composition.fs` extracted a
directly-testable `deleteRetiredSteamRefreshToken` (called once at startup, idempotent) and
removed the `/api/stream/steam-connect` route registration; `Api.fs`'s `runSteamFamilyImport`
and `fetchSteamFamilyMembers` now read only `steam_family_token`, with no refresh-token
re-reads. `IMediathecaApi.getSteamConnectionStatus` removed. Client: `Settings/Types.fs`/
`State.fs` replaced `SteamConnected`/`IsConnectingSteam`/`SteamConnectQrDataUrl`/
`SteamConnectError`/`SteamNeedsReconnect` and every Connect-related message with a single
`SteamFamilyTokenRejected` flag (driven by the new prefix, cleared on a successful token save);
`Views.fs` deleted the Connect/Reconnect block and QR image, promoted the token paste
input to the primary step, and rewrote the how-to around `ajaxgetasyncconfig` → `webapi_token`.

Wrote ADR-0070 (new), marked ADR-0061 `status: superseded` / `superseded_by: [0070]`, amended
ADR-0019 (points 2 and 4, "yet" → permanent, browser fallback closed as will-not-build) and
ADR-0067 (an appended 2026-09-05 amendment retiring the escalation ladder, the
no-speculative-reconnect rule, and the device-identity rung) in place, and fixed the
verifier-noted stale "MobileApp-from-datacenter-IP" wording in ADR-0019 and ADR-0061's
Consequences. Updated the BC README's Adapter/Refresh token/Connect entries and Open Questions,
and rewrote `concepts/steam-account-flag-risk-surface.md` to the settled two-surface state.

Tests: `tests/Server.Tests/SteamFamilyTokenTests.fs` rewritten — 401/403 → `"family token
rejected: "` mapping, prefix-distinctness from the Web API key message, empty-token
"not configured, no HTTP call" for both `importSteamFamily` and `fetchSteamFamilyMembers`, and
three new cases for `Composition.deleteRetiredSteamRefreshToken`. Updated
`SteamFamilyImportOwnedGamesTests.fs`'s stale "reconnect required" assertion to check against
the new prefix instead. Added `src/Client/Pages/Settings/FamilyTokenRejected.test.fs` (Vitest/
Fable.Mocha) covering `Settings.State.update`'s family-token-rejected detection end to end.
Expecto: 685/685 passing. Vitest: 20/20 passing (5 files). `npm run build` and
`dotnet build src/Server/Server.fsproj` both clean.

Key files: `src/Server/Steam.fs`, `src/Server/Api.fs`, `src/Server/Composition.fs`,
`src/Server/Server.fsproj`, `src/Shared/Shared.fs`, `src/Client/Pages/Settings/{Types,State,
Views}.fs`, `src/Client/Client.fsproj`, `tests/Server.Tests/{SteamFamilyTokenTests,
SteamFamilyImportOwnedGamesTests}.fs`, `tests/Server.Tests/Server.Tests.fsproj`,
`src/Client/Pages/Settings/FamilyTokenRejected.test.fs`,
`.agentheim/knowledge/decisions/{0019,0061,0067,0070}-*.md`,
`.agentheim/contexts/integration/README.md`,
`.agentheim/contexts/integration/concepts/steam-account-flag-risk-surface.md`.
