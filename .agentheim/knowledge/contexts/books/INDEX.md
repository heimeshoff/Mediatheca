# books — Index (knowledge)

Catalog of ADRs, research, and concept synthesis pages scoped to this bounded context.

> Updated by: `work` (BC-scoped ADRs, concept page links), `research` (BC-scoped reports).
> Hand-edits are fine but the skills will append at the section markers below.

---

## ADRs scoped to this BC

<!-- adr-local:start -->
- **0082** — A source's first-ever reading-progress report per book is a prior, not a session: `Prior_reading_progress_recorded` (never InFocus-promoting, dated by the source's own last-known-true day when available, `book_progress.kind = 'prior'`), and a manual Finished click stamps today's local date instead of the event's UTC append timestamp (amends ADR-0076 §2, ADR-0077 §5) — 2026-09-18 — `../../decisions/0082-prior-reading-progress-and-manual-finish-local-date.md`
- **0077** — Book status changes carry `effectiveOn: string option` (amends ADR-0076 §5): a Finished status can be backdated to the day a source says it became true (Goodreads `user_read_at`, an observation's own day); `finished_at` is a `yyyy-MM-dd` date string; re-dating an already-Finished book is a legitimate event — 2026-09-16 — `../../decisions/0077-book-status-change-carries-effective-on-date.md`
- **0076** — Books model: a reading-progress observation is an event (ADR-0043 engagement test), length/description are cache tier, the status lifecycle mirrors Games (`Backlog | InFocus | Finished | Abandoned`) with progress-driven promotion and 100 %-driven finish — 2026-09-16 — `../../decisions/0076-books-progress-observation-events-and-status-lifecycle.md`
<!-- adr-local:end -->

## Research touching this BC

<!-- research-local:start -->
<!-- research-local:end -->

## Concepts (opt-in synthesis pages)

<!-- concepts:start -->
<!-- concepts:end -->

## Pointers

- BC README (ubiquitous language, invariants): `README.md`
- Task board (tasks by status) for this BC: `../../../board/books/INDEX.md`
