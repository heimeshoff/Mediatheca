module Mediatheca.Client.Components.JournalEditor

// Notion-style block editor for game journals.
//
// The document is a flat list of JournalBlockDto forming a tree via ParentId
// (see Shared.fs). This mirrors Notion's editor model:
//   - every paragraph/image/etc. is a block with a hover drag handle
//   - Enter splits/creates blocks, Backspace at start merges/converts
//   - "/" opens a slash menu; markdown prefixes ("# ", "- ", "1. ", "[] ",
//     "> ", "```") autoformat
//   - blocks can be dragged above/below each other, or onto the left/right
//     edge of another block to form resizable side-by-side columns
//     (columnList → column containers, like Notion's column_list/column)
//   - toggle blocks collapse/expand nested child blocks
//
// The editor is self-contained: it loads, edits and debounce-saves the whole
// document through its own API proxy (plain storage, not event-sourced).

open System
open System.Collections.Generic
open Fable.Core.JsInterop
open Feliz
open Fable.Remoting.Client
open Mediatheca.Shared

module B = Mediatheca.Shared.JournalBlockTypes

let private api: IMediathecaApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<IMediathecaApi>

type private Side = Above | Below | LeftSide | RightSide

type private SaveState =
    | SaveIdle
    | SaveDirty
    | SaveDone
    | SaveFailed of string

type private Node = {
    Block: JournalBlockDto
    Children: Node list
}

// ── Pure document model ─────────────────────────────────────────────────────

module private Doc =

    let newId () = Guid.NewGuid().ToString("N")

    let mkBlock (blockType: string) : JournalBlockDto = {
        Id = newId ()
        ParentId = None
        BlockType = blockType
        Content = ""
        Checked = false
        Collapsed = false
        Language = None
        Url = None
        ImageRef = None
        Caption = None
        Position = 0
        Width = 1.0
    }

    /// Block types edited through a plain textarea
    let isTextLike (t: string) =
        t = B.text || t = B.heading1 || t = B.heading2 || t = B.heading3 || t = B.heading4
        || t = B.bullet || t = B.numbered || t = B.todo || t = B.toggle
        || t = B.quote || t = B.callout

    let isListType (t: string) =
        t = B.bullet || t = B.numbered || t = B.todo

    let toTree (blocks: JournalBlockDto list) : Node list =
        let byParent = blocks |> List.groupBy (fun b -> b.ParentId) |> Map.ofList
        let rec build (pid: string option) =
            match Map.tryFind pid byParent with
            | None -> []
            | Some bs ->
                bs
                |> List.sortBy (fun b -> b.Position)
                |> List.map (fun b -> { Block = b; Children = build (Some b.Id) })
        build None

    let flatten (nodes: Node list) : JournalBlockDto list =
        let acc = ResizeArray()
        let rec go (pid: string option) (nodes: Node list) =
            nodes |> List.iteri (fun i n ->
                acc.Add { n.Block with ParentId = pid; Position = i }
                go (Some n.Block.Id) n.Children)
        go None nodes
        List.ofSeq acc

    let rec findNode (id: string) (nodes: Node list) : Node option =
        nodes |> List.tryPick (fun n ->
            if n.Block.Id = id then Some n
            else findNode id n.Children)

    let rec removeNode (id: string) (nodes: Node list) : Node list =
        nodes
        |> List.filter (fun n -> n.Block.Id <> id)
        |> List.map (fun n -> { n with Children = removeNode id n.Children })

    let private normalizeWidths (cols: Node list) : Node list =
        let total = cols |> List.sumBy (fun c -> c.Block.Width)
        let total = if total <= 0.0 then float cols.Length else total
        cols |> List.map (fun c -> { c with Block = { c.Block with Width = c.Block.Width / total } })

    /// Enforce structural invariants: columns live only inside columnLists,
    /// empty columns disappear, single-column lists dissolve, widths sum to 1.
    let rec normalizeNodes (nodes: Node list) : Node list =
        nodes |> List.collect (fun n ->
            if n.Block.BlockType = B.columnList then
                let cols =
                    n.Children
                    |> List.filter (fun c -> c.Block.BlockType = B.column)
                    |> List.map (fun c -> { c with Children = normalizeNodes c.Children })
                    |> List.filter (fun c -> not c.Children.IsEmpty)
                match cols with
                | [] -> []
                | [ single ] -> single.Children
                | cols -> [ { n with Children = normalizeWidths cols } ]
            elif n.Block.BlockType = B.column then
                // stray column outside a columnList — splice its contents up
                normalizeNodes n.Children
            else
                [ { n with Children = normalizeNodes n.Children } ])

    let normalize (blocks: JournalBlockDto list) : JournalBlockDto list =
        // reattach orphans (broken ParentId) at root so nothing is ever lost
        let ids = blocks |> List.map (fun b -> b.Id) |> Set.ofList
        blocks
        |> List.map (fun b ->
            match b.ParentId with
            | Some p when not (ids.Contains p) -> { b with ParentId = None }
            | _ -> b)
        |> toTree
        |> normalizeNodes
        |> flatten

    let private insertAt (index: int) (item: 'a) (items: 'a list) : 'a list =
        let index = max 0 (min index items.Length)
        (items |> List.truncate index) @ [ item ] @ (items |> List.skip index)

    /// Insert `node` relative to the block `targetId`, searching the whole tree.
    /// LeftSide/RightSide either wraps the target in a new 50/50 columnList or,
    /// when the target already sits inside a column, adds a column beside it.
    let rec insertRelative (targetId: string) (side: Side) (node: Node) (nodes: Node list) : Node list =
        // A left/right drop beside a block that already lives in a column adds a
        // column to that columnList — checked at the level owning the columnList
        // so we never nest columnLists by accident.
        let columnListHit =
            (side = LeftSide || side = RightSide)
            && nodes |> List.exists (fun n ->
                n.Block.BlockType = B.columnList
                && n.Children |> List.exists (fun c -> c.Children |> List.exists (fun b -> b.Block.Id = targetId)))
        if columnListHit then
            nodes |> List.map (fun n ->
                let colIdx =
                    if n.Block.BlockType = B.columnList then
                        n.Children |> List.tryFindIndex (fun c -> c.Children |> List.exists (fun b -> b.Block.Id = targetId))
                    else None
                match colIdx with
                | Some i ->
                    let count = n.Children.Length
                    let newCol = { Block = { mkBlock B.column with Width = 1.0 / float (count + 1) }; Children = [ node ] }
                    let insertIdx = if side = LeftSide then i else i + 1
                    { n with Children = insertAt insertIdx newCol n.Children }
                | None -> n)
        elif nodes |> List.exists (fun n -> n.Block.Id = targetId) then
            match side with
            | Above | Below ->
                nodes |> List.collect (fun n ->
                    if n.Block.Id = targetId then
                        if side = Above then [ node; n ] else [ n; node ]
                    else [ n ])
            | LeftSide | RightSide ->
                nodes |> List.collect (fun n ->
                    if n.Block.Id = targetId then
                        let mkCol (content: Node) =
                            { Block = { mkBlock B.column with Width = 0.5 }; Children = [ content ] }
                        let pair =
                            if side = LeftSide then [ mkCol node; mkCol n ]
                            else [ mkCol n; mkCol node ]
                        [ { Block = mkBlock B.columnList; Children = pair } ]
                    else [ n ])
        else
            nodes |> List.map (fun n -> { n with Children = insertRelative targetId side node n.Children })

    let rec containsBlock (id: string) (node: Node) : bool =
        node.Block.Id = id || node.Children |> List.exists (containsBlock id)

    /// Move a block (with its subtree) to a new location.
    let moveBlock (draggedId: string) (targetId: string) (side: Side) (blocks: JournalBlockDto list) : JournalBlockDto list =
        if draggedId = targetId then blocks
        else
            let tree = toTree blocks
            match findNode draggedId tree with
            | None -> blocks
            | Some dragged ->
                if containsBlock targetId dragged then blocks
                else
                    let without = removeNode draggedId tree
                    if (findNode targetId without).IsNone then blocks
                    else
                        insertRelative targetId side dragged without
                        |> normalizeNodes
                        |> flatten

    /// Insert a fresh block relative to an existing one.
    let insertBlock (targetId: string) (side: Side) (newBlock: JournalBlockDto) (blocks: JournalBlockDto list) : JournalBlockDto list =
        blocks
        |> toTree
        |> insertRelative targetId side { Block = newBlock; Children = [] }
        |> normalizeNodes
        |> flatten

    /// Insert a fresh block as the first child of a parent (open toggles).
    let insertFirstChild (parentId: string) (newBlock: JournalBlockDto) (blocks: JournalBlockDto list) : JournalBlockDto list =
        let rec go (nodes: Node list) =
            nodes |> List.map (fun n ->
                if n.Block.Id = parentId then
                    { n with Children = { Block = newBlock; Children = [] } :: n.Children }
                else { n with Children = go n.Children })
        blocks |> toTree |> go |> normalizeNodes |> flatten

    let appendAtRoot (newBlock: JournalBlockDto) (blocks: JournalBlockDto list) : JournalBlockDto list =
        let tree = toTree blocks
        (tree @ [ { Block = newBlock; Children = [] } ]) |> normalizeNodes |> flatten

    let removeBlockAndChildren (id: string) (blocks: JournalBlockDto list) : JournalBlockDto list =
        blocks |> toTree |> removeNode id |> normalizeNodes |> flatten

    let updateBlock (id: string) (f: JournalBlockDto -> JournalBlockDto) (blocks: JournalBlockDto list) : JournalBlockDto list =
        blocks |> List.map (fun b -> if b.Id = id then f b else b)

    /// Keyboard-reachable blocks (textarea-editable), in visual order.
    let focusableIds (blocks: JournalBlockDto list) : string list =
        let rec walk (nodes: Node list) : string list =
            nodes |> List.collect (fun n ->
                let t = n.Block.BlockType
                if t = B.columnList || t = B.column then walk n.Children
                elif isTextLike t || t = B.code then
                    let children =
                        if t = B.toggle && n.Block.Collapsed then []
                        else walk n.Children
                    n.Block.Id :: children
                else [])
        walk (toTree blocks)

