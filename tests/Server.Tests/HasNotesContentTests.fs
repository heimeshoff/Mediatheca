module Mediatheca.Tests.HasNotesContentTests

open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

/// curation-h98ve (ADR-0080): `HasNotesContent` on all four detail DTOs,
/// re-derived fresh from `notes_blocks` on every read (ADR-0043) — replaces
/// `GameDetail.HasJournalContent`. Each media type gets its own minimal
/// fixture mirroring the existing per-BC projection test files
/// (MoviesIntegrationTests.fs, SeriesProjectionReadsTests.fs,
/// GameDeckCompatProjectionTests.fs, BookProjectionTests.fs).

let private textBlock (id: string) (content: string) : JournalBlockDto = {
    Id = id
    ParentId = None
    BlockType = JournalBlockTypes.text
    Content = content
    Checked = false
    Collapsed = false
    Language = None
    Url = None
    ImageRef = None
    Caption = None
    Position = 0
    Width = 1.0
}

let private saveNotes (conn: SqliteConnection) (mediaType: MediaType) (slug: string) (blocks: JournalBlockDto list) =
    let streamId = Notes.streamId mediaType slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Notes.Serialization.toEventData (Notes.Notes_saved blocks) ] |> ignore
    Projection.runProjection conn NotesProjection.handler

[<Tests>]
let hasNotesContentTests =
    testList "HasNotesContent" [

        testCase "movie: false with no notes, true once notes carry non-whitespace content" <| fun _ ->
            let conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            CastStore.initialize conn
            JellyfinStore.initialize conn
            NotesProjection.handler.Init conn
            MovieProjection.handler.Init conn

            let slug = "the-matrix-1999"
            let movieData: Movies.MovieAddedData = {
                Name = "The Matrix"; Year = 1999; Runtime = Some 136
                Overview = ""; Genres = []; PosterRef = None; BackdropRef = None
                TmdbId = 603; TmdbRating = None
            }
            EventStore.appendToStream conn (Movies.streamId slug) -1L [ Movies.Serialization.toEventData (Movies.Movie_added_to_library movieData) ] |> ignore
            Projection.runProjection conn MovieProjection.handler

            let before = MovieProjection.getBySlug conn slug
            Expect.equal (before |> Option.map (fun d -> d.HasNotesContent)) (Some false) "no notes yet"

            saveNotes conn Movie slug [ textBlock "b1" "A note about this movie" ]

            let after = MovieProjection.getBySlug conn slug
            Expect.equal (after |> Option.map (fun d -> d.HasNotesContent)) (Some true) "notes now carry content"

        testCase "series: false with no notes, true once notes carry non-whitespace content" <| fun _ ->
            let conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            MetadataCache.initialize conn
            SeriesProjection.handler.Init conn
            CastStore.initialize conn
            JellyfinStore.initialize conn
            NotesProjection.handler.Init conn

            let slug = "the-wire-2002"
            let seriesData: Series.SeriesAddedData = {
                Name = "The Wire"; Year = 2002; Overview = ""; Genres = []
                Status = "Ended"; PosterRef = None; BackdropRef = None
                TmdbId = 1438; TmdbRating = None; EpisodeRuntime = None; Seasons = []
            }
            EventStore.appendToStream conn (Series.streamId slug) -1L [ Series.Serialization.toEventData (Series.Series_added_to_library seriesData) ] |> ignore
            Projection.runProjection conn SeriesProjection.handler

            let before = SeriesProjection.getBySlug conn slug None
            Expect.equal (before |> Option.map (fun d -> d.HasNotesContent)) (Some false) "no notes yet"

            saveNotes conn Series slug [ textBlock "b1" "A note about this series" ]

            let after = SeriesProjection.getBySlug conn slug None
            Expect.equal (after |> Option.map (fun d -> d.HasNotesContent)) (Some true) "notes now carry content"

        testCase "book: false with no notes, true once notes carry non-whitespace content" <| fun _ ->
            let conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            FriendProjection.handler.Init conn
            NotesProjection.handler.Init conn
            MetadataCache.initialize conn
            BookProjection.handler.Init conn

            let slug = "project-hail-mary-2021"
            let bookData: Books.BookAddedData = {
                Title = "Project Hail Mary"; Authors = [ "Andy Weir" ]; Year = Some 2021
                CoverRef = None; Subjects = []; Format = Print; ExternalIds = []
            }
            EventStore.appendToStream conn (Books.streamId slug) -1L [ Books.Serialization.toEventData (Books.Book_added_to_library bookData) ] |> ignore
            Projection.runProjection conn BookProjection.handler

            let before = BookProjection.getBySlug conn slug
            Expect.equal (before |> Option.map (fun d -> d.HasNotesContent)) (Some false) "no notes yet"

            saveNotes conn MediaType.Book slug [ textBlock "b1" "A note about this book" ]

            let after = BookProjection.getBySlug conn slug
            Expect.equal (after |> Option.map (fun d -> d.HasNotesContent)) (Some true) "notes now carry content"

        testCase "game: false with no notes and no legacy journal, true once notes carry content" <| fun _ ->
            let conn = new SqliteConnection("Data Source=:memory:")
            conn.Open()
            EventStore.initialize conn
            NotesProjection.handler.Init conn
            GameProjection.handler.Init conn
            PlaySessionProjection.handler.Init conn
            MetadataCache.initialize conn

            let slug = "hades-2020"
            let gameData: Games.GameAddedData = {
                Name = "Hades"; Year = 2020; Genres = []; Description = ""
                ShortDescription = ""; WebsiteUrl = None; CoverRef = None; BackdropRef = None
                RawgId = None; RawgRating = None
            }
            EventStore.appendToStream conn (Games.streamId slug) -1L [ Games.Serialization.toEventData (Games.Game_added_to_library gameData) ] |> ignore
            Projection.runProjection conn GameProjection.handler

            let before = GameProjection.getBySlug conn slug
            Expect.equal (before |> Option.map (fun d -> d.HasNotesContent)) (Some false) "no notes, no legacy journal"

            saveNotes conn Game slug [ textBlock "b1" "A note about this game" ]

            let after = GameProjection.getBySlug conn slug
            Expect.equal (after |> Option.map (fun d -> d.HasNotesContent)) (Some true) "notes now carry content"
    ]
