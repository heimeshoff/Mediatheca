---
id: integration-zwnh4
title: Give the Steam Connect QR login a stable, honest device identity — a fixed device name, a "Mobile" website id and a fixed OS type instead of SteamKit2's per-deploy container-id defaults — and amend ADR-0067 with the corrected (home-IP, not datacenter) hypothesis after the third Valve alert
status: done
type: bug
context: integration
created: 2026-09-04
completed: 2026-09-04
depends_on: []
blocks: []
tags: [steam, steam-connect, auth, token, security, risk, adr, steamkit2]
related_adrs: [0019, 0061, 0067]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
prior_art: [integration-p2hxn, integration-hebjs, integration-r8kwd]
---

## Why

Valve raised a **third** "this account may have been accessed by someone else" alert on
2026-09-03 at 18:54 CEST (location: Muenster, DE). Production container logs pin it down
exactly:

| Time (UTC) | Event |
|---|---|
| 16:39:01 | `mediatheca` container redeployed — new hostname `0868c9f227f8` |
| 16:54:30 | `GET /api/stream/steam-connect` — the QR ceremony (`SteamConnect.startConnect`) |
| 16:54:46 | `GET /api/stream/import-steam-family` — the (already incremental) family import |

16:54 UTC is 18:54 CEST, the minute in Valve's alert. The alert was the **QR login**, not the
import. Two things ADR-0067 assumed are now known to be wrong or already handled:

1. **The login came from a residential IP.** The production host `harbour` is the builder's
   home server in Münster — Valve's own alert says "Muenster, DE". ADR-0067's "MobileApp
   session from a datacenter IP" hypothesis is disproven; the IP half was never the signal.
2. **The enumeration half was already fixed.** ADR-0066 (spacing) and ADR-0069 (count) were
   live on this deploy; the import 16 s later issued its steady-state handful of requests.

What remains is the **device fingerprint SteamKit2 sends by default** in `device_details`,
which `SteamConnect.fs` never overrides:

- `DeviceFriendlyName` defaults to `$"{Environment.MachineName} (SteamKit2)"` — inside Docker
  that is the random container id, so every deploy + reconnect registers a brand-new
  "device" named like `0868c9f227f8 (SteamKit2)`.
- `PlatformType = MobileApp` with `ClientOSType = Android9`, but `WebsiteID` is left at the
  SteamKit2 default `"Client"`. The real Steam mobile app sends `"Mobile"` (node-steam-session
  maps MobileApp → `'Mobile'`, SteamClient → `'Client'`, WebBrowser → `'Community'`).
- A MobileApp-platform device performing a **QR** login is itself anomalous — the real app is
  the scanner, never the thing scanned — and steam-session-based hijack tooling does exactly
  this. That part is inherent to ADR-0019's platform choice and stays accepted; the two
  bullets above are not, and cost one file to fix.

