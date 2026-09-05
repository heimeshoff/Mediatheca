---
name: steam-account-flag-risk-surface
description: What Mediatheca's Steam account-hijack-flag risk was made of, and how it was settled — the login surface is deleted; two credentialed surfaces remain, both non-login
context: integration
created: 2026-08-18
last_updated: 2026-09-05
derived_from:
  - 0019            # MobileApp platform choice — now permanent-and-absolute per its 0070 amendment
  - 0061            # QR session and family-token refresh wiring — superseded by 0070
  - 0065            # typed Web API key rejection, fault isolation
  - 0066            # Adapter-owned storefront throttle (spacing)
  - 0067            # login signature accepted as risk; retired by its 0070 amendment
  - 0068            # empty owned-games is inconclusive, not success
  - 0069            # incremental family import (count)
  - 0070            # login removed outright; manual-token-only, permanently
  - steam-family-api-auto-token-refresh-2026-07-20
  - integration-hebjs    # one-click family import (reversed by v0xmv)
  - integration-ygwsa    # family token spike
  - integration-r8kwd    # opaque 401 / reconnect loop removed
  - integration-w7ktb    # storefront pacing into the Adapter
  - integration-k4vqm    # empty-response handling at three call sites
  - integration-n3vqa    # diff-don't-re-import
  - integration-p2hxn    # accepted-risk ADR (superseded by v0xmv's removal)
  - integration-zwnh4    # stable device identity (retired with the ceremony it fixed)
  - integration-v0xmv    # settles this page: login deleted, manual-token-only
max_lines: 60
---

# Steam account-flag risk surface — concept

## What it is
Valve warned three times that this project's Steam traffic "resembles account hijacking," then
threatened a **permanent ban** for further login-API use (2026-09-05). ADR-0070 settled this by
deleting the login capability outright rather than continuing to mitigate it. This page is the
settled picture: what surfaces remain, and why the login surface is gone for good.

## Two independent surfaces remain — never conflate them
- **Family access token** — a short-lived, **browser-obtained** token the user pastes into
  Settings (`steam_family_token`). No login, no mint, no refresh token — the app only *uses* a
  token the user's own logged-in browser already holds. A 401/403 maps to a fixed
  `"family token rejected: "` message (ADR-0070); the only remedy is pasting a fresh one.
- **Web API key** — separate credential, separate remedy (regenerate it); never conflated with
  the family token (ADR-0065). **Storefront** — no credential at all, only rate limits (ADR-0066).

## What was removed, and why
The QR login (`SteamConnect.fs`, SteamKit2), the refresh-token mint-and-retry seam
(`Steam.withTokenRefresh`/`mintFamilyAccessToken`), and the stored `steam_family_refresh_token`
are deleted end to end (ADR-0070) — not merely deprecated. The third Valve alert traced, to the
minute, to the QR login itself; a ban threat removed the option to keep testing cheaper
mitigations (ADR-0067's escalation ladder, its no-speculative-reconnect rule, and its
stable-device-identity fix are all retired with the code they governed). The browser-retrieval
fallback ADR-0019/ADR-0067 held in reserve is now closed as *will not build* — driving a browser
through Steam's own login is the same class of act, whoever drives it.

## What's unaffected
- **Enumeration fix stays fixed**: request spacing (ADR-0066) and count (ADR-0069) were never
  part of the login-half removal — only the login itself was the ban-threat trigger.
- **Ambiguity is not success** (ADR-0068): an empty `GetOwnedGames` still means "owns nothing" *or*
  "game details are private" — never evidence a key is bad.

## Open questions
- Whether the account was ever *actually* compromised, independent of Mediatheca's traffic shape
  — still open, still the builder's own action to take (was integration-p2hxn's precondition).
- ~~Whether a scheduled family import is ever justifiable~~ **Settled 2026-09-05 (ADR-0070): no.**
  Import stays a manual click, permanently — not "for now" pending a login surface that no longer
  exists.

## See also
- `[ADR 0070]` — the removal decision: manual-token-only, permanently (start here)
- `[ADR 0067]` — the accepted-risk period this superseded, and why it stopped being sufficient
- `[ADR 0019]`, `[ADR 0061]` — the login shape that used to exist, and why (historical)
- `[ADR 0065]`, `[ADR 0068]` — credential attribution and response ambiguity (unaffected)
- `[ADR 0066]`, `[ADR 0069]` — spacing and count (unaffected)
- `[research/steam-family-api-auto-token-refresh-2026-07-20]`
- `[done/integration-v0xmv]` — the removal task
