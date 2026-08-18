---
id: 0066
title: Steam storefront calls are paced inside the Adapter, not by callers — a single injectable-interval gate replaces eleven independently-remembered `Async.Sleep`s
scope: integration
status: accepted
date: 2026-08-18
supersedes: []
superseded_by: []
related_tasks: [integration-w7ktb]
related_research: []
---

# ADR 0066: Steam storefront calls are paced inside the Adapter, not by callers

## Context

Valve has **twice** alerted the builder's Steam account after a Mediatheca family import
("using the Steam API in the same way a certain brand of account hijacking does"). Valve's
detection heuristics are undocumented, so any claim about what specifically trips them is
speculation this decision does not rest on. What is verifiable from source:
`Steam.getSteamStoreDetails` (`appdetails`) had **11 call sites** across 5 files, and exactly
3 were paced — each by its own copy-pasted `do! Async.Sleep 300 // Rate limit Steam Store API
calls`. The Family import's three call sites — the caller that iterates the largest
collection — were among the eight paced by nothing. Pacing was **caller-owned**, so it failed
open on every new caller; the Family import is simply the caller that forgot.

Two further facts sharpened the fix:

1. **300ms was never the right number.** Steam's storefront ceiling is informally ~200
   requests per 5 minutes — 1.5s/request. The existing precedent was ~5x too fast.
2. **There is no store-details cache**, despite a stale comment claiming one ("Separate from
   the full-response cache used by `getSteamStoreDetails` callers"). No such cache exists —
   `storeMetaCache` is search-only.

## Decision

**1. The throttle lives inside `Steam.fs`, not at call sites.**
`Steam.throttleStorefrontCall (fetch: unit -> Async<'a>) : Async<'a>` wraps a single
`SemaphoreSlim(1,1)` held for the gate's *entire* duration — the interval wait AND the fetch
itself, not just the wait — so at most one storefront request is ever in flight process-wide,
and consecutive gated calls' *start* times are spaced by at least
`throttleStorefrontInterval` (default 1500ms, the derived ceiling). `getSteamStoreDetails`,
`getSteamStoreTrailer`, `getSteamStoreTrailers`, and the search ranking's `fetchStoreMeta`
(only its network-fetch branch — a cache hit never touches the storefront and shouldn't wait
on one) are routed through it. Every caller — the Family import's three per-app branches,
`GameFacetBackfill`, `GameReleaseDateBackfill`, `AddGameFromSteam`, the description-backfill
loop in `Api.fs` — inherits pacing for free; none of them pace themselves anymore. The three
redundant `Async.Sleep 300` call-site sleeps were deleted.

**2. `getDeckCompatibility` (the Deck-compat store-*page* HTML scrape — same host, different
path) is routed through the same gate too**, and `GameDeckCompatBackfill.fs`'s own
`Async.Sleep 300` deleted. This was an explicit open question left for the implementing
worker. Decided yes: a vendor's rate ceiling is safest treated as one shared per-host budget
rather than assuming a page fetch is exempt from a JSON-API ceiling neither side has
documentation for, and routing it cost nothing — no new dependency, one extra
`throttleStorefrontCall` wrap.

**3. The interval is a mutable, not a parameter threaded through every caller.**
`mutable throttleStorefrontInterval` and the gate function itself are exposed (not `private`)
specifically so tests can drive the interval fast and exercise the gate directly — the same
public-for-testability precedent `decodeDeckCompatFromHtml` already sets in this file.
Production code never touches it.

**4. 1500ms, not 300ms.** Derived directly from the storefront's informal ~200 req/5min
ceiling (1.5s/request), not chosen to "feel" safe. Known consequence, accepted: a full
re-enrich of a several-hundred-title family library now takes minutes (400 apps ≈ 10 min at
1500ms/app). This is the correct trade for a rarely-used full sweep and is exactly why
integration-n3vqa (shrinking the steady-state import to a handful of calls, not the full
sweep) matters more after this lands, not less — this task lowers the *rate*, n3vqa lowers
the *count*; neither subsumes the other.

**5. Caching stays out of scope.** An `appdetails` response cache was considered and
rejected: it adds staleness/eviction/storage decisions, and n3vqa's "skip known apps
entirely" design supersedes most of its value — skipping a call beats caching its answer.

## Alternatives considered

- **Lowering the default interval below the derived 1.5s ceiling** to keep the full-sweep UX
  faster. Rejected — the task explicitly calls for surfacing that trade-off rather than
  quietly weakening the mitigation the whole task exists to ship.
- **Threading an explicit throttle/interval parameter through every caller** instead of a
  module-level gate. Rejected — the entire point is that callers should never need to know
  pacing is their job; a vendor's rate ceiling is vendor knowledge and belongs inside the
  Adapter that already owns everything else about talking to Steam.

## Consequences

`Steam.fs` gained a shared, process-wide gate — a deliberate trade of some inter-test
blocking risk (unrelated storefront-calling tests can, at worst, wait behind each other; the
`>=` interval assertions this task's own tests make can only be *helped*, never broken, by
such contention, since it can only lengthen an observed gap) for the correctness guarantee
that production traffic is genuinely serialized and paced regardless of which of the now
eleven call sites originates a request. `npm test`: 696 tests green (2 added), no live Steam
call in any of them.
