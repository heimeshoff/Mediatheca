module Mediatheca.Tests.NotesMigrationTests

// curation-j4qqt (ADR-0080 §10-11): the full "Migrate to Notes" (Gate 1) /
// "Purge legacy stores" (Gate 2) flow through the real Administration.create
// IAdminApi surface. The legacy `content_blocks`/`game_journal_blocks`
// tables are raw-SQL fixtures here — their owning modules
// (ContentBlocks.fs/ContentBlockProjection.fs/GameJournal.fs) are deleted by
// this same task, so the migration (and this fixture) talk to those tables
// directly, exactly as `Administration.fs`'s migration code does.

open System
open System.IO
open System.Threading
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

let private createLegacyTables (conn: SqliteConnection) : unit =
    conn
    |> Db.newCommand """
        CREATE TABLE IF NOT EXISTS content_blocks (
            block_id     TEXT PRIMARY KEY,
            movie_slug   TEXT NOT NULL,
            session_id   TEXT,
            block_type   TEXT NOT NULL,
            content      TEXT NOT NULL DEFAULT '',
            image_ref    TEXT,
            url          TEXT,
            caption      TEXT,
            position     INTEGER NOT NULL DEFAULT 0,
            row_group    TEXT,
            row_position INTEGER
        );
        CREATE TABLE IF NOT EXISTS game_journal_blocks (
            id         TEXT PRIMARY KEY,
            game_slug  TEXT NOT NULL,
            parent_id  TEXT,
            block_type TEXT NOT NULL,
            content    TEXT NOT NULL DEFAULT '',
            checked    INTEGER NOT NULL DEFAULT 0,
            collapsed  INTEGER NOT NULL DEFAULT 0,
            language   TEXT,
            url        TEXT,
            image_ref  TEXT,
            caption    TEXT,
            position   INTEGER NOT NULL DEFAULT 0,
            width      REAL NOT NULL DEFAULT 1.0
        );
    """
    |> Db.exec

let private bootstrapAdmin (conn: SqliteConnection) =
    EventStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    MetadataCache.initialize conn
    SettingsStore.initialize conn
    NotesProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    BookProjection.handler.Init conn
    PlaySessionProjection.handler.Init conn
    CatalogProjection.handler.Init conn
    Administration.initializeJobRuns conn
    createLegacyTables conn

let private allProjectionHandlers = [
    MovieProjection.handler
    FriendProjection.handler
    CatalogProjection.handler
    SeriesProjection.handler
    GameProjection.handler
    BookProjection.handler
    PlaySessionProjection.handler
    NotesProjection.handler
]

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private createApi (factory: unit -> SqliteConnection) (dbPath: string) : IAdminApi =
    Administration.create factory dbPath noImagesDir allProjectionHandlers [] (Administration.makeJobRunRecorder (factory ()) (new SemaphoreSlim(1, 1))) (Administration.makeGuards ())

let private backupsDirFor (dbPath: string) = Path.Combine(Path.GetDirectoryName(dbPath), "backups")
let private cleanupBackups (dbPath: string) =
    let dir = backupsDirFor dbPath
    if Directory.Exists(dir) then try Directory.Delete(dir, true) with _ -> ()

// ── Legacy fixture helpers (raw SQL — the modules that used to own these
// tables are deleted by this task) ──

let private insertContentBlockRow (conn: SqliteConnection) (blockId: string) (movieSlug: string) (content: string) : unit =
    conn
    |> Db.newCommand "INSERT INTO content_blocks (block_id, movie_slug, block_type, content) VALUES (@id, @slug, 'text', @content)"
    |> Db.setParams [ "id", SqlType.String blockId; "slug", SqlType.String movieSlug; "content", SqlType.String content ]
    |> Db.exec

let private appendLegacyContentBlockEvent (conn: SqliteConnection) (slug: string) : unit =
    let sid = sprintf "ContentBlocks-%s" slug
    let eventData : EventStore.EventData = { EventType = "Content_block_added"; Data = "{}"; Metadata = "{}" }
    EventStore.appendToStream conn sid -1L [ eventData ] |> ignore

let private insertGameJournalRow (conn: SqliteConnection) (id: string) (gameSlug: string) (content: string) : unit =
    conn
    |> Db.newCommand "INSERT INTO game_journal_blocks (id, game_slug, block_type, content) VALUES (@id, @slug, 'text', @content)"
    |> Db.setParams [ "id", SqlType.String id; "slug", SqlType.String gameSlug; "content", SqlType.String content ]
    |> Db.exec

// ── Home-projection fixture helpers (real aggregates, mirroring
// HasNotesContentTests.fs's shape) ──

