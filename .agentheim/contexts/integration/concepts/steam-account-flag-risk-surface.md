---
name: steam-account-flag-risk-surface
description: What Mediatheca's Steam account-hijack-flag risk is made of — three separate credentials, two halves of the signature, and which half is accepted as unfixable
context: integration
created: 2026-08-18
last_updated: 2026-09-04
derived_from:
  - 0019            # MobileApp platform choice + browser-retrieval fallback
  - 0061            # QR session and family-token refresh wiring
  - 0065            # typed Web API key rejection, fault isolation
  - 0066            # Adapter-owned storefront throttle (spacing)
  - 0067            # login signature accepted as risk; no-speculative-reconnect rule; ladder
  - 0068            # empty owned-games is inconclusive, not success
  - 0069            # incremental family import (count)
  - steam-family-api-auto-token-refresh-2026-07-20
  - integration-hebjs    # one-click family import
  - integration-ygwsa    # family token spike
  - integration-r8kwd    # opaque 401 / reconnect loop removed
  - integration-w7ktb    # storefront pacing into the Adapter
  - integration-k4vqm    # empty-response handling at three call sites
  - integration-n3vqa    # diff-don't-re-import
  - integration-p2hxn    # accepted-risk ADR
  - integration-zwnh4    # stable device identity; the hosting-provider-IP hypothesis retracted
max_lines: 60
---

# Steam account-flag risk surface — concept

## What it is
Valve has **three times** warned that this project's Steam traffic "resembles account hijacking".
This page is the one picture of what that risk is made of, what has been done about it, and
which part is accepted as unfixable. Every claim about *why* Valve flags accounts is
speculation — its heuristics are undocumented (ADR-0067). The shape of our own traffic is not
speculation, and is what the project can act on.

## Why it exists
Beyond the incidents, the expensive failure here was **credential confusion**: a rejected Web
API key was misattributed as a family-token failure, driving the builder into a repeated
"Reconnect Steam" loop — every iteration opened a *fresh login session*, the exact signature
under suspicion, self-reinforcing for no benefit (integration-r8kwd, ADR-0065). Knowing
precisely which credential failed is an account-safety property, not tidy error handling.

## Three independent surfaces — never conflate them
- **Family refresh token** — minted by the QR ceremony (`src/Server/SteamConnect.fs`, ADR-0061).
  Only a failure of *this* can ever justify a reconnect. Routine re-minting via
  `Steam.withTokenRefresh` is plain HTTP and cheap; the **QR ceremony is the rare, risky act**.
- **Web API key** — separate credential, separate remedy (regenerate it); never a reconnect
  reason (ADR-0065). **Storefront** — no credential at all, only rate limits (ADR-0066).

## Current shape
- **Login half — accepted, partly fixed** (ADR-0067, amended 2026-09-04): a `MobileApp`,
  persistent-session QR login. A third alert (2026-09-03) traced the login IP to the builder's
  own **residential** home server, retracting the earlier hosting-provider-IP hypothesis. Live
  hypothesis now: the device fingerprint SteamKit2 sent by default (random per-deploy device
  name, a platform-inconsistent website id) plus the QR-as-MobileApp inversion itself.
  integration-zwnh4 fixed the fingerprint half — `SteamConnect.fs` sends a fixed device name
  (`STEAM_DEVICE_NAME`-overridable) and `WebsiteID = "Mobile"`. Reversing the platform entirely
  still costs a permanent SteamKit2 + live-CM dependency (ADR-0019 pt 2) — do not re-litigate blind.
- **No-speculative-reconnect rule** (ADR-0067 pt 4): the QR ceremony runs *only* on an explicit
  "reconnect required" signal — never speculatively, never on a timer, never as a diagnostic
  first step for an unrelated Steam failure, and never pre-emptively after a redeploy or alert.
- **Enumeration half — fixed**: request *spacing* is Adapter-owned (ADR-0066, one gate, 1500ms),
  request *count* is import-owned (ADR-0069 — a steady-state import is 3 requests, not one per title).
- **Ambiguity is not success** (ADR-0068): an empty `GetOwnedGames` means "owns nothing" *or*
  "game details are private" — never evidence that a key is bad.
- **Escalation ladder, amended** (ADR-0067 pt 5): enumeration fixes observed (discharged) →
  stable device identity (integration-zwnh4, 2026-09-04, observe for one usage cycle) →
  browser-retrieval fallback (ADR-0019 pt 4, evaluated not built) → reverse ADR-0019 pt 2 last
  resort. A **fourth** alert after this fix is live is the next trigger to climb.

## Open questions
- Whether the account was ever *actually* compromised — an unrecognised key at
  `steamcommunity.com/dev/apikey`, unfamiliar authorized devices — rather than false-positived.
  The "accepted risk" framing is only sound once this is answered (integration-p2hxn).
- integration-r8kwd's builder gate — one live family import, end to end — remains undischarged;
  it is now a small incremental import rather than a full sweep.
- Whether a *scheduled* family import is ever justifiable: automated periodic traffic is a
  different risk profile than a manual click; ADR-0067's framing must be weighed first.

## See also
- `[ADR 0067]` — the accepted-risk decision, the rule, and the ladder (start here)
- `[ADR 0019]`, `[ADR 0061]` — why the login shape is what it is
- `[ADR 0065]`, `[ADR 0068]` — credential attribution and response ambiguity
- `[ADR 0066]`, `[ADR 0069]` — spacing and count
- `[research/steam-family-api-auto-token-refresh-2026-07-20]`
- `[done/integration-r8kwd]` — the reconnect loop, and why attribution is a safety property