// ── Markdown-prefix autoformat ──────────────────────────────────────────────

let private markdownPrefixes = [
    "#", B.heading1
    "##", B.heading2
    "###", B.heading3
    "####", B.heading4
    "-", B.bullet
    "*", B.bullet
    "1.", B.numbered
    "[]", B.todo
    "[ ]", B.todo
    ">", B.quote
]

// ── Slash menu ──────────────────────────────────────────────────────────────

type private SlashItem = {
    Label: string
    Keywords: string
    BlockType: string
    Hint: string
}

let private slashItems = [
    { Label = "Text"; Keywords = "text paragraph plain"; BlockType = B.text; Hint = "T" }
    { Label = "Heading 1"; Keywords = "heading1 h1 title"; BlockType = B.heading1; Hint = "H1" }
    { Label = "Heading 2"; Keywords = "heading2 h2 subtitle"; BlockType = B.heading2; Hint = "H2" }
    { Label = "Heading 3"; Keywords = "heading3 h3"; BlockType = B.heading3; Hint = "H3" }
    { Label = "Heading 4"; Keywords = "heading4 h4"; BlockType = B.heading4; Hint = "H4" }
    { Label = "Bullet list"; Keywords = "bullet list unordered ul"; BlockType = B.bullet; Hint = "•" }
    { Label = "Numbered list"; Keywords = "numbered list ordered ol"; BlockType = B.numbered; Hint = "1." }
    { Label = "To-do list"; Keywords = "todo task checkbox check"; BlockType = B.todo; Hint = "[]" }
    { Label = "Toggle list"; Keywords = "toggle collapse expand"; BlockType = B.toggle; Hint = "▸" }
    { Label = "Quote"; Keywords = "quote blockquote citation"; BlockType = B.quote; Hint = "\"" }
    { Label = "Callout"; Keywords = "callout note info banner"; BlockType = B.callout; Hint = "!" }
    { Label = "Code"; Keywords = "code snippet codeblock"; BlockType = B.code; Hint = "</>" }
    { Label = "Link"; Keywords = "link url bookmark web"; BlockType = B.link; Hint = "@" }
    { Label = "Image"; Keywords = "image picture photo screenshot upload"; BlockType = B.image; Hint = "img" }
]

let private filterSlashItems (query: string) =
    let q = query.Trim().ToLowerInvariant()
    if q = "" then slashItems
    else
        slashItems
        |> List.filter (fun i ->
            i.Label.ToLowerInvariant().Contains(q) || i.Keywords.Contains(q))

let private turnIntoTypes = [
    B.text, "Text"
    B.heading1, "Heading 1"
    B.heading2, "Heading 2"
    B.heading3, "Heading 3"
    B.heading4, "Heading 4"
    B.bullet, "Bullet list"
    B.numbered, "Numbered list"
    B.todo, "To-do list"
    B.toggle, "Toggle list"
    B.quote, "Quote"
    B.callout, "Callout"
    B.code, "Code"
]

// ── Inline markdown links ([text](url)), rendered when a block is idle ─────

let private containsMarkdownLink (content: string) =
    content.Contains("](")

let private renderInlineContent (content: string) =
    let rec parse (s: string) (acc: ReactElement list) (idx: int) =
        let linkStart = s.IndexOf("[")
        if linkStart = -1 then
            if s.Length > 0 then acc @ [ Html.span [ prop.key $"t{idx}"; prop.text s ] ] else acc
        else
            let closeBracket = s.IndexOf("](", linkStart + 1)
            if closeBracket = -1 then acc @ [ Html.span [ prop.key $"t{idx}"; prop.text s ] ]
            else
                let closeParen = s.IndexOf(")", closeBracket + 2)
                if closeParen = -1 then acc @ [ Html.span [ prop.key $"t{idx}"; prop.text s ] ]
                else
                    let displayText = s.[linkStart + 1 .. closeBracket - 1]
                    let url = s.[closeBracket + 2 .. closeParen - 1]
                    let before = if linkStart > 0 then s.[0 .. linkStart - 1] else ""
                    let rest = if closeParen + 1 < s.Length then s.[closeParen + 1 ..] else ""
                    let acc =
                        if before.Length > 0 then acc @ [ Html.span [ prop.key $"t{idx}"; prop.text before ] ]
                        else acc
                    let acc = acc @ [
                        Html.a [
                            prop.key $"l{idx}"
                            prop.href url
                            prop.target "_blank"
                            prop.rel "noopener noreferrer"
                            prop.className "link link-primary"
                            prop.onClick (fun e -> e.stopPropagation ())
                            prop.text displayText
                        ] ]
                    parse rest acc (idx + 1)
    React.fragment (parse content [] 0)

