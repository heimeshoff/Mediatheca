---
id: 0070
title: Mediatheca never performs a Steam login or mints a Steam token; the Steam Family import runs only on a browser-obtained access token pasted by the user
scope: integration
status: accepted
date: 2026-09-05
supersedes: [0061]
superseded_by: []
related_tasks: [integration-v0xmv, integration-hebjs, integration-ygwsa, integration-p2hxn, integration-zwnh4, integration-r8kwd]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
---

# ADR 0070: Mediatheca never performs a Steam login or mints a Steam token — Steam Family import runs only on a browser-obtained access token pasted by the user

## Context

Valve has escalated from three "this account may have been accessed by someone else" alerts to
an explicit threat: **the account will be permanently banned if the login API is used against
Steam's rules again** (builder, 2026-09-05). ADR-0067's entire framing — accept the
login-shaped traffic as a risk, mitigate it, escalate up a costed ladder if it keeps
recurring — assumed each further alert bought time to try the next cheaper mitigation. A
permanent-ban threat removes that assumption: there is no mitigation cheaper than not
performing the login at all, and no further alert is affordable while testing one.

ADR-0067's 2026-09-04 amendment had already traced the third alert, to the minute, to
`GET /api/stream/steam-connect` — the SteamKit2 QR ceremony (`SteamConnect.fs`,
integration-hebjs) that opens a `MobileApp`-platform, persistent-session Steam login to obtain
a refresh token, from which the server then mints access tokens via
`IAuthenticationService/GenerateAccessTokenForApp`. Both of those are Steam's **login API**,
driven by a third-party program — exactly what Valve's alert wording ("using the Steam API in
the same way a certain brand of account hijacking does") describes. The enumeration half of the
original two-part signature hypothesis (ADR-0066's storefront throttle, ADR-0069's import
call-count reduction) was already fixed and live at the time of the third alert and did not
stop it, isolating the remaining signature to the login half specifically.

The manual mode that preceded integration-hebjs — the builder copies their own browser
session's `access_token` and pastes it into Settings — ran without any alert across all three
incidents, and touches no login API at all: the app never authenticates as anything, it only
*uses* a token the builder's own already-logged-in browser holds.

**Compliance check of the remaining import shape**, recorded here as the decision's basis (the
task restates this in its own Why section): the Steam Web API Terms of Use
(steamcommunity.com/dev/apiterms) bind the project to ≤100,000 calls/day, never intercepting or
storing the end user's Steam password, following the documentation, not violating the
Subscriber Agreement, and not degrading Steam. None of these are about traffic *volume* in a
way this feature approaches, and Valve's alert wording is specifically about the *login* shape,
not volume. After this decision, the family import per click consists of:

- **`IFamilyGroupsService/GetFamilyGroupForUser` + `GetSharedLibraryApps` (+ `GetFamilyGroup`
  for member discovery)** — 2–3 GETs, authenticated with the builder's own browser
  `access_token`, i.e. the exact calls the Steam store's own family page makes. This is an
  undocumented interface (ADR-0019 already recorded this) but read-only, and the app performs
  no login of any kind to use it — this is the one residual gray area, and it is the same one
  the pre-integration-hebjs mode lived in without incident. It cannot be made "documented" from
  our side; every other known family-library tool (research report `steam-family-api-auto-
  token-refresh-2026-07-20`'s [9], Chachigo's `FamilyBot`) works this way too.
- **`IPlayerService/GetOwnedGames`** via the Web API key — documented, key-authenticated, a
  best-effort supplement (ADR-0065/0068, unaffected by this decision).
- **`store.steampowered.com/api/appdetails`** — public, unauthenticated, Adapter-throttled at
  1500ms (ADR-0066), only for genuinely new apps (ADR-0069).
- **No call to `api.steampowered.com/IAuthenticationService/*` of any kind, no SteamKit2, no CM
  connection, no stored refresh token, no device registered on the account** — this is what
  this ADR removes.

Verdict: compliant with every explicit clause; the one gray area (undocumented interface) is
inherent to the feature and predates every incident to date. What is removed is the only part
that plausibly qualifies as "using the login API against the rules." Import stays a manual
click, never scheduled — the concept page's open question on this is thereby closed: no.

## Decision

**Delete the QR ceremony, the refresh-token storage, and the token-mint path end to end; make
the paste-a-token flow the one and only way in.**

1. **Server**: `SteamConnect.fs` (the SteamKit2 QR ceremony) is deleted, along with the
   `SteamKit2`/`QRCoder` package references and the `/api/stream/steam-connect` route.
   `Steam.fs` keeps `FamilyFetchError` (`Rejected`/`FamilyOtherFailure`) and the plain
   `fetch*`/`get*` family functions — the 401/403 distinction is still needed for attribution —
   but `withTokenRefresh`, `TokenMinter`, `mintFamilyAccessToken`, `steamIdFromRefreshToken`,
   and the three `*WithRefresh` wrappers are deleted outright, not merely unused.
   `mapFamilyFetchError` now maps `Rejected` to a fixed, typed
   **`"family token rejected: "`**-prefixed message, replacing the retired
   `"reconnect required: "` convention. Startup deletes any stored
   `steam_family_refresh_token` — a live Steam credential the app must no longer hold — via a
   one-time, idempotent `SettingsStore.deleteSetting` call in `Composition.fs`.
