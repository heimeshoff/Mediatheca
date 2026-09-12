---
id: integration-p2hxn
title: Accept and document the MobileApp-from-datacenter-IP login signature as a known Steam account-flag risk — mitigations, a no-speculative-reconnect rule, and an escalation ladder
status: done
type: decision
context: integration
created: 2026-08-18
completed: 2026-08-18
depends_on: []
blocks: []
tags: [steam, steam-connect, auth, token, security, risk, adr]
related_adrs: [0019, 0061, 0065, 0067]
related_research: [steam-family-api-auto-token-refresh-2026-07-20]
prior_art: [integration-hebjs, integration-ygwsa, integration-r8kwd]
---

## Why

Valve has twice alerted the builder's Steam account after Mediatheca activity, warning that
the API usage resembles account hijacking. The plausible signature has two halves. The
**enumeration** half — an unthrottled per-app storefront sweep — is a real defect with a real
fix (integration-w7ktb, integration-n3vqa). The **login** half is different: it may not be
fixable at all.

`SteamConnect.fs`'s QR ceremony opens a new **`MobileApp`-platform persistent session from the
Docker host's IP** — a datacenter address. To Valve that reads as a phone signing in from a
server. But this shape is *deliberate*: ADR-0019 point 2 chose `PlatformType = MobileApp` +
`IsPersistentSession = true` precisely because a `SteamClient`-platform token requires an
authenticated CM connection to refresh (an April 2025 Steam-side change), which would force a
permanent SteamKit2 + live-CM dependency into the server. The datacenter IP is inherent to
self-hosting (ADR-0007). ADR-0061 already noticed this reads as "a new device signing in from
a new location" and that repeated reconnects reinforce it — but recorded it as a passing note,
not a decision.

So the risk is currently documented only in scattered asides across two ADRs and a task's
Notes. That is the actual problem this task fixes: a future session could "helpfully" reverse
ADR-0019's platform choice without knowing why it was made, or re-run the QR ceremony
speculatively because nothing says not to. One accepted-risk ADR closes both gaps.

Note this is **speculation about Valve's heuristics**, which are undocumented and not knowable
from outside. The ADR must say so plainly rather than asserting a mechanism.

## What

Write an ADR (scope: integration) that:

- **Names the signature and labels it a hypothesis**, not a finding.
- **States why it is not fixable** under ADR-0019's constraint, so the platform choice is not
  re-litigated blindly — and names what the alternative would actually cost (permanent
  SteamKit2 + live-CM dependency in the server).
- **Records the mitigations already in place**: reconnect only on an explicit "reconnect
  required" marker; mint only on `Rejected`; and integration-r8kwd's removal of the
  misattributed-401 reconnect loop that had the builder reconnecting repeatedly to no effect
  (the single largest source of reconnect churn to date).
- **Establishes one behavioural rule**: the QR ceremony runs only when the app says a reconnect
  is required — never speculatively, never as a diagnostic first step.
- **Defines an escalation ladder** if flags continue: (1) let integration-w7ktb and
  integration-n3vqa land and observe; (2) the browser-retrieval fallback already evaluated in
  ADR-0019 point 4 — no server-side login session at all; (3) reversing ADR-0019 point 2 as a
  last resort, accepting the SteamKit2/CM dependency.
- **Names the trigger that would reopen the decision** — e.g. a third flag, or a
  silently-invalidated refresh token.

## Acceptance criteria

- [x] An ADR exists under `.agentheim/knowledge/decisions/` with `scope: integration`,
      cross-referenced from ADR-0019 and ADR-0061 (and those two updated to point at it, so the
      note-in-passing now leads somewhere).
- [x] Every claim about Valve's detection is explicitly labelled speculation.
- [x] The ADR records the no-speculative-reconnect rule in a form a future worker or refiner
      can actually follow.
- [x] The escalation ladder names all three steps with their costs.
- [x] A "what would change this decision" section names a concrete trigger.

## Notes

- **No code change is expected.** This is a `type: decision` task — its deliverable is the ADR.
  If refinement turns up a cheap code lever, the obvious candidate is making reconnect
  frequency *observable* (log/persist a timestamp per QR ceremony) so "are we reconnecting too
  often" stops being a guess. Optional, not required.
- Do not pre-spike the browser-retrieval fallback (ladder step 2) — it is a documented option,
  and building it before a third flag arrives is speculative work.
- **This task is independent of integration-w7ktb and integration-n3vqa** — no dependency in
  either direction. It documents the half we cannot fix; they fix the half we can.
- Related open item: the builder should independently confirm the account is not *actually*
  compromised (an unrecognised key registered at steamcommunity.com/dev/apikey, unfamiliar
  authorized devices). Valve's warning may be literal rather than a false positive — that
  question is outside this ADR's scope but must be answered before its "accepted risk" framing
  is sound.

## Outcome

Wrote ADR-0067 (`.agentheim/knowledge/decisions/0067-steam-mobileapp-login-signature-accepted-risk-and-escalation-ladder.md`),
accepting the `MobileApp`-from-datacenter-IP login signature as a documented, currently
unfixable-under-ADR-0019 risk. It: names the signature and labels the causal mechanism a
hypothesis (Valve's heuristics are undocumented); explains why reversing ADR-0019 point 2
would reintroduce a permanent SteamKit2/live-CM dependency; records the mitigations already
live (user-click-only QR ceremony, mint-only-on-`Rejected`, integration-r8kwd's removal of
the misattributed-401 reconnect loop); states the no-speculative-reconnect rule in a
checkable form (`Start_steam_connect`/`SteamNeedsReconnect` may only be driven by an explicit
"reconnect required" signal, never automatically, diagnostically, or for an unrelated
credential's failure); defines the three-step escalation ladder (let ADR-0066/integration-n3vqa's
enumeration-half fixes land and observe → the ADR-0019-point-4 browser-retrieval fallback,
evaluated not built → reverse ADR-0019 point 2 as a last resort) with each step's cost; and
names concrete reopening triggers (a third flag after the enumeration-half fixes have had a
full usage cycle; a silently-invalidated refresh token; confirmed actual compromise, which
would invalidate the ADR's premise entirely rather than feed its ladder).

Cross-referenced both source ADRs: ADR-0019 point 2 and point 4 now point at ADR-0067 (and
its `related_tasks` frontmatter includes this task); ADR-0061's Consequences section gained a
new "Negative / accepted tradeoff" bullet promoting its passing "new device" note into a
pointer at ADR-0067 (and its `related_tasks` frontmatter likewise updated). The `integration`
BC README gained one Open Questions bullet naming the accepted risk and pointing at ADR-0067,
so a future session reading the README first learns of it before touching `SteamConnect.fs`.

No code changed — factual claims (platform type, persistent session, user-click-only trigger,
the `"reconnect required: ..."` substring gate) were checked directly against
`src/Server/SteamConnect.fs` and `src/Client/Pages/Settings/State.fs`/`Views.fs` before being
asserted in the ADR. The optional code lever noted below (observable reconnect-frequency
logging) was not built, per the task's own "optional, not required" framing.