let private isUrl (text: string) =
    Text.RegularExpressions.Regex.IsMatch(text.Trim(), @"^https?://[^\s]+$")

// ── Component ───────────────────────────────────────────────────────────────

[<ReactComponent>]
let view (slug: string) =
    let blocks, setBlocks = React.useState<JournalBlockDto list>([])
    let loaded, setLoaded = React.useState(false)
    let saveState, setSaveState = React.useState(SaveIdle)
    let focusedId, setFocusedId = React.useState<string option>(None)
    let draggedId, setDraggedId = React.useState<string option>(None)
    let dropTarget, setDropTarget = React.useState<(string * Side) option>(None)
    let menuBlockId, setMenuBlockId = React.useState<string option>(None)
    let slashIndex, setSlashIndex = React.useState(0)
    let slashDismissedFor, setSlashDismissedFor = React.useState<string option>(None)
    let editingLinkId, setEditingLinkId = React.useState<string option>(None)

    let blocksRef = React.useRef<JournalBlockDto list>([])
    let dirtyRef = React.useRef(false)
    let saveTimerRef = React.useRef<float option>(None)
    let focusRequestRef = React.useRef<(string * int) option>(None)
    let textareaRefs = React.useRef(Dictionary<string, Browser.Types.HTMLTextAreaElement>())
    let pendingImageRef = React.useRef<string option>(None)
    let fileInputRef = React.useRef<Browser.Types.HTMLInputElement option>(None)

    // ── Persistence ─────────────────────────────────────────────────────────

    let doSave () =
        dirtyRef.current <- false
        let toSave = blocksRef.current
        async {
            match! api.saveGameJournal slug toSave with
            | Ok () -> setSaveState SaveDone
            | Error e -> setSaveState (SaveFailed e)
        } |> Async.StartImmediate

    let scheduleSave () =
        dirtyRef.current <- true
        setSaveState SaveDirty
        match saveTimerRef.current with
        | Some t -> emitJsExpr t "clearTimeout($0)"
        | None -> ()
        let t: float = emitJsExpr (fun () -> doSave ()) "setTimeout($0, 800)"
        saveTimerRef.current <- Some t

    /// Apply a document transformation; structural ops re-normalize the tree.
    let mutate (structural: bool) (f: JournalBlockDto list -> JournalBlockDto list) =
        let next = f blocksRef.current
        let next = if structural then Doc.normalize next else next
        blocksRef.current <- next
        setBlocks next
        scheduleSave ()

    let requestFocus (id: string) (caret: int) =
        focusRequestRef.current <- Some (id, caret)
        setFocusedId (Some id)

    // Load on mount / slug change; flush pending edits on unmount
    React.useEffect((fun () ->
        setLoaded false
        setBlocks []
        blocksRef.current <- []
        setFocusedId None
        setSaveState SaveIdle
        async {
            let! bs = api.getGameJournal slug
            blocksRef.current <- bs
            setBlocks bs
            setLoaded true
        } |> Async.StartImmediate
        React.createDisposable (fun () ->
            match saveTimerRef.current with
            | Some t -> emitJsExpr t "clearTimeout($0)"
            | None -> ()
            if dirtyRef.current then doSave ())
    ), [| box slug |])

    // After every render: honour pending focus requests, re-fit textarea heights
    React.useEffect(fun () ->
        for KeyValue (_, ta) in textareaRefs.current do
            if not (isNull (box ta)) then
                emitJsExpr ta "$0.style.height = 'auto'"
                emitJsExpr ta "$0.style.height = $0.scrollHeight + 'px'"
        match focusRequestRef.current with
        | Some (id, caret) ->
            match textareaRefs.current.TryGetValue id with
            | true, ta when not (isNull (box ta)) ->
                ta.focus ()
                emitJsExpr (ta, caret) "$0.setSelectionRange($1, $1)"
                focusRequestRef.current <- None
            | _ -> ()
        | None -> ())

    // ── Images ──────────────────────────────────────────────────────────────

    let readFileAsBytes (file: Browser.Types.File) (callback: byte array -> string -> unit) =
        let reader = Browser.Dom.FileReader.Create()
        reader.onload <- fun _ ->
            let bytes: byte array = emitJsExpr reader.result "new Uint8Array($0)"
            callback bytes file.name
        reader.readAsArrayBuffer (file)

    let uploadImageInto (blockId: string) (file: Browser.Types.File) =
        let fileType: string = emitJsExpr file "$0.type"
        if fileType.StartsWith("image/") then
            readFileAsBytes file (fun bytes name ->
                async {
                    match! api.uploadContentImage bytes name with
                    | Ok imageRef ->
                        mutate false (Doc.updateBlock blockId (fun b -> { b with ImageRef = Some imageRef }))
                    | Error e ->
                        mutate true (Doc.removeBlockAndChildren blockId)
                        setSaveState (SaveFailed e)
                } |> Async.StartImmediate)

    let openImagePicker (blockId: string) =
        pendingImageRef.current <- Some blockId
        match fileInputRef.current with
        | Some input -> input.click ()
        | None -> ()

    // ── Block-level edit operations ─────────────────────────────────────────

    let convertBlockType (blockId: string) (newType: string) (newContent: string option) =
        mutate false (Doc.updateBlock blockId (fun b ->
            { b with
                BlockType = newType
                Content = newContent |> Option.defaultValue b.Content
                Collapsed = false }))
        if Doc.isTextLike newType || newType = B.code then
            requestFocus blockId 0
        elif newType = B.image then
            setFocusedId None
            openImagePicker blockId
        elif newType = B.link then
            setFocusedId None
            setEditingLinkId (Some blockId)

    let applySlashItem (block: JournalBlockDto) (item: SlashItem) =
        setSlashDismissedFor None
        setSlashIndex 0
        convertBlockType block.Id item.BlockType (Some "")

    let addBlockBelow (targetId: string) =
        let nb = Doc.mkBlock B.text
        mutate true (Doc.insertBlock targetId Below nb)
        requestFocus nb.Id 0

    let addBlockAtEnd () =
        let lastRoot =
            blocksRef.current
            |> List.filter (fun b -> b.ParentId = None)
            |> List.sortBy (fun b -> b.Position)
            |> List.tryLast
        match lastRoot with
        | Some b when b.BlockType = B.text && b.Content = "" ->
            requestFocus b.Id 0
        | _ ->
            let nb = Doc.mkBlock B.text
            mutate true (Doc.appendAtRoot nb)
            requestFocus nb.Id 0

    let deleteBlock (blockId: string) =
        let ids = Doc.focusableIds blocksRef.current
        let prev =
            ids
            |> List.tryFindIndex (fun i -> i = blockId)
            |> Option.bind (fun i -> if i > 0 then Some ids.[i - 1] else None)
        mutate true (Doc.removeBlockAndChildren blockId)
        match prev with
        | Some p ->
            let len =
                blocksRef.current
                |> List.tryFind (fun b -> b.Id = p)
                |> Option.map (fun b -> b.Content.Length)
                |> Option.defaultValue 0
            requestFocus p len
        | None -> setFocusedId None

    // ── Keyboard behaviour ──────────────────────────────────────────────────

    let focusNeighbor (blockId: string) (delta: int) (caret: int) =
        let ids = Doc.focusableIds blocksRef.current
        ids
        |> List.tryFindIndex (fun i -> i = blockId)
        |> Option.iter (fun i ->
            let j = i + delta
            if j >= 0 && j < ids.Length then
                let targetId = ids.[j]
                let len =
                    blocksRef.current
                    |> List.tryFind (fun b -> b.Id = targetId)
                    |> Option.map (fun b -> b.Content.Length)
                    |> Option.defaultValue 0
                requestFocus targetId (min caret len))

    let handleEnterSplit (block: JournalBlockDto) (content: string) (selStart: int) (selEnd: int) =
        if Doc.isListType block.BlockType && content = "" then
            // empty list item → Notion converts it back to text
            convertBlockType block.Id B.text None
        else
            let before = content.Substring(0, selStart)
            let after = content.Substring(min selEnd content.Length)
            let newType = if Doc.isListType block.BlockType then block.BlockType else B.text
            let nb = { Doc.mkBlock newType with Content = after }
            let hasChildren = blocksRef.current |> List.exists (fun b -> b.ParentId = Some block.Id)
            mutate true (fun bs ->
                let bs = Doc.updateBlock block.Id (fun b -> { b with Content = before }) bs
                if block.BlockType = B.toggle && not block.Collapsed && hasChildren then
                    Doc.insertFirstChild block.Id nb bs
                else
                    Doc.insertBlock block.Id Below nb bs)
            requestFocus nb.Id 0

    let handleBackspaceAtStart (block: JournalBlockDto) (content: string) (e: Browser.Types.KeyboardEvent) =
        if block.BlockType <> B.text then
            // first Backspace turns the block back into plain text (Notion behaviour)
            e.preventDefault ()
            mutate false (Doc.updateBlock block.Id (fun b -> { b with BlockType = B.text }))
            requestFocus block.Id 0
        else
            let ids = Doc.focusableIds blocksRef.current
            let prevId =
                ids
                |> List.tryFindIndex (fun i -> i = block.Id)
                |> Option.bind (fun i -> if i > 0 then Some ids.[i - 1] else None)
            match prevId with
            | None -> ()
            | Some pid ->
                let prev = blocksRef.current |> List.tryFind (fun b -> b.Id = pid)
                match prev with
                | Some p when Doc.isTextLike p.BlockType || p.BlockType = B.code ->
                    e.preventDefault ()
                    let mergePoint = p.Content.Length
                    let hasChildren = blocksRef.current |> List.exists (fun b -> b.ParentId = Some block.Id)
                    if hasChildren then
                        // don't merge a toggle-with-children away; just move the caret
                        requestFocus pid mergePoint
                    else
                        mutate true (fun bs ->
                            bs
                            |> Doc.updateBlock pid (fun b -> { b with Content = b.Content + content })
                            |> Doc.removeBlockAndChildren block.Id)
                        requestFocus pid mergePoint
                | _ ->
                    if content = "" then
                        e.preventDefault ()
                        deleteBlock block.Id

    let handleTextKeyDown (block: JournalBlockDto) (e: Browser.Types.KeyboardEvent) =
        let ta: Browser.Types.HTMLTextAreaElement = unbox e.target
        let content: string = emitJsExpr ta "$0.value"
        let selStart: int = emitJsExpr ta "$0.selectionStart"
        let selEnd: int = emitJsExpr ta "$0.selectionEnd"
        let slashItemsNow = if content.StartsWith("/") then filterSlashItems (content.Substring(1)) else []
        let slashOpen =
            content.StartsWith("/")
            && slashDismissedFor <> Some block.Id
            && not slashItemsNow.IsEmpty
        if slashOpen && (e.key = "ArrowDown" || e.key = "ArrowUp" || e.key = "Enter" || e.key = "Tab" || e.key = "Escape") then
            e.preventDefault ()
            match e.key with
            | "ArrowDown" -> setSlashIndex ((slashIndex + 1) % slashItemsNow.Length)
            | "ArrowUp" -> setSlashIndex ((slashIndex + slashItemsNow.Length - 1) % slashItemsNow.Length)
            | "Escape" -> setSlashDismissedFor (Some block.Id)
            | _ ->
                let idx = min slashIndex (slashItemsNow.Length - 1)
                applySlashItem block slashItemsNow.[idx]
        else
            match e.key with
            | "Enter" when not e.shiftKey ->
                e.preventDefault ()
                handleEnterSplit block content selStart selEnd
            | "Backspace" when selStart = 0 && selEnd = 0 ->
                handleBackspaceAtStart block content e
            | " " when block.BlockType = B.text && selStart = selEnd && selStart > 0 ->
                let prefix = content.Substring(0, selStart)
                match markdownPrefixes |> List.tryFind (fun (p, _) -> p = prefix) with
                | Some (_, newType) ->
                    e.preventDefault ()
                    let rest = content.Substring(selStart)
                    mutate false (Doc.updateBlock block.Id (fun b -> { b with BlockType = newType; Content = rest }))
                    requestFocus block.Id 0
                | None -> ()
            | "ArrowUp" when selStart = selEnd && (content.IndexOf('\n') = -1 || selStart <= content.IndexOf('\n')) ->
                e.preventDefault ()
                focusNeighbor block.Id -1 selStart
            | "ArrowDown" when selStart = selEnd && (content.LastIndexOf('\n') = -1 || selStart > content.LastIndexOf('\n')) ->
                e.preventDefault ()
                focusNeighbor block.Id 1 selStart
            | "ArrowLeft" when selStart = 0 && selEnd = 0 ->
                e.preventDefault ()
                focusNeighbor block.Id -1 999999
            | "ArrowRight" when selStart = content.Length && selEnd = content.Length ->
                e.preventDefault ()
                focusNeighbor block.Id 1 0
            | _ -> ()

    let handleCodeKeyDown (block: JournalBlockDto) (e: Browser.Types.KeyboardEvent) =
        let ta: Browser.Types.HTMLTextAreaElement = unbox e.target
        let content: string = emitJsExpr ta "$0.value"
        let selStart: int = emitJsExpr ta "$0.selectionStart"
        let selEnd: int = emitJsExpr ta "$0.selectionEnd"
        match e.key with
        | "Backspace" when selStart = 0 && selEnd = 0 && content = "" ->
            e.preventDefault ()
            convertBlockType block.Id B.text None
        | "Tab" ->
            e.preventDefault ()
            let newContent = content.Substring(0, selStart) + "  " + content.Substring(selEnd)
            mutate false (Doc.updateBlock block.Id (fun b -> { b with Content = newContent }))
            requestFocus block.Id (selStart + 2)
        | "ArrowUp" when selStart = selEnd && (content.IndexOf('\n') = -1 || selStart <= content.IndexOf('\n')) ->
            e.preventDefault ()
            focusNeighbor block.Id -1 selStart
        | "ArrowDown" when selStart = selEnd && (content.LastIndexOf('\n') = -1 || selStart > content.LastIndexOf('\n')) ->
            e.preventDefault ()
            focusNeighbor block.Id 1 selStart
        | "Escape" -> ta.blur ()
        | _ -> ()

    let handleContentChange (block: JournalBlockDto) (v: string) =
        if block.BlockType = B.text && v = "```" then
            mutate false (Doc.updateBlock block.Id (fun b -> { b with BlockType = B.code; Content = "" }))
            requestFocus block.Id 0
        else
            mutate false (Doc.updateBlock block.Id (fun b -> { b with Content = v }))
            if slashDismissedFor = Some block.Id && not (v.StartsWith("/")) then
                setSlashDismissedFor None
            if v = "/" then setSlashIndex 0

    let handlePaste (block: JournalBlockDto) (e: Browser.Types.ClipboardEvent) =
        let fileCount: int = emitJsExpr e.clipboardData "$0.files.length"
        if fileCount > 0 then
            let file: Browser.Types.File = emitJsExpr e.clipboardData "$0.files[0]"
            let fileType: string = emitJsExpr file "$0.type"
            if fileType.StartsWith("image/") then
                e.preventDefault ()
                let nb = Doc.mkBlock B.image
                mutate true (Doc.insertBlock block.Id Below nb)
                uploadImageInto nb.Id file
        else
            let text = e.clipboardData.getData("text")
            if isUrl text then
                let ta: Browser.Types.HTMLTextAreaElement = unbox e.target
                let value: string = emitJsExpr ta "$0.value"
                let selStart: int = emitJsExpr ta "$0.selectionStart"
                let selEnd: int = emitJsExpr ta "$0.selectionEnd"
                let url = text.Trim()
                if value = "" && block.BlockType = B.text then
                    // pasting a bare URL into an empty block → link block (Notion-ish)
                    e.preventDefault ()
                    mutate false (Doc.updateBlock block.Id (fun b ->
                        { b with BlockType = B.link; Url = Some url; Content = url }))
                    setFocusedId None
                elif selStart <> selEnd then
                    // pasting a URL over selected text → inline markdown link
                    e.preventDefault ()
                    let selected = value.Substring(selStart, selEnd - selStart)
                    let newVal =
                        value.Substring(0, selStart) + "[" + selected + "](" + url + ")" + value.Substring(selEnd)
                    mutate false (Doc.updateBlock block.Id (fun b -> { b with Content = newVal }))

    // ── Drag & drop ─────────────────────────────────────────────────────────

    let detectSide (allowColumns: bool) (e: Browser.Types.DragEvent) : Side =
        let target: Browser.Types.Element = emitJsExpr e.currentTarget "$0"
        let rect: {| left: float; top: float; width: float; height: float |} =
            emitJsExpr target "$0.getBoundingClientRect()"
        let relX = (e.clientX - rect.left) / rect.width
        let relY = (e.clientY - rect.top) / rect.height
        if allowColumns && relX < 0.15 then LeftSide
        elif allowColumns && relX > 0.85 then RightSide
        elif relY < 0.5 then Above
        else Below

    let dropHandlers (bid: string) (allowColumns: bool) : IReactProperty list = [
        prop.onDragOver (fun e ->
            let hasFiles: bool = emitJsExpr e.dataTransfer.types "Array.from($0).includes('Files')"
            if hasFiles || (draggedId.IsSome && draggedId <> Some bid) then
                e.preventDefault ()
                e.stopPropagation ()
                e.dataTransfer.dropEffect <- (if hasFiles then "copy" else "move")
                let side = detectSide allowColumns e
                if dropTarget <> Some (bid, side) then
                    setDropTarget (Some (bid, side)))
        prop.onDragLeave (fun _ ->
            match dropTarget with
            | Some (tid, _) when tid = bid -> setDropTarget None
            | _ -> ())
        prop.onDrop (fun e ->
            e.preventDefault ()
            e.stopPropagation ()
            let side = detectSide allowColumns e
            let fileCount: int = emitJsExpr e.dataTransfer "$0.files.length"
            if fileCount > 0 then
                let file: Browser.Types.File = emitJsExpr e.dataTransfer "$0.files[0]"
                let fileType: string = emitJsExpr file "$0.type"
                if fileType.StartsWith("image/") then
                    let nb = Doc.mkBlock B.image
                    mutate true (Doc.insertBlock bid side nb)
                    uploadImageInto nb.Id file
            else
                match draggedId with
                | Some did when did <> bid -> mutate true (Doc.moveBlock did bid side)
                | _ -> ()
            setDraggedId None
            setDropTarget None)
    ]

    let startColumnResize (leftId: string) (rightId: string) (e: Browser.Types.MouseEvent) =
        e.preventDefault ()
        let containerWidth: float = emitJsExpr e.currentTarget "$0.parentElement.getBoundingClientRect().width"
        let startX = e.clientX
        let widthOf id =
            blocksRef.current
            |> List.tryFind (fun b -> b.Id = id)
            |> Option.map (fun b -> b.Width)
            |> Option.defaultValue 0.5
        let leftW = widthOf leftId
        let rightW = widthOf rightId
        let mutable onMove = Unchecked.defaultof<Browser.Types.Event -> unit>
        let mutable onUp = Unchecked.defaultof<Browser.Types.Event -> unit>
        onMove <- fun ev ->
            let me: Browser.Types.MouseEvent = unbox ev
            let delta = (me.clientX - startX) / containerWidth
            let total = leftW + rightW
            let newLeft = max 0.15 (min (total - 0.15) (leftW + delta))
            let next =
                blocksRef.current
                |> List.map (fun b ->
                    if b.Id = leftId then { b with Width = newLeft }
                    elif b.Id = rightId then { b with Width = total - newLeft }
                    else b)
            blocksRef.current <- next
            setBlocks next
        onUp <- fun _ ->
            Browser.Dom.document.removeEventListener("mousemove", onMove)
            Browser.Dom.document.removeEventListener("mouseup", onUp)
            scheduleSave ()
        Browser.Dom.document.addEventListener("mousemove", onMove)
        Browser.Dom.document.addEventListener("mouseup", onUp)

    // ── Views ───────────────────────────────────────────────────────────────

    let dropIndicators (bid: string) : ReactElement list =
        match dropTarget with
        | Some (tid, side) when tid = bid ->
            let cls =
                match side with
                | Above -> "absolute -top-0.5 left-0 right-0 h-[3px]"
                | Below -> "absolute -bottom-0.5 left-0 right-0 h-[3px]"
                | LeftSide -> "absolute top-0 bottom-0 -left-1 w-[3px]"
                | RightSide -> "absolute top-0 bottom-0 -right-1 w-[3px]"
            [ Html.div [ prop.className (cls + " bg-primary rounded-full pointer-events-none z-20") ] ]
        | _ -> []

    let blockMenuView (block: JournalBlockDto) =
        React.fragment [
            Html.div [
                prop.className "fixed inset-0 z-40"
                prop.onClick (fun _ -> setMenuBlockId None)
            ]
            Html.div [
                prop.className "rating-dropdown absolute -left-7 top-8 z-50 min-w-[200px] max-h-96 overflow-y-auto"
                prop.children [
                    Html.button [
                        prop.className "rating-dropdown-item w-full text-error/80 hover:text-error"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            setMenuBlockId None
                            deleteBlock block.Id)
                        prop.children [
                            Html.span [ prop.className "text-sm"; prop.text "×" ]
                            Html.span [ prop.className "text-sm"; prop.text "Delete" ]
                        ]
                    ]
                    Html.button [
                        prop.className "rating-dropdown-item w-full"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            setMenuBlockId None
                            addBlockBelow block.Id)
                        prop.children [
                            Html.span [ prop.className "text-sm"; prop.text "+" ]
                            Html.span [ prop.className "text-sm"; prop.text "Add block below" ]
                        ]
                    ]
                    if Doc.isTextLike block.BlockType || block.BlockType = B.code then
                        Html.div [ prop.className "border-t border-base-content/10 my-1" ]
                        Html.div [
                            prop.className "px-3 py-1"
                            prop.children [
                                Html.span [
                                    prop.className "text-xs text-base-content/40 uppercase tracking-wider font-bold"
                                    prop.text "Turn into"
                                ]
                            ]
                        ]
                        for (t, label) in turnIntoTypes do
                            let isActive = block.BlockType = t
                            Html.button [
                                prop.className (
                                    "rating-dropdown-item w-full" +
                                    (if isActive then " rating-dropdown-item-active" else ""))
                                prop.onClick (fun e ->
                                    e.stopPropagation ()
                                    setMenuBlockId None
                                    if not isActive then convertBlockType block.Id t None)
                                prop.children [
                                    Html.span [ prop.className "text-sm"; prop.text label ]
                                ]
                            ]
                ]
            ]
        ]

    let handleView (block: JournalBlockDto) =
        let visible = menuBlockId = Some block.Id
        Html.div [
            prop.className (
                "absolute -left-7 top-1 z-10 transition-opacity" +
                (if visible then " opacity-100" else " opacity-0 group-hover:opacity-100"))
            prop.children [
                Html.button [
                    prop.className "w-5 h-5 flex items-center justify-center text-base-content/30 hover:text-base-content/60 cursor-grab transition-colors"
                    prop.title "Drag to move, click for options"
                    prop.draggable true
                    prop.onDragStart (fun e ->
                        e.stopPropagation ()
                        e.dataTransfer.effectAllowed <- "move"
                        e.dataTransfer.setData("text/plain", block.Id) |> ignore
                        // drag the whole block visually, not just the grip
                        emitJsExpr (e, e.clientX, e.clientY)
                            "(function(ev,x,y){var b=ev.currentTarget.closest('.journal-block'); if(b){var r=b.getBoundingClientRect(); ev.dataTransfer.setDragImage(b, x-r.left, y-r.top);}})($0,$1,$2)" |> ignore
                        emitJsExpr (fun () -> setDraggedId (Some block.Id)) "setTimeout($0, 0)")
                    prop.onDragEnd (fun _ ->
                        setDraggedId None
                        setDropTarget None)
                    prop.onClick (fun e ->
                        e.stopPropagation ()
                        setMenuBlockId (if visible then None else Some block.Id))
                    prop.children [ Icons.gripVertical () ]
                ]
            ]
        ]

    let slashMenuView (block: JournalBlockDto) =
        let items = filterSlashItems (block.Content.Substring(1))
        Html.div [
            prop.className "rating-dropdown absolute left-0 top-full z-50 mt-1 w-64 max-h-80 overflow-y-auto"
            prop.children [
                for i in 0 .. items.Length - 1 do
                    let item = items.[i]
                    Html.button [
                        prop.key item.Label
                        prop.className (
                            "rating-dropdown-item w-full" +
                            (if i = min slashIndex (items.Length - 1) then " rating-dropdown-item-active" else ""))
                        prop.onMouseDown (fun e ->
                            e.preventDefault ()
                            applySlashItem block item)
                        prop.children [
                            Html.span [ prop.className "w-8 flex-shrink-0 text-xs font-mono text-base-content/50"; prop.text item.Hint ]
                            Html.span [ prop.className "text-sm"; prop.text item.Label ]
                        ]
                    ]
            ]
        ]

    let baseTextarea (block: JournalBlockDto) (cls: string) (placeholder: string) (keyHandler: JournalBlockDto -> Browser.Types.KeyboardEvent -> unit) =
        Html.textarea [
            prop.className ("block w-full bg-transparent outline-none resize-none overflow-hidden whitespace-pre-wrap p-0 border-0 m-0 placeholder:text-base-content/20 " + cls)
            prop.value block.Content
            prop.rows 1
            prop.placeholder placeholder
            prop.onFocus (fun _ -> setFocusedId (Some block.Id))
            prop.onBlur (fun _ -> setFocusedId None)
            prop.onChange (fun (v: string) -> handleContentChange block v)
            prop.onKeyDown (keyHandler block)
            prop.onPaste (handlePaste block)
            prop.onInput (fun e ->
                let t = e.target
                emitJsExpr t "$0.style.height = 'auto'"
                emitJsExpr t "$0.style.height = $0.scrollHeight + 'px'")
            prop.ref (fun el ->
                if isNull el then textareaRefs.current.Remove(block.Id) |> ignore
                else
                    let ta: Browser.Types.HTMLTextAreaElement = unbox el
                    textareaRefs.current.[block.Id] <- ta
                    emitJsExpr ta "$0.style.height = 'auto'"
                    emitJsExpr ta "$0.style.height = $0.scrollHeight + 'px'")
        ]

    /// Textarea while editing; rendered inline links when idle
    let renderEditable (block: JournalBlockDto) (cls: string) (placeholder: string) =
        if focusedId <> Some block.Id && containsMarkdownLink block.Content then
            Html.div [
                prop.className (cls + " cursor-text whitespace-pre-wrap")
                prop.onClick (fun _ -> requestFocus block.Id block.Content.Length)
                prop.children [ renderInlineContent block.Content ]
            ]
        else
            baseTextarea block cls placeholder handleTextKeyDown

    let imageView (block: JournalBlockDto) =
        match block.ImageRef with
        | Some imageRef ->
            Html.figure [
                prop.className "space-y-1"
                prop.children [
                    Html.img [
                        prop.src $"/images/{imageRef}"
                        prop.alt (block.Caption |> Option.defaultValue "image")
                        prop.className "w-full rounded-lg border border-base-content/10"
                    ]
                    Html.input [
                        prop.className "w-full bg-transparent text-xs text-base-content/50 italic text-center outline-none placeholder:text-base-content/20"
                        prop.placeholder "Add a caption…"
                        prop.value (block.Caption |> Option.defaultValue "")
                        prop.onChange (fun (v: string) ->
                            let caption = if v = "" then None else Some v
                            mutate false (Doc.updateBlock block.Id (fun b -> { b with Caption = caption })))
                    ]
                ]
            ]
        | None ->
            Html.div [
                prop.className "w-full rounded-lg border-2 border-dashed border-base-content/15 bg-base-200/30 p-6 flex items-center justify-center gap-4"
                prop.children [
                    Html.div [ prop.className "text-base-content/30"; prop.children [ Icons.screenshotBlock () ] ]
                    Html.button [
                        prop.className "btn btn-sm btn-ghost"
                        prop.onClick (fun _ -> openImagePicker block.Id)
                        prop.text "Choose an image…"
                    ]
                    Html.button [
                        prop.className "btn btn-sm btn-ghost text-error/70"
                        prop.onClick (fun _ -> deleteBlock block.Id)
                        prop.text "Remove"
                    ]
                ]
            ]

    let linkView (block: JournalBlockDto) =
        let editing = editingLinkId = Some block.Id || block.Url.IsNone
        if editing then
            Html.div [
                prop.className "flex flex-col gap-1.5 p-3 rounded-lg border border-base-content/10 bg-base-content/5"
                prop.children [
                    Html.input [
                        prop.className "w-full bg-transparent outline-none text-sm text-base-content/80 placeholder:text-base-content/20"
                        prop.placeholder "https://…"
                        prop.autoFocus true
                        prop.value (block.Url |> Option.defaultValue "")
                        prop.onChange (fun (v: string) ->
                            mutate false (Doc.updateBlock block.Id (fun b ->
                                { b with Url = (if v = "" then None else Some v) })))
                        prop.onKeyDown (fun e ->
                            if e.key = "Enter" || e.key = "Escape" then
                                e.preventDefault ()
                                setEditingLinkId None)
                    ]
                    Html.input [
                        prop.className "w-full bg-transparent outline-none text-xs text-base-content/60 placeholder:text-base-content/20"
                        prop.placeholder "Title (optional)"
                        prop.value block.Content
                        prop.onChange (fun (v: string) ->
                            mutate false (Doc.updateBlock block.Id (fun b -> { b with Content = v })))
                        prop.onKeyDown (fun e ->
                            if e.key = "Enter" || e.key = "Escape" then
                                e.preventDefault ()
                                setEditingLinkId None)
                    ]
                    Html.div [
                        prop.className "flex gap-2 justify-end"
                        prop.children [
                            Html.button [
                                prop.className "btn btn-xs btn-ghost text-error/70"
                                prop.onClick (fun _ -> deleteBlock block.Id)
                                prop.text "Remove"
                            ]
                            Html.button [
                                prop.className "btn btn-xs btn-ghost"
                                prop.onClick (fun _ -> setEditingLinkId None)
                                prop.text "Done"
                            ]
                        ]
                    ]
                ]
            ]
        else
            let url = block.Url |> Option.defaultValue ""
            Html.div [
                prop.className "group/link flex items-center gap-2 p-3 rounded-lg border border-base-content/10 hover:bg-base-content/5 transition-colors"
                prop.children [
                    Html.div [ prop.className "text-base-content/40 flex-shrink-0"; prop.children [ Icons.externalLink () ] ]
                    Html.a [
                        prop.href url
                        prop.target "_blank"
                        prop.rel "noopener noreferrer"
                        prop.className "flex-1 min-w-0 truncate text-sm link link-hover text-base-content/80"
                        prop.text (if block.Content = "" then url else block.Content)
                    ]
                    Html.button [
                        prop.className "opacity-0 group-hover/link:opacity-100 transition-opacity text-base-content/40 hover:text-base-content/70"
                        prop.onClick (fun _ -> setEditingLinkId (Some block.Id))
                        prop.children [ Icons.edit () ]
                    ]
                ]
            ]

    let blockContentView (numIndex: int) (block: JournalBlockDto) =
        let bodyCls = "text-sm leading-6 font-sans text-base-content/80"
        match block.BlockType with
        | t when t = B.heading1 ->
            renderEditable block "font-display text-3xl leading-tight text-base-content" "Heading 1"
        | t when t = B.heading2 ->
            renderEditable block "font-display text-2xl leading-tight text-base-content" "Heading 2"
        | t when t = B.heading3 ->
            renderEditable block "font-display text-xl leading-tight text-base-content" "Heading 3"
        | t when t = B.heading4 ->
            renderEditable block "font-display text-lg leading-tight text-base-content" "Heading 4"
        | t when t = B.bullet ->
            Html.div [
                prop.className "flex gap-1"
                prop.children [
                    Html.span [ prop.className "select-none w-5 flex-shrink-0 text-center text-base-content/60 text-sm leading-6"; prop.text "•" ]
                    Html.div [ prop.className "flex-1 min-w-0"; prop.children [ renderEditable block bodyCls "List item" ] ]
                ]
            ]
        | t when t = B.numbered ->
            Html.div [
                prop.className "flex gap-1"
                prop.children [
                    Html.span [ prop.className "select-none min-w-5 flex-shrink-0 pr-1 text-right text-base-content/60 text-sm leading-6 font-mono"; prop.text (string numIndex + ".") ]
                    Html.div [ prop.className "flex-1 min-w-0"; prop.children [ renderEditable block bodyCls "List item" ] ]
                ]
            ]
        | t when t = B.todo ->
            Html.div [
                prop.className "flex gap-2 items-start"
                prop.children [
                    Html.input [
                        prop.type' "checkbox"
                        prop.className "checkbox checkbox-xs mt-1.5 flex-shrink-0"
                        prop.isChecked block.Checked
                        prop.onChange (fun (v: bool) ->
                            mutate false (Doc.updateBlock block.Id (fun b -> { b with Checked = v })))
                    ]
                    Html.div [
                        prop.className "flex-1 min-w-0"
                        prop.children [
                            renderEditable block
                                (bodyCls + (if block.Checked then " line-through text-base-content/40" else ""))
                                "To-do"
                        ]
                    ]
                ]
            ]
        | t when t = B.toggle ->
            Html.div [
                prop.className "flex gap-1 items-start"
                prop.children [
                    Html.button [
                        prop.className "select-none w-5 h-6 flex-shrink-0 flex items-center justify-center text-base-content/50 hover:text-base-content/80 transition-colors text-xs"
                        prop.onClick (fun _ ->
                            mutate false (Doc.updateBlock block.Id (fun b -> { b with Collapsed = not b.Collapsed })))
                        prop.text (if block.Collapsed then "▶" else "▼")
                    ]
                    Html.div [ prop.className "flex-1 min-w-0"; prop.children [ renderEditable block (bodyCls + " font-medium") "Toggle" ] ]
                ]
            ]
        | t when t = B.quote ->
            Html.div [
                prop.className "border-l-4 border-primary/40 pl-4"
                prop.children [ renderEditable block "text-sm leading-6 font-sans italic text-base-content/70" "Quote" ]
            ]
        | t when t = B.callout ->
            Html.div [
                prop.className "flex gap-3 border-l-4 border-info/40 bg-info/5 rounded-r-lg p-3"
                prop.children [
                    Html.div [ prop.className "text-info/70 flex-shrink-0 mt-0.5"; prop.children [ Icons.calloutBlock () ] ]
                    Html.div [ prop.className "flex-1 min-w-0"; prop.children [ renderEditable block bodyCls "Callout" ] ]
                ]
            ]
        | t when t = B.code ->
            Html.div [
                prop.className "relative bg-base-300/50 border border-base-content/10 rounded-lg"
                prop.children [
                    Html.input [
                        prop.className "absolute top-1.5 right-2 w-24 bg-transparent outline-none text-right font-mono text-[10px] text-base-content/40 placeholder:text-base-content/20"
                        prop.placeholder "language"
                        prop.value (block.Language |> Option.defaultValue "")
                        prop.onChange (fun (v: string) ->
                            mutate false (Doc.updateBlock block.Id (fun b ->
                                { b with Language = (if v = "" then None else Some v) })))
                    ]
                    Html.div [
                        prop.className "p-3"
                        prop.children [
                            baseTextarea block "font-mono text-xs leading-5 text-base-content/80" "Code" handleCodeKeyDown
                        ]
                    ]
                ]
            ]
        | t when t = B.link -> linkView block
        | t when t = B.image -> imageView block
        | _ ->
            renderEditable block bodyCls
                (if focusedId = Some block.Id then "Type '/' for commands" else "")

    let slashOpenFor (block: JournalBlockDto) =
        focusedId = Some block.Id
        && Doc.isTextLike block.BlockType
        && block.Content.StartsWith("/")
        && slashDismissedFor <> Some block.Id
        && not (filterSlashItems (block.Content.Substring(1))).IsEmpty

    let rec renderSiblings (nodes: Node list) : ReactElement list =
        let mutable num = 0
        [ for node in nodes do
            if node.Block.BlockType = B.numbered then num <- num + 1 else num <- 0
            yield renderNode num node ]

    and renderNode (numIndex: int) (node: Node) : ReactElement =
        if node.Block.BlockType = B.columnList then renderColumnList node
        else renderBlockRow numIndex node

    and renderBlockRow (numIndex: int) (node: Node) : ReactElement =
        let block = node.Block
        let isDragged = draggedId = Some block.Id
        Html.div ([
            prop.key block.Id
            prop.className (
                "journal-block group relative py-0.5" +
                (if isDragged then " opacity-40" else ""))
        ] @ dropHandlers block.Id true @ [
            prop.children [
                yield! dropIndicators block.Id
                handleView block
                if menuBlockId = Some block.Id then blockMenuView block
                blockContentView numIndex block
                if block.BlockType = B.toggle && not block.Collapsed then
                    Html.div [
                        prop.className "ml-6 mt-0.5"
                        prop.children (
                            if node.Children.IsEmpty then
                                [ Html.button [
                                    prop.className "text-xs text-base-content/30 italic hover:text-base-content/50 py-1"
                                    prop.onClick (fun _ ->
                                        let nb = Doc.mkBlock B.text
                                        mutate true (Doc.insertFirstChild block.Id nb)
                                        requestFocus nb.Id 0)
                                    prop.text "Empty toggle. Click to add a block."
                                  ] ]
                            else renderSiblings node.Children)
                    ]
                if slashOpenFor block then slashMenuView block
            ]
        ])

    and renderColumnList (node: Node) : ReactElement =
        let cols = node.Children
        Html.div ([
            prop.key node.Block.Id
            prop.className "group relative py-0.5"
        ] @ dropHandlers node.Block.Id false @ [
            prop.children [
                yield! dropIndicators node.Block.Id
                Html.div [
                    prop.className "flex items-start w-full"
                    prop.children [
                        for i in 0 .. cols.Length - 1 do
                            let col = cols.[i]
                            if i > 0 then
                                Html.div [
                                    prop.key (col.Block.Id + "-divider")
                                    prop.className "relative w-2 self-stretch flex-shrink-0 cursor-col-resize group/divider"
                                    prop.onMouseDown (startColumnResize cols.[i - 1].Block.Id col.Block.Id)
                                    prop.children [
                                        Html.div [
                                            prop.className "absolute inset-y-0 left-1/2 -translate-x-1/2 w-[3px] rounded-full bg-transparent group-hover/divider:bg-primary/50 transition-colors"
                                        ]
                                    ]
                                ]
                            Html.div [
                                prop.key col.Block.Id
                                prop.className "min-w-0"
                                prop.style [
                                    style.custom ("flexBasis", sprintf "%.4f%%" (col.Block.Width * 100.0))
                                    style.custom ("flexGrow", "1")
                                    style.custom ("flexShrink", "1")
                                ]
                                prop.children (renderSiblings col.Children)
                            ]
                    ]
                ]
            ]
        ])

    // ── Layout ──────────────────────────────────────────────────────────────

    let tree = Doc.toTree blocks

    Html.div [
        prop.className "relative"
        prop.children [
            // save indicator (fixed height to avoid layout shift)
            Html.div [
                prop.className "flex justify-end items-center h-4 mb-2"
                prop.children [
                    match saveState with
                    | SaveDirty ->
                        Html.span [ prop.className "text-[10px] font-mono text-base-content/30"; prop.text "saving…" ]
                    | SaveDone ->
                        Html.span [ prop.className "text-[10px] font-mono text-base-content/20"; prop.text "saved" ]
                    | SaveFailed err ->
                        Html.span [ prop.className "text-[10px] font-mono text-error"; prop.text ("save failed: " + err) ]
                    | SaveIdle -> ()
                ]
            ]
            if not loaded then
                Html.div [
                    prop.className "text-sm text-base-content/30 italic py-8"
                    prop.text "Loading journal…"
                ]
            else
                Html.div [
                    prop.children (renderSiblings tree)
                ]
                // click-to-append zone
                Html.div [
                    prop.className "h-24 cursor-text"
                    prop.onClick (fun _ -> addBlockAtEnd ())
                    prop.children [
                        if blocks.IsEmpty then
                            Html.p [
                                prop.className "text-sm text-base-content/30 italic pt-1"
                                prop.text "Click to start writing. Type '/' for block types."
                            ]
                    ]
                ]
            // hidden file input for image blocks
            Html.input [
                prop.type' "file"
                prop.accept "image/*"
                prop.className "hidden"
                prop.ref (fun el ->
                    if isNull el then fileInputRef.current <- None
                    else fileInputRef.current <- Some (unbox el))
                prop.onChange (fun (e: Browser.Types.Event) ->
                    let input: Browser.Types.HTMLInputElement = unbox e.target
                    let fileCount: int = emitJsExpr input "$0.files.length"
                    if fileCount > 0 then
                        let file: Browser.Types.File = emitJsExpr input "$0.files[0]"
                        match pendingImageRef.current with
                        | Some bid -> uploadImageInto bid file
                        | None -> ()
                    pendingImageRef.current <- None
                    emitJsExpr input "$0.value = ''")
            ]
        ]
    ]
