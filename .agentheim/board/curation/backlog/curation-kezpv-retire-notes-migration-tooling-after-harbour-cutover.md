---
id: curation-kezpv
title: Retire the one-off Notes migration tooling after the harbour cutover — delete Gate 1 "Migrate to Notes", Gate 2 "Purge legacy stores" (five `IAdminApi` members, DTOs, AdminSurgery sections, `ContentBlockConversion.fs` and tests) and the Health-tab catalog media-type backfill action, once both have run on the live store
status: backlog
type: chore
context: curation
created: 2026-09-16
completed:
depends_on: [design-system-001-formalize-styleguide]
blocks: []
tags: [notes, migration, administration, surgery, health, catalog, curation]
related_adrs: [0080, 0034, 0079]
related_research: []
prior_art: [curation-j4qqt, curation-w9fkq]
---

## Why

Both migration actions are one-off operator tooling with no purpose once the live store has been
migrated: Gate 1 / Gate 2 (curation-j4qqt, ADR-0080) and the Health-tab catalog media-type
backfill (curation-w9fkq, ADR-0079 §5). Neither ADR planned a retirement. Left in place they sit
on the Surgery and Health tabs forever — the gates erroring (or, after curation-tb0nn, reporting
"already purged") and the backfill reporting zero / zero / zero on every run. The builder asked on
2026-09-16 what happens to them after the migration; the answer is "nothing, unless we delete
them", so this task does.

**Precondition — a real-world event, not a task dependency:** Gate 1, Gate 2 and the backfill
have each been run on harbour's live store (and a Rebuild-all done, Health tab clean). Promote
this task only after the builder confirms that; do not promote it from the dev copy's state.

## What

- Delete Gate 1 and Gate 2 end to end: `IAdminApi.previewNotesMigration` / `runNotesMigration` /
  `previewPurgeLegacyNotes` / `purgeLegacyNotes` (and the fifth member, the backfill below);
  `NotesMigrationPreview`, `NotesMigrationReport`, `NotesMigrationResolvedOwner`,
  `NotesMigrationOwnerRef`, `PurgeLegacyNotesPreview` in Shared; the `Administration.fs`
  implementation (`previewNotesMigrationCore`, `runNotesMigrationCore`, `readLegacyContentBlocks`,
  `readGameJournalBlocks`, `previewPurgeLegacyNotesCore`, `purgeLegacyNotesCore`); the
  AdminSurgery Types/State/Views sections including the `NotesMigrationConfirmedThisSession` flag;
  `src/Server/ContentBlockConversion.fs`; `tests/Server.Tests/ContentBlockConversionTests.fs` and
  `NotesMigrationTests.fs`.
- Delete the Health-tab backfill: `IAdminApi.backfillCatalogEntryMediaTypes`,
  `CatalogMediaTypeBackfillReport` and its entry DTOs, `Administration`'s implementation and the
  Health-tab button/report in the client. **Keep** `Catalogs.Entry_media_types_inferred` as a
  handled event type with its `EventFormatting.formatCatalogEvent` arm and its `evolve` arm —
  the events already appended are permanent history the projection must keep replaying.
- **Keep** `EventStore.deleteEventsByStreamIds` / `previewBulkDeleteByStreamIds` and their
  `EventSurgeryTests.fs` cases — general-purpose surgery primitives (ADR-0080 §11), not migration
  code. Keep `CatalogProjection.resolveMediaType` if anything else still calls it; delete it
  only if the backfill was its last caller.
- Sweep tests that reference any deleted name.

## Acceptance criteria

- [ ] `grep -rn "NotesMigration\|PurgeLegacyNotes\|purgeLegacyNotes\|ContentBlockConversion\|LegacyContentBlock\|backfillCatalogEntryMediaTypes\|CatalogMediaTypeBackfill\|CatalogBackfill" src tests --include=*.fs` returns nothing.
- [ ] `src/Server/ContentBlockConversion.fs`, `tests/Server.Tests/ContentBlockConversionTests.fs`
      and `tests/Server.Tests/NotesMigrationTests.fs` no longer exist and are gone from both
      `.fsproj` files.
- [ ] `EventSurgeryTests.fs`'s `deleteEventsByStreamIds` / `previewBulkDeleteByStreamIds` cases
      are unchanged and green.
- [ ] Expecto: a stored `Entry_media_types_inferred` event still deserializes, formats, and
      replays into `catalog_entries.media_type` (existing CatalogProjection tests unchanged).
- [ ] The Surgery tab no longer renders a Gate 1 or Gate 2 card; the Health tab no longer
      renders the media-type backfill action. [human-eye]
- [ ] `dotnet build`, `npm run build`, `npm test`, `npm run test:client` green.
- [ ] Builder has recorded in this task's Notes the date Gate 1, Gate 2 and the backfill were run
      on harbour, before promotion. [human-eye]

## Notes

- Sequencing: curation-tb0nn (the post-purge preview guard) ships first and is deleted again by
  this task; that is intended — the guard covers the window between the harbour purge and this
  retirement.
- Known, deliberately unfixed data-loss trap in Gate 1 (builder decision 2026-09-16): an owner
  whose Notes document already exists — including an editor-created empty document from merely
  opening a Notes tab on the new build — is skipped, and Gate 2 then drops its legacy content.
  On harbour, run Gate 1 before opening any detail page's Notes tab. On the dev copy this bit
  starcom-unknown-space-2022 (12 journal blocks) and disco-elysium-2019 (1); both survive in the
  ADR-0034 pre-purge backup `~/app/mediatheca/backups/mediatheca-20260916T2018583609196-97182b8d.db`.
- ADR-0080 §12 describes the registry entries that were removed with Gate 2; nothing further
  changes in the registries here — `Notes`/`notes_blocks` stay.
- After this ships, `TableClassificationTests.fs` and the handled-event guard should be untouched:
  no table or event type is added or removed by this task.