2. **Shared/Client**: `getSteamConnectionStatus` is removed from `IMediathecaApi`. Settings'
   `SteamConnected`/`IsConnectingSteam`/`SteamConnectQrDataUrl`/`SteamConnectError` state and
   every Connect/Reconnect message are deleted; `SteamNeedsReconnect` is renamed
   `SteamFamilyTokenRejected` (driven by the new prefix, set from the same three call sites as
   before, cleared on a successful token save). The view drops the Connect/Reconnect block and
   QR image entirely; the token paste input moves out of the demoted "Manual token entry
   (fallback)" collapse into the primary card body, preceded by a rewritten how-to built around
   the browser's own `store.steampowered.com/pointssummary/ajaxgetasyncconfig` →
   `webapi_token` route (no DevTools Network-tab ritual, six steps shorter, same token).
3. **`spikes/steam-family-token-spike/`** (the working SteamKit2 QR-login + token-mint scripts
   against the builder's own account, per ADR-0019/ADR-0061) is deleted outright — it is the
   precise class of act this decision commits never to run again. Git history and
   ADR-0019/ADR-0061 keep the record of what it proved.
4. **This decision is not conditional on future evidence.** Unlike ADR-0067's ladder (which
   held a next-cheaper-step in reserve), this decision does not leave the browser-retrieval
   fallback (ADR-0019 point 4, ADR-0067's former ladder step 2/3) in reserve either — a headless
   or driven browser (Chrome DevTools MCP, Playwright, Selenium) performing or automating a
   Steam login is the same class of act as the SteamKit2 path, regardless of which program
   drives it. It is closed as *will not build*, not merely deferred.

## Alternatives considered

- **Climb ADR-0067's escalation ladder one more rung** (the stable-device-identity fix,
  landed 2026-09-04, had not yet been observed through a full usage cycle). Rejected: a
  permanent-ban threat is qualitatively different from "another alert" — the ladder's whole
  premise was that each rung was affordable to try and observe. There is no rung left that is
  affordable to test against a ban threat; the only response left that doesn't gamble the
  account is removing the login capability entirely.
- **Reverse ADR-0019 point 2** to `PlatformType = SteamClient`, hoping a different login
  signature reads differently to Valve's detection. Rejected: still a login, still driven by a
  third-party program, still exactly the shape Valve's wording names — this changes the
  signature's shape, not its category, and a ban threat is not the moment to test an unproven
  variant of the same act.
- **Keep the manual-paste path as a demoted fallback alongside the QR ceremony** (today's
  shape). Rejected: the task is specifically to stop the login capability from existing at all,
  not to make it less likely to be invoked — as long as the code path exists, a future click
  (accidental, or "just to reconnect") can still trigger it.

## Consequences

### Positive
- No code path in the server can open a Steam login session or mint a Steam token —
  grep-verified (`IAuthenticationService`, `SteamKit2`, `steam_family_refresh_token`,
  `reconnect required`, `steam-connect` all absent from `src/`/`tests/` except the one-time
  startup cleanup that deletes the legacy setting).
- ADR-0067's entire accepted-risk apparatus (the no-speculative-reconnect rule, the escalation
  ladder, the device-identity rung) is retired with the code it governed — there is nothing
  left to reopen it over. See ADR-0067's own 2026-09-05 amendment for the itemized retirement.
- ADR-0019's mint-and-retry seam and ADR-0061's QR session both proved out technically (the
  live audience/scope check passed, the self-heal cycle worked end-to-end) before being
  retired for an account-safety reason unrelated to whether they worked — that record stays in
  git history and in ADR-0019/ADR-0061 rather than being erased.

### Negative / accepted tradeoff
- **Token lifetime becomes user-facing again.** A `webapi_token` from `ajaxgetasyncconfig`
  lasts roughly 24 hours (research report [9]; integration-hebjs's builder gate measured
  ≈24h for the minted variant) — the builder must paste a fresh token roughly once a day for
  the family import to keep working, reversing integration-hebjs's one-click product goal
  deliberately. This is the explicit price of keeping the account.
- **The one-click "Connect Steam" UX is gone.** Settings' Steam Family card returns to a
  single-step paste-and-save flow with no automatic refresh — a real regression in convenience,
  accepted because the alternative risks the account outright.
- **The residual undocumented-interface gray area is unchanged and unresolved.**
  `IFamilyGroupsService` remains undocumented by Valve; using it at all — even token-only, even
  read-only — is not something this ADR can make "compliant" in an absolute sense, only argue
  is the least risky way to keep using it. If Valve's future behavior treats *any* third-party
  use of this interface as against the rules regardless of login shape, this ADR's compliance
  argument would not hold, and Steam Family import would need to be dropped or reworked
  entirely — a scenario this ADR does not otherwise plan for.
