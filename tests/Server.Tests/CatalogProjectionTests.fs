module Mediatheca.Tests.CatalogProjectionTests

// curation-cyxbc (ADR-0079): `CatalogProjection.getEntries`'s typed joins (one
// per MediaType) and its `media_type IS NULL` legacy fallback, plus the
// type-filtered lookups (`getCatalogsForMedia`, `getCatalogsForSeriesWithChildren`,
// `getEntriesByMediaSlug`) that keep a removal cascade or a catalogs-for lookup
// from crossing into another media type's same-slugged entry.

open System.Net.Http
open Expecto
open Microsoft.Data.Sqlite
open Donald
open Mediatheca.Server
open Mediatheca.Shared

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    BookProjection.handler.Init conn
    CatalogProjection.handler.Init conn
    CastStore.initialize conn

let private allProjectionHandlers =
    [ MovieProjection.handler; SeriesProjection.handler; GameProjection.handler
      BookProjection.handler; CatalogProjection.handler ]

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private createApi (factory: unit -> SqliteConnection) : IMediathecaApi =
    Api.create
        factory
        (new HttpClient())
        (Qbittorrent.createHttpClient ())
        (fun () -> ({ ApiKey = ""; ImageBaseUrl = "" } : Tmdb.TmdbConfig))
        (fun () -> ({ ApiKey = "" } : Rawg.RawgConfig))
        (fun () -> ({ ApiKey = ""; SteamId = "" } : Steam.SteamConfig))
        (fun () -> ({ ServerUrl = ""; Username = ""; Password = ""; UserId = ""; AccessToken = "" } : Jellyfin.JellyfinConfig))
        (fun () -> ({ Url = ""; Username = ""; Password = "" } : Qbittorrent.QbittorrentConfig))
        (fun () -> ({ UserAgent = "Mediatheca/1.0 (+https://github.com/heimeshoff/mediatheca)" } : OpenLibrary.OpenLibraryConfig))
        (fun () -> ({ AuthFile = None; Marketplace = "de"; CachedAccessToken = None; CachedAccessTokenExpiresAt = None } : Audible.AudibleConfig))
        (fun () -> ({ UserId = None; ImportShelves = [ "currently-reading" ] } : Goodreads.GoodreadsConfig))
        (fun () -> async { return Error "not wired in tests" })
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        noImagesDir
        allProjectionHandlers

// ── Seeding helpers: real events through the real aggregate + projection,
//    exactly the shape production code writes, bypassing only the
//    network-bound `addMovie`/`addGame`/etc. command handlers. ──

let private appendMovieEvent (conn: SqliteConnection) (slug: string) (event: Movies.MovieEvent) =
    let streamId = Movies.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Movies.Serialization.toEventData event ] |> ignore
    Projection.runProjection conn MovieProjection.handler

let private appendSeriesEvent (conn: SqliteConnection) (slug: string) (event: Series.SeriesEvent) =
    let streamId = Series.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Series.Serialization.toEventData event ] |> ignore
    Projection.runProjection conn SeriesProjection.handler

let private appendGameEvent (conn: SqliteConnection) (slug: string) (event: Games.GameEvent) =
    let streamId = Games.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Games.Serialization.toEventData event ] |> ignore
    Projection.runProjection conn GameProjection.handler

let private appendBookEvent (conn: SqliteConnection) (slug: string) (event: Books.BookEvent) =
    let streamId = Books.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Books.Serialization.toEventData event ] |> ignore
    Projection.runProjection conn BookProjection.handler

/// Appends a raw, legacy-shaped (`MediaType = None`) `Entry_added` event
/// directly — the shape every `Entry_added` had before ADR-0079, which the
/// command layer (`AddCatalogEntryRequest`) can no longer produce.
let private appendLegacyEntry (conn: SqliteConnection) (catalogSlug: string) (entryId: string) (mediaSlug: string) (position: int) =
    let streamId = Catalogs.streamId catalogSlug
    let streamPosition = EventStore.getStreamPosition conn streamId
    let data: Catalogs.EntryAddedData = {
        EntryId = entryId
        MovieSlug = mediaSlug
        Note = None
        MediaType = None
    }
    EventStore.appendToStream conn streamId streamPosition [ Catalogs.Serialization.toEventData (Catalogs.Entry_added (data, position)) ] |> ignore
    Projection.runProjection conn CatalogProjection.handler

let private sampleMovie (name: string) (year: int) (posterRef: string option) : Movies.MovieAddedData = {
    Name = name
    Year = year
    Runtime = None
    Overview = ""
    Genres = []
    PosterRef = posterRef
    BackdropRef = None
    TmdbId = 1
    TmdbRating = None
}

