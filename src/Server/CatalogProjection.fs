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

    let getEntries (conn: SqliteConnection) (catalogSlug: string) : Mediatheca.Shared.CatalogEntryDto list =
        conn
        |> Db.newCommand """
            SELECT ce.entry_id, ce.movie_slug, ce.note, ce.position,
                   COALESCE(ml.name, ce.movie_slug) as movie_name,
                   COALESCE(ml.year, 0) as movie_year,
                   ml.poster_ref as movie_poster_ref
            FROM catalog_entries ce
            LEFT JOIN movie_list ml ON ml.slug = ce.movie_slug
            WHERE ce.catalog_slug = @slug
            ORDER BY ce.position
        """
        |> Db.setParams [ "slug", SqlType.String catalogSlug ]
        |> Db.query (fun (rd: IDataReader) ->
            { Mediatheca.Shared.CatalogEntryDto.EntryId = rd.ReadString "entry_id"
              MovieSlug = rd.ReadString "movie_slug"
              MovieName = rd.ReadString "movie_name"
              MovieYear = rd.ReadInt32 "movie_year"
              MoviePosterRef =
                if rd.IsDBNull(rd.GetOrdinal("movie_poster_ref")) then None
                else Some (rd.ReadString "movie_poster_ref")
              Note =
                if rd.IsDBNull(rd.GetOrdinal("note")) then None
                else Some (rd.ReadString "note")
              Position = rd.ReadInt32 "position" }
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
