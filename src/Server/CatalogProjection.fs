namespace Mediatheca.Server

open System.Data
open Microsoft.Data.Sqlite
open Donald

module CatalogProjection =

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
                        INSERT OR REPLACE INTO catalog_entries (entry_id, catalog_slug, movie_slug, note, position)
                        VALUES (@entry_id, @catalog_slug, @movie_slug, @note, @position)
                    """
                    |> Db.setParams [
                        "entry_id", SqlType.String data.EntryId
                        "catalog_slug", SqlType.String slug
                        "movie_slug", SqlType.String data.MovieSlug
                        "note", match data.Note with Some n -> SqlType.String n | None -> SqlType.Null
                        "position", SqlType.Int32 position
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

    let getEntriesByMovieSlug (conn: SqliteConnection) (movieSlug: string) : (string * string) list =
        conn
        |> Db.newCommand "SELECT catalog_slug, entry_id FROM catalog_entries WHERE movie_slug = @movie_slug"
        |> Db.setParams [ "movie_slug", SqlType.String movieSlug ]
        |> Db.query (fun (rd: IDataReader) ->
            (rd.ReadString "catalog_slug", rd.ReadString "entry_id"))

    let getCatalogsForMovie (conn: SqliteConnection) (movieSlug: string) : Mediatheca.Shared.CatalogRef list =
        conn
        |> Db.newCommand """
            SELECT ce.catalog_slug, ce.entry_id, cl.name, ce.movie_slug
            FROM catalog_entries ce
            INNER JOIN catalog_list cl ON cl.slug = ce.catalog_slug
            WHERE ce.movie_slug = @movie_slug
            ORDER BY cl.name
        """
        |> Db.setParams [ "movie_slug", SqlType.String movieSlug ]
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.CatalogRef.Slug = rd.ReadString "catalog_slug"
              Name = rd.ReadString "name"
              EntryId = rd.ReadString "entry_id"
              MovieSlug = rd.ReadString "movie_slug" })

    let getCatalogsForSeriesWithChildren (conn: SqliteConnection) (seriesSlug: string) : Mediatheca.Shared.CatalogRef list =
        conn
        |> Db.newCommand """
            SELECT ce.catalog_slug, ce.entry_id, cl.name, ce.movie_slug
            FROM catalog_entries ce
            INNER JOIN catalog_list cl ON cl.slug = ce.catalog_slug
            WHERE ce.movie_slug = @slug OR ce.movie_slug LIKE @prefix
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
              MovieSlug = rd.ReadString "movie_slug" })

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
            SELECT p.entry_id, p.movie_slug, p.note, p.position,
                   COALESCE(ml.name, sl.name, p.movie_slug) as movie_name,
                   COALESCE(ml.year, sl.year, 0) as movie_year,
                   COALESCE(
                       se.still_ref,
                       ss.poster_ref,
                       ml.poster_ref,
                       sl.poster_ref
                   ) as movie_poster_ref,
                   CASE WHEN ml.slug IS NOT NULL THEN 'movies'
                        WHEN sl.slug IS NOT NULL THEN 'series'
                        ELSE 'movies' END as route_prefix
            FROM parsed p
            LEFT JOIN movie_list ml ON ml.slug = p.movie_slug
            LEFT JOIN series_list sl ON sl.slug = p.base_slug
            LEFT JOIN series_seasons ss ON ss.series_slug = p.base_slug
                AND p.suffix LIKE 's%'
                AND ss.season_number = CAST(
                    CASE WHEN INSTR(p.suffix, 'e') > 0
                    THEN SUBSTR(p.suffix, 2, INSTR(p.suffix, 'e') - 2)
                    ELSE SUBSTR(p.suffix, 2)
                    END AS INTEGER)
            LEFT JOIN series_episodes se ON se.series_slug = p.base_slug
                AND INSTR(p.suffix, 'e') > 0
                AND se.season_number = CAST(SUBSTR(p.suffix, 2, INSTR(p.suffix, 'e') - 2) AS INTEGER)
                AND se.episode_number = CAST(SUBSTR(p.suffix, INSTR(p.suffix, 'e') + 1) AS INTEGER)
            ORDER BY p.position
        """
        |> Db.setParams [ "slug", SqlType.String catalogSlug ]
        |> Db.query (fun (rd: IDataReader) ->
            let slug = rd.ReadString "movie_slug"
            let baseName = rd.ReadString "movie_name"
            { Mediatheca.Shared.CatalogEntryDto.EntryId = rd.ReadString "entry_id"
              MovieSlug = slug
              MovieName = enhanceDisplayName slug baseName
              MovieYear = rd.ReadInt32 "movie_year"
              MoviePosterRef =
                if rd.IsDBNull(rd.GetOrdinal("movie_poster_ref")) then None
                else Some (rd.ReadString "movie_poster_ref")
              Note =
                if rd.IsDBNull(rd.GetOrdinal("note")) then None
                else Some (rd.ReadString "note")
              Position = rd.ReadInt32 "position"
              RoutePrefix = rd.ReadString "route_prefix" }
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
