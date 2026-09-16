namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Shared

module CatalogProjection =

    /// curation-cyxbc (ADR-0079): DU case names, matching how other
    /// projections store DU-valued columns (e.g. `status`).
    let private encodeMediaType (mediaType: MediaType) : string =
        match mediaType with
        | MediaType.Movie -> "Movie"
        | MediaType.Series -> "Series"
        | MediaType.Game -> "Game"
        | MediaType.Book -> "Book"

    let private decodeMediaType (s: string) : MediaType option =
        match s with
        | "Movie" -> Some MediaType.Movie
        | "Series" -> Some MediaType.Series
        | "Game" -> Some MediaType.Game
        | "Book" -> Some MediaType.Book
        | _ -> None

    let private createTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            CREATE TABLE IF NOT EXISTS catalog_list (
                slug         TEXT PRIMARY KEY,
                name         TEXT NOT NULL,
                description  TEXT NOT NULL DEFAULT '',
                is_sorted    INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS catalog_entries (
                entry_id     TEXT PRIMARY KEY,
                catalog_slug TEXT NOT NULL,
                movie_slug   TEXT NOT NULL,
                note         TEXT,
                position     INTEGER NOT NULL DEFAULT 0,
                UNIQUE(catalog_slug, movie_slug)
            );
            CREATE INDEX IF NOT EXISTS idx_catalog_entries_catalog ON catalog_entries(catalog_slug);
        """
        |> Db.exec

        // curation-cyxbc (ADR-0079): typed entries — nullable so legacy rows
        // (recorded before entries were typed) stay NULL and keep resolving
        // via the read-time join-order inference. UNIQUE(catalog_slug,
        // movie_slug) is deliberately left unchanged (ADR-0079 §5) — SQLite
        // treats NULLs as distinct in a UNIQUE index, so widening it now
        // would drop duplicate protection for every legacy row.
        try
            conn |> Db.newCommand "ALTER TABLE catalog_entries ADD COLUMN media_type TEXT" |> Db.exec
        with _ -> ()

    let private dropTables (conn: SqliteConnection) : unit =
        conn
        |> Db.newCommand """
            DROP TABLE IF EXISTS catalog_entries;
            DROP TABLE IF EXISTS catalog_list;
        """
        |> Db.exec

    let private handleEvent (conn: SqliteConnection) (event: EventStore.StoredEvent) : unit =
        if not (event.StreamId.StartsWith("Catalog-")) then ()
        else
            let slug = event.StreamId.Substring(8) // Remove "Catalog-" prefix
            match Catalogs.Serialization.fromStoredEvent event with
            | None -> ()
            | Some catalogEvent ->
                match catalogEvent with
                | Catalogs.Catalog_created data ->
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO catalog_list (slug, name, description, is_sorted)
                        VALUES (@slug, @name, @description, @is_sorted)
                    """
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "description", SqlType.String data.Description
                        "is_sorted", SqlType.Int32 (if data.IsSorted then 1 else 0)
                    ]
                    |> Db.exec

                | Catalogs.Catalog_updated data ->
                    conn
                    |> Db.newCommand "UPDATE catalog_list SET name = @name, description = @description WHERE slug = @slug"
                    |> Db.setParams [
                        "slug", SqlType.String slug
                        "name", SqlType.String data.Name
                        "description", SqlType.String data.Description
                    ]
                    |> Db.exec

                | Catalogs.Catalog_removed ->
                    conn
                    |> Db.newCommand "DELETE FROM catalog_entries WHERE catalog_slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec
                    conn
                    |> Db.newCommand "DELETE FROM catalog_list WHERE slug = @slug"
                    |> Db.setParams [ "slug", SqlType.String slug ]
                    |> Db.exec

                | Catalogs.Entry_added (data, position) ->
                    conn
                    |> Db.newCommand """
                        INSERT OR REPLACE INTO catalog_entries (entry_id, catalog_slug, movie_slug, note, position, media_type)
                        VALUES (@entry_id, @catalog_slug, @movie_slug, @note, @position, @media_type)
                    """
                    |> Db.setParams [
                        "entry_id", SqlType.String data.EntryId
                        "catalog_slug", SqlType.String slug
                        "movie_slug", SqlType.String data.MovieSlug
                        "note", match data.Note with Some n -> SqlType.String n | None -> SqlType.Null
                        "position", SqlType.Int32 position
                        "media_type", match data.MediaType with Some mt -> SqlType.String (encodeMediaType mt) | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Catalogs.Entry_updated data ->
                    conn
                    |> Db.newCommand "UPDATE catalog_entries SET note = @note WHERE entry_id = @entry_id"
                    |> Db.setParams [
                        "entry_id", SqlType.String data.EntryId
                        "note", match data.Note with Some n -> SqlType.String n | None -> SqlType.Null
                    ]
                    |> Db.exec

                | Catalogs.Entry_removed entryId ->
                    conn
                    |> Db.newCommand "DELETE FROM catalog_entries WHERE entry_id = @entry_id"
                    |> Db.setParams [ "entry_id", SqlType.String entryId ]
                    |> Db.exec

                | Catalogs.Entries_reordered entryIds ->
                    entryIds
                    |> List.iteri (fun i eid ->
                        conn
                        |> Db.newCommand "UPDATE catalog_entries SET position = @position WHERE entry_id = @entry_id"
                        |> Db.setParams [
                            "entry_id", SqlType.String eid
                            "position", SqlType.Int32 i
                        ]
                        |> Db.exec)

    let handler: Projection.ProjectionHandler = {
        Name = "CatalogProjection"
        Handle = handleEvent
        Init = createTables
        Drop = dropTables
    }

    // Query functions

    // Note: column is named `movie_slug` for historical reasons but stores any media slug
    // (movie, series, game, book). curation-cyxbc (ADR-0079): type-filtered so a removal
    // cascade for one media type can never delete another type's same-slugged entry;
    // `media_type IS NULL` (legacy, recorded before entries were typed) still matches
    // every type, exactly as it always has.
    let getEntriesByMediaSlug (conn: SqliteConnection) (mediaType: MediaType) (mediaSlug: string) : (string * string) list =
        conn
        |> Db.newCommand """
            SELECT catalog_slug, entry_id FROM catalog_entries
            WHERE movie_slug = @movie_slug AND (media_type = @media_type OR media_type IS NULL)
        """
        |> Db.setParams [
            "movie_slug", SqlType.String mediaSlug
            "media_type", SqlType.String (encodeMediaType mediaType)
        ]
        |> Db.query (fun (rd: IDataReader) ->
            (rd.ReadString "catalog_slug", rd.ReadString "entry_id"))

    /// curation-cyxbc (ADR-0079): replaces `getCatalogsForMovie` — resolves
    /// the catalogs referencing `(mediaType, mediaSlug)`, matching legacy
    /// (`media_type IS NULL`) rows too since they predate typing.
    let getCatalogsForMedia (conn: SqliteConnection) (mediaType: MediaType) (mediaSlug: string) : Mediatheca.Shared.CatalogRef list =
        conn
        |> Db.newCommand """
            SELECT ce.catalog_slug, ce.entry_id, cl.name, ce.movie_slug, ce.media_type
            FROM catalog_entries ce
            INNER JOIN catalog_list cl ON cl.slug = ce.catalog_slug
            WHERE ce.movie_slug = @movie_slug AND (ce.media_type = @media_type OR ce.media_type IS NULL)
            ORDER BY cl.name
        """
        |> Db.setParams [
            "movie_slug", SqlType.String mediaSlug
            "media_type", SqlType.String (encodeMediaType mediaType)
        ]
        |> Db.query (fun (rd: IDataReader) ->
            let resolvedMediaType =
                if rd.IsDBNull(rd.GetOrdinal("media_type")) then mediaType
                else rd.ReadString "media_type" |> decodeMediaType |> Option.defaultValue mediaType
            { Mediatheca.Shared.CatalogRef.Slug = rd.ReadString "catalog_slug"
              Name = rd.ReadString "name"
              EntryId = rd.ReadString "entry_id"
              MediaSlug = rd.ReadString "movie_slug"
              MediaType = resolvedMediaType })

    let getCatalogsForSeriesWithChildren (conn: SqliteConnection) (seriesSlug: string) : Mediatheca.Shared.CatalogRef list =
        conn
        |> Db.newCommand """
            SELECT ce.catalog_slug, ce.entry_id, cl.name, ce.movie_slug, ce.media_type
            FROM catalog_entries ce
            INNER JOIN catalog_list cl ON cl.slug = ce.catalog_slug
            WHERE (ce.movie_slug = @slug OR ce.movie_slug LIKE @prefix)
                AND (ce.media_type = 'Series' OR ce.media_type IS NULL)
            ORDER BY cl.name
        """
        |> Db.setParams [
            "slug", SqlType.String seriesSlug
            "prefix", SqlType.String (seriesSlug + ":%")
        ]
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.CatalogRef.Slug = rd.ReadString "catalog_slug"
              Name = rd.ReadString "name"
              EntryId = rd.ReadString "entry_id"
              MediaSlug = rd.ReadString "movie_slug"
              MediaType = MediaType.Series })

    let getAll (conn: SqliteConnection) : Mediatheca.Shared.CatalogListItem list =
        conn
        |> Db.newCommand """
            SELECT c.slug, c.name, c.description, c.is_sorted,
                   (SELECT COUNT(*) FROM catalog_entries WHERE catalog_slug = c.slug) as entry_count
            FROM catalog_list c
            ORDER BY c.name
        """
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.CatalogListItem.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Description = rd.ReadString "description"
              IsSorted = rd.ReadInt32 "is_sorted" > 0
              EntryCount = rd.ReadInt32 "entry_count" }
        )

    let private enhanceDisplayName (movieSlug: string) (baseName: string) =
        if movieSlug.Contains(":") then
            let suffix = movieSlug.Substring(movieSlug.IndexOf(':') + 1)
            if suffix.Length >= 4 && suffix.[0] = 's' && suffix.Contains("e") then
                // Episode: s01e05 -> " - S01E05"
                baseName + " - " + suffix.ToUpperInvariant()
            elif suffix.Length >= 2 && suffix.[0] = 's' then
                // Season: s01 -> " - Season 1"
                match System.Int32.TryParse(suffix.Substring(1)) with
                | true, n -> baseName + $" - Season {n}"
                | _ -> baseName + " - " + suffix
            else baseName
        else baseName

    /// curation-cyxbc (ADR-0079): when `media_type` is set, resolve exactly
    /// that type's `*_list` table. When `media_type IS NULL` (legacy, recorded
    /// before entries were typed), fall back to the original join-order
    /// inference (movies, then series, default movies) so legacy rows keep
    /// rendering exactly as before.
    let getEntries (conn: SqliteConnection) (catalogSlug: string) : Mediatheca.Shared.CatalogEntryDto list =
        conn
        |> Db.newCommand """
            WITH parsed AS (
                SELECT ce.*,
                    CASE WHEN INSTR(ce.movie_slug, ':') > 0
                        THEN SUBSTR(ce.movie_slug, 1, INSTR(ce.movie_slug, ':') - 1)
                        ELSE ce.movie_slug END as base_slug,
                    CASE WHEN INSTR(ce.movie_slug, ':') > 0
                        THEN SUBSTR(ce.movie_slug, INSTR(ce.movie_slug, ':') + 1)
                        ELSE '' END as suffix
                FROM catalog_entries ce
                WHERE ce.catalog_slug = @slug
            )
            SELECT p.entry_id, p.movie_slug, p.note, p.position, p.media_type,
                   CASE p.media_type
                       WHEN 'Movie' THEN ml.name
                       WHEN 'Series' THEN sl.name
                       WHEN 'Game' THEN gl.name
                       WHEN 'Book' THEN bl.title
                       ELSE COALESCE(ml.name, sl.name, p.movie_slug)
                   END as media_title,
                   CASE p.media_type
                       WHEN 'Movie' THEN ml.year
                       WHEN 'Series' THEN sl.year
                       WHEN 'Game' THEN gl.year
                       WHEN 'Book' THEN COALESCE(bl.year, 0)
                       ELSE COALESCE(ml.year, sl.year, 0)
                   END as media_year,
                   CASE p.media_type
                       WHEN 'Movie' THEN ml.poster_ref
                       WHEN 'Series' THEN COALESCE(se.still_ref, ss.poster_ref, sl.poster_ref)
                       WHEN 'Game' THEN gl.cover_ref
                       WHEN 'Book' THEN bl.cover_ref
                       ELSE COALESCE(se.still_ref, ss.poster_ref, ml.poster_ref, sl.poster_ref)
                   END as media_poster_ref,
                   ml.slug as ml_match,
                   sl.slug as sl_match
            FROM parsed p
            LEFT JOIN movie_list ml ON ml.slug = p.movie_slug
            LEFT JOIN series_list sl ON sl.slug = p.base_slug
            LEFT JOIN game_list gl ON gl.slug = p.movie_slug
            LEFT JOIN book_list bl ON bl.slug = p.movie_slug
            LEFT JOIN series_season_cache ss ON ss.series_slug = p.base_slug
                AND p.suffix LIKE 's%'
                AND ss.season_number = CAST(
                    CASE WHEN INSTR(p.suffix, 'e') > 0
                    THEN SUBSTR(p.suffix, 2, INSTR(p.suffix, 'e') - 2)
                    ELSE SUBSTR(p.suffix, 2)
                    END AS INTEGER)
            LEFT JOIN series_episode_cache se ON se.series_slug = p.base_slug
                AND INSTR(p.suffix, 'e') > 0
                AND se.season_number = CAST(SUBSTR(p.suffix, 2, INSTR(p.suffix, 'e') - 2) AS INTEGER)
                AND se.episode_number = CAST(SUBSTR(p.suffix, INSTR(p.suffix, 'e') + 1) AS INTEGER)
            ORDER BY p.position
        """
        |> Db.setParams [ "slug", SqlType.String catalogSlug ]
        |> Db.query (fun (rd: IDataReader) ->
            let slug = rd.ReadString "movie_slug"
            let baseTitle = rd.ReadString "media_title"
            let storedMediaType =
                if rd.IsDBNull(rd.GetOrdinal("media_type")) then None
                else rd.ReadString "media_type" |> decodeMediaType
            let inferredMediaType =
                if not (rd.IsDBNull(rd.GetOrdinal("ml_match"))) then MediaType.Movie
                elif not (rd.IsDBNull(rd.GetOrdinal("sl_match"))) then MediaType.Series
                else MediaType.Movie
            let resolvedMediaType = storedMediaType |> Option.defaultValue inferredMediaType
            let title =
                if resolvedMediaType = MediaType.Series then enhanceDisplayName slug baseTitle
                else baseTitle
            { Mediatheca.Shared.CatalogEntryDto.EntryId = rd.ReadString "entry_id"
              MediaSlug = slug
              Title = title
              Year = rd.ReadInt32 "media_year"
              PosterRef =
                if rd.IsDBNull(rd.GetOrdinal("media_poster_ref")) then None
                else Some (rd.ReadString "media_poster_ref")
              Note =
                if rd.IsDBNull(rd.GetOrdinal("note")) then None
                else Some (rd.ReadString "note")
              Position = rd.ReadInt32 "position"
              MediaType = resolvedMediaType }
        )

    let getBySlug (conn: SqliteConnection) (slug: string) : Mediatheca.Shared.CatalogDetail option =
        conn
        |> Db.newCommand "SELECT slug, name, description, is_sorted FROM catalog_list WHERE slug = @slug"
        |> Db.setParams [ "slug", SqlType.String slug ]
        |> Db.querySingle (fun (rd: IDataReader) ->
            let entries = getEntries conn slug
            { Mediatheca.Shared.CatalogDetail.Slug = rd.ReadString "slug"
              Name = rd.ReadString "name"
              Description = rd.ReadString "description"
              IsSorted = rd.ReadInt32 "is_sorted" > 0
              Entries = entries }
        )