let private appendMovie (conn: SqliteConnection) (slug: string) =
    let movieData: Movies.MovieAddedData = {
        Name = slug; Year = 2000; Runtime = None; Overview = ""; Genres = []
        PosterRef = None; BackdropRef = None; TmdbId = abs (slug.GetHashCode()); TmdbRating = None
    }
    EventStore.appendToStream conn (Movies.streamId slug) -1L [ Movies.Serialization.toEventData (Movies.Movie_added_to_library movieData) ] |> ignore
    Projection.runProjection conn MovieProjection.handler

let private appendSeries (conn: SqliteConnection) (slug: string) =
    let seriesData: Series.SeriesAddedData = {
        Name = slug; Year = 2000; Overview = ""; Genres = []
        Status = "Ended"; PosterRef = None; BackdropRef = None
        TmdbId = 2; TmdbRating = None; EpisodeRuntime = None; Seasons = []
    }
    EventStore.appendToStream conn (Series.streamId slug) -1L [ Series.Serialization.toEventData (Series.Series_added_to_library seriesData) ] |> ignore
    Projection.runProjection conn SeriesProjection.handler

let private appendBook (conn: SqliteConnection) (slug: string) =
    let bookData: Books.BookAddedData = {
        Title = slug; Authors = [ "Someone" ]; Year = Some 2000
        CoverRef = None; Subjects = []; Format = Print; ExternalIds = []
    }
    EventStore.appendToStream conn (Books.streamId slug) -1L [ Books.Serialization.toEventData (Books.Book_added_to_library bookData) ] |> ignore
    Projection.runProjection conn BookProjection.handler

