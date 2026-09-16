module Mediatheca.Tests.BooksApiTests

/// books-y9kxy: `IMediathecaApi.addBook`'s duplicate detection and
/// `setBookProgress`'s Manual/page-to-percent derivation — the two
/// acceptance criteria that need a real `Api.create` round trip rather than
/// a pure `Books.decide` unit test.

open System.Net.Http
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private noImagesDir = "test-fixtures-do-not-exist/images"

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    ContentBlockProjection.handler.Init conn
    BookProjection.handler.Init conn
    MetadataCache.initialize conn

let private allProjectionHandlers =
    [ ContentBlockProjection.handler; BookProjection.handler ]

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
        LocalCopyRemoval.defaultMountRoots
        noImagesDir
        allProjectionHandlers

let private sampleRequest : AddBookRequest = {
    Title = "Project Hail Mary"
    Authors = [ "Andy Weir" ]
    Year = Some 2021
    CoverUrl = None
    Subjects = []
    Format = Audiobook
    ExternalIds = [ AudibleAsin "B08GB43BXN" ]
    SkipDuplicateCheck = false
}

[<Tests>]
let booksApiTests =
    testList "IMediathecaApi (books-y9kxy)" [

        testCase "addBook twice with the same ASIN returns Duplicate_found" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory

            let first = api.addBook sampleRequest |> Async.RunSynchronously
            match first with
            | Ok (AddBookOutcome.Book_added _) -> ()
            | other -> failtestf "Expected the first call to create; got %A" other

            let second = api.addBook sampleRequest |> Async.RunSynchronously
            match second with
            | Ok (AddBookOutcome.Duplicate_found (_, existingTitle)) ->
                Expect.equal existingTitle "Project Hail Mary" "Duplicate_found should name the existing book"
            | other -> failtestf "Expected Duplicate_found; got %A" other

        testCase "addBook with SkipDuplicateCheck = true creates a second slug" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory

            let first = api.addBook sampleRequest |> Async.RunSynchronously
            let firstSlug =
                match first with
                | Ok (AddBookOutcome.Book_added slug) -> slug
                | other -> failtestf "Expected the first call to create; got %A" other

            let second = api.addBook { sampleRequest with SkipDuplicateCheck = true } |> Async.RunSynchronously
            match second with
            | Ok (AddBookOutcome.Book_added secondSlug) ->
                Expect.notEqual secondSlug firstSlug "SkipDuplicateCheck should create a second, distinctly-slugged book"
            | other -> failtestf "Expected a second Book_added; got %A" other

        testCase "setBookProgress derives percent 40 from Page 120 of TotalPages 300, source Manual" <| fun _ ->
            use db = TestDb.withTempDbFactory bootstrap
            let api = createApi db.Factory

            let created = api.addBook sampleRequest |> Async.RunSynchronously
            let slug =
                match created with
                | Ok (AddBookOutcome.Book_added slug) -> slug
                | other -> failtestf "Expected Book_added; got %A" other

            let request: SetReadingProgressRequest = {
                Slug = slug
                Percent = None
                Page = Some 120
                TotalPages = Some 300
                ObservedOn = Some "2026-01-01"
            }
            let result = api.setBookProgress request |> Async.RunSynchronously
            match result with
            | Ok () -> ()
            | Error e -> failtestf "Expected success, got Error %s" e

            let history = BookProjection.getProgressHistory db.Connection slug
            Expect.equal (List.length history) 1 "One observation should be recorded"
            Expect.equal history.[0].Percent 40 "120 of 300 pages should derive 40 percent"
            Expect.equal history.[0].Source ProgressSource.Manual "setBookProgress always records Manual"
            Expect.equal history.[0].Position (Some (Page (120, Some 300))) "Position should carry the page/total"
    ]
