---
id: integration-w7ktb
title: Steam storefront calls are paced by the caller, not the Adapter — the family import paces not at all; move throttling into `Steam.fs` so every storefront caller inherits it
status: done
type: bug
context: integration
created: 2026-08-18
completed: 2026-08-18
depends_on: []
blocks: []
tags: [steam, steam-family, import, adapter, rate-limit, throttle]
related_adrs: [0019, 0061, 0065, 0066]
related_research: []
prior_art: [integration-r8kwd, integration-hebjs, integration-004]
---

## Why

Valve has **twice** alerted the builder's Steam account after a Mediatheca family import:
*"Your accounts appear to be using the Steam API in the same way a certain brand of account
hijacking does."* Earlier the same account was flagged as "probably being used by another
user" and its Web API key was (hypothesised) revoked — the trigger for integration-r8kwd,
which made the import *survive* that 401 but did nothing about why the account keeps being
flagged.

Valve's detection heuristics are undocumented, so any claim about what specifically trips
them is **speculation**. This task does not rest on that. It rests on a fact readable
straight from the source: **`runSteamFamilyImport` calls the Steam storefront once per app
across the entire family library with no delay whatsoever**, and the project already
considers that call rate-limit-sensitive everywhere else.

Concretely, verified 2026-08-18:

- `Steam.getSteamStoreDetails` (`src/Server/Steam.fs:805`) hits
  `https://store.steampowered.com/api/appdetails`. It has **11 call sites** across 5 files:
  `Api.fs:584`, `:623`, `:657` (the three family-import paths), `Api.fs:1193`, `:1302`,
  `:3778`, `:3818`, `:3920`, `GameFacetBackfill.fs:51`, `GameReleaseDateBackfill.fs:45`,
  `PlaytimeTracker.fs:289`.
- Exactly **3 of those 11** are paced, each by its own copy-pasted
  `do! Async.Sleep 300 // Rate limit Steam Store API calls` (`Api.fs:3919`,
  `GameFacetBackfill.fs:50`, `GameReleaseDateBackfill.fs:44`). A fourth 300ms sleep
  (`GameDeckCompatBackfill.fs:44`) paces store *page* fetches — same host, different call.
- **The family import's three call sites are among the eight that are not paced at all.**

That is the shape of the defect: pacing is **caller-owned**, so it fails open on every new
caller. The family import is simply the caller that forgot — and it happens to be the one
that iterates the largest collection.

Two further facts sharpen it:

1. **300ms was never the right number.** The storefront's informal ceiling is ~200 requests
   per 5 minutes, i.e. **1.5 s per request**. The existing precedent is ~5× too fast; copying
   it would leave the defect in place while looking fixed.
2. **There is no store-details cache**, despite a comment at `Steam.fs:966-967` claiming one
   ("Separate from the full-response cache used by `getSteamStoreDetails` callers"). No such
   cache exists — `storeMetaCache` (`Steam.fs:973`) is search-only and `achievementsCache`
   (`:736`) is unrelated. The comment is stale/aspirational and has been actively misleading;
   fix it here.

A vendor's rate ceiling is vendor knowledge. In this BC that belongs **inside the Adapter**
— the anticorruption layer that already owns everything else about talking to Steam — not
scattered across eleven call sites, three of which happen to remember.

## What

- **Add an Adapter-owned throttle to `Steam.fs`**: a minimum-interval gate that serializes and
  spaces all Steam *storefront* calls — `getSteamStoreDetails` (`appdetails`), the store-trailer
  fetches, and the search store-meta fetch. Default interval **1500ms** (one call per
  ceiling-interval); the interval must be injectable/settable so tests can drive it fast.
- **Route every existing storefront caller through the gate**, and delete the now-redundant
  call-site sleeps at `Api.fs:3919`, `GameFacetBackfill.fs:50`, `GameReleaseDateBackfill.fs:44`.
  Leave `GameDeckCompatBackfill.fs:44` (store *page* fetches) alone unless it routes trivially
  — see Notes.
- **Keep the family import strictly sequential.** The per-app loop is sequential today (`let!`
  inside `for`); the gate must not introduce concurrency, and this should be pinned so a later
  "optimisation" can't quietly parallelise the burst.
- **Fix the stale cache comment** at `Steam.fs:966-967`.
- **Write an ADR (scope: integration)**: the Adapter owns pacing for an external system's rate
  ceiling; callers never pace themselves. Record the 1500ms derivation and the eleven-call-site
  history that motivated it.

## Acceptance criteria

- [ ] A minimum-interval gate lives in `Steam.fs`, is applied to `getSteamStoreDetails`, and its
      interval is injectable for tests.
- [ ] Expecto (fake `HttpMessageHandler` recording a timestamp per `SendAsync`, following
      `tests/Server.Tests/SteamFamilyImportOwnedGamesTests.fs`'s established shape —
      `TestDb.withTempDbFactory` + the `createApi` helper): `importSteamFamily` against a stub
      returning 3 shared apps issues exactly 3 `appdetails` requests, with consecutive
      timestamps spaced ≥ the configured test interval.
