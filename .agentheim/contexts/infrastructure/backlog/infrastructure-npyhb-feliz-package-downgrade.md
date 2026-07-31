---
id: infrastructure-npyhb
title: "NU1605 — Client.fsproj pins Feliz 2.* while Feliz.DaisyUI 5.3.0 requires >= 3.1.1, so NuGet silently downgrades 3.1.1 → 2.9.0 and the client compiles against a Feliz surface its dependency does not expect"
status: backlog
type: spike
context: infrastructure
created: 2026-07-31
completed:
depends_on: []
blocks: []
tags: [nuget, packages, feliz, daisyui, build-health, tech-debt]
related_adrs: [0001]
related_research: []
prior_art: []
---

## Why

`dotnet build src/Client/Client.fsproj` reports:

```
warning NU1605: Detected package downgrade: Feliz from 3.1.1 to 2.9.0.
  Client -> Feliz.DaisyUI 5.3.0 -> Feliz (>= 3.1.1)
  Client -> Feliz (>= 2.0.0)
```

`Client.fsproj` pins `Feliz Version="2.*"` and `Feliz.DaisyUI Version="5.*"`.
The latter floats to 5.3.0, which declares a dependency on `Feliz >= 3.1.1` —
but the direct `2.*` pin wins, so Feliz.DaisyUI 5.3.0 runs against Feliz 2.9.0,
a major version below what it asks for.

Nothing is currently known to be broken by this, which is exactly why it wants
a spike rather than a fix: it is a **latent** mismatch of the same family as
`design-system-q4ebg` — code compiled against an API surface that is not the
one actually resolved. It is plausibly *why* the `bordered` confusion was easy
to fall into: the Feliz/Feliz.DaisyUI version story in this project is not
coherent, so "which modifiers exist" is not something a contributor can reason
about from the fsproj.

Surfaced while refining `administration-bq4tw` (now `design-system-q4ebg`).

## What

Find out what the downgrade actually costs and what it would take to resolve
it, then either fix it or record why it is being left.

- Establish whether anything in the client currently depends on Feliz 2.x
  behavior that 3.x changes — Feliz 2 → 3 is a major bump, so `prop`/`Html`
  surface changes are the risk area.
- Determine what bumping `Feliz` to `3.*` breaks, if anything. The client is
  large (`src/Client/`, ~all pages); a compile pass is the cheap first probe.
- Check whether `Feliz.UseElmish 2.*` and `Feliz.Router 4.*` have their own
  constraints that conflict with a Feliz 3 bump.
- Decide: bump `Feliz` to `3.*`, pin `Feliz.DaisyUI` back to a 5.x that is
  content with Feliz 2, or accept the downgrade with a recorded rationale.

If, mid-spike, the mitigation is already known and cheap, record it and stop —
do not keep investigating past the point where the answer is in hand.

## Acceptance criteria

- [ ] The concrete blast radius of bumping `Feliz` to `3.*` is recorded — at
      minimum whether `dotnet build src/Client/Client.fsproj` still exits 0
      under the bump, and the count and nature of any new errors.
- [ ] A decision is recorded (bump / re-pin DaisyUI / accept with rationale),
      with the reasoning, as an ADR if the outcome is "accept" or "re-pin".
- [ ] If the decision is to bump or re-pin, `NU1605` no longer appears in
      `dotnet build src/Client/Client.fsproj` output and the client still
      builds and renders.
- [ ] If the decision is to accept, the rationale is recorded in the
      infrastructure README and the warning is explicitly acknowledged rather
      than left as unexplained build noise.

## Notes

- Filed at the builder's direction during `administration-bq4tw`'s refinement,
  as a separate under-refined item rather than folded into the build-gate task
  (`infrastructure-p1h9a`) — a Feliz major-version bump can cascade across the
  entire client and must not be allowed to block a one-line build-script gate.
- Explicitly **not** a dependency of `infrastructure-p1h9a`: that gate must
  fail on errors only, never on warnings, so it can ship while this stays open.
- Routing: globally-true — the client's base library versions are a
  whole-system tooling concern, not any single BC's.
