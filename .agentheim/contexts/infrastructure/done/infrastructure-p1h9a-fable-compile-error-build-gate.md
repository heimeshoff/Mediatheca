---
id: infrastructure-p1h9a
title: "Fail the client build on Fable compile errors — `vite build` exits 0 while emitting throwing placeholders for FS-level errors, so broken UI ships silently (twice already)"
status: done
type: chore
context: infrastructure
created: 2026-07-31
depends_on: [design-system-q4ebg, infrastructure-npyhb]
completed: 2026-07-31
blocks: []
tags: [build-health, fable, vite, tooling, ci]
related_adrs: [0036, 0037]
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

Confirm the gate is not itself noisy: `dotnet build src/Client/Client.fsproj -v q
--nologo` now exits 0 with exactly `1 Warning(s)  0 Error(s)`. The single
remaining warning is `FS0020` (implicitly-ignored `ReactElement`) at
`src/Client/Pages/AdminProjections/Views.fs(199,13)`. The two `NU1605`
package-downgrade warnings that existed when this task was captured are gone —
`infrastructure-npyhb`'s `Feliz.DaisyUI` 5.2.0 pin (ADR-0036) cleared them along
with the `FS0193` error they had been masking. Warnings must **not** fail the
gate — only errors; there is no remaining reason to reach for
`TreatWarningsAsErrors`. Leave the `FS0020` unfixed (see Notes) — it is a real
but separate one-line cleanup, not this gate's job.

Both `depends_on` edges were load-bearing preconditions and are now met.
`design-system-q4ebg` (done 2026-07-31) cleared the sixteen `.bordered`
`FS0039`s. `infrastructure-npyhb` (done 2026-07-31) pinned `Feliz.DaisyUI` to the
exact `5.2.0` (ADR-0036), clearing the `FS0193` that q4ebg's fix had exposed once
the `FS0039`s stopped masking it. `dotnet build src/Client/Client.fsproj -v q
--nologo` now exits 0 — the tree is clean, and the gate's chosen mechanism is
confirmed able to succeed on it. This paragraph is retained as a record of why
the two edges existed, not as an open precondition.

## Acceptance criteria

- [x] `npm run typecheck` exists and runs a real F# compile of
      `src/Client/Client.fsproj`.
- [x] `npm run build` invokes the typecheck first and **exits non-zero** when
      the client has any F# compile error.
- [x] Proven by construction, not by assertion: temporarily reintroduce one
      `textarea.bordered` (or any deliberate FS error), show `npm run build`
      exits non-zero, then revert. Record the observed exit code in the task's
      Outcome. After reverting, confirm via `git status` / `git diff --stat` that
      the tree carries no leftover changes from the reintroduced error (the same
      verification discipline `infrastructure-npyhb` used for its own temporary-aid
      check).
- [x] On the clean tree, `npm run build` still exits 0 and still emits the same
      bundle to `deploy/public/` — the existing 1 warning (`FS0020`) does not
      fail it.
- [x] `npm run dev:client` is unchanged (no typecheck added to the watch loop).
- [x] The infrastructure README (or an inline comment beside the `typecheck`
      script in `package.json`) records the gate's known blind spot per ADR-0036:
      `dotnet build` typechecks this project's own F# but is not proof of what
      Fable will emit, since the two pathways consume different inputs (prebuilt
      `.dll` vs Fable-compiled `.fs` sources) — confirmed `FS0193`-class failures
      can happen on the MSBuild side with zero effect on the shipped bundle, and
      in principle a Fable-source-only issue could go uncaught by this gate.

## Outcome

Added `"typecheck": "dotnet build src/Client/Client.fsproj"` to
`package.json` and changed `"build"` to
`"npm run typecheck && vite build"`. `dev:client` is untouched.