- [ ] The same test asserts **no two storefront requests are ever in flight at once** (the stub
      tracks concurrent entries and fails if it ever exceeds 1) — this is the sequential-loop
      guarantee, pinned.
- [ ] A direct unit test on the gate itself: two back-to-back gated calls are spaced ≥ interval.
- [ ] No `Async.Sleep` remains adjacent to any `getSteamStoreDetails` call site (the three
      deleted above).
- [ ] The comment at `Steam.fs:966-967` no longer claims a cache that does not exist.
- [x] `npm test` and `npm run build` green. **No test makes a live Steam call.**

## Notes

- **Do NOT verify this with a live family import.** Running one is precisely the act under
  suspicion. Every criterion above is deliberately satisfiable against a fake `HttpClient`.
- **Caching is deliberately out of scope.** An `appdetails` response cache was considered and
  rejected here: it introduces staleness, eviction and storage decisions, and
  integration-n3vqa's "skip known apps entirely" design supersedes most of its value —
  skipping a call beats caching its answer. A pointer note is recorded on n3vqa; revisit only
  if a residual need survives that design.
- **Throttle and incremental import are complementary, not alternatives.** This task lowers the
  *rate*; integration-n3vqa lowers the *count*. Neither subsumes the other, and they touch
  independent code paths — no dependency either way.
- **Known consequence:** at 1500ms a full re-enrich of a several-hundred-title family library
  takes minutes (400 apps ≈ 10 min). That is the correct trade for a rarely-used full sweep,
  and it is exactly why n3vqa (making the steady-state import a handful of calls) matters more
  after this lands, not less. If the worker judges the full-sweep UX unacceptable, surface it —
  do not quietly lower the interval below the derived ceiling.
- RAWG search and cover/backdrop downloads hit different hosts (RAWG API, Steam CDN) and are
  out of scope; the storefront gate serialises the loop between them in any case.
- No automatic retries exist on this path today — nothing to remove, but do not add any.
- An identifying `User-Agent` on Steam requests is cheap hygiene and may be folded in; not
  required.
- **Open question for the worker:** should `GameDeckCompatBackfill`'s HTML store-*page* fetches
  share the same gate (same host, different path)? Cheap if yes, not required for this fix.

## Ubiquitous language addition

- **Throttle** — Adapter-owned minimum spacing between calls to one external endpoint family.
  Lives inside the Adapter, because a vendor's rate ceiling is vendor knowledge; callers never
  pace themselves.

## Outcome

Added `Steam.throttleStorefrontCall` (a `SemaphoreSlim(1,1)`-backed gate held for a call's
*full* duration plus a minimum-interval wait, default 1500ms, mutable/public for test
injection) and routed every storefront caller through it: `getSteamStoreDetails`,
`getSteamStoreTrailer`, `getSteamStoreTrailers`, `fetchStoreMeta` (network-fetch branch
only — cache hits stay instant), and `getDeckCompatibility` (the open question in Notes —
decided yes, same host, one shared budget). Deleted the four now-redundant call-site
`Async.Sleep 300`s (`Api.fs`, `GameFacetBackfill.fs`, `GameReleaseDateBackfill.fs`,
`GameDeckCompatBackfill.fs`). Fixed the stale "full-response cache" comment at
`Steam.fs`'s `storeMetaCache` declaration. Two new Expecto tests in
`tests/Server.Tests/SteamStorefrontThrottleTests.fs` (`testSequenced` to avoid racing each
other over the shared mutable interval): a direct unit test on the gate (two back-to-back
gated calls spaced ≥ interval) and a full `importSteamFamily` run against a stub
`HttpMessageHandler` with 3 shared apps, asserting exactly 3 `appdetails` requests spaced by
the interval and never more than one in flight (the sequential per-app loop's guarantee,
pinned). `npm test`: 696 tests green (2 added), no live Steam call in any of them. `npm run
build`: clean. ADR-0066 records the derivation, the eleven-call-site history, and the
Deck-compat routing decision.

Key files: `src/Server/Steam.fs`, `src/Server/Api.fs`, `src/Server/GameFacetBackfill.fs`,
`src/Server/GameReleaseDateBackfill.fs`, `src/Server/GameDeckCompatBackfill.fs`,
`tests/Server.Tests/SteamStorefrontThrottleTests.fs`,
`.agentheim/knowledge/decisions/0066-steam-storefront-throttle-is-adapter-owned.md`,
`.agentheim/contexts/integration/README.md`.

## Verifier note (iteration 1)

**VERDICT: FAIL**

**REASONS:**
- `npm test` is **not reliably green** — the newly added test fails intermittently. Run 1 from the
  worktree: `696 tests run … 695 passed, 1 failed` — `Steam storefront throttle
  (integration-w7ktb).importSteamFamily against 3 shared apps issues exactly 3 appdetails requests,
  spaced by the interval, never two in flight` failed with `Expected consecutive appdetails requests
  spaced at least 00:00:00.0800000 apart, got 00:00:00.0797100`
  (`tests/Server.Tests/SteamStorefrontThrottleTests.fs:176`). Run 2, unchanged tree: 696/696 passed.
  Two back-to-back runs, one red one green — this is a flaky timing assertion, not a transient
  environment problem, so the last acceptance criterion ("`npm test` … green") is not met.