let private appendGame (conn: SqliteConnection) (slug: string) =
    let gameData: Games.GameAddedData = {
        Name = slug; Year = 2000; Genres = []; Description = ""
        ShortDescription = ""; WebsiteUrl = None; CoverRef = None; BackdropRef = None
        RawgId = None; RawgRating = None
    }
    EventStore.appendToStream conn (Games.streamId slug) -1L [ Games.Serialization.toEventData (Games.Game_added_to_library gameData) ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private tableExistsRaw (conn: SqliteConnection) (name: string) : bool =
    conn
    |> Db.newCommand "SELECT name FROM sqlite_master WHERE type = 'table' AND name = @name"
    |> Db.setParams [ "name", SqlType.String name ]
    |> Db.querySingle (fun rd -> rd.ReadString "name")
    |> Option.isSome

[<Tests>]
let notesMigrationTests =
    testList "NotesMigration" [

        testCase "Gate 1 preview names exactly the ambiguous and orphan owners; Gate 1 confirm converts the rest, sourcing a game with both stores from its journal only; Gate 2 excludes ambiguous/orphan by default; confirm purges only the resolved streams" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrapAdmin
            let conn = db.Connection
            try
                let movieSlug = "movie-owner-2001"
                let seriesSlug = "series-owner-2002"
                let bookSlug = "book-owner-2003"
                let gameSlug = "game-owner-2004"
                let ambiguousSlug = "ambiguous-owner-2005"
                let orphanSlug = "orphan-owner-2006"

                // Four normally-resolvable owners, each with one content_blocks
                // row + its ContentBlocks-* event.
                appendMovie conn movieSlug
                insertContentBlockRow conn "b-movie" movieSlug "movie note content"
                appendLegacyContentBlockEvent conn movieSlug

                appendSeries conn seriesSlug
                insertContentBlockRow conn "b-series" seriesSlug "series note content"
                appendLegacyContentBlockEvent conn seriesSlug

                appendBook conn bookSlug
                insertContentBlockRow conn "b-book" bookSlug "book note content"
                appendLegacyContentBlockEvent conn bookSlug

                // The game: BOTH a game_journal_blocks row (the authoritative,
                // already-migrated-once-from-content-blocks source) AND a
                // leftover content_blocks row that must NOT be converted a
                // second time (ADR-0080 §10).
                appendGame conn gameSlug
                insertGameJournalRow conn "j-game" gameSlug "journal-content"
                insertContentBlockRow conn "b-game" gameSlug "old-content-should-be-ignored"
                appendLegacyContentBlockEvent conn gameSlug

                // Ambiguous: registered in TWO home projections (Movie and Game).
                appendMovie conn ambiguousSlug
                appendGame conn ambiguousSlug
                insertContentBlockRow conn "b-ambig" ambiguousSlug "ambiguous content"
                appendLegacyContentBlockEvent conn ambiguousSlug

                // Orphan: registered in no home projection at all.
                insertContentBlockRow conn "b-orphan" orphanSlug "orphan content"
                appendLegacyContentBlockEvent conn orphanSlug

                let api = createApi db.Factory db.Path

                // ── Gate 1 preview ──
                let preview = api.previewNotesMigration () |> Async.RunSynchronously
                Expect.equal (preview.Resolved |> List.map (fun r -> r.Slug) |> List.sort)
                    (List.sort [ movieSlug; seriesSlug; bookSlug; gameSlug ])
                    "preview should resolve exactly the four unambiguous owners"
                Expect.equal (preview.Ambiguous |> List.map (fun r -> r.Slug)) [ ambiguousSlug ]
                    "preview should name the ambiguous owner, never guess"
                Expect.equal (preview.Orphan |> List.map (fun r -> r.Slug)) [ orphanSlug ]
                    "preview should name the orphan owner, never guess"

                // ── Gate 1 confirm ──
                let report = api.runNotesMigration () |> Async.RunSynchronously
                Expect.equal report.Converted 4 "exactly the four resolved owners should be converted"
                Expect.equal (report.Ambiguous |> List.map (fun r -> r.Slug)) [ ambiguousSlug ] "report echoes the same ambiguous owner"
                Expect.equal (report.Orphan |> List.map (fun r -> r.Slug)) [ orphanSlug ] "report echoes the same orphan owner"

                let movieNotes = NotesProjection.getForOwner conn Movie movieSlug
                Expect.equal (movieNotes |> List.map (fun b -> b.Content)) [ "movie note content" ] "movie's content_blocks content migrated"

                let seriesNotes = NotesProjection.getForOwner conn Series seriesSlug
                Expect.equal (seriesNotes |> List.map (fun b -> b.Content)) [ "series note content" ] "series's content_blocks content migrated"

                let bookNotes = NotesProjection.getForOwner conn Book bookSlug
                Expect.equal (bookNotes |> List.map (fun b -> b.Content)) [ "book note content" ] "book's content_blocks content migrated"

                let gameNotes = NotesProjection.getForOwner conn Game gameSlug
                Expect.equal (gameNotes |> List.map (fun b -> b.Content)) [ "journal-content" ]
                    "game must be sourced from game_journal_blocks only — the leftover content_blocks row is NOT converted a second time"
                Expect.equal (EventStore.readStream conn (Notes.streamId Game gameSlug) |> List.length) 1
                    "exactly one Notes_saved event should have been appended for the game"

                // Ambiguous/orphan owners must NOT have been converted.
                Expect.isEmpty (NotesProjection.getForOwner conn Movie ambiguousSlug) "ambiguous owner (as Movie) must not be converted"
                Expect.isEmpty (NotesProjection.getForOwner conn Game ambiguousSlug) "ambiguous owner (as Game) must not be converted"

                // ── Gate 1 idempotency: a second confirm emits zero events ──
                let secondReport = api.runNotesMigration () |> Async.RunSynchronously
                Expect.equal secondReport.Converted 0 "a second confirm must convert nothing — every resolved owner already has a Notes-* stream"

                // ── Gate 2 preview ──
                let purgePreview = api.previewPurgeLegacyNotes () |> Async.RunSynchronously
                let expectedDefaultStreams =
                    [ movieSlug; seriesSlug; bookSlug; gameSlug ] |> List.map (sprintf "ContentBlocks-%s") |> List.sort
                Expect.equal (purgePreview.StreamIds |> List.sort) expectedDefaultStreams
                    "Gate 2's default set should be exactly the four resolved owners' ContentBlocks-* streams"
                let expectedExcludedStreams =
                    [ ambiguousSlug; orphanSlug ] |> List.map (sprintf "ContentBlocks-%s") |> List.sort
                Expect.equal (purgePreview.ExcludedStreamIds |> List.sort) expectedExcludedStreams
                    "Gate 2's excluded set should name the ambiguous/orphan owners' streams"
                Expect.equal purgePreview.EventCount 4 "each of the four default streams carries exactly one event"

                // ── Gate 2 confirm ──
                match api.purgeLegacyNotes purgePreview.StreamIds |> Async.RunSynchronously with
                | BackupFailed reason -> failtest (sprintf "Expected purgeLegacyNotes to succeed, got BackupFailed: %s" reason)
                | Applied(_, affected) ->
                    Expect.equal affected 4 "exactly the four resolved streams' events should be deleted"

                for slug in [ movieSlug; seriesSlug; bookSlug; gameSlug ] do
                    Expect.isEmpty (EventStore.readStream conn (sprintf "ContentBlocks-%s" slug))
                        (sprintf "%s's ContentBlocks-* stream should be gone after purge" slug)

                for slug in [ ambiguousSlug; orphanSlug ] do
                    Expect.isFalse (List.isEmpty (EventStore.readStream conn (sprintf "ContentBlocks-%s" slug)))
                        (sprintf "%s's ContentBlocks-* stream should survive — it was excluded from the default purge set" slug)

                Expect.isFalse (tableExistsRaw conn "content_blocks") "content_blocks table should be dropped"
                Expect.isFalse (tableExistsRaw conn "game_journal_blocks") "game_journal_blocks table should be dropped"

                // Notes content, being a separate Notes-* stream, survives the purge untouched.
                Expect.equal (NotesProjection.getForOwner conn Movie movieSlug |> List.map (fun b -> b.Content)) [ "movie note content" ]
                    "notes_blocks content must survive Gate 2's purge"
            finally
                cleanupBackups db.Path
    ]
