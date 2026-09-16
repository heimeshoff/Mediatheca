module Mediatheca.Tests.NotesProjectionTests

open Expecto
open Microsoft.Data.Sqlite
open Mediatheca.Server
open Mediatheca.Shared

/// curation-h98ve (ADR-0080): `NotesProjection`'s schema and `handleEvent`
/// — mirrors `BookProjectionTests.fs`'s "append event, run projection,
/// assert rows" shape.

let private createConnection () =
    let conn = new SqliteConnection("Data Source=:memory:")
    conn.Open()
    EventStore.initialize conn
    NotesProjection.handler.Init conn
    conn

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

let private appendNotesEvent (conn: SqliteConnection) (mediaType: MediaType) (slug: string) (event: Notes.NotesEvent) =
    let streamId = Notes.streamId mediaType slug
    let position = EventStore.getStreamPosition conn streamId
    EventStore.appendToStream conn streamId position [ Notes.Serialization.toEventData event ] |> ignore
    Projection.runProjection conn NotesProjection.handler

[<Tests>]
let notesProjectionTests =
    testList "NotesProjection" [

        testCase "Notes_saved projects into notes_blocks, ordered by position" <| fun _ ->
            use conn = createConnection ()
            let blocks = [ mkBlock "b1" 0; mkBlock "b2" 1 ]
            appendNotesEvent conn Game "elden-ring-2022" (Notes.Notes_saved blocks)

            let loaded = NotesProjection.getForOwner conn Game "elden-ring-2022"
            Expect.equal (loaded |> List.map (fun b -> b.Id)) [ "b1"; "b2" ] "both blocks come back in position order"

        testCase "rebuilding from several snapshots yields exactly the LATEST snapshot's rows" <| fun _ ->
            use conn = createConnection ()
            appendNotesEvent conn Game "elden-ring-2022" (Notes.Notes_saved [ mkBlock "old1" 0; mkBlock "old2" 1 ])
            appendNotesEvent conn Game "elden-ring-2022" (Notes.Notes_saved [ mkBlock "mid1" 0 ])
            appendNotesEvent conn Game "elden-ring-2022" (Notes.Notes_saved [ mkBlock "new1" 0; mkBlock "new2" 1; mkBlock "new3" 2 ])

            let loaded = NotesProjection.getForOwner conn Game "elden-ring-2022"
            Expect.equal (loaded |> List.map (fun b -> b.Id)) [ "new1"; "new2"; "new3" ]
                "only the latest snapshot's blocks survive a full replay — none of the superseded ones"

        testCase "documents are scoped per (media type, slug) — same slug, different media type, stay separate" <| fun _ ->
            use conn = createConnection ()
            appendNotesEvent conn Game "same-slug-2020" (Notes.Notes_saved [ mkBlock "g1" 0 ])
            appendNotesEvent conn Movie "same-slug-2020" (Notes.Notes_saved [ mkBlock "m1" 0 ])

            Expect.equal (NotesProjection.getForOwner conn Game "same-slug-2020" |> List.map (fun b -> b.Id)) [ "g1" ] "game keeps its own notes"
            Expect.equal (NotesProjection.getForOwner conn Movie "same-slug-2020" |> List.map (fun b -> b.Id)) [ "m1" ] "movie keeps its own notes"

        testCase "round-trips block fields (tree links, checked/collapsed, language, url/imageRef, width)" <| fun _ ->
            use conn = createConnection ()
            let blocks = [
                { mkBlock "todo1" 0 with BlockType = JournalBlockTypes.todo; Checked = true }
                { mkBlock "toggle1" 1 with BlockType = JournalBlockTypes.toggle; Collapsed = true }
                { mkBlock "code1" 0 with ParentId = Some "toggle1"; BlockType = JournalBlockTypes.code; Language = Some "fsharp" }
                { mkBlock "link1" 2 with BlockType = JournalBlockTypes.link; Url = Some "https://example.com" }
                { mkBlock "col1" 0 with ParentId = Some "list1"; BlockType = JournalBlockTypes.column; Width = 0.3 }
            ]
            appendNotesEvent conn Book "project-hail-mary-2021" (Notes.Notes_saved blocks)

            let loaded = NotesProjection.getForOwner conn Book "project-hail-mary-2021"
            let byId id = loaded |> List.find (fun b -> b.Id = id)
            Expect.isTrue (byId "todo1").Checked "checked survives"
            Expect.isTrue (byId "toggle1").Collapsed "collapsed survives"
            Expect.equal (byId "code1").ParentId (Some "toggle1") "parent link survives"
            Expect.equal (byId "code1").Language (Some "fsharp") "language survives"
            Expect.equal (byId "link1").Url (Some "https://example.com") "url survives"
            Expect.equal (byId "col1").Width 0.3 "width survives as a float"
    ]
