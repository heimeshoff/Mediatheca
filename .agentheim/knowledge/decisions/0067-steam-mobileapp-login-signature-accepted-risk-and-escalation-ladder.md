---
id: 0067
title: The Steam Connect QR ceremony's MobileApp-from-datacenter-IP login signature is an accepted, unfixable-under-ADR-0019 risk — no-speculative-reconnect rule and a three-step escalation ladder
scope: integration
status: accepted
date: 2026-08-18
supersedes: []
superseded_by: []
related_tasks: [integration-p2hxn, integration-hebjs, integration-ygwsa, integration-r8kwd, integration-w7ktb, integration-n3vqa, integration-zwnh4]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
---

# ADR 0067: The Steam Connect QR ceremony's MobileApp-from-datacenter-IP login signature is an accepted, unfixable-under-ADR-0019 risk

## Context

Valve has **twice** alerted the builder's Steam account after Mediatheca activity, warning
that the API usage resembles account hijacking. The plausible signature behind that has two
halves:

1. **Enumeration** — an unthrottled per-app storefront sweep. A real defect with a real fix:
   ADR-0066 moved storefront pacing into the Adapter (landed, 2026-08-18); integration-n3vqa
   (shrinking the family import to diff-and-enrich-only-newcomers, rather than a full sweep)
   is in flight in this same session and is not yet landed as of this ADR.
2. **Login** — `SteamConnect.fs`'s QR ceremony (integration-hebjs, ADR-0061) opens a new
   Steam login session **from the Docker host's IP**, which for a self-hosted deployment
   (ADR-0007) is ordinarily a datacenter/VPS address, not a residential one. To Valve's side
   that plausibly reads as a phone signing in from a server in a new location. Unlike the
   enumeration half, this shape is **deliberate**, not accidental, and it is not obviously
   fixable without giving up something ADR-0019 explicitly chose to avoid.

This ADR exists because that second half was previously recorded only as scattered asides —
a load-bearing footnote in ADR-0019 point 2, a "passing note" in ADR-0061's context section,
and a Notes-section aside in integration-r8kwd — with no single place declaring it an accepted
risk, no rule stopping a future session from "helpfully" undoing ADR-0019's platform choice
without knowing why it was made, and no rule stopping a future session from re-running the QR
ceremony speculatively (e.g. as a diagnostic first step) because nothing said not to.

**Everything this ADR says about *why* Valve flags accounts is speculation.** Valve's account
flagging heuristics are undocumented and not knowable from outside this project. What is
verifiable from source and from the two lived incidents is the *shape* of Mediatheca's own
traffic (call volume, login platform/persistence, source IP); the *causal mechanism* — that
this shape is what specifically trips Valve's detector — is a hypothesis this ADR treats as
the most plausible reading of two data points, never as a confirmed finding.

## Decision

**1. Name the signature; it is a hypothesis, not a finding.** Hypothesis: a `MobileApp`-
platform, `IsPersistentSession = true` Steam login session, opened from a datacenter IP,
reads to Valve's undocumented abuse detection as "a new device signing in from a new
location" — and that reading is reinforced each time the ceremony repeats in a short window.
This ADR does not, and cannot, assert Valve's actual detection logic; it asserts only that
this is the most plausible explanation available for two lived flagging incidents, given
what the codebase and public research (`steam-family-api-auto-token-refresh-2026-07-20`)
say about how this login shape looks from Steam's side.

