module Mediatheca.Tests.NotesRemovalCleanupTests

/// curation-h4k2p (ADR-0080 SS5/SS6, ADR-0043, ADR-0025/0045): removing a
/// movie/series/game/book clears its Notes document via an ordinary
/// `Notes_saved []` event (never an imperative `notes_blocks` DELETE) and
/// deletes its `content/`-prefixed uploaded images -- the ADR-0080
/// successor to the deleted `GameJournal.deleteForGame`
/// (`tests/Server.Tests/GameJournalTests.fs`, deleted by curation-j4qqt;
/// recoverable via `git show 59c86f4^:tests/Server.Tests/GameJournalTests.fs`).
/// Exercised here through `Api.create`'s `removeGame`, mirroring the old
/// test's "removes the game's blocks and its uploaded content images,
/// leaving other games alone" case, the same way that test exercised
/// `GameJournal.deleteForGame` directly -- plus the no-Notes no-op case.

open System
open System.IO
open System.Net.Http
open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

let private bootstrap (conn: SqliteConnection) =
    EventStore.initialize conn
    NotesProjection.handler.Init conn
    GameProjection.handler.Init conn
    CatalogProjection.handler.Init conn

let private allProjectionHandlers =
    [ GameProjection.handler; CatalogProjection.handler; NotesProjection.handler ]

let private createApi (factory: unit -> SqliteConnection) (imageBasePath: string) : IMediathecaApi =
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
        (fun () -> async { return Error "not wired in tests" })
        LocalCopyRemoval.defaultMountRoots
        imageBasePath
        allProjectionHandlers

/// A real temp image directory (AudibleApiTests.fs's `withTempImageDir`
/// pattern) -- deliberately not the `noImagesDir` stub
/// `CatalogProjectionTests.fs` uses, because this test needs real files on
/// disk for `ImageStore.deleteImage` to actually delete.
let private withTempImageDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), sprintf "mediatheca-notes-removal-test-images-%s" (Guid.NewGuid().ToString("N")))
    Directory.CreateDirectory(dir) |> ignore
    try f dir
    finally (try Directory.Delete(dir, true) with _ -> ())

let private mkBlock (id: string) (position: int) : JournalBlockDto = {
    Id = id
    ParentId = None
    BlockType = JournalBlockTypes.text
    Content = sprintf "content-%s" id
    Checked = false
    Collapsed = false
    Language = None
    Url = None
    ImageRef = None
    Caption = None
    Position = position
    Width = 1.0
}

let private sampleGame (name: string) (year: int) : AddGameRequest = {
    Name = name
    Year = year
    Genres = []
    Description = ""
    CoverRef = None
    BackdropRef = None
    RawgId = None
    RawgRating = None
    SkipDuplicateCheck = false
}

let private addGame (api: IMediathecaApi) (name: string) (year: int) : string =
    match api.addGame (sampleGame name year) |> Async.RunSynchronously with
    | Ok (Created slug) -> slug
    | other -> failtestf "Expected the game to be created; got %A" other

[<Tests>]
let notesRemovalCleanupTests =
    testList "IMediathecaApi.removeGame clears Notes and deletes content images (curation-h4k2p)" [

        testCase "removes the game's Notes content and its uploaded content images, leaving other games' Notes alone" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory imageBasePath
                Directory.CreateDirectory(Path.Combine(imageBasePath, "content")) |> ignore
                Directory.CreateDirectory(Path.Combine(imageBasePath, "posters")) |> ignore

                let doomedSlug = addGame api "Doomed" 2020
                let keptSlug = addGame api "Kept" 2021

                let doomedImage = "content/doomed.png"
                let keptImage = "content/kept.png"
                let unrelatedPoster = "posters/unrelated.jpg"
                File.WriteAllBytes(Path.Combine(imageBasePath, doomedImage), [| 1uy |])
                File.WriteAllBytes(Path.Combine(imageBasePath, keptImage), [| 2uy |])
                File.WriteAllBytes(Path.Combine(imageBasePath, unrelatedPoster), [| 3uy |])

                api.saveNotes Game doomedSlug [
                    mkBlock "d1" 0
                    { mkBlock "d2" 1 with BlockType = JournalBlockTypes.image; ImageRef = Some doomedImage }
                ] |> Async.RunSynchronously |> ignore
                api.saveNotes Game keptSlug [
                    { mkBlock "k1" 0 with BlockType = JournalBlockTypes.image; ImageRef = Some keptImage }
                ] |> Async.RunSynchronously |> ignore

                let sidDoomed = Notes.streamId Game doomedSlug
                let eventsBefore = EventStore.readStream db.Connection sidDoomed
                Expect.equal (List.length eventsBefore) 1 "one Notes_saved snapshot before removal"

                let removeResult = api.removeGame doomedSlug |> Async.RunSynchronously
                Expect.isTrue (Result.isOk removeResult) "removeGame should succeed"

                Expect.isEmpty (NotesProjection.getForOwner db.Connection Game doomedSlug) "doomed game's Notes projection is cleared"
                Expect.isFalse (File.Exists(Path.Combine(imageBasePath, doomedImage))) "doomed game's content image deleted"

                // AC: the removal appends a `Notes_saved []` event -- never an
                // imperative row delete -- and the prior snapshot remains in
                // the event log.
                let eventsAfter = EventStore.readStream db.Connection sidDoomed
                Expect.equal (List.length eventsAfter) 2 "a clearing Notes_saved [] event is appended, not replacing history"
                Expect.equal eventsAfter.[0].EventType "Notes_saved" "original snapshot event type preserved"
                Expect.equal eventsAfter.[0].Data eventsBefore.[0].Data "original snapshot data preserved verbatim"
                Expect.equal eventsAfter.[1].EventType "Notes_saved" "clearing event's type"
                Expect.stringContains eventsAfter.[1].Data "\"blocks\":[]" "clearing event carries an empty block list"

                Expect.equal (NotesProjection.getForOwner db.Connection Game keptSlug |> List.map (fun b -> b.Id)) [ "k1" ] "other game's Notes content untouched"
                Expect.isTrue (File.Exists(Path.Combine(imageBasePath, keptImage))) "other game's content image untouched"
                Expect.isTrue (File.Exists(Path.Combine(imageBasePath, unrelatedPoster))) "posters are not this cleanup's business")

        testCase "removing a game with no Notes content is a clean no-op: no error, no Notes_saved event, no files deleted" <| fun _ ->
            withTempImageDir (fun imageBasePath ->
                use db = TestDb.withTempDbFactory bootstrap
                let api = createApi db.Factory imageBasePath
                Directory.CreateDirectory(Path.Combine(imageBasePath, "posters")) |> ignore

                let slug = addGame api "Untouched" 2019
                let unrelatedPoster = "posters/unrelated.jpg"
                File.WriteAllBytes(Path.Combine(imageBasePath, unrelatedPoster), [| 9uy |])

                let removeResult = api.removeGame slug |> Async.RunSynchronously
                Expect.isTrue (Result.isOk removeResult) "removeGame should succeed even with no Notes content"

                let sid = Notes.streamId Game slug
                Expect.equal (EventStore.getStreamPosition db.Connection sid) -1L "no Notes stream was ever created"
                Expect.isEmpty (NotesProjection.getForOwner db.Connection Game slug) "no Notes projection rows"
                Expect.isTrue (File.Exists(Path.Combine(imageBasePath, unrelatedPoster))) "unrelated poster untouched")
    ]
    |> testSequenced
