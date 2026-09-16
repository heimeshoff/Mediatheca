---
id: curation-kezpv
title: Retire the one-off Notes migration tooling after the harbour cutover — delete Gate 1 "Migrate to Notes", Gate 2 "Purge legacy stores" (five `IAdminApi` members, DTOs, AdminSurgery sections, `ContentBlockConversion.fs` and tests) and the Health-tab catalog media-type backfill action, once both have run on the live store
status: todo
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
migrated: Gate 1 / Gate 2 (curation-j4qqt, ADR-0080 §10-§12) and the Health-tab catalog media-type
backfill (curation-w9fkq, ADR-0079 §5). Neither ADR planned a retirement. Left in place they sit
on the Surgery and Health tabs forever — the gate previews now answer HTTP 500 ("no such table:
content_blocks") on every click because the legacy tables are gone, and the backfill reports
zero / zero / zero on every run. The builder asked on 2026-09-16 what happens to them after the
migration; the answer is "nothing, unless we delete them", so this task does.

**Precondition — met (verified 2026-09-16, see Notes).** Gate 1, Gate 2 and the backfill have all
run on harbour's live store. The interim guard task (curation-tb0nn) was dismissed the same
evening: there is no window left to guard, the retirement is the fix.

## What

**Gate 1 + Gate 2, end to end (ADR-0080 §10-§12):**

- `IAdminApi` (Shared, around line 2567-2585): drop `previewNotesMigration`, `runNotesMigration`,
  `previewPurgeLegacyNotes`, `purgeLegacyNotes`, and the fifth member
  `backfillCatalogEntryMediaTypes` (below).
- Shared DTOs: `NotesMigrationOwnerRef`, `NotesMigrationResolvedOwner`, `NotesMigrationPreview`,
  `NotesMigrationReport`, `PurgeLegacyNotesPreview`.
- `src/Server/Administration.fs`: `previewNotesMigrationCore`, `runNotesMigrationCore`,
  `readLegacyContentBlocks`, `readGameJournalBlocks`, `getContentBlockOwnerSlugs` /
  `getGameJournalOwnerSlugs` (or however the owner-slug reads are named),
  `previewPurgeLegacyNotesCore`, `purgeLegacyNotesCore`, and their `IAdminApi` wiring.
- `src/Server/ContentBlockConversion.fs` — delete the file and its `Server.fsproj` entry.
- `src/Client/Pages/AdminSurgery/{Types,State,Views}.fs`: the `PendingPurgeLegacyNotes` case, the
  `NotesMigrationConfirmedThisSession` flag and the Gate 1 / Gate 2 model fields, the seven
  `*_notes_migration_*` / `*_purge_legacy_notes_*` `Msg` cases and their `update` arms, and both
  gate cards in the view. The edit / delete / rename sections and backup stats stay.
- Tests: delete `tests/Server.Tests/ContentBlockConversionTests.fs` and
  `tests/Server.Tests/NotesMigrationTests.fs` (and their `Server.Tests.fsproj` entries).

**Health-tab backfill (ADR-0079 §5):**

- `IAdminApi.backfillCatalogEntryMediaTypes`, `CatalogMediaTypeBackfillReport` and its entry DTOs
  (Shared, around line 2424-2445), the `Administration.fs` implementation (around lines 125-240),
  and the button + report in `src/Client/Pages/AdminHealth/{Types,State,Views}.fs`.
- `CatalogProjection.resolveMediaType` **and** its `MediaTypeResolution` DU
  (`Resolved | Ambiguous | Orphan`, `CatalogProjection.fs` ~447-490): the two backfill call sites
  in `Administration.fs` (~167 and ~233) are its only callers — the `evolve` arm for
  `Entry_media_types_inferred` takes already-resolved values from the event and never calls it.
  Delete both, plus the three `resolveMediaType` cases in `CatalogProjectionTests.fs` (~356-385).
- `tests/Server.Tests/AdministrationTests.fs`: delete the backfill helper block (~128-170) and the
  four `backfillCatalogEntryMediaTypes` cases (~1202-1300). **Keep** the `getHealthStats
  Entry_media_types_inferred appears in neither the unhandled nor the unformattable list` case
  (~542) — it guards the event type, not the action.

**Keep — not migration code:**

- `Catalogs.Entry_media_types_inferred` as a handled event type: its `decide`-less `evolve` arm
  (`Catalogs.fs`), the `CatalogProjection.fs` arm, its Serialization encode/decode, and the
  `EventFormatting.formatCatalogEvent` arm (~434). Three such events already sit in harbour's
  log; the projection must keep replaying them.
- `EventStore.deleteEventsByStreamIds` / `previewBulkDeleteByStreamIds` and their
  `EventSurgeryTests.fs` cases — general-purpose surgery primitives (ADR-0080 §11).
- `Notes` / `notes_blocks` and every registry entry ADR-0080 §12 added; the handled-event guard
  and `TableClassificationTests.fs` see no table or event type added or removed by this task.

**ADR retirement notes (reported via `ADRS_WRITTEN`, materialized by the conductor):**

- ADR-0080: an `## Amendment <date> (curation-kezpv, retirement)` section under §10-§12 stating
  the gates ran on harbour on 2026-09-16 (timestamps in Notes) and were deleted by this task;
  §11's bulk-purge primitive stays.
- ADR-0079: a matching amendment under the curation-w9fkq one, stating the backfill ran on
  harbour on 2026-09-16 (3 `Entry_media_types_inferred` events, 0 untyped entries left) and was
  deleted; the event type is permanent.

Sweep every remaining reference (comments and doc-strings included) to any deleted name.

## Acceptance criteria

- [ ] `grep -rn "NotesMigration\|PurgeLegacyNotes\|purgeLegacyNotes\|ContentBlockConversion\|LegacyContentBlock\|backfillCatalogEntryMediaTypes\|CatalogMediaTypeBackfill\|CatalogBackfill\|resolveMediaType\|MediaTypeResolution" src tests --include=*.fs` returns nothing.
- [ ] `src/Server/ContentBlockConversion.fs`, `tests/Server.Tests/ContentBlockConversionTests.fs`
      and `tests/Server.Tests/NotesMigrationTests.fs` no longer exist and are gone from both
      `.fsproj` files.
- [ ] `EventSurgeryTests.fs`'s `deleteEventsByStreamIds` / `previewBulkDeleteByStreamIds` cases
      are unchanged and green.
- [ ] Expecto: a stored `Entry_media_types_inferred` event still deserializes, formats, and
      replays into `catalog_entries.media_type` — the existing CatalogProjection replay tests and
      the `getHealthStats Entry_media_types_inferred …` guard in `AdministrationTests.fs` are
      unchanged and green.
- [ ] The `AdminSurgery` page still offers edit, delete, rename and backup stats; no `Msg` case,
      model field or `PendingAction` case mentioning notes migration or legacy purge remains
      (`grep -n -i "notes_migration\|purge_legacy\|NotesMigrationConfirmed" src/Client/Pages/AdminSurgery/*.fs` is empty).
- [ ] The Surgery tab no longer renders a Gate 1 or Gate 2 card; the Health tab no longer
      renders the media-type backfill action. [human-eye]
- [ ] `ADRS_WRITTEN` carries the two retirement amendments (ADR-0080, ADR-0079) described in
      What; no other ADR section is edited.
- [ ] `dotnet build`, `npm run build`, `npm test`, `npm run test:client` green.

## Notes

- **Harbour precondition, verified 2026-09-16 23:33 CEST** by the modeling session, read-only,
  on a temp copy of `/mnt/media/mediatheca/mediatheca.db` (+wal/+shm) — never the live file:
  - Gate 1 ran at 20:36:18Z (22:36 CEST): 14 `Notes_saved` events on 14 `Notes-*` streams;
    `ContentBlocks-*` streams: 0.
  - Gate 2 ran between 22:38 and 22:57 CEST: `content_blocks` and `game_journal_blocks` no
    longer exist (`notes_blocks` does); six ADR-0034 pre-purge backups sit in
    `/mnt/media/mediatheca/backups/` (`mediatheca-20260916T2038…` through `…T2057…`).
  - The backfill ran at 20:58:31Z (22:58 CEST): 3 `Entry_media_types_inferred` events,
    0 `catalog_entries` rows with a NULL/empty `media_type`.
  - The container was redeployed at 23:27 CEST with the build that includes curation-n2nkm
    (image created 2026-09-16T21:26:33Z), so the Surgery/Health tabs on harbour currently show
    the erroring gates and the no-op backfill until this task deploys.
  - Rebuild-all done and Health tab clean afterwards: confirmed by the builder on 2026-09-16
    (relayed from the parallel modeling session that dismissed curation-tb0nn). Precondition
    fully closed.
- curation-tb0nn (the post-purge preview guard) was **dismissed 2026-09-16 23:31**; it is not a
  dependency and nothing of it ships. The 500s from the gate previews on harbour are cosmetic
  until this retirement deploys — the purge they would guard has already happened.
- Known, deliberately unfixed data-loss trap in Gate 1 (builder decision 2026-09-16): an owner
  whose Notes document already exists — including an editor-created empty document from merely
  opening a Notes tab on the new build — is skipped, and Gate 2 then drops its legacy content.
  Moot once this task deletes both gates. On the dev copy this bit
  starcom-unknown-space-2022 (12 journal blocks) and disco-elysium-2019 (1); both survive in the
  ADR-0034 pre-purge backup `~/app/mediatheca/backups/mediatheca-20260916T2018583609196-97182b8d.db`.
  Whether harbour's run skipped any owner is not recorded anywhere the modeling session can
  read; the six harbour backups above are the recovery path if a note turns out missing.
- ADR-0080 §12 describes the registry entries that were removed with Gate 2; nothing further
  changes in the registries here — `Notes`/`notes_blocks` stay.
- `tableExists` in `Administration.fs` is used by the Health-tab orphan-image and drift checks
  and is untouched by this task.
- Line numbers above are as of commit `adfc2dc` (2026-09-16) and are pointers, not contracts.
- Orchestrator not consulted during refinement: the shape of the retirement is fixed by
  ADR-0080/ADR-0079 and curation-j4qqt/w9fkq's own file lists; the open items were factual
  (callers of `resolveMediaType`, the harbour precondition, tb0nn's fate) and were checked
  directly against the code and the live store.