**2. This is not fixable under ADR-0019's constraint — do not re-litigate the platform
choice blind.** ADR-0019 point 2 chose `PlatformType = MobileApp` + `IsPersistentSession =
true` for `SteamConnect.startConnect` (`src/Server/SteamConnect.fs`) specifically *because*
of an April 2025 Steam-side change: a `SteamClient`-platform refresh token can only be
refreshed over an **authenticated CM connection** — i.e. the server would need to hold open
(or re-establish) a live SteamKit2 connection to Steam's network indefinitely just to keep
minting family access tokens. `MobileApp`-platform tokens are the one shape confirmed (by
research, then proven live in integration-hebjs's builder gate) to refresh via a plain HTTP
POST (`GenerateAccessTokenForApp`) with no CM connection at all. Reversing that choice to
`SteamClient` would remove the "reads like a phone" signature, but at the cost of exactly
what ADR-0019 was written to avoid: **a permanent SteamKit2 + live-CM dependency inside the
server process**, not just at one-time-login. The datacenter-sourced IP is not separately
fixable either — it is inherent to self-hosting on a VPS/datacenter host per ADR-0007's
deployment posture, not a Mediatheca-specific choice.

**3. Mitigations already in place — record them so they aren't lost or duplicated.**
- The QR ceremony (`Start_steam_connect`, `src/Client/Pages/Settings/State.fs` /
  `Views.fs`) is only ever invoked by an explicit user click on "Connect Steam" or
  "Reconnect Steam" — there is no code path today that fires it automatically or
  speculatively.
- `Steam.withTokenRefresh` (ADR-0019/0061) mints a fresh family access token — the ordinary,
  frequent, HTTP-only operation — **only** on an `Error Rejected` from the token-consuming
  fetch itself, never as a background timer or a "just in case" step. Minting is not the same
  operation as the QR ceremony: minting reuses the existing refresh token over plain HTTP
  (no new login session); only a `"reconnect required: ..."`-prefixed error (no refresh token
  stored, or the stored one is itself rejected) surfaces the "Reconnect Steam" prompt that can
  lead to a *new* QR ceremony.
- integration-r8kwd (ADR-0065) removed the single largest source of reconnect churn to date:
  a Steam Web API key rejection (a *different*, independent credential) was misattributed as
  a family-token failure, driving the builder into a repeated, useless "Reconnect Steam" loop
  — each iteration of which was itself a fresh QR login session, i.e. exactly the signature
  this ADR is about, self-reinforcing for no benefit. Typing that failure and giving it its
  own remedy (regenerate the Web API key) closed the loop.

**4. The no-speculative-reconnect rule.** The QR ceremony
(`SteamConnect.startConnect`/`Start_steam_connect`) runs **only** when the app itself has
surfaced an explicit "reconnect required" state (today: the `"reconnect required: ..."`
error-prefix convention driving `SteamNeedsReconnect`, per ADR-0061 point 3) — **never**:
  - speculatively, "just to check" whether the family token still works;
  - automatically, on a timer or on every family-fetch failure regardless of cause (a new
    failure category must get its own typed/attributed error, per ADR-0065's precedent,
    before it is allowed to drive a reconnect prompt);
  - as a diagnostic first step when investigating an unrelated Steam failure (e.g. a Web API
    key or storefront-rate problem) — those are different credentials/subsystems entirely and
    reconnecting does nothing for them, per integration-r8kwd.

  A future worker or refiner adding a new Steam failure path must ask "does this failure mean
  the *family refresh token* specifically needs re-minting via a new login?" before wiring it
  to `SteamNeedsReconnect`/`Start_steam_connect`. If unsure, it does not qualify — leave it as
  a generic error.

**5. Escalation ladder, if flags continue despite the above:**

  1. **Let the enumeration-half fixes land and observe.** ADR-0066's storefront throttle has
     landed; integration-n3vqa (shrinking the family import's steady-state call count) is in
     flight this session, not yet landed. Cost: none beyond time — this is the cheapest step
     and addresses the half of the signature that *is* fixable. Do not proceed to step 2 on
     the strength of the login-half hypothesis alone without giving this time to show whether
     the enumeration half was sufficient on its own.
  2. **The browser-retrieval fallback**, evaluated (not built) in ADR-0019 point 4:
     semi-automated retrieval of the family access token by driving a logged-in Chrome
     session (Chrome DevTools MCP or Playwright) through the same manual DevTools ritual the
     app was built to eliminate, instead of minting via a Steam login session at all. Cost:
     no SteamKit2/CM dependency and **no server-side Steam login session of any kind** (the
     login-half signature disappears entirely), but trades that for a standing browser
     profile signed into Steam and a scrape that must be re-driven periodically — reintroduces
     some of the fragility the refresh-token approach was built to remove, and is unverified
     against this codebase (evaluated only on paper in ADR-0019, never spiked). Building this
     before a third flag is exactly the speculative work this ADR's Notes forbid pre-empting.
  3. **Reverse ADR-0019 point 2 as a last resort**: switch `SteamConnect.startConnect` to
     `PlatformType = SteamClient`, accepting a permanent SteamKit2 + live-CM dependency in the
     server (the cost ADR-0019 chose to avoid) to remove the "phone-shaped" login signature.
     This is the most invasive and most expensive step, reserved for the case where step 2 is
     also judged unacceptable or has itself failed.

**6. What would reopen this decision.** Any of the following is a concrete trigger to revisit
this ADR (escalate up the ladder, not silently work around):
  - **A third Valve flag** on the same account, after the enumeration-half fixes (ADR-0066,
    integration-n3vqa) have been live for at least one full ordinary usage cycle (i.e. flags
    continuing *despite* the fixable half being fixed, not merely two-flags-total repeated).
  - **A refresh token silently invalidated by Steam** outside of an explicit revocation the
    builder performed — per the research report, Valve can invalidate a persistent-session
    refresh token server-side for undocumented reasons "suspected but not confirmed" to
    include IP changes, which would be additional, independent evidence for this ADR's
    hypothesis even without a formal "flag" notification.
  - **Confirmation that the account was, separately, actually compromised** (an unrecognised
    key at steamcommunity.com/dev/apikey, an unfamiliar authorized device) — this is a
    prerequisite to trusting this ADR's "accepted risk" framing at all (see Notes), not a
    ladder step, but if it turns out true it invalidates this ADR's premise entirely and the
    situation becomes an account-security incident, not a rate-shape question.

## Alternatives considered

- **Do nothing and rely on the scattered asides in ADR-0019/0061/integration-r8kwd.**
  Rejected — the actual defect this ADR fixes is that a future session has no single place to
  learn *why* the platform choice can't be casually reversed, and no explicit rule preventing
  a well-meaning "let's just reconnect to check" from silently reinforcing the exact signature
  under investigation.
- **Pre-emptively switch to the browser-retrieval fallback (ladder step 2) now**, since it
  removes the login-half signature entirely. Rejected per the task's own instruction: building
  it before a third flag arrives is speculative work against an unconfirmed hypothesis, and
  the fallback has its own real costs (a standing signed-in browser profile) that shouldn't be
  paid without a stronger signal than two incidents, one of which (integration-r8kwd) turned
  out to be almost entirely explained by an unrelated, now-fixed misattribution bug.
- **Reverse ADR-0019 point 2 now** to be safe. Rejected — this is the ladder's last, most
  expensive resort specifically because it reintroduces a permanent SteamKit2/CM dependency
  ADR-0019 worked to avoid, and neither of the two lived flags has yet been observed *after*
  the fixable enumeration half was actually fixed.

## Consequences

### Positive
- ADR-0019's platform choice and ADR-0061's "reads as a new device" note now lead somewhere:
  a future session reading either ADR follows the cross-reference here instead of
  re-discovering (or re-litigating) the reasoning from scratch.
- The no-speculative-reconnect rule (point 4) is stated in a form directly checkable against
  new code: does this new failure path drive `Start_steam_connect`/`SteamNeedsReconnect`
  without an explicit reconnect-required signal? If yes, it violates this ADR.
- The escalation ladder gives a concrete, costed answer to "what do we do if this keeps
  happening" instead of an ad hoc decision made under the stress of a live flag.

### Negative / accepted tradeoff
- This ADR accepts an ongoing, unresolved risk rather than eliminating it — by design, since
  the cheapest fix (reversing ADR-0019) costs more than the risk currently warrants absorbing.
  If the hypothesis in point 1 is wrong (Valve's real trigger is something else entirely),
  this ADR's mitigations and ladder may do nothing for the actual cause; there is no way to
  know from outside Valve's system, which is exactly why point 1 labels it a hypothesis.
- **Outstanding, outside this ADR's scope but a precondition for its framing being sound**:
  the builder should independently confirm the account is not *actually* compromised (check
  for an unrecognised key at steamcommunity.com/dev/apikey, review authorized devices, confirm
  mobile Steam Guard). Valve's warning may be literal rather than a false positive triggered by
  this app's traffic shape. This ADR's "accepted risk" framing assumes the warnings are a
  false-positive-shaped side effect of Mediatheca's traffic, not evidence of genuine
  compromise; if that assumption is wrong, the correct response is an account-security
  incident response, not anything in this ADR's ladder.

## Amended 2026-09-04 (integration-zwnh4)

Valve raised a **third** "this account may have been accessed by someone else" alert on
2026-09-03 at 18:54 CEST (location reported by Valve's own alert: Muenster, DE). Production
container logs on `harbour` (the builder's home server) pin the trigger down exactly:

| Time (UTC) | Event |
|---|---|
| 16:39:01 | `mediatheca` container redeployed — new hostname `0868c9f227f8` |
| 16:54:30 | `GET /api/stream/steam-connect` — the QR ceremony (`SteamConnect.startConnect`) |
| 16:54:46 | `GET /api/stream/import-steam-family` — the (already incremental) family import |

16:54 UTC is 18:54 CEST, the minute in Valve's alert. The alert traces to the **QR login**,
not the import.

**The datacenter-IP half of point 1's hypothesis is retracted — it was factually wrong for
this deployment.** `harbour` is the builder's home server in Münster; Valve's own alert location
("Muenster, DE") confirms the login IP is residential, not a datacenter/VPS address. Point 2's
"the datacenter-sourced IP is not separately fixable" reasoning and point 5 step 3's premise
that reversing ADR-0019 removes a datacenter-sourced signature are accordingly also stale —
there was never a datacenter IP to fix or trade away here. This does not change point 2's
conclusion (the platform choice is still not casually reversible — see below), only the
reasoning offered for why the login *looked* anomalous.

**Replacement hypothesis: the device fingerprint, not the IP.** SteamKit2's
`AuthSessionDetails` defaults, unless overridden, are: `DeviceFriendlyName =
"{Environment.MachineName} (SteamKit2)"` (inside Docker, the random per-container hostname —
`0868c9f227f8 (SteamKit2)` on this redeploy, a *different* string on every redeploy),
`WebsiteID = "Client"` (the SteamKit2 default, not what the real Steam mobile app sends —
node-steam-session's own MobileApp → `'Mobile'` mapping confirms `"Client"` is a website-id
mismatch for this platform), and `ClientOSType` from `Utils.GetOSType()` (a runtime lookup,
though `SteamConnect.fs` was already pinning this to `Android9` explicitly before this
amendment). The replacement hypothesis: a `MobileApp`-platform QR login presenting a
randomly-named "device" on every deploy, with a website id that doesn't match its own claimed
platform, reads to Valve's undocumented abuse detection as a new, unrecognised device signing
in — reinforced by the fact that a `MobileApp`-platform session performing a **QR** login is
itself an inversion of the real Steam app's own role (the real app is the scanner, never the
thing scanned). As with point 1's original wording, **this is a hypothesis, not a finding** —
Valve's detection logic remains undocumented and unverifiable from outside Valve's system.

**Ladder step 1 (point 5) is discharged.** ADR-0066 (storefront throttle) and ADR-0069 (import
call-count reduction) were both live on the deploy the third alert traces to — the import at
16:54:46 issued only its steady-state handful of requests, 16 seconds after the QR login. The
enumeration half of the original two-part signature (Context, points 1) was already fixed at
the time of this alert; whatever produced it, it wasn't an unthrottled sweep. Step 1's original
instruction — "let the enumeration-half fixes land and observe" — has been satisfied: they
landed, and a flag still occurred, isolating the remaining signature to the login half.

**New ladder rung inserted ahead of browser retrieval — the stable device identity fix.**
Before climbing to point 5's former step 2 (browser retrieval, now **step 3**) or former step 3
(reversing ADR-0019 point 2, now **step 4**), integration-zwnh4 ships a cheaper rung: stop
presenting a new, randomly-named "device" on every deploy. `SteamConnect.authSessionDetails`
(`src/Server/SteamConnect.fs`) now sets `DeviceFriendlyName` to a fixed constant (`"Mediatheca"`
by default, overridable via the `STEAM_DEVICE_NAME` environment variable — never derived from
`Environment.MachineName` or the container hostname) and `WebsiteID = "Mobile"` (matching the
`MobileApp` platform instead of SteamKit2's `"Client"` default). `PlatformType = MobileApp` and
`IsPersistentSession = true` are unchanged — point 2's reasoning for that choice still holds,
independent of the IP-half retraction above. This is now:

  1. **Stable device identity** (integration-zwnh4, this amendment) — present a fixed,
     recognisable device name and a platform-consistent website id on every QR ceremony instead
     of a new one per deploy. Cost: one file, no new dependency, no behavior change to the
     token-refresh path. Landed 2026-09-04.
  2. **Let the enumeration-half fixes and this device-identity fix run for one ordinary usage
     cycle and observe** — the same "give a cheap fix time to work before climbing" discipline
     the original step 1 applied, reapplied to this new rung.
  3. **The browser-retrieval fallback** (formerly step 2, ADR-0019 point 4) — semi-automated
     retrieval of the family access token via a logged-in Chrome session, trading the login
     session away entirely for a standing signed-in browser profile. Still evaluated, not
     built.
  4. **Reverse ADR-0019 point 2** (formerly step 3) — switch to `PlatformType = SteamClient`,
     accepting a permanent SteamKit2 + live-CM dependency in the server. Still the last resort.

  **A fourth alert**, after this device-identity fix has been live for at least one full
  ordinary usage cycle, is the trigger to climb to step 3. Point 6's other two triggers (a
  refresh token silently invalidated by Steam outside an explicit revocation; confirmation of
  actual account compromise) are unchanged by this amendment.

**Reconnect-loop mitigation note, added to point 3's mitigations.** The third alert's timeline
shows Connect Steam run 16 seconds before the family import, immediately after a fresh deploy.
Steam's own hijack-recovery flow commonly invalidates existing sessions as part of recovering
a flagged account — if that happens here, the stored family refresh token would be silently
revoked, and the next import would report "reconnect required", which per point 4's rule is a
legitimate, non-speculative trigger to reconnect — but each such reconnect is itself a fresh QR
login, i.e. exactly the signature this ADR is about, and running it *reactively* in a tight loop
(alert → recovery revokes token → "reconnect required" → new QR ceremony → new alert) would be
self-reinforcing for no benefit, same shape as the already-closed integration-r8kwd loop. Point
4's rule already forbids running Connect Steam pre-emptively "to be safe" after an alert or a
deploy — this note makes explicit that the rule applies here too: reconnect **only** when
Settings surfaces the `"reconnect required: ..."`-driven "Reconnect Steam" prompt, never
proactively after a redeploy and never "to be safe" after an alert. The builder can confirm
this device-identity fix is taking effect by checking
`store.steampowered.com/twofactor/manage` for a single stable "Mediatheca" device, rather than
one `(SteamKit2)`-suffixed entry per past reconnect.
