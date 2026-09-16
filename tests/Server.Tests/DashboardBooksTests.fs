module Mediatheca.Tests.DashboardBooksTests

// intelligence-dnv2y: the dashboard's Books tab and the All tab's "Reading"
// rail. Mirrors `DashboardLingerTests.fs`'s shape (intelligence-b1nz5) — a
// finished book lingers on the All tab for 7 days, marked `Finished`, while
// the Books tab's own Currently Reading card stays strict. Covers:
//   - `BookProjection.getAllTabCurrentlyReading` (All tab; lingers)
//   - `BookProjection.getCurrentlyReading` / `getRecentlyFinished` /
//     `getRecentlyAddedUnfinished` (Books tab; strict rails)
//   - `Api.getDashboardCardItems AllCurrentlyReading` parity with the
//     collapsed `getDashboardAllTab` payload
//   - `DashboardActivityDay.Reading`'s distinct-book-per-day count

open System
open System.Net.Http
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private isoDaysAgo (days: int) : string =
    DateTime.UtcNow.AddDays(-(float days)).ToString("yyyy-MM-dd")

let private apiBootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    SettingsStore.initialize conn
    CastStore.initialize conn
    JellyfinStore.initialize conn
    ContentBlockProjection.handler.Init conn
    FriendProjection.handler.Init conn
    MovieProjection.handler.Init conn
    SeriesProjection.handler.Init conn
    GameProjection.handler.Init conn
    BookProjection.handler.Init conn
    GameJournal.initialize conn
    PlaySessionProjection.handler.Init conn
    MetadataCache.initialize conn

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
        "test-fixtures-do-not-exist/images"
        [ ContentBlockProjection.handler; FriendProjection.handler; MovieProjection.handler
          SeriesProjection.handler; GameProjection.handler; BookProjection.handler; PlaySessionProjection.handler ]

let private bookData (title: string) (asin: string) : Books.BookAddedData = {
    Title = title
    Authors = [ "Some Author" ]
    Year = Some 2020
    CoverRef = None
    Subjects = []
    Format = Audiobook
    ExternalIds = [ AudibleAsin asin ]
}

let private appendBookEvent (conn: SqliteConnection) (slug: string) (event: Books.BookEvent) =
    let streamId = Books.streamId slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Books.Serialization.toEventData event ] |> ignore
    Projection.runProjection conn BookProjection.handler

let private addBook (conn: SqliteConnection) (slug: string) (title: string) (asin: string) =
    appendBookEvent conn slug (Books.Book_added_to_library (bookData title asin))

let private setInFocusWithProgress (conn: SqliteConnection) (slug: string) (percent: int) (observedOn: string) =
    appendBookEvent conn slug (Books.Reading_progress_observed
        { Percent = percent; Position = None; Source = Audible; ObservedOn = observedOn; Finished = false })
    appendBookEvent conn slug (Books.Book_status_changed (BookStatus.InFocus, Some observedOn))

let private finishOn (conn: SqliteConnection) (slug: string) (effectiveOn: string) =
    appendBookEvent conn slug (Books.Book_status_changed (BookStatus.Finished, Some effectiveOn))

/// Seeds the acceptance criterion's four books: one In Focus at 40%, one
/// finished 3 days ago, one finished 10 days ago, one Backlog.
let private seedFourBooks (conn: SqliteConnection) =
    addBook conn "in-focus-2020" "In Focus Book" "B0INFOCUS01"
    setInFocusWithProgress conn "in-focus-2020" 40 (isoDaysAgo 1)

    addBook conn "finished-recent-2020" "Finished Recently" "B0FINISHED1"
    finishOn conn "finished-recent-2020" (isoDaysAgo 3)

    addBook conn "finished-old-2020" "Finished A While Ago" "B0FINISHED2"
    finishOn conn "finished-old-2020" (isoDaysAgo 10)

    addBook conn "backlog-2020" "Still On The Shelf" "B0BACKLOG01"