**Proven by construction:** temporarily added `textarea.bordered` to
`Daisy.textarea` in `src/Client/Pages/AdminSurgery/Views.fs` (line 91-96).
`npm run build` exited **1**, printing
`error FS0039: The type 'textarea' does not define the field, constructor or
member 'bordered'` and `2 Error(s)`. Reverted the edit; `git status --porcelain`
and `git diff --stat` on that file both returned empty, confirming no residue.
Re-ran `npm run build` on the clean tree: exited **0** with `1 Warning(s)  0
Error(s)` (the pre-existing `FS0020` at `AdminProjections/Views.fs(199,13)`,
left unfixed as scoped). Bundle content hashes were measured identical across
the pre-error and post-revert builds:
`assets/index-Dnf1E92D.css` and `assets/index-UcBhDRFf.js`, both times.

Recorded the ADR-0036-inherited blind spot (MSBuild binds prebuilt `.dll`
assemblies, Fable compiles `.fs` sources directly — the gate typechecks this
project's own F# but is not proof of what Fable will emit) in the
infrastructure BC README under a new "Client build gate" bullet, and wrote
ADR-0037 documenting the full decision, the ruled-out plugin-option and
log-scraping alternatives, and the same blind spot.

`npm test` (Expecto, 416 tests) passed unaffected after the change.

Key files: `package.json`,
`.agentheim/contexts/infrastructure/README.md`,
`.agentheim/knowledge/decisions/0037-client-build-fails-on-fable-compile-errors.md`.

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
- **Checked and closed: `vite-plugin-fable` (installed `0.1.1`) has no option to
  make FS errors fatal.** Established during this refinement by reading
  `node_modules/vite-plugin-fable/index.js` in full. Its `PluginOptions` typedef
  exposes only `fsproj`, `jsx`, `noReflection`, `exclude` — nothing
  severity-related. `logDiagnostics` logs every diagnostic via
  `console.log`/`warn`/`error` regardless of `"error"`/`"warning"` severity and
  never throws, never calls Rollup's `this.error(...)`, never sets
  `process.exitCode`. The two `throw new Error(...)` sites fire only on a daemon
  RPC-transport failure — a compile that succeeds but *contains* FS errors still
  returns the `Success` case (`Fable.Daemon/Types.fs`'s
  `ProjectChangedResult.Success` carries `diagnostics` as a normal, non-failing
  field), so those errors flow into `logDiagnostics` rather than the throwing
  path. `transform` returns `state.compilableFiles` unconditionally — that is the
  exact mechanism emitting the `throw 1` placeholder with no severity check.
  Grepping for `this.error`, `exitCode`, `process.exit`, `strict`, `fatal`,
  `failOn` returns zero matches. The package README describes it as pre-alpha and
  "up for adoption" (unmaintained). **Conclusion: the secondary `dotnet build`
  pass is not a stopgap, it is the only available mechanism today.** Do not
  re-open this question; if the plugin later gains such an option it should
  supersede this gate (recorded in the ADR below).
- The `dotnet build` pass's blind spot stands (ADR-0036): `dotnet build` and Fable
  consume *different inputs* — prebuilt `lib/*.dll` assemblies versus `fable/*.fs`
  sources. An MSBuild pass is a genuine typecheck of *this project's own F#*,
  which is what this gate wants from it, but it is **not** evidence about what
  Fable will emit: it can fail on assembly-binding problems that never reach the
  bundle (`FS0193` is the worked example, now closed) and in principle could miss
  a Fable-source-only problem. Acceptance criterion 6 makes recording this limit
  a deliverable rather than only prose here.
- Measured cost: the `dotnet build` pass takes ~27s warm on the dev machine (the
  earlier "~20s" estimate was close but unmeasured).
- **ADR-0037 to be written by the worker** — the errors-fatal client build gate,
  its rejected alternatives (log-scraping; a plugin option, now definitively ruled
  out), and its ADR-0036-inherited blind spot. `0036` is the highest ADR on disk;
  `administration-n8kqw` nominally claims `0038`, so re-confirm the next free
  number at write time in case that task lands first.