This is the "third flag after the enumeration fixes landed" trigger ADR-0067 point 6 names,
so the decision is formally reopened. Before climbing to ladder step 2 (browser retrieval) or
step 3 (reverse ADR-0019's platform choice), there is a cheaper rung nobody had on the ladder:
stop presenting a new randomly-named phone to Valve on every ceremony.

A second, related risk surfaced while reading the logs: the builder ran Connect Steam
**16 s before** the import on a freshly deployed container. Steam's hijack-recovery flow
commonly invalidates existing sessions, which would kill the stored refresh token and produce
a **loop**: alert → recovery revokes token → next import says "reconnect required" → QR
ceremony → new alert. Nothing in the app causes this loop (ADR-0067 point 4's rule holds in
code), but nothing documents it for the builder either.

Everything about *why* Valve flags remains a hypothesis — its heuristics are undocumented
(ADR-0067 point 1 still applies). What is verifiable is the exact fingerprint we send, and
that is what this task changes.

## What

**1. Stable device identity in `src/Server/SteamConnect.fs`.** Extract the
`AuthSessionDetails` construction into a pure, testable function (e.g.
`SteamConnect.authSessionDetails : unit -> AuthSessionDetails`, or a record the ceremony maps
from) and set explicitly:

- `DeviceFriendlyName` — a fixed, human-recognisable constant, `"Mediatheca"` by default,
  overridable via an optional `STEAM_DEVICE_NAME` environment variable (so the builder can
  make it read "Mediatheca on harbour" without a code change). **Never** derived from
  `Environment.MachineName`, the container hostname, or anything else that changes per deploy.
- `WebsiteID = "Mobile"` — matching the `MobileApp` platform instead of SteamKit2's `"Client"`.
- `ClientOSType` — a single fixed value (keep `Android9` unless the worker finds a documented
  reason to prefer another Android `EOSType`), never `Utils.GetOSType()`.
- `PlatformType = MobileApp`, `IsPersistentSession = true` — **unchanged** (ADR-0019 point 2).

**2. Amend ADR-0067** (in place, with a dated "Amended 2026-09-04" section — do not supersede;
the rule and ladder survive, only the hypothesis and the ladder's first rung change):

- Record the third alert with the log evidence above.
- Retract the datacenter-IP half of the hypothesis: the login IP is residential. Replace it
  with the device-fingerprint hypothesis (random per-deploy device name + `Client` website id
  on a `MobileApp`/Android session over a QR login), labelled a hypothesis as before.
- Note the enumeration half (ADR-0066/0069) was live at the time of the third alert, so
  ladder step 1 ("let the enumeration fixes land and observe") is **discharged**.
- Insert this task's change as the **new cheapest rung** of the escalation ladder, ahead of
  browser retrieval (now step 3) and reversing ADR-0019 point 2 (now step 4). A **fourth**
  alert after this task is live for one ordinary usage cycle is the trigger to climb further.
- Add the **reconnect-loop** note to the mitigations: Steam hijack recovery may invalidate the
  stored refresh token; the builder reconnects **only** when Settings shows the
  "Reconnect Steam" prompt driven by the `"reconnect required: ..."` marker — never
  pre-emptively after a deploy, never "to be safe" after an alert. Point at where the
  builder can inspect registered devices (`store.steampowered.com/twofactor/manage`) to confirm
  the single stable "Mediatheca" device after this change.

**3. Sync the derived prose.** Update the integration README's "Accepted risk" open-question
line and `contexts/integration/concepts/steam-account-flag-risk-surface.md` ("Login half",
"Escalation ladder") so neither still says "datacenter IP" or lists a three-step ladder.

Out of scope: any change to `PlatformType`, the mint path (`Steam.mintFamilyAccessToken`),
the import, or the Settings UI. No live QR ceremony is to be run by a worker — the ceremony
is the act under suspicion (ADR-0067 point 4) and the live token is builder-only.

## Acceptance criteria

- [x] `SteamConnect.fs` builds its `AuthSessionDetails` through a pure function whose result is
      asserted by an Expecto test: `DeviceFriendlyName` does not contain `Environment.MachineName`
      and does not contain `"(SteamKit2)"`; `WebsiteID = "Mobile"`; `PlatformType =
      k_EAuthTokenPlatformType_MobileApp`; `IsPersistentSession = true`; `ClientOSType` is a fixed
      Android value. Two consecutive calls return equal field values (stability).
- [x] With `STEAM_DEVICE_NAME` unset the device name is exactly `"Mediatheca"`; with it set the
      device name is that value (test both via the pure function's input, not the process env).
- [x] A live-tree grep shows no remaining `Environment.MachineName` / `Utils.GetOSType` use in
      `src/Server/SteamConnect.fs`.
- [x] ADR-0067 carries a dated 2026-09-04 amendment section that: retracts the datacenter-IP
      half, records the third alert's UTC log timeline, marks ladder step 1 discharged, inserts
      the stable-device-identity rung before browser retrieval, names a fourth alert as the next
      trigger, and adds the reconnect-loop mitigation note. `related_tasks` includes
      `integration-zwnh4`.
- [x] `contexts/integration/README.md` line "Accepted risk, not yet resolved" and
      `concepts/steam-account-flag-risk-surface.md` no longer say "datacenter"; both point at the
      amended ladder.
- [x] `npm test` passes; `npm run build` passes.

## Outcome

`SteamConnect.fs` now builds its QR ceremony `AuthSessionDetails` through a pure
`authSessionDetails : string option -> AuthSessionDetails` function: `DeviceFriendlyName`
defaults to `"Mediatheca"` (never `Environment.MachineName`/container hostname), overridable via
the `STEAM_DEVICE_NAME` env var (blank/whitespace override falls back to the default);
`WebsiteID = "Mobile"` (was SteamKit2's `"Client"` default); `ClientOSType = Android9` fixed
(unchanged, already explicit pre-fix); `PlatformType = MobileApp`/`IsPersistentSession = true`
unchanged per ADR-0019 point 2 (out of scope here). `startConnect` reads `STEAM_DEVICE_NAME`
once and passes it in, keeping the mapping itself pure and unit-testable without touching
SteamKit2's network/CM connection. 8 new Expecto tests in
`tests/Server.Tests/SteamConnectDeviceIdentityTests.fs` cover the default, the override, the
blank-override fallback, the absence of the `(SteamKit2)` marker, `WebsiteID`, platform/session
flags, OS type, and call-to-call stability.

ADR-0067 carries a dated "Amended 2026-09-04 (integration-zwnh4)" section: retracts the
datacenter-IP half of the original hypothesis (the third alert's login IP traced to the
builder's own residential home server), records the third alert's UTC log timeline, marks
former ladder step 1 (enumeration fixes) as discharged, inserts the stable-device-identity fix
as the new cheapest rung ahead of browser retrieval (now step 3) and reversing ADR-0019 (now
step 4), names a fourth alert as the trigger to climb further, and adds a reconnect-loop
mitigation note (Steam hijack-recovery may silently revoke the stored refresh token; reconnect
only on an explicit "reconnect required" prompt, never pre-emptively after a deploy or alert).
`related_tasks` now includes `integration-zwnh4`.

`contexts/integration/README.md`'s "Accepted risk, not yet resolved" line and
`concepts/steam-account-flag-risk-surface.md` ("Login half", "Escalation ladder") were rewritten
to drop the retracted datacenter-IP framing and describe the residential-IP finding, the
device-fingerprint hypothesis, and the amended four-step ladder. The concept page's ubiquitous
`authSessionDetails`/`STEAM_DEVICE_NAME` addition is also noted in the README's "Adapter"
ubiquitous-language entry. No new ADR was written — the existing ADR-0067 was amended in place
per the task's explicit instruction.

`npm test`: 693/693 passing (685 pre-existing + 8 new). `npm run build`: client build succeeded;
`dotnet build src/Server/Server.fsproj` succeeded with 0 warnings/errors.

Key files: `src/Server/SteamConnect.fs`,
`tests/Server.Tests/SteamConnectDeviceIdentityTests.fs`,
`tests/Server.Tests/Server.Tests.fsproj`,
`.agentheim/knowledge/decisions/0067-steam-mobileapp-login-signature-accepted-risk-and-escalation-ladder.md`,
`.agentheim/contexts/integration/README.md`,
`.agentheim/contexts/integration/concepts/steam-account-flag-risk-surface.md`.

## Notes

- **Evidence trail (2026-09-04):** `docker logs -t mediatheca` on `harbour` for 2026-09-03;
  `docker inspect mediatheca` → created `2026-09-03T16:39:01Z`, hostname `0868c9f227f8`.
  SteamKit2 3.1.0 defaults verified from
  `SteamKit2/Steam/Authentication/AuthSessionDetails.cs` (`DeviceFriendlyName =
  $"{Environment.MachineName} (SteamKit2)"`, `WebsiteID = "Client"`, `ClientOSType =
  Utils.GetOSType()`); `SteamAuthentication.cs` sends exactly `device_friendly_name`,
  `platform_type`, `os_type` and `website_id` from these details (no `machine_id`,
  no `gaming_device_type`). node-steam-session's `_defaultWebsiteId` maps MobileApp → `'Mobile'`.
- **Convention check (ADR-0059):** the "device identity never derives from the runtime host"
  rule is enforced by the Expecto test in criterion 1; the ADR-0067 amendment itself is
  prose-only, unenforced (it records a hypothesis and a ladder, not a rule other code follows).
- **Why `bug`, not `decision`:** the decision (accept the login risk, climb a ladder on repeat
  flags) was made in integration-p2hxn / ADR-0067; this task executes the ladder's newly
  identified cheapest rung and corrects a factual premise. The ADR amendment is bookkeeping
  for that correction.
- **Not to be re-litigated here:** `PlatformType = MobileApp` (ADR-0019 point 2). If the
  fourth alert arrives after this change, *that* is when the platform choice goes back on the
  table, per the amended ladder.
- **Builder-side, outside the task:** contact Steam Support via the alert's own link; after
  recovery, check `store.steampowered.com/twofactor/manage` for the `(SteamKit2)` devices and
  `steamcommunity.com/dev/apikey` for an unrecognised key (ADR-0067's precondition). Do **not**
  click Connect Steam after recovery unless an import reports "reconnect required".