let private sampleGame (name: string) (year: int) (coverRef: string option) : Games.GameAddedData = {
    Name = name
    Year = year
    Genres = []
    Description = ""
    ShortDescription = ""
    WebsiteUrl = None
    CoverRef = coverRef
    BackdropRef = None
    RawgId = None
    RawgRating = None
}

let private sampleBook (title: string) (year: int option) (coverRef: string option) : Books.BookAddedData = {
    Title = title
    Authors = []
    Year = year
    CoverRef = coverRef
    Subjects = []
    Format = BookFormat.Unknown
    ExternalIds = []
}

let private sampleSeries (name: string) (year: int) (posterRef: string option) (seasons: Series.SeasonImportData list) : Series.SeriesAddedData = {
    Name = name
    Year = year
    Overview = ""
    Genres = []
    Status = "Returning"
    PosterRef = posterRef
    BackdropRef = None
    TmdbId = 1
    TmdbRating = None
    EpisodeRuntime = None
    Seasons = seasons
}

[<Tests>]
let catalogProjectionTests =
    testList "CatalogProjection (curation-cyxbc, ADR-0079)" [

        // Amended during curation-cyxbc's implementation (ADR-0079's amendment
        // note): `Add_entry` rejects any same-slug add within ONE catalog
        // regardless of type (the projection's UNIQUE(catalog_slug, movie_slug)
        // stays slug-only until a `media_type` backfill lands — see
        // `Catalogs.decide`). So the movie and the book below share a slug
        // ACROSS two catalogs, not within one; that is what this criterion
        // protects against: a book must never resolve as a guessed movie (or
        // vice versa) just because they happen to share a slug.
        testCase "typed entries of every media type resolve their own type's title/year/cover, and a movie and a book sharing a slug across two catalogs each resolve to their own row" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory
            let conn = db.Connection

            appendMovieEvent conn "collision-2020" (Movies.Movie_added_to_library (sampleMovie "Collision (Movie)" 2020 (Some "posters/collision-2020.jpg")))
            appendBookEvent conn "collision-2020" (Books.Book_added_to_library (sampleBook "Collision (Book)" None (Some "book-covers/collision-2020.jpg")))
            appendGameEvent conn "some-game-2019" (Games.Game_added_to_library (sampleGame "Some Game" 2019 (Some "game-covers/some-game-2019.jpg")))

            let mkCatalog (name: string) =
                match api.createCatalog { Name = name; Description = ""; IsSorted = false } |> Async.RunSynchronously with
                | Ok slug -> slug
                | Error e -> failtestf "Expected catalog creation to succeed; got %s" e

            let movieCatalog = mkCatalog "Movies Mix"
            let bookCatalog = mkCatalog "Books Mix"

            let addTyped (catalogSlug: string) (mediaSlug: string) (mediaType: MediaType) =
                match api.addCatalogEntry catalogSlug { MediaSlug = mediaSlug; MediaType = mediaType; Note = None } |> Async.RunSynchronously with
                | Ok _ -> ()
                | Error e -> failtestf "Expected addCatalogEntry to succeed for %s; got %s" mediaSlug e

            addTyped movieCatalog "collision-2020" MediaType.Movie
            addTyped movieCatalog "some-game-2019" MediaType.Game
            addTyped bookCatalog "collision-2020" MediaType.Book

            let movieCatalogEntries = CatalogProjection.getEntries conn movieCatalog
            Expect.equal (List.length movieCatalogEntries) 2 "The movie catalog's two typed entries should resolve"

            let movieEntry = movieCatalogEntries |> List.find (fun e -> e.MediaType = MediaType.Movie)
            Expect.equal movieEntry.Title "Collision (Movie)" "Movie title should resolve from movie_list"
            Expect.equal movieEntry.Year 2020 "Movie year should resolve from movie_list"
            Expect.equal movieEntry.PosterRef (Some "posters/collision-2020.jpg") "Movie poster should resolve from movie_list"

            let gameEntry = movieCatalogEntries |> List.find (fun e -> e.MediaType = MediaType.Game)
            Expect.equal gameEntry.Title "Some Game" "Game title should resolve from game_list"
            Expect.equal gameEntry.Year 2019 "Game year should resolve from game_list"
            Expect.equal gameEntry.PosterRef (Some "game-covers/some-game-2019.jpg") "Game cover should resolve from game_list"

            let bookCatalogEntries = CatalogProjection.getEntries conn bookCatalog
            Expect.equal (List.length bookCatalogEntries) 1 "The book catalog's one typed entry should resolve"

            let bookEntry = bookCatalogEntries.[0]
            Expect.equal bookEntry.MediaType MediaType.Book "The same-slugged entry in the book catalog should resolve as a Book, not a guessed Movie"
            Expect.equal bookEntry.Title "Collision (Book)" "Book title should resolve from book_list"
            Expect.equal bookEntry.Year 0 "A null book year should resolve to 0"
            Expect.equal bookEntry.PosterRef (Some "book-covers/collision-2020.jpg") "Book cover should resolve from book_list"

        testCase "a media_type IS NULL row resolves exactly as before the legacy join-order inference" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory
            let conn = db.Connection

            appendMovieEvent conn "legacy-movie-2015" (Movies.Movie_added_to_library (sampleMovie "Legacy Movie" 2015 (Some "posters/legacy-movie-2015.jpg")))
            let legacySeasons : Series.SeasonImportData list =
                [ { SeasonNumber = 1; Name = "Season 1"; Overview = ""; PosterRef = Some "seasons/legacy-series-2010-s01.jpg"; AirDate = None; Episodes = [] } ]
            appendSeriesEvent conn "legacy-series-2010" (Series.Series_added_to_library (sampleSeries "Legacy Series" 2010 (Some "posters/legacy-series-2010.jpg") legacySeasons))
            // series-r2xhv: the season/episode cache tier is populated imperatively at
            // command time (`Api.addSeriesToLibraryImpl`), never from event replay — mirror
            // that here rather than relying on `Series_added_to_library`'s payload alone.
            SeriesRefresh.upsertSeasonEpisodeCache conn "legacy-series-2010" legacySeasons

            let catalogSlug =
                match api.createCatalog { Name = "Legacy"; Description = ""; IsSorted = false } |> Async.RunSynchronously with
                | Ok slug -> slug
                | Error e -> failtestf "Expected catalog creation to succeed; got %s" e

            appendLegacyEntry conn catalogSlug "legacy-entry-1" "legacy-movie-2015" 0
            appendLegacyEntry conn catalogSlug "legacy-entry-2" "legacy-series-2010:s01" 1

            let entries = CatalogProjection.getEntries conn catalogSlug |> List.sortBy (fun e -> e.Position)
            Expect.equal (List.length entries) 2 "Both legacy entries should resolve"

            let movieEntry = entries.[0]
            Expect.equal movieEntry.MediaType MediaType.Movie "A legacy row for a movie slug should infer MediaType = Movie"
            Expect.equal movieEntry.Title "Legacy Movie" "Legacy movie name should resolve unchanged"
            Expect.equal movieEntry.Year 2015 "Legacy movie year should resolve unchanged"
            Expect.equal movieEntry.PosterRef (Some "posters/legacy-movie-2015.jpg") "Legacy movie poster should resolve unchanged"

            let seasonEntry = entries.[1]
            Expect.equal seasonEntry.MediaType MediaType.Series "A legacy row for a series season slug should infer MediaType = Series"
            Expect.equal seasonEntry.Title "Legacy Series - Season 1" "Legacy season display name should still get its suffix"
            Expect.equal seasonEntry.PosterRef (Some "seasons/legacy-series-2010-s01.jpg") "Legacy season poster should still resolve from the season cache"

        // The movie and the book live in separate catalogs (`decide` rejects a
        // same-slug add within one catalog regardless of type — see the test
        // above) — `getEntriesByMediaSlug` scans across ALL catalogs by slug,
        // so the type filter is exactly what has to keep the book's entry,
        // in its own catalog, untouched by a movie removal.
        testCase "removing a movie leaves a same-slugged book's catalog entry, in a different catalog, in place" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory
            let conn = db.Connection

            appendMovieEvent conn "shared-2021" (Movies.Movie_added_to_library (sampleMovie "Shared Movie" 2021 None))
            appendBookEvent conn "shared-2021" (Books.Book_added_to_library (sampleBook "Shared Book" (Some 2021) None))

            let mkCatalog (name: string) =
                match api.createCatalog { Name = name; Description = ""; IsSorted = false } |> Async.RunSynchronously with
                | Ok slug -> slug
                | Error e -> failtestf "Expected catalog creation to succeed; got %s" e

            let movieCatalog = mkCatalog "Shared Movie Catalog"
            let bookCatalog = mkCatalog "Shared Book Catalog"

            api.addCatalogEntry movieCatalog { MediaSlug = "shared-2021"; MediaType = MediaType.Movie; Note = None } |> Async.RunSynchronously |> ignore
            api.addCatalogEntry bookCatalog { MediaSlug = "shared-2021"; MediaType = MediaType.Book; Note = None } |> Async.RunSynchronously |> ignore

            match api.removeMovie "shared-2021" |> Async.RunSynchronously with
            | Ok () -> ()
            | Error e -> failtestf "Expected removeMovie to succeed; got %s" e

            let movieCatalogEntries = CatalogProjection.getEntries conn movieCatalog
            Expect.equal (List.length movieCatalogEntries) 0 "The movie's own entry should be removed"

            let bookCatalogEntries = CatalogProjection.getEntries conn bookCatalog
            Expect.equal (List.length bookCatalogEntries) 1 "The book's entry, in its own catalog, should remain"
            Expect.equal bookCatalogEntries.[0].MediaType MediaType.Book "The remaining entry should be the book's"

        testCase "getCatalogsForMovie/Game/Book resolve only their own type's catalog for a shared slug, and getCatalogsForSeries keeps resolving season/episode children" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory
            let conn = db.Connection

            appendMovieEvent conn "dup-2020" (Movies.Movie_added_to_library (sampleMovie "Dup Movie" 2020 None))
            appendGameEvent conn "dup-2020" (Games.Game_added_to_library (sampleGame "Dup Game" 2020 None))
            appendBookEvent conn "dup-2020" (Books.Book_added_to_library (sampleBook "Dup Book" (Some 2020) None))
            appendSeriesEvent conn "series-2015" (Series.Series_added_to_library (sampleSeries "Series Show" 2015 None
                [ { SeasonNumber = 1; Name = "Season 1"; Overview = ""; PosterRef = None; AirDate = None
                    Episodes = [ { EpisodeNumber = 1; Name = "Ep 1"; Overview = ""; Runtime = None; AirDate = None; StillRef = None; TmdbRating = None }
                                 { EpisodeNumber = 2; Name = "Ep 2"; Overview = ""; Runtime = None; AirDate = None; StillRef = None; TmdbRating = None } ] } ]))

            let mkCatalog (name: string) =
                match api.createCatalog { Name = name; Description = ""; IsSorted = false } |> Async.RunSynchronously with
                | Ok slug -> slug
                | Error e -> failtestf "Expected catalog creation to succeed; got %s" e

            let catA = mkCatalog "Cat A (movie)"
            let catB = mkCatalog "Cat B (game)"
            let catC = mkCatalog "Cat C (book)"
            let catD = mkCatalog "Cat D (season)"
            let catE = mkCatalog "Cat E (episode)"

            api.addCatalogEntry catA { MediaSlug = "dup-2020"; MediaType = MediaType.Movie; Note = None } |> Async.RunSynchronously |> ignore
            api.addCatalogEntry catB { MediaSlug = "dup-2020"; MediaType = MediaType.Game; Note = None } |> Async.RunSynchronously |> ignore
            api.addCatalogEntry catC { MediaSlug = "dup-2020"; MediaType = MediaType.Book; Note = None } |> Async.RunSynchronously |> ignore
            api.addCatalogEntry catD { MediaSlug = "series-2015:s01"; MediaType = MediaType.Series; Note = None } |> Async.RunSynchronously |> ignore
            api.addCatalogEntry catE { MediaSlug = "series-2015:s01e02"; MediaType = MediaType.Series; Note = None } |> Async.RunSynchronously |> ignore

            let movieCatalogs = api.getCatalogsForMovie "dup-2020" |> Async.RunSynchronously
            Expect.equal (movieCatalogs |> List.map (fun c -> c.Slug)) [ catA ] "getCatalogsForMovie should resolve only the movie's catalog"

            let gameCatalogs = api.getCatalogsForGame "dup-2020" |> Async.RunSynchronously
            Expect.equal (gameCatalogs |> List.map (fun c -> c.Slug)) [ catB ] "getCatalogsForGame should resolve only the game's catalog, no longer aliasing the movie lookup"

            let bookCatalogs = api.getCatalogsForBook "dup-2020" |> Async.RunSynchronously
            Expect.equal (bookCatalogs |> List.map (fun c -> c.Slug)) [ catC ] "getCatalogsForBook should resolve only the book's catalog"

            let seriesCatalogs = api.getCatalogsForSeries "series-2015" |> Async.RunSynchronously
            let seriesCatalogSlugs = seriesCatalogs |> List.map (fun c -> c.Slug) |> Set.ofList
            Expect.equal seriesCatalogSlugs (Set.ofList [ catD; catE ]) "getCatalogsForSeries should still resolve both the season and the episode child entries"
    ]