[<Tests>]
let tests =
    testList "Dashboard Books tab and All-tab Reading rail (intelligence-dnv2y)" [

        testCase "getDashboardAllTab.CurrentlyReading has the In Focus book first and the 3-day finished book lingering" <| fun _ ->
            use db = TestDb.withTempDbFactory apiBootstrap
            let api = createApi db.Factory
            seedFourBooks db.Connection

            let allTab = api.getDashboardAllTab () |> Async.RunSynchronously
            Expect.equal (List.length allTab.CurrentlyReading) 2 "the In Focus book and the 3-day-lingering finished book, nothing else"
            Expect.equal allTab.CurrentlyReading.[0].Slug "in-focus-2020" "the In Focus book comes first"
            Expect.isFalse allTab.CurrentlyReading.[0].Finished "the In Focus book is not marked finished"
            Expect.equal allTab.CurrentlyReading.[0].ProgressPercent 40 "its progress percent carries through"
            Expect.equal allTab.CurrentlyReading.[1].Slug "finished-recent-2020" "the 3-day-lingering finished book comes second"
            Expect.isTrue allTab.CurrentlyReading.[1].Finished "it is marked finished"

        testCase "getDashboardBooksTab.CurrentlyReading stays strict (In Focus only, no linger)" <| fun _ ->
            use db = TestDb.withTempDbFactory apiBootstrap
            let api = createApi db.Factory
            seedFourBooks db.Connection

            let booksTab = api.getDashboardBooksTab () |> Async.RunSynchronously
            Expect.equal (List.length booksTab.CurrentlyReading) 1 "only the In Focus book — no lingering finished book"
            Expect.equal booksTab.CurrentlyReading.[0].Slug "in-focus-2020" "the strict In Focus book"

        testCase "getDashboardBooksTab.RecentlyFinished includes both finished books within the 90-day window" <| fun _ ->
            use db = TestDb.withTempDbFactory apiBootstrap
            let api = createApi db.Factory
            seedFourBooks db.Connection

            let booksTab = api.getDashboardBooksTab () |> Async.RunSynchronously
            Expect.equal (List.length booksTab.RecentlyFinished) 2 "both the 3-day and 10-day finished books are within 90 days"
            let slugs = booksTab.RecentlyFinished |> List.map (fun b -> b.Slug) |> Set.ofList
            Expect.isTrue (slugs.Contains "finished-recent-2020") "the recently-finished book is present"
            Expect.isTrue (slugs.Contains "finished-old-2020") "the 10-day-old finished book is present too"

        testCase "getDashboardCardItems AllCurrentlyReading returns every qualifying item, matching the collapsed All-tab payload" <| fun _ ->
            use db = TestDb.withTempDbFactory apiBootstrap
            let api = createApi db.Factory
            seedFourBooks db.Connection

            let allTab = api.getDashboardAllTab () |> Async.RunSynchronously
            match api.getDashboardCardItems AllCurrentlyReading |> Async.RunSynchronously with
            | BookReadingItems items ->
                Expect.equal items allTab.CurrentlyReading "the expanded card's uncapped query matches the collapsed All-tab payload exactly"
            | other -> failtestf "Expected BookReadingItems, got %A" other

        testCase "DashboardActivityDay.Reading counts distinct books per day, not observation rows" <| fun _ ->
            use db = TestDb.withTempDbFactory apiBootstrap
            let api = createApi db.Factory
            let day = isoDaysAgo 2
            addBook db.Connection "reading-day-2020" "Reading Day Book" "B0READINGD1"
            appendBookEvent db.Connection "reading-day-2020" (Books.Reading_progress_observed
                { Percent = 20; Position = None; Source = Audible; ObservedOn = day; Finished = false })
            appendBookEvent db.Connection "reading-day-2020" (Books.Reading_progress_observed
                { Percent = 35; Position = None; Source = ProgressSource.Manual; ObservedOn = day; Finished = false })

            let allTab = api.getDashboardAllTab () |> Async.RunSynchronously
            let dayEntry = allTab.ActivityDays |> List.tryFind (fun d -> d.Date = day)
            match dayEntry with
            | Some entry -> Expect.equal entry.Reading 1 "two observations of one book on the same day count as one"
            | None -> failtest "Expected an activity day entry for the seeded observation day"
    ]
