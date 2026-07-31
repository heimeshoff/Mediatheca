---
id: infrastructure-p1h9a
title: "Fail the client build on Fable compile errors — `vite build` exits 0 while emitting throwing placeholders for FS-level errors, so broken UI ships silently (twice already)"
status: backlog
type: chore
context: infrastructure
created: 2026-07-31
depends_on: [design-system-q4ebg, infrastructure-npyhb]
completed:
blocks: []
tags: [build-health, fable, vite, tooling, ci]
related_adrs: []
related_research: []
prior_art: [infrastructure-w8fnp]
---

## Why

`vite-plugin-fable` treats F# compile errors as **non-fatal to bundle
emission**. It prints `ERROR FS…` mid-log, emits a throwing placeholder
(`throw 1`) where the unresolvable expression was, and lets `vite build` exit
**0**. The bundle ships. The first user to render that component gets an
exception with no error boundary to contain it, so the entire `#feliz-app`
root unmounts — a blank page.

This is not hypothetical, and it is not a one-off:

| Date | Event |
|---|---|
| 2026-07-21 | `design-system-dib4q` clears 3 recurring `FS0039`s and explicitly warns they "can mask a genuinely new compile error a future change introduces" |
| 2026-07-22 | `administration-xjmda` ships 2 new `FS0039`s — composer dead on arrival |
| 2026-07-22 | `administration-wwc36` ships 2 more — Surgery Edit panel dead on arrival |
| 2026-07-31 | Discovered only because `administration-svq3t` tried to drive the Surgery tab with Playwright and caught the `pageerror` |

Two shipped features were broken for nine days behind a green build. Nobody
scrolled past the asset-size summary at the tail of a `vite build` run, and
`administration-wwc36`'s own `[human-eye]` UI sign-off passed anyway. A grep-
based cleanup (dib4q) demonstrably does not hold — the same mistake returned
the next day on a sibling element type. The gap is structural: **there is no
step in this project that fails when the client does not typecheck.**

The server side has no such hole — `npm test` and `dotnet run` both go through
a real MSBuild pass that fails on errors. Only the Fable-compiled client
escapes.

## What

Add a typecheck step that fails on F# compile errors, and put it in front of
the client build.

**The mechanism is already verified to work.** A plain MSBuild pass over the
client project catches these exactly:

```
$ dotnet build src/Client/Client.fsproj -v q --nologo
... error FS0039: The type 'select' does not define ... 'bordered'
    3 Warning(s)
    16 Error(s)
EXIT=1
```

So no log-scraping for `ERROR FS` is needed — a real compiler pass already
exits non-zero. Preferred shape:

- Add `"typecheck": "dotnet build src/Client/Client.fsproj"` to
  `package.json` scripts.
- Make `"build"` run it first, so `npm run build` cannot succeed on a client
  that does not compile.
- Leave `dev:client` alone — the dev loop should stay fast and tolerant; this
  gate is for the build, not the watch.

Confirm the gate is not itself noisy: the client currently carries 3 warnings
(two `NU1605` package-downgrade pairs, one `FS0020` implicit-ignore in
`AdminProjections/Views.fs:199`). Warnings must **not** fail the gate — only
errors. Do not reach for `TreatWarningsAsErrors`; the `NU1605` downgrade is a
real but separate issue tracked as `infrastructure-npyhb`, and coupling the two
would block this gate on a package-version investigation.

Both `depends_on` edges are load-bearing — the gate must land on an already-clean
tree, or `npm run build` breaks for everyone the moment it merges:

- `design-system-q4ebg` clears the sixteen `.bordered` `FS0039`s.
- `infrastructure-npyhb` clears the `FS0193` that q4ebg's fix *exposes* — the
  gate's own mechanism (`dotnet build src/Client/Client.fsproj`) cannot exit 0
  until `Feliz.DaisyUI` is pinned to 5.2.0 (ADR-0036). This edge was added
  2026-07-31 during npyhb's refinement; the "3 warnings, errors-only" premise
  below was written before the `FS0193` was known.

## Acceptance criteria

- [ ] `npm run typecheck` exists and runs a real F# compile of
      `src/Client/Client.fsproj`.
- [ ] `npm run build` invokes the typecheck first and **exits non-zero** when
      the client has any F# compile error.
- [ ] Proven by construction, not by assertion: temporarily reintroduce one
      `textarea.bordered` (or any deliberate FS error), show `npm run build`
      exits non-zero, then revert. Record the observed exit code in the task's
      Outcome.
- [ ] On the clean tree, `npm run build` still exits 0 and still emits the same
      bundle to `deploy/public/` — the existing 3 warnings do not fail it.
- [ ] `npm run dev:client` is unchanged (no typecheck added to the watch loop).

## Notes

- Routing: globally-true per the infrastructure scope test — the gate protects
  every BC's client code, and would still be needed if the administration and
  design-system BCs did not exist. Split out of `administration-bq4tw` at the
  builder's direction during that task's refinement.
- Alternative considered and rejected: grepping the `vite build` log for
  `ERROR FS` and failing the script on a match. Works, but reimplements in
  shell what MSBuild already reports via exit code, and is brittle against log
  format changes and ANSI coloring (the real log lines are wrapped in escape
  sequences).
- Deliberately out of scope: adding CI. This project has no CI pipeline; the
  gate belongs in the build script that already exists, and remains useful the
  day CI does arrive.
- Worth checking while here, but not a criterion: whether `vite-plugin-fable`
  exposes an option to make FS errors fatal directly. If it does, that is a
  cleaner mechanism than a second MSBuild pass and should be preferred — note
  the finding either way, since a second full compile adds ~20s to
  `npm run build`. **This is now more than a nicety** (established during
  `infrastructure-npyhb`'s refinement, ADR-0036): `dotnet build` and Fable
  consume *different inputs* — prebuilt `lib/*.dll` assemblies versus
  `fable/*.fs` sources. An MSBuild pass is a genuine typecheck of *this
  project's own F#*, which is what this gate wants from it, but it is **not**
  evidence about what Fable will emit: it can fail on assembly-binding problems
  that never reach the bundle (`FS0193` is the worked example) and in principle
  could miss a Fable-source-only problem. Worth recording that limit in whatever
  the gate ends up being.