- Root cause is the measurement, not the throttle. `Steam.throttleStorefrontCall`
  (`src/Server/Steam.fs:270-285`) spaces the moments it *records*
  (`lastStorefrontCallStartedAt <- DateTime.UtcNow`, then `fetch ()`), while the test measures at
  `SendAsync` entry inside the stub (`SteamStorefrontThrottleTests.fs:118-121`). The variable
  HttpClient-pipeline delay between "gate releases" and "SendAsync entered" — larger on the first
  request (warm-up) than on later ones — subtracts from the observed gap, so a strict
  `gap >= interval` against an 80ms interval can undershoot by a fraction of a millisecond.
  Everything else about the change checks out (gate present and injectable, all storefront call
  sites routed, three call-site `Async.Sleep`s deleted, stale cache comment fixed, default interval
  genuinely 1500ms, ADR-0066 records the Deck-compat open question, README `Throttle` entry added,
  `Steam.fs` churn is purely mechanical re-indent) — this single assertion is what blocks the commit.
- Note (not itself the FAIL): `Expect.equal log.MaxInFlight 1` at
  `SteamStorefrontThrottleTests.fs:180` would still pass with the gate deleted entirely, since
  `runSteamFamilyImport`'s per-app loop is already sequential. That is exactly what the criterion
  asks for ("the sequential-loop guarantee, pinned"), so it is acceptable — but the *spacing*
  assertion is the only thing pinning the throttle itself, which makes its flakiness worse than
  cosmetic.

**SUGGESTED_FIX:** Make the spacing assertion robust without weakening it — e.g. assert
`gap >= interval - TimeSpan.FromMilliseconds 5.0` (a stated clock/dispatch-overhead tolerance, still
~4x below the 15ms stub delay so a removed interval wait would fail loudly), or raise the test
interval to ~250ms so pipeline jitter cannot cross the threshold, or record timestamps at the gate
boundary rather than at `SendAsync`. Then re-run `npm test` (several times) plus `npm run build`,
which was not reached in this verification.

**ITERATION_HINT:** likely-fixable

## Iteration 2 fix

The flaky spacing assertion in `tests/Server.Tests/SteamStorefrontThrottleTests.fs` is fixed on the
test side only (no production-code or default-interval change).

Root cause, refined from the verifier's diagnosis: it isn't only the HttpClient-pipeline hop
between the gate releasing and `SendAsync` being entered — a *direct* mutation-check run (interval
raised to 250ms, no HTTP involved at all) showed the same undershoot on the gate-only unit test
too, by up to ~2.5ms. `Async.Sleep`/`Task.Delay` (which backs `throttleStorefrontCall`'s interval
wait) is only guaranteed to sleep *at least* the requested duration in principle; in practice the
*measured* gap between two gated calls can land a few ms under the nominal interval from ordinary
timer-resolution/scheduling jitter — a roughly-bounded absolute quantity, not proportional to the
configured interval. That means raising the interval alone (the "raise to ~250ms" suggested fix)
narrows the problem but does not eliminate it category — confirmed by reproducing a flake at 250ms
too during iteration-2 testing.

Fix applied: combined the two remaining suggested-fix options rather than picking one in
isolation, because neither alone survived reproduction:
- Raised the family-import test's interval from 80ms to 250ms (keeps the HTTP-pipeline-hop jitter
  proportionally negligible, sub-second total runtime).
- Added a single shared `clockTolerance = 5ms` (`SteamStorefrontThrottleTests.fs:85-95`), applied
  to **both** timing assertions (the gate-only unit test and the family-import test) as
  `gap >= interval - clockTolerance`. 5ms comfortably covers every jitter magnitude observed during
  iteration-2 reproduction (largest seen: ~2.5ms) while staying ~3x below the family-import stub's
  15ms simulated response delay, so a deleted interval wait still fails loudly (verified below).

Mutation-tested twice against the final version (temporarily replaced the gate's `Async.Sleep
remaining` wait with a no-op in `Steam.fs`, confirmed both assertions fail loudly, restored the
original code, confirmed a byte-for-byte match against git before rebuilding) — both timing
assertions caught the deleted wait every time.

`npm test`: 6 consecutive full green runs (696/696) after the fix, from a clean rebuild (`bin`/
`obj` deleted for `Server`, `Server.Tests`, `Shared` — stale build artifacts from the mutation-check
step had briefly caused misleading results against unchanged, correct source; a clean rebuild
confirmed the real state). One unrelated SQLite/thread-pool anomaly occurred once during
back-to-back invocations without a full process exit in between; it left no trace in the
throttle tests' own output and did not recur across 6 subsequent runs — not something this task's
scope covers. `npm run build`: clean (`✓ built in 3m 25s`, only the pre-existing unrelated
`AdminProjections/Views.fs` warning).

Key file: `tests/Server.Tests/SteamStorefrontThrottleTests.fs` (test-only change).
