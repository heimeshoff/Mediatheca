namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Thoth.Json.Net
open Mediatheca.Shared

/// Read model for the Book aggregate (books-y9kxy). Mirrors
/// `GameProjection.fs`'s shape (Init/Drop/Handle, query functions) exactly.
/// A `book_metadata_cache` join happens at query time in `getBySlug`
/// (ADR-0045's "join in the query function, never the API layer") — this
/// handler never reads or writes that cache tier.
module BookProjection =

    /// True when the LIVE `book_progress` table (if any) still carries the
    /// pre-books-wk67x `PRIMARY KEY (book_slug, observed_on, source)` —
    /// read straight from `sqlite_master`'s stored DDL text, the same
    /// idiom `CatalogProjection.fs`'s `catalogEntriesNeedsWidening` uses.
    /// `None` (table doesn't exist yet — a fresh database, or the
    /// shadow-replay drift check's always-fresh connection) -> false: the
    /// `CREATE TABLE IF NOT EXISTS` just above always creates the CURRENT
    /// (entry_id-keyed) shape, so there is nothing to migrate.
    let private oldBookProgressPrimaryKey = "PRIMARY KEY (book_slug, observed_on, source)"

    let private bookProgressNeedsEntryIdMigration (conn: SqliteConnection) : bool =
        conn
        |> Db.newCommand "SELECT sql FROM sqlite_master WHERE type='table' AND name='book_progress'"
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "sql")
        |> Option.map (fun sql -> sql.Contains(oldBookProgressPrimaryKey))
        |> Option.defaultValue false

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS book_list (
                slug                    TEXT PRIMARY KEY,
                title                   TEXT NOT NULL,
                authors                 TEXT NOT NULL DEFAULT '[]',
                year                    INTEGER,
                cover_ref               TEXT,
                subjects                TEXT NOT NULL DEFAULT '[]',
                format                  TEXT NOT NULL DEFAULT 'Unknown',
                status                  TEXT NOT NULL DEFAULT 'Backlog',
                progress_percent        INTEGER NOT NULL DEFAULT 0,
                progress_source         TEXT,
                progress_observed_on    TEXT,
                progress_kind           TEXT,
                personal_rating         INTEGER,
                isbn13                  TEXT,
                openlibrary_work_key    TEXT,
                openlibrary_edition_key TEXT,
                audible_asin            TEXT,
                finished_at             TEXT,
                added_at                TEXT
            );

            CREATE TABLE IF NOT EXISTS book_detail (
                slug                    TEXT PRIMARY KEY,
                title                   TEXT NOT NULL,
                authors                 TEXT NOT NULL DEFAULT '[]',
                year                    INTEGER,
                cover_ref               TEXT,
                subjects                TEXT NOT NULL DEFAULT '[]',
                format                  TEXT NOT NULL DEFAULT 'Unknown',
                status                  TEXT NOT NULL DEFAULT 'Backlog',
                progress_percent        INTEGER NOT NULL DEFAULT 0,
                progress_source         TEXT,
                progress_observed_on    TEXT,
                progress_kind           TEXT,
                personal_rating         INTEGER,
                isbn13                  TEXT,
                openlibrary_work_key    TEXT,
                openlibrary_edition_key TEXT,
                audible_asin            TEXT,
                finished_at             TEXT,
                added_at                TEXT,
                recommended_by          TEXT NOT NULL DEFAULT '[]'
            );

            CREATE TABLE IF NOT EXISTS book_progress (
                entry_id      INTEGER PRIMARY KEY,
                book_slug     TEXT NOT NULL,
                observed_on   TEXT NOT NULL,
                source        TEXT NOT NULL,
                percent       INTEGER NOT NULL,
                position_json TEXT,
                kind          TEXT NOT NULL DEFAULT 'observation'
            );
            CREATE INDEX IF NOT EXISTS idx_book_progress_book_slug ON book_progress(book_slug);
        """
        |> Db.exec

        // `progress_kind`/`book_progress.kind` are additive columns
        // (books-d4wtc, ADR-0082 §5) — an already-existing table from before
        // this task predates them, and `CREATE TABLE IF NOT EXISTS` never
        // retrofits a pre-existing table's columns. Mirrors
        // `GameProjection.fs`'s `try ALTER TABLE ... ADD COLUMN with _ -> ()`
        // migration idiom exactly.
        try
            conn |> Db.newCommand "ALTER TABLE book_list ADD COLUMN progress_kind TEXT" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE book_detail ADD COLUMN progress_kind TEXT" |> Db.exec
        with _ -> ()
        try
            conn |> Db.newCommand "ALTER TABLE book_progress ADD COLUMN kind TEXT NOT NULL DEFAULT 'observation'" |> Db.exec
        with _ -> ()

        // books-wk67x (amending ADR-0076 §2): `book_progress` drops the old
        // `(book_slug, observed_on, source)` primary key in favour of
        // `entry_id` alone, so the same day/source can hold several
        // append-only entries. `ALTER TABLE` cannot change a table's
        // primary key, so a table that predates this task is migrated by
        // rename + recreate + copy — the exact idiom
        // `CatalogProjection.fs`'s `catalogEntriesNeedsWidening` self-heal
        // established for curation-w9fkq's own widened-UNIQUE migration.
        // Detected from `sqlite_master`'s stored DDL text (a table freshly
        // created by the `CREATE TABLE IF NOT EXISTS` above, or the
        // shadow-replay drift check's own always-fresh connection, never
        // matches — no `events` table dependency for either of those).
        // Each pre-existing row's `entry_id` is recovered as the real
        // `global_position` of whichever `Reading_progress_observed` /
        // `Prior_reading_progress_recorded` event on this book's own
        // stream last wrote its (observed_on, source) pair — the same
        // event a full rebuild would derive that row from — falling back
        // to `-rowid` (negative, so it can never collide with a real,
        // always-positive `global_position`) on the rare row with no
        // matching event to find (e.g. a row inserted directly by a test).
        if bookProgressNeedsEntryIdMigration conn then
            // `MetadataCache.fs`'s own module doc comment documents the
            // exact quirk this defends against: SQLite revalidates EVERY
            // view in the schema during ANY `ALTER TABLE ... RENAME`, on
            // ANY table — this connection may carry `series_next_up`/
            // `series_episode_counts` (created by `MetadataCache.initialize`,
            // which runs before every projection's `Init` in
            // `Composition.fs`'s real boot order) with no
            // `series_episode_cache` table behind them yet on THIS
            // connection, which would otherwise fail the rename below with
            // "no such table: main.series_episode_cache" — an unrelated
            // Series view breaking an unrelated Books migration. Dropped
            // defensively (`IF EXISTS`, harmless whether or not they exist,
            // or whether `series_episode_cache` itself exists) and restored
            // immediately after via `MetadataCache.initialize`'s own
            // idempotent `CREATE VIEW IF NOT EXISTS` step — the same
            // drop-then-let-the-owning-initializer-recreate idiom
            // `MetadataCache.recoverStranded`'s own view-safety fix
            // established, just called from the migration that needs it
            // instead of from inside `MetadataCache.fs` itself.
            conn |> Db.newCommand "DROP VIEW IF EXISTS series_next_up" |> Db.exec
            conn |> Db.newCommand "DROP VIEW IF EXISTS series_episode_counts" |> Db.exec

            conn |> Db.newCommand "ALTER TABLE book_progress RENAME TO book_progress_pre_wk67x" |> Db.exec
            conn
            |> Db.newCommand """
                CREATE TABLE book_progress (
                    entry_id      INTEGER PRIMARY KEY,
                    book_slug     TEXT NOT NULL,
                    observed_on   TEXT NOT NULL,
                    source        TEXT NOT NULL,
                    percent       INTEGER NOT NULL,
                    position_json TEXT,
                    kind          TEXT NOT NULL DEFAULT 'observation'
                );

                INSERT INTO book_progress (entry_id, book_slug, observed_on, source, percent, position_json, kind)
                SELECT
                    COALESCE(
                        (SELECT e.global_position FROM events e
                         WHERE e.stream_id = 'Book-' || bp.book_slug
                           AND e.event_type IN ('Reading_progress_observed', 'Prior_reading_progress_recorded')
                           AND json_extract(e.data, '$.observedOn') = bp.observed_on
                           AND json_extract(e.data, '$.source') = bp.source
                         ORDER BY e.global_position DESC
                         LIMIT 1),
                        -bp.rowid
                    ),
                    bp.book_slug, bp.observed_on, bp.source, bp.percent, bp.position_json, bp.kind
                FROM book_progress_pre_wk67x bp;

                DROP TABLE book_progress_pre_wk67x;

                CREATE INDEX IF NOT EXISTS idx_book_progress_book_slug ON book_progress(book_slug);
            """
            |> Db.exec

            // Restores the two views the drop above stranded — idempotent,
            // and cheap even though it re-runs every one of
            // `MetadataCache.fs`'s own migration steps (each individually
            // a no-op once already applied).
            MetadataCache.initialize conn

        // Backfill `progress_kind` on `book_list`/`book_detail` rows that
        // predate the ALTER above (books-d4wtc iteration 2, verifier fix).
        // A live NULL versus a rebuilt 'observation'/'prior' value is drift
        // (ADR-0031's shadow-replay comparison), so leaving the column NULL
        // for every pre-existing book — relying on the query-side COALESCE
        // alone — is not enough. Mirrors `recomputeProgress`'s own
        // latest-row-per-book ordering exactly (observed_on, then entry_id
        // — books-wk67x replaces the old Manual/Audible tie-break with
        // append order, since either source's entries can now repeat
        // same-day), so the backfilled value is the same one a full
        // rebuild would produce. No-op once a row's `progress_kind` is
        // non-NULL, so this is safe to run every boot.
        conn
        |> Db.newCommand """
            UPDATE book_list
            SET progress_kind = (
                SELECT kind FROM book_progress
                WHERE book_progress.book_slug = book_list.slug
                ORDER BY observed_on DESC, entry_id DESC
                LIMIT 1
            )
            WHERE progress_kind IS NULL
        """
        |> Db.exec
        conn
        |> Db.newCommand """
            UPDATE book_detail
            SET progress_kind = (
                SELECT kind FROM book_progress
                WHERE book_progress.book_slug = book_detail.slug
                ORDER BY observed_on DESC, entry_id DESC
                LIMIT 1
            )
            WHERE progress_kind IS NULL
        """
        |> Db.exec

        // Duplicate-add backstop (mirrors movie_detail.tmdb_id) — partial
        // since audible_asin is nullable and a book may carry it or not.
        try
            conn |> Db.newCommand "CREATE UNIQUE INDEX IF NOT EXISTS idx_book_detail_audible_asin ON book_detail(audible_asin) WHERE audible_asin IS NOT NULL" |> Db.exec
        with ex -> eprintfn "[BookProjection] Could not create UNIQUE index on book_detail.audible_asin: %s" ex.Message

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS book_list;
            DROP TABLE IF EXISTS book_detail;
            DROP TABLE IF EXISTS book_progress;
        """
        |> Db.exec

    // Local encode/decode duplicates (ADR-0045's "no *Projection.fs file
    // references MetadataCache in code" precedent already duplicates
    // encode/decode locally per-module rather than sharing — same idiom
    // here for the event-payload shapes this handler reads).

    let private encodeBookFormat (format: BookFormat) =
        match format with
        | Audiobook -> "Audiobook"
        | Print -> "Print"
        | Ebook -> "Ebook"
        | BookFormat.Unknown -> "Unknown"

    let private parseBookFormat (s: string) : BookFormat =
        match s with
        | "Audiobook" -> Audiobook
        | "Print" -> Print
        | "Ebook" -> Ebook
        | _ -> BookFormat.Unknown

    let private encodeBookStatus (status: BookStatus) =
        match status with
        | BookStatus.Backlog -> "Backlog"
        | BookStatus.InFocus -> "InFocus"
        | BookStatus.Finished -> "Finished"
        | BookStatus.Abandoned -> "Abandoned"

    let private parseBookStatus (s: string) : BookStatus =
        match s with
        | "InFocus" -> BookStatus.InFocus
        | "Finished" -> BookStatus.Finished
        | "Abandoned" -> BookStatus.Abandoned
        | _ -> BookStatus.Backlog

    let private encodeProgressSource (source: ProgressSource) =
        match source with
        | Audible -> "Audible"
        | ProgressSource.Manual -> "Manual"

    let private parseProgressSource (s: string) : ProgressSource =
        match s with
        | "Audible" -> Audible
        | _ -> ProgressSource.Manual

    let private parseProgressKind (s: string) : ProgressKind =
        match s with
        | "prior" -> Prior
        | _ -> Observed

    let private encodeReadingPosition (pos: ReadingPosition) =
        match pos with
        | Page (page, total) ->
            Encode.object [ "kind", Encode.string "Page"; "value", Encode.int page; "total", Encode.option Encode.int total ]
        | Minutes (minutes, total) ->
            Encode.object [ "kind", Encode.string "Minutes"; "value", Encode.int minutes; "total", Encode.option Encode.int total ]

    let private decodeReadingPositionJson (json: string) : ReadingPosition option =
        Decode.fromString (Decode.object (fun get ->
            let kind = get.Required.Field "kind" Decode.string
            let value = get.Required.Field "value" Decode.int
            let total = get.Optional.Field "total" Decode.int
            match kind with
            | "Minutes" -> Minutes (value, total)
            | _ -> Page (value, total)
        )) json
        |> Result.toOption

    let private jsonStringList (values: string list) : string =
        values |> List.map Encode.string |> Encode.list |> Encode.toString 0

    let private parseJsonStringList (json: string) : string list =
        Decode.fromString (Decode.list Decode.string) json |> Result.defaultValue []

    let private sqlOptString (v: string option) = match v with Some s -> SqlType.String s | None -> SqlType.Null
    let private sqlOptInt (v: int option) = match v with Some i -> SqlType.Int32 i | None -> SqlType.Null

    let private externalIdColumnAndValue (eid: BookExternalId) : string * string =
        match eid with
        | Isbn13 v -> "isbn13", v
        | OpenLibraryWork v -> "openlibrary_work_key", v
        | OpenLibraryEdition v -> "openlibrary_edition_key", v
        | AudibleAsin v -> "audible_asin", v

    let private updateJsonList (conn: SqliteConnection) (table: string) (column: string) (slug: string) (add: bool) (value: string) : unit =
        let currentJson =
            conn
            |> Db.newCommand (sprintf "SELECT %s FROM %s WHERE slug = @slug" column table)
            |> Db.setParams [ "slug", SqlType.String slug ]
            |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString column)
            |> Option.defaultValue "[]"
        let current = parseJsonStringList currentJson
        let updated =
            if add then current @ [ value ] |> List.distinct
            else current |> List.filter (fun s -> s <> value)
        conn
        |> Db.newCommand (sprintf "UPDATE %s SET %s = @value WHERE slug = @slug" table column)
        |> Db.setParams [ "slug", SqlType.String slug; "value", SqlType.String (jsonStringList updated) ]
        |> Db.exec

    /// Recomputes `book_list`/`book_detail`'s denormalized `progress_*`
    /// columns from `book_progress`'s latest row — ordered by `observed_on`,
    /// then `entry_id` (append order) within a day (books-wk67x, amending
    /// ADR-0076 §2: same-day entries no longer collapse, and either
    /// source's entry can now repeat same-day, so the old Manual > Audible
    /// tie-break no longer identifies "the latest" — the entry recorded
    /// LAST wins, whichever source it came from) — or clears them when no
    /// observation rows remain (e.g. the last one was removed).
    let private recomputeProgress (conn: SqliteConnection) (slug: string) : unit =
        let latest =
            conn
            |> Db.newCommand """
                SELECT source, percent, observed_on, kind FROM book_progress
                WHERE book_slug = @slug
                ORDER BY observed_on DESC, entry_id DESC
                LIMIT 1
            """
            |> Db.setParams [ "slug", SqlType.String slug ]
            |> Db.querySingle (fun (rd: IDataReader) ->
                rd.ReadString "source", rd.ReadInt32 "percent", rd.ReadString "observed_on", rd.ReadString "kind")
        let percent, source, observedOn, kind =
            match latest with
            | Some (source, percent, observedOn, kind) -> percent, SqlType.String source, SqlType.String observedOn, SqlType.String kind
            | None -> 0, SqlType.Null, SqlType.Null, SqlType.Null
        conn
        |> Db.newCommand "UPDATE book_list SET progress_percent = @percent, progress_source = @source, progress_observed_on = @observed_on, progress_kind = @kind WHERE slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug; "percent", SqlType.Int32 percent; "source", source; "observed_on", observedOn; "kind", kind ]
        |> Db.exec
        conn
        |> Db.newCommand "UPDATE book_detail SET progress_percent = @percent, progress_source = @source, progress_observed_on = @observed_on, progress_kind = @kind WHERE slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug; "percent", SqlType.Int32 percent; "source", source; "observed_on", observedOn; "kind", kind ]
        |> Db.exec

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Book-")) then ()
        else
            let slug = event.StreamId.Substring(5) // Remove "Book-" prefix
            match Books.Serialization.fromStoredEvent event with
            | None -> ()
            | Some bookEvent ->
                match bookEvent with
                | Books.Book_added_to_library data ->
                    let authorsJson = jsonStringList data.Authors
                    let subjectsJson = jsonStringList data.Subjects
                    let formatStr = encodeBookFormat data.Format
                    let addedAt = event.Timestamp.ToString("o")
                    let externalCols =
                        data.ExternalIds
                        |> List.map externalIdColumnAndValue
                        |> List.fold (fun (m: Map<string, string>) (col, v) -> m |> Map.add col v) Map.empty
                    let extVal col = externalCols |> Map.tryFind col
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO book_list
                            (slug, title, authors, year, cover_ref, subjects, format, status,
                             progress_percent, progress_source, progress_observed_on, personal_rating,
                             isbn13, openlibrary_work_key, openlibrary_edition_key, audible_asin,
                             finished_at, added_at)
                        VALUES
                            (@slug, @title, @authors, @year, @cover_ref, @subjects, @format, 'Backlog',
                             0, NULL, NULL, NULL,
                             @isbn13, @openlibrary_work_key, @openlibrary_edition_key, @audible_asin,
                             NULL, @added_at)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "title", SqlType.String data.Title
                        "authors", SqlType.String authorsJson
                        "year", sqlOptInt data.Year
                        "cover_ref", sqlOptString data.CoverRef
                        "subjects", SqlType.String subjectsJson
                        "format", SqlType.String formatStr
                        "isbn13", sqlOptString (extVal "isbn13")
                        "openlibrary_work_key", sqlOptString (extVal "openlibrary_work_key")
                        "openlibrary_edition_key", sqlOptString (extVal "openlibrary_edition_key")
                        "audible_asin", sqlOptString (extVal "audible_asin")
                        "added_at", SqlType.String addedAt
                    ]
                    |> Db.exec

                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO book_detail
                            (slug, title, authors, year, cover_ref, subjects, format, status,
                             progress_percent, progress_source, progress_observed_on, personal_rating,
                             isbn13, openlibrary_work_key, openlibrary_edition_key, audible_asin,
                             finished_at, added_at, recommended_by)
                        VALUES
                            (@slug, @title, @authors, @year, @cover_ref, @subjects, @format, 'Backlog',
                             0, NULL, NULL, NULL,
                             @isbn13, @openlibrary_work_key, @openlibrary_edition_key, @audible_asin,
                             NULL, @added_at, '[]')
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "title", SqlType.String data.Title
                        "authors", SqlType.String authorsJson
                        "year", sqlOptInt data.Year
                        "cover_ref", sqlOptString data.CoverRef
                        "subjects", SqlType.String subjectsJson
                        "format", SqlType.String formatStr
                        "isbn13", sqlOptString (extVal "isbn13")
                        "openlibrary_work_key", sqlOptString (extVal "openlibrary_work_key")
                        "openlibrary_edition_key", sqlOptString (extVal "openlibrary_edition_key")
                        "audible_asin", sqlOptString (extVal "audible_asin")
                        "added_at", SqlType.String addedAt
                    ]
                    |> Db.exec

                | Books.Book_removed_from_library ->
                    conn |> Db.newCommand "DELETE FROM book_list WHERE slug = @slug" |> Db.setParams [ "slug", SqlType.String slug ] |> Db.exec
                    conn |> Db.newCommand "DELETE FROM book_detail WHERE slug = @slug" |> Db.setParams [ "slug", SqlType.String slug ] |> Db.exec
                    conn |> Db.newCommand "DELETE FROM book_progress WHERE book_slug = @slug" |> Db.setParams [ "slug", SqlType.String slug ] |> Db.exec

                | Books.Book_cover_replaced coverRef ->
                    conn |> Db.newCommand "UPDATE book_list SET cover_ref = @cover_ref WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "cover_ref", SqlType.String coverRef ] |> Db.exec
                    conn |> Db.newCommand "UPDATE book_detail SET cover_ref = @cover_ref WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "cover_ref", SqlType.String coverRef ] |> Db.exec

                | Books.Book_external_id_linked externalId ->
                    let col, value = externalIdColumnAndValue externalId
                    conn |> Db.newCommand (sprintf "UPDATE book_list SET %s = @value WHERE slug = @slug" col)
                         |> Db.setParams [ "slug", SqlType.String slug; "value", SqlType.String value ] |> Db.exec
                    conn |> Db.newCommand (sprintf "UPDATE book_detail SET %s = @value WHERE slug = @slug" col)
                         |> Db.setParams [ "slug", SqlType.String slug; "value", SqlType.String value ] |> Db.exec

                | Books.Book_format_set format ->
                    let formatStr = encodeBookFormat format
                    conn |> Db.newCommand "UPDATE book_list SET format = @format WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "format", SqlType.String formatStr ] |> Db.exec
                    conn |> Db.newCommand "UPDATE book_detail SET format = @format WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "format", SqlType.String formatStr ] |> Db.exec

                | Books.Book_status_changed (status, effectiveOn) ->
                    let statusStr = encodeBookStatus status
                    // ADR-0077 §2: only Finished is read; `finished_at` is a
                    // DATE STRING (`effectiveOn`, defaulting to this event's
                    // own local date), cleared on any other status.
                    let finishedAt =
                        match status with
                        | BookStatus.Finished ->
                            SqlType.String (effectiveOn |> Option.defaultValue (event.Timestamp.ToString("yyyy-MM-dd")))
                        | _ -> SqlType.Null
                    conn |> Db.newCommand "UPDATE book_list SET status = @status, finished_at = @finished_at WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "status", SqlType.String statusStr; "finished_at", finishedAt ] |> Db.exec
                    conn |> Db.newCommand "UPDATE book_detail SET status = @status, finished_at = @finished_at WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "status", SqlType.String statusStr; "finished_at", finishedAt ] |> Db.exec

                | Books.Reading_progress_observed data ->
                    // books-wk67x (amending ADR-0076 §2): a plain insert,
                    // one row per event — same-day/same-source no longer
                    // collapses. `entry_id` is this event's own store
                    // position, exactly the id `Books.evolve` assigns this
                    // same entry when the aggregate reconstitutes.
                    let positionJson = data.Position |> Option.map (encodeReadingPosition >> Encode.toString 0)
                    conn
                    |> Db.newCommand """
                        INSERT INTO book_progress (entry_id, book_slug, observed_on, source, percent, position_json, kind)
                        VALUES (@entry_id, @slug, @observed_on, @source, @percent, @position_json, 'observation')
                    """
                    |> Db.setParams [
                        "entry_id", SqlType.Int64 event.GlobalPosition
                        "slug", SqlType.String slug
                        "observed_on", SqlType.String data.ObservedOn
                        "source", SqlType.String (encodeProgressSource data.Source)
                        "percent", SqlType.Int32 data.Percent
                        "position_json", sqlOptString positionJson
                    ]
                    |> Db.exec
                    recomputeProgress conn slug

                | Books.Prior_reading_progress_recorded data ->
                    // ADR-0082 §5: identical INSERT to Reading_progress_observed
                    // above, except `kind = 'prior'` — a source's first-ever
                    // reported position, not a session read that day.
                    let positionJson = data.Position |> Option.map (encodeReadingPosition >> Encode.toString 0)
                    conn
                    |> Db.newCommand """
                        INSERT INTO book_progress (entry_id, book_slug, observed_on, source, percent, position_json, kind)
                        VALUES (@entry_id, @slug, @observed_on, @source, @percent, @position_json, 'prior')
                    """
                    |> Db.setParams [
                        "entry_id", SqlType.Int64 event.GlobalPosition
                        "slug", SqlType.String slug
                        "observed_on", SqlType.String data.ObservedOn
                        "source", SqlType.String (encodeProgressSource data.Source)
                        "percent", SqlType.Int32 data.Percent
                        "position_json", sqlOptString positionJson
                    ]
                    |> Db.exec
                    recomputeProgress conn slug

                | Books.Reading_progress_observation_removed (observedOn, source) ->
                    // Legacy replay-only (books-wk67x, point 6): deletes
                    // every entry of that day/source — there is at most one
                    // for events recorded before this task, and this same
                    // WHERE clause is exactly as correct for however many
                    // exist AT THIS POINT in a replay of a stream that mixes
                    // pre- and post-task events.
                    conn
                    |> Db.newCommand "DELETE FROM book_progress WHERE book_slug = @slug AND observed_on = @observed_on AND source = @source"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "observed_on", SqlType.String observedOn
                        "source", SqlType.String (encodeProgressSource source)
                    ]
                    |> Db.exec
                    recomputeProgress conn slug

                | Books.Reading_progress_entry_removed entryId ->
                    conn
                    |> Db.newCommand "DELETE FROM book_progress WHERE entry_id = @entry_id"
                    |> Db.setParams [ "entry_id", SqlType.Int64 entryId ]
                    |> Db.exec
                    recomputeProgress conn slug

                | Books.Book_personal_rating_set rating ->
                    conn |> Db.newCommand "UPDATE book_list SET personal_rating = @rating WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "rating", sqlOptInt rating ] |> Db.exec
                    conn |> Db.newCommand "UPDATE book_detail SET personal_rating = @rating WHERE slug = @slug"
                         |> Db.setParams [ "slug", SqlType.String slug; "rating", sqlOptInt rating ] |> Db.exec

                | Books.Book_recommended_by friendSlug ->
                    updateJsonList conn "book_detail" "recommended_by" slug true friendSlug

                | Books.Book_recommendation_removed friendSlug ->
                    updateJsonList conn "book_detail" "recommended_by" slug false friendSlug

    let handler: Projection.ProjectionHandler = {
        Name = "BookProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    let private resolveFriendRefs (conn: SqliteConnection) (slugs: string list) : FriendRef list =
        if List.isEmpty slugs then []
        else
            let friendMap =
                conn
                |> Db.newCommand "SELECT slug, name, image_ref FROM friend_list"
                |> Db.query (fun (rd: IDataReader) ->
                    rd.ReadString "slug",
                    (rd.ReadString "name",
                     if rd.IsDBNull(rd.GetOrdinal("image_ref")) then None
                     else Some (rd.ReadString "image_ref")))
                |> Map.ofList
            slugs |> List.map (fun s ->
                let name, imageRef = friendMap |> Map.tryFind s |> Option.defaultValue (s, None)
                { FriendRef.Slug = s; Name = name; ImageRef = imageRef })

    let private readOptString (rd: IDataReader) (col: string) =
        if rd.IsDBNull(rd.GetOrdinal(col)) then None else Some (rd.ReadString col)
    let private readOptInt (rd: IDataReader) (col: string) =
        if rd.IsDBNull(rd.GetOrdinal(col)) then None else Some (rd.ReadInt32 col)

    let private readListItemRow (rd: IDataReader) : BookListItem =
        { BookListItem.Slug = rd.ReadString "slug"
          Title = rd.ReadString "title"
          Authors = parseJsonStringList (rd.ReadString "authors")
          Year = readOptInt rd "year"
          CoverRef = readOptString rd "cover_ref"
          Subjects = parseJsonStringList (rd.ReadString "subjects")
          Format = parseBookFormat (rd.ReadString "format")
          Status = parseBookStatus (rd.ReadString "status")
          ProgressPercent = rd.ReadInt32 "progress_percent"
          ProgressSource = readOptString rd "progress_source" |> Option.map parseProgressSource
          ProgressObservedOn = readOptString rd "progress_observed_on"
          PersonalRating = readOptInt rd "personal_rating"
          FinishedAt = readOptString rd "finished_at" }

    let getAll (conn: SqliteConnection) : BookListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, title, authors, year, cover_ref, subjects, format, status,
                   progress_percent, progress_source, progress_observed_on, personal_rating, finished_at
            FROM book_list
            ORDER BY title
        """
        |> Db.query readListItemRow

    /// A book's full reading-progress history, oldest first, several rows
    /// per day when they exist (books-wk67x, amending ADR-0076 §2) — the
    /// detail page's progress-history list. `entry_id` is the secondary
    /// sort key within a day, so same-day entries come back in the order
    /// they were actually recorded.
    let getProgressHistory (conn: SqliteConnection) (slug: string) : ReadingProgressDto list =
        conn
        |> Db.newCommand "SELECT entry_id, observed_on, source, percent, position_json, kind FROM book_progress WHERE book_slug = @slug ORDER BY observed_on, entry_id"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.query (fun (rd: IDataReader) ->
            { ReadingProgressDto.EntryId = rd.ReadInt64 "entry_id"
              ObservedOn = rd.ReadString "observed_on"
              Source = parseProgressSource (rd.ReadString "source")
              Percent = rd.ReadInt32 "percent"
              Position =
                if rd.IsDBNull(rd.GetOrdinal("position_json")) then None
                else decodeReadingPositionJson (rd.ReadString "position_json")
              Kind = parseProgressKind (rd.ReadString "kind") })

    let getBySlug (conn: SqliteConnection) (slug: string) : BookDetail option =
        conn
        |> Db.newCommand """
            SELECT
                bd.slug, bd.title, bd.authors, bd.year, bd.cover_ref, bd.subjects, bd.format, bd.status,
                bd.progress_percent, bd.progress_source, bd.progress_observed_on, bd.personal_rating,
                bd.isbn13, bd.openlibrary_work_key, bd.openlibrary_edition_key, bd.audible_asin,
                bd.finished_at, bd.added_at, bd.recommended_by,
                mc.description, mc.page_count, mc.runtime_minutes, mc.narrators, mc.series_name, mc.series_position,
                mc.publisher, mc.published_date, mc.average_rating, mc.language
            FROM book_detail bd
            LEFT JOIN book_metadata_cache mc ON mc.book_slug = bd.slug
            WHERE bd.slug = @slug
        """
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            let recommendedBySlugs = parseJsonStringList (rd.ReadString "recommended_by")
            { BookDetail.Slug = rd.ReadString "slug"
              Title = rd.ReadString "title"
              Authors = parseJsonStringList (rd.ReadString "authors")
              Year = readOptInt rd "year"
              CoverRef = readOptString rd "cover_ref"
              Subjects = parseJsonStringList (rd.ReadString "subjects")
              Format = parseBookFormat (rd.ReadString "format")
              Status = parseBookStatus (rd.ReadString "status")
              ProgressPercent = rd.ReadInt32 "progress_percent"
              ProgressSource = readOptString rd "progress_source" |> Option.map parseProgressSource
              ProgressObservedOn = readOptString rd "progress_observed_on"
              PersonalRating = readOptInt rd "personal_rating"
              FinishedAt = readOptString rd "finished_at"
              AddedAt = readOptString rd "added_at"
              Isbn13 = readOptString rd "isbn13"
              OpenLibraryWorkKey = readOptString rd "openlibrary_work_key"
              OpenLibraryEditionKey = readOptString rd "openlibrary_edition_key"
              AudibleAsin = readOptString rd "audible_asin"
              RecommendedBy = resolveFriendRefs conn recommendedBySlugs
              Description = readOptString rd "description"
              PageCount = readOptInt rd "page_count"
              RuntimeMinutes = readOptInt rd "runtime_minutes"
              Narrators = readOptString rd "narrators" |> Option.map parseJsonStringList |> Option.defaultValue []
              SeriesName = readOptString rd "series_name"
              SeriesPosition = readOptInt rd "series_position"
              Publisher = readOptString rd "publisher"
              PublishedDate = readOptString rd "published_date"
              AverageRating =
                if rd.IsDBNull(rd.GetOrdinal("average_rating")) then None
                else Some (rd.ReadDouble "average_rating")
              Language = readOptString rd "language"
              ProgressHistory = getProgressHistory conn slug
              // curation-h98ve (ADR-0080): re-derived fresh from notes_blocks
              // on every read, never cached (ADR-0043).
              HasNotesContent = NotesProjection.getForOwner conn MediaType.Book slug |> JournalBlock.hasContent }
        )

    /// `(kind, value)` -> the matching book's slug, for adapter duplicate/
    /// re-link checks (`findByExternalId (AudibleAsin "B0...")`).
    let findByExternalId (conn: SqliteConnection) (externalId: BookExternalId) : string option =
        let col, value = externalIdColumnAndValue externalId
        conn
        |> Db.newCommand (sprintf "SELECT slug FROM book_detail WHERE %s = @value LIMIT 1" col)
        |> Db.setParams [ "value", SqlType.String value ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadString "slug")

    /// integration-dtdbb (ADR-0082 §2): does this book already carry a
    /// `book_progress` row from `source`? Gates whether
    /// `Api.importAudibleLibraryImpl` issues `Record_prior_reading_progress`
    /// (no row yet) or a plain `Observe_reading_progress` (a row already
    /// exists) -- a small read-only existence check, never a count
    /// consumer needs.
    let hasSourceProgress (conn: SqliteConnection) (slug: string) (source: ProgressSource) : bool =
        conn
        |> Db.newCommand "SELECT COUNT(*) as cnt FROM book_progress WHERE book_slug = @slug AND source = @source"
        |> Db.setParams [ "slug", SqlType.String slug; "source", SqlType.String (encodeProgressSource source) ]
        |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt64 "cnt")
        |> Option.defaultValue 0L
        |> fun cnt -> cnt > 0L

    /// integration-dtdbb (ADR-0082 §7): a book's ONLY `book_progress` row
    /// from `source` predates priors -- exactly one row, `kind =
    /// 'observation'`. `Some (entryId, observedOn)` when that legacy shape
    /// is found (so the caller can `Remove_reading_progress_entry` it and
    /// re-record a correctly-dated prior -- books-wk67x replaces the old
    /// day+source removal with entry id); `None` for a book already
    /// carrying a prior, more than one row, or no row at all.
    let legacyObservationToRepair (conn: SqliteConnection) (slug: string) (source: ProgressSource) : (int64 * string) option =
        let rows =
            conn
            |> Db.newCommand "SELECT entry_id, observed_on, kind FROM book_progress WHERE book_slug = @slug AND source = @source"
            |> Db.setParams [ "slug", SqlType.String slug; "source", SqlType.String (encodeProgressSource source) ]
            |> Db.query (fun (rd: IDataReader) -> rd.ReadInt64 "entry_id", rd.ReadString "observed_on", rd.ReadString "kind")
        match rows with
        | [ (entryId, observedOn, "observation") ] -> Some (entryId, observedOn)
        | _ -> None

    /// Case-insensitive title match — the `addBook` duplicate-check fallback
    /// when no external id matched.
    let findByTitle (conn: SqliteConnection) (title: string) : (string * string) list =
        conn
        |> Db.newCommand "SELECT slug, title FROM book_detail WHERE title = @title COLLATE NOCASE"
        |> Db.setParams [ "title", SqlType.String title ]
        |> Db.query (fun (rd: IDataReader) -> rd.ReadString "slug", rd.ReadString "title")

    /// In Focus books — the dashboard's "Currently Reading" cohort
    /// (`intelligence-dnv2y`, not built by this task).
    let getCurrentlyReading (conn: SqliteConnection) : BookListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, title, authors, year, cover_ref, subjects, format, status,
                   progress_percent, progress_source, progress_observed_on, personal_rating, finished_at
            FROM book_list
            WHERE status = 'InFocus'
            ORDER BY progress_observed_on DESC
        """
        |> Db.query readListItemRow

    /// Books finished within the last `days` days (date comparison, per
    /// ADR-0077 §3 — never a timestamp parse) — the dashboard's 7-day
    /// "just finished" linger (`intelligence-dnv2y`, not built by this task).
    let getRecentlyFinished (conn: SqliteConnection) (days: int) : BookListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, title, authors, year, cover_ref, subjects, format, status,
                   progress_percent, progress_source, progress_observed_on, personal_rating, finished_at
            FROM book_list
            WHERE status = 'Finished' AND finished_at IS NOT NULL AND finished_at >= date('now', @lookback)
            ORDER BY finished_at DESC
        """
        |> Db.setParams [ "lookback", SqlType.String (sprintf "-%d days" days) ]
        |> Db.query readListItemRow

    let getRecentlyAdded (conn: SqliteConnection) (limit: int option) : BookListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, title, authors, year, cover_ref, subjects, format, status,
                   progress_percent, progress_source, progress_observed_on, personal_rating, finished_at
            FROM book_list
            ORDER BY added_at DESC
            LIMIT @limit
        """
        |> Db.setParams [ "limit", SqlType.Int32 (RowLimit.toSql limit) ]
        |> Db.query readListItemRow

    // ── intelligence-dnv2y: Dashboard queries ──

    /// `BookListItem` -> the dashboard's shared `DashboardBookItem` card shape.
    /// `finished` is the caller's to set — a strict rail (Currently Reading,
    /// Recently Added) is never finished by construction; `getRecentlyFinished`
    /// results always are.
    let toDashboardBookItem (finished: bool) (item: Mediatheca.Shared.BookListItem) : Mediatheca.Shared.DashboardBookItem =
        { Mediatheca.Shared.DashboardBookItem.Slug = item.Slug
          Title = item.Title
          Authors = item.Authors
          CoverRef = item.CoverRef
          ProgressPercent = item.ProgressPercent
          ProgressSource = item.ProgressSource
          Finished = finished
          FinishedOn = item.FinishedAt }

    /// The All-tab "Reading" card's own query (mirrors
    /// `MovieProjection.getAllTabMoviesToWatch`, intelligence-b1nz5): In Focus
    /// books ordered by latest `progress_observed_on` desc (then `added_at`
    /// desc), plus any book `finished_at` within the last 7 days (a date-string
    /// comparison, ADR-0077 §3), marked `Finished = true`. The Books tab's own
    /// Currently Reading card stays strict — see `getCurrentlyReading` above.
    let getAllTabCurrentlyReading (conn: SqliteConnection) : Mediatheca.Shared.DashboardBookItem list =
        conn
        |> Db.newCommand """
            SELECT slug, title, authors, cover_ref, progress_percent, progress_source, status, finished_at
            FROM book_list
            WHERE status = 'InFocus'
               OR (status = 'Finished' AND finished_at IS NOT NULL AND finished_at >= date('now', '-7 days'))
            ORDER BY
                CASE WHEN status = 'InFocus' THEN 0 ELSE 1 END,
                CASE WHEN status = 'InFocus' THEN progress_observed_on END DESC,
                CASE WHEN status = 'InFocus' THEN added_at END DESC,
                CASE WHEN status = 'Finished' THEN finished_at END DESC
        """
        |> Db.query (fun (rd: IDataReader) ->
            let status = parseBookStatus (rd.ReadString "status")
            { Mediatheca.Shared.DashboardBookItem.Slug = rd.ReadString "slug"
              Title = rd.ReadString "title"
              Authors = parseJsonStringList (rd.ReadString "authors")
              CoverRef = readOptString rd "cover_ref"
              ProgressPercent = rd.ReadInt32 "progress_percent"
              ProgressSource = readOptString rd "progress_source" |> Option.map parseProgressSource
              Finished = (status = BookStatus.Finished)
              FinishedOn = readOptString rd "finished_at" })

    /// The Books tab's own "Recently Added" rail — newest first, excluding
    /// already-finished books (those belong to the tab's "Recently Finished"
    /// rail instead, `getRecentlyFinished` above).
    let getRecentlyAddedUnfinished (conn: SqliteConnection) (limit: int option) : BookListItem list =
        conn
        |> Db.newCommand """
            SELECT slug, title, authors, year, cover_ref, subjects, format, status,
                   progress_percent, progress_source, progress_observed_on, personal_rating, finished_at
            FROM book_list
            WHERE status <> 'Finished'
            ORDER BY added_at DESC
            LIMIT @limit
        """
        |> Db.setParams [ "limit", SqlType.Int32 (RowLimit.toSql limit) ]
        |> Db.query readListItemRow

    /// The Books tab's stat-tile row. `PagesReadThisYear` names each
    /// finished-this-year book's own LATEST `book_progress` row via a
    /// correlated subquery ordered `(observed_on, entry_id)` — the same
    /// ordering `recomputeProgress` above uses, since `book_progress` no
    /// longer has a `(book_slug, observed_on, source)` primary key to join
    /// on directly (books-wk67x/ADR-0085: several rows can now share that
    /// triple) — and sums any `Page` position found; `HoursListenedThisYear`
    /// sums `runtime_minutes x percent` (in hours) over the same cohort
    /// restricted to an Audible-sourced latest observation with a known
    /// cache-tier runtime. Both are `None`, not `Some 0`, when nothing
    /// contributes.
    let getReadingStats (conn: SqliteConnection) : Mediatheca.Shared.DashboardBookStats =
        let count (whereClause: string) =
            conn
            |> Db.newCommand (sprintf "SELECT COUNT(*) as cnt FROM book_list WHERE %s" whereClause)
            |> Db.querySingle (fun (rd: IDataReader) -> rd.ReadInt32 "cnt")
            |> Option.defaultValue 0
        let total = count "1 = 1"
        let inFocus = count "status = 'InFocus'"
        let finishedThisYear = count "status = 'Finished' AND strftime('%Y', finished_at) = strftime('%Y', 'now')"
        let finishedAllTime = count "status = 'Finished'"
        // books-wk67x (amending ADR-0076 §2): a plain JOIN on
        // (observed_on, source) would now fan out across every same-day
        // same-source entry a finished book happens to carry, double
        // (or triple-) counting its pages. A correlated subquery picking
        // book_progress's own latest row per book — the exact
        // `recomputeProgress` ordering (observed_on, then entry_id) —
        // names the ONE row unambiguously instead.
        let pagesReadThisYear =
            conn
            |> Db.newCommand """
                SELECT (
                    SELECT bp.position_json FROM book_progress bp
                    WHERE bp.book_slug = bl.slug
                    ORDER BY bp.observed_on DESC, bp.entry_id DESC
                    LIMIT 1
                ) as position_json
                FROM book_list bl
                WHERE bl.status = 'Finished' AND strftime('%Y', bl.finished_at) = strftime('%Y', 'now')
            """
            |> Db.query (fun (rd: IDataReader) ->
                if rd.IsDBNull(rd.GetOrdinal("position_json")) then None
                else
                    decodeReadingPositionJson (rd.ReadString "position_json")
                    |> Option.bind (function Page (page, _) -> Some page | _ -> None))
            |> List.choose id
            |> function
                | [] -> None
                | pages -> Some (List.sum pages)
        let hoursListenedThisYear =
            conn
            |> Db.newCommand """
                SELECT bl.progress_percent as percent, mc.runtime_minutes as runtime_minutes
                FROM book_list bl
                JOIN book_metadata_cache mc ON mc.book_slug = bl.slug
                WHERE bl.status = 'Finished'
                  AND strftime('%Y', bl.finished_at) = strftime('%Y', 'now')
                  AND bl.progress_source = 'Audible'
                  AND COALESCE(bl.progress_kind, 'observation') <> 'prior'
                  AND mc.runtime_minutes IS NOT NULL
            """
            |> Db.query (fun (rd: IDataReader) ->
                let percent = rd.ReadInt32 "percent"
                let runtime = rd.ReadInt32 "runtime_minutes"
                float runtime * (float percent / 100.0) / 60.0)
            |> function
                | [] -> None
                | hours -> Some (List.sum hours)
        { Mediatheca.Shared.DashboardBookStats.Total = total
          InFocus = inFocus
          FinishedThisYear = finishedThisYear
          FinishedAllTime = finishedAllTime
          PagesReadThisYear = pagesReadThisYear
          HoursListenedThisYear = hoursListenedThisYear }
