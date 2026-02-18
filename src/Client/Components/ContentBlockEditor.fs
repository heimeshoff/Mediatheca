module Mediatheca.Client.Components.ContentBlockEditor

open Fable.Core.JsInterop
open Feliz
open Mediatheca.Shared

type private EditingBlock = {
    BlockId: string
    Content: string
}

type private DropZone = Left | Right

type private DropLocation =
    | BlockDrop of blockId: string * zone: DropZone
    | GapDrop of gapIndex: int

type private RenderItem =
    | SingleBlock of ContentBlockDto
    | RowPair of ContentBlockDto * ContentBlockDto

let private optionIfNotEmpty (s: string) =
    if System.String.IsNullOrWhiteSpace(s) then None else Some s

let private isUrl (text: string) =
    let trimmed = text.Trim()
    System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^https?://[^\s]+$")

/// Convert legacy link blocks to inline markdown-style links
let private getDisplayContent (block: ContentBlockDto) =
    match block.BlockType, block.Url with
    | "link", Some url ->
        let displayText = if System.String.IsNullOrWhiteSpace(block.Content) then url else block.Content
        $"[{displayText}]({url})"
    | _ -> block.Content

let private editingFromBlock (block: ContentBlockDto) = {
    BlockId = block.BlockId
    Content = getDisplayContent block
}

/// Parse markdown-style [text](url) links and render as React elements
let private renderContent (content: string) =
    let rec parse (s: string) (acc: ReactElement list) (idx: int) =
        let linkStart = s.IndexOf("[")
        if linkStart = -1 then
            if s.Length > 0 then acc @ [Html.span [ prop.key $"t{idx}"; prop.text s ]]
            else acc
        else
            let closeBracket = s.IndexOf("](", linkStart + 1)
            if closeBracket = -1 then
                acc @ [Html.span [ prop.key $"t{idx}"; prop.text s ]]
            else
                let closeParen = s.IndexOf(")", closeBracket + 2)
                if closeParen = -1 then
                    acc @ [Html.span [ prop.key $"t{idx}"; prop.text s ]]
                else
                    let displayText = s.[linkStart + 1..closeBracket - 1]
                    let url = s.[closeBracket + 2..closeParen - 1]
                    let before = if linkStart > 0 then s.[0..linkStart - 1] else ""
                    let rest = if closeParen + 1 < s.Length then s.[closeParen + 1..] else ""
                    let acc =
                        if before.Length > 0 then acc @ [Html.span [ prop.key $"t{idx}"; prop.text before ]]
                        else acc
                    let acc = acc @ [
                        Html.a [
                            prop.key $"l{idx}"
                            prop.href url
                            prop.target "_blank"
                            prop.rel "noopener noreferrer"
                            prop.className "link link-primary"
                            prop.text displayText
                        ]]
                    parse rest acc (idx + 1)
    let elements = parse content [] 0
    match elements with
    | [single] -> single
    | many -> React.fragment many

/// Smart paste: if URL is pasted over selected text, create markdown link inline
let private smartPasteHandler (currentText: string) (setText: string -> unit) (e: Browser.Types.ClipboardEvent) =
    let clipboardText = e.clipboardData.getData("text")
    if isUrl (clipboardText.Trim()) then
        let target : Browser.Types.HTMLInputElement = unbox e.target
        let selStart : int = emitJsExpr target "$0.selectionStart"
        let selEnd : int = emitJsExpr target "$0.selectionEnd"
        if selStart <> selEnd then
            e.preventDefault ()
            let selectedText = currentText.[selStart..selEnd - 1]
            let url = clipboardText.Trim()
            let before = if selStart > 0 then currentText.[0..selStart - 1] else ""
            let after = if selEnd < currentText.Length then currentText.[selEnd..] else ""
            setText (before + $"[{selectedText}]({url})" + after)

/// Auto-sizing textarea that matches the rendered text style
let private editableTextarea (extraClass: string) (eb: EditingBlock) (setEditingBlock: EditingBlock option -> unit) (saveEditing: EditingBlock -> unit) (cancelEditing: unit -> unit) =
    Html.textarea [
        prop.className ("block w-full bg-transparent outline-none resize-none overflow-hidden whitespace-pre-wrap p-0 border-0 m-0 " + extraClass)
        prop.autoFocus true
        prop.value eb.Content
        prop.rows 1
        prop.onChange (fun (v: string) -> setEditingBlock (Some { eb with Content = v }))
        prop.onBlur (fun _ -> saveEditing eb)
        prop.onKeyDown (fun e ->
            match e.key with
            | "Escape" ->
                e.preventDefault ()
                cancelEditing ()
            | "Tab" | "Enter" when e.ctrlKey || e.metaKey ->
                e.preventDefault ()
                saveEditing eb
            | _ -> ())
        prop.onPaste (fun (e: Browser.Types.ClipboardEvent) ->
            smartPasteHandler eb.Content (fun v -> setEditingBlock (Some { eb with Content = v })) e)
        prop.ref (fun el ->
            if not (isNull el) then
                emitJsExpr el "$0.style.height = 'auto'"
                emitJsExpr el "$0.style.height = $0.scrollHeight + 'px'")
        prop.onInput (fun e ->
            let target = e.target
            emitJsExpr target "$0.style.height = 'auto'"
            emitJsExpr target "$0.style.height = $0.scrollHeight + 'px'")
    ]

/// Render block content based on block type
let private renderBlockContent (block: ContentBlockDto) =
    let content = getDisplayContent block
    match block.BlockType with
    | "quote" ->
        Html.div [
            prop.className "border-l-4 border-primary/40 pl-4 italic text-base-content/70"
            prop.children [
                Html.p [
                    prop.className "text-sm whitespace-pre-wrap"
                    prop.children [ renderContent content ]
                ]
            ]
        ]
    | "callout" ->
        Html.div [
            prop.className "flex gap-3 border-l-4 border-info/40 bg-info/5 rounded-r-lg p-3"
            prop.children [
                Html.div [
                    prop.className "text-info/70 flex-shrink-0 mt-0.5"
                    prop.children [ Icons.calloutBlock () ]
                ]
                Html.p [
                    prop.className "text-sm text-base-content/80 whitespace-pre-wrap"
                    prop.children [ renderContent content ]
                ]
            ]
        ]
    | "code" ->
        Html.pre [
            prop.className "font-mono text-xs bg-base-300/50 border border-base-content/10 rounded-lg p-3 whitespace-pre-wrap overflow-x-auto"
            prop.children [
                Html.code [
                    prop.className "text-base-content/80"
                    prop.text content
                ]
            ]
        ]
    | "screenshot" ->
        Html.div [
            prop.className "space-y-2"
            prop.children [
                match block.ImageRef with
                | Some imageRef ->
                    Html.img [
                        prop.src $"/images/{imageRef}"
                        prop.alt (block.Caption |> Option.defaultValue "Screenshot")
                        prop.className "w-full rounded-lg border border-base-content/10"
                    ]
                | None ->
                    Html.div [
                        prop.className "w-full h-48 rounded-lg border border-base-content/10 bg-base-200/50 flex items-center justify-center text-base-content/30"
                        prop.children [ Icons.screenshotBlock () ]
                    ]
                match block.Caption with
                | Some caption when not (System.String.IsNullOrWhiteSpace caption) ->
                    Html.p [
                        prop.className "text-xs text-base-content/50 text-center italic"
                        prop.text caption
                    ]
                | _ -> ()
            ]
        ]
    | _ -> // "text" and any other
        Html.p [
            prop.className "text-sm text-base-content/80 whitespace-pre-wrap"
            prop.children [ renderContent content ]
        ]

/// Render block in editing mode — keeps the visual wrapper, replaces text with textarea
let private renderEditingBlock (block: ContentBlockDto) (eb: EditingBlock) (setEditingBlock: EditingBlock option -> unit) (saveEditing: EditingBlock -> unit) (cancelEditing: unit -> unit) =
    let textarea cls = editableTextarea cls eb setEditingBlock saveEditing cancelEditing
    match block.BlockType with
    | "quote" ->
        Html.div [
            prop.className "border-l-4 border-primary/40 pl-4 italic text-base-content/70 bg-base-content/5 rounded-r"
            prop.children [ textarea "text-sm leading-5 font-sans" ]
        ]
    | "callout" ->
        Html.div [
            prop.className "flex gap-3 border-l-4 border-info/40 bg-info/10 rounded-r-lg p-3"
            prop.children [
                Html.div [
                    prop.className "text-info/70 flex-shrink-0 mt-0.5"
                    prop.children [ Icons.calloutBlock () ]
                ]
                textarea "text-sm leading-5 font-sans text-base-content/80"
            ]
        ]
    | "code" ->
        Html.pre [
            prop.className "font-mono text-xs bg-base-300/70 border border-base-content/10 rounded-lg p-3 whitespace-pre-wrap overflow-x-auto"
            prop.children [ textarea "font-mono text-xs leading-4 text-base-content/80" ]
        ]
    | _ -> // "text"
        Html.div [
            prop.className "rounded bg-base-content/5"
            prop.children [ textarea "text-sm leading-5 font-sans text-base-content/80" ]
        ]

/// Context menu for a block (glassmorphic, Notion-style)
let private contextMenu
    (block: ContentBlockDto)
    (onRemove: unit -> unit)
    (onChangeType: string -> unit)
    (onUngroup: (unit -> unit) option)
    (onClose: unit -> unit) =
    Html.div [
        prop.children [
            // Click-away backdrop
            Html.div [
                prop.className "fixed inset-0 z-40"
                prop.onClick (fun _ -> onClose ())
            ]
            // Menu — positioned directly below the drag handle
            Html.div [
                prop.className "absolute -left-7 top-8 z-50 rating-dropdown min-w-[180px]"
                prop.children [
                    // Delete
                    Html.button [
                        prop.className "rating-dropdown-item w-full text-error/80 hover:text-error"
                        prop.onClick (fun e ->
                            e.stopPropagation ()
                            onClose ()
                            onRemove ())
                        prop.children [
                            Html.span [ prop.className "text-sm"; prop.text "\u00D7" ]
                            Html.span [ prop.className "text-sm"; prop.text "Delete" ]
                        ]
                    ]
                    // Ungroup option (when block is in a row group)
                    match onUngroup with
                    | Some ungroupFn ->
                        Html.button [
                            prop.className "rating-dropdown-item w-full"
                            prop.onClick (fun e ->
                                e.stopPropagation ()
                                onClose ()
                                ungroupFn ())
                            prop.children [
                                Html.span [ prop.className "text-sm"; prop.text "\u2194" ]
                                Html.span [ prop.className "text-sm"; prop.text "Ungroup" ]
                            ]
                        ]
                    | None -> ()
                    if block.BlockType <> "screenshot" then
                        // Separator
                        Html.div [ prop.className "border-t border-base-content/10 my-1" ]
                        // Turn into header
                        Html.div [
                            prop.className "px-3 py-1"
                            prop.children [
                                Html.span [
                                    prop.className "text-xs text-base-content/40 uppercase tracking-wider font-bold"
                                    prop.text "Turn into"
                                ]
                            ]
                        ]
                        // Block type options
                        for (blockType, label, icon) in [
                            "text", "Text", Icons.textBlock
                            "quote", "Quote", Icons.quoteBlock
                            "callout", "Callout", Icons.calloutBlock
                            "code", "Code", Icons.codeBlock
                        ] do
                            let isActive = block.BlockType = blockType
                            let itemClass =
                                if isActive then "rating-dropdown-item rating-dropdown-item-active w-full"
                                else "rating-dropdown-item w-full"
                            Html.button [
                                prop.className itemClass
                                prop.onClick (fun e ->
                                    e.stopPropagation ()
                                    onClose ()
                                    if not isActive then onChangeType blockType)
                                prop.children [
                                    Html.span [
                                        prop.className "text-base-content/60"
                                        prop.children [ icon () ]
                                    ]
                                    Html.span [ prop.className "text-sm"; prop.text label ]
                                ]
                            ]
                ]
            ]
        ]
    ]

/// Detect drop zone based on cursor position within the target element (50/50 split for left/right)
let private detectDropZone (e: Browser.Types.DragEvent) : DropZone =
    let target : Browser.Types.Element = emitJsExpr e.currentTarget "$0"
    let rect : {| left: float; width: float |} = emitJsExpr target "$0.getBoundingClientRect()"
    let relX = e.clientX - rect.left
    if relX < rect.width / 2.0 then Left
    else Right

/// Build RenderItem list by grouping blocks on RowGroup
let private buildRenderItems (sortedBlocks: ContentBlockDto list) : RenderItem list =
    let mutable seen = Set.empty
    [
        for block in sortedBlocks do
            if not (seen.Contains block.BlockId) then
                match block.RowGroup with
                | Some rg ->
                    let partner =
                        sortedBlocks
                        |> List.tryFind (fun b -> b.BlockId <> block.BlockId && b.RowGroup = Some rg)
                    match partner with
                    | Some p ->
                        seen <- seen.Add block.BlockId
                        seen <- seen.Add p.BlockId
                        // Sort by RowPosition within the pair
                        let left, right =
                            if (block.RowPosition |> Option.defaultValue 0) <= (p.RowPosition |> Option.defaultValue 1)
                            then block, p
                            else p, block
                        RowPair (left, right)
                    | None ->
                        seen <- seen.Add block.BlockId
                        SingleBlock block
                | None ->
                    seen <- seen.Add block.BlockId
                    SingleBlock block
    ]

/// Returns true when a gap indicator should be suppressed (no-op for SingleBlocks adjacent to their own position)
let private shouldSuppressGap (gapIndex: int) (renderItems: RenderItem list) (draggedId: string option) =
    match draggedId with
    | None -> false
    | Some did ->
        renderItems
        |> List.tryFindIndex (fun item ->
            match item with
            | SingleBlock b -> b.BlockId = did
            | _ -> false)
        |> Option.map (fun ri -> gapIndex = ri || gapIndex = ri + 1)
        |> Option.defaultValue false

/// Maps a gap index (in render-item space) to a flat block list index
let private calculateInsertIndex (gapIndex: int) (renderItems: RenderItem list) =
    renderItems
    |> List.truncate gapIndex
    |> List.sumBy (fun item ->
        match item with
        | SingleBlock _ -> 1
        | RowPair _ -> 2)

[<ReactComponent>]
let view
    (blocks: ContentBlockDto list)
    (onAddBlock: AddContentBlockRequest -> unit)
    (onUpdateBlock: string -> UpdateContentBlockRequest -> unit)
    (onRemoveBlock: string -> unit)
    (onChangeBlockType: string -> string -> unit)
    (onReorderBlocks: string list -> unit)
    (onUploadScreenshot: (byte array -> string -> string option -> unit) option)
    (onGroupBlocks: (string -> string -> unit) option)
    (onUngroupBlock: (string -> unit) option)
    =
    let inputText, setInputText = React.useState("")
    let editingBlock, setEditingBlock = React.useState<EditingBlock option>(None)
    let menuBlockId, setMenuBlockId = React.useState<string option>(None)
    let draggedId, setDraggedId = React.useState<string option>(None)
    let dropLocation, setDropLocation = React.useState<DropLocation option>(None)

    let sortedBlocks = blocks |> List.sortBy (fun b -> b.Position)
    let renderItems = buildRenderItems sortedBlocks

    let startEditing (block: ContentBlockDto) =
        setEditingBlock (Some (editingFromBlock block))
        setInputText ""

    let cancelEditing () =
        setEditingBlock None

    let saveEditing (eb: EditingBlock) =
        let req : UpdateContentBlockRequest = {
            Content = eb.Content
            ImageRef = None
            Url = None
            Caption = None
        }
        onUpdateBlock eb.BlockId req
        cancelEditing ()

    let saveNewBlock (text: string) =
        let trimmed = text.Trim()
        if not (System.String.IsNullOrWhiteSpace trimmed) then
            let req : AddContentBlockRequest = {
                BlockType = "text"
                Content = trimmed
                ImageRef = None
                Url = None
                Caption = None
            }
            onAddBlock req
            setInputText ""

    let readFileAsBytes (file: Browser.Types.File) (insertBefore: string option) (callback: byte array -> string -> string option -> unit) =
        let reader = Browser.Dom.FileReader.Create()
        reader.onload <- fun _ ->
            let bytes : byte array = emitJsExpr reader.result "new Uint8Array($0)"
            callback bytes file.name insertBefore
        reader.readAsArrayBuffer(file)

    let handleDrop (targetBlockId: string) (zone: DropZone) (e: Browser.Types.DragEvent) =
        let files = e.dataTransfer.files
        let isFileDrop = draggedId.IsNone && files.length > 0
        match isFileDrop, onUploadScreenshot with
        | true, Some uploadFn ->
            let file = files.[0]
            let fileType : string = emitJsExpr file "$0.type"
            if fileType.StartsWith("image/") then
                readFileAsBytes file (Some targetBlockId) uploadFn
        | _ ->
            match draggedId, onGroupBlocks with
            | Some did, Some groupFn when did <> targetBlockId ->
                let draggedBlock = sortedBlocks |> List.tryFind (fun b -> b.BlockId = did)
                let targetBlock = sortedBlocks |> List.tryFind (fun b -> b.BlockId = targetBlockId)
                let sameGroup =
                    match draggedBlock, targetBlock with
                    | Some d, Some t -> d.RowGroup.IsSome && d.RowGroup = t.RowGroup
                    | _ -> false
                if sameGroup then
                    // Same RowGroup — always swap positions regardless of drop zone
                    match draggedBlock with
                    | Some d when (d.RowPosition |> Option.defaultValue 0) = 0 ->
                        groupFn targetBlockId did
                    | _ ->
                        groupFn did targetBlockId
                else
                    // Different groups — reorder and group by drop zone
                    let blockIds = sortedBlocks |> List.map (fun b -> b.BlockId)
                    let withoutDragged = blockIds |> List.filter (fun id -> id <> did)
                    let newOrder =
                        withoutDragged
                        |> List.collect (fun id ->
                            if id = targetBlockId then [did; id]
                            else [id])
                    onReorderBlocks newOrder
                    match zone with
                    | Left -> groupFn did targetBlockId
                    | Right -> groupFn targetBlockId did
            | _ -> ()
        setDraggedId None
        setDropLocation None

    let handleGapDrop (gapIndex: int) (e: Browser.Types.DragEvent) =
        let files = e.dataTransfer.files
        let isFileDrop = draggedId.IsNone && files.length > 0
        match isFileDrop, onUploadScreenshot with
        | true, Some uploadFn ->
            let file = files.[0]
            let fileType : string = emitJsExpr file "$0.type"
            if fileType.StartsWith("image/") then
                readFileAsBytes file None uploadFn
        | _ ->
            match draggedId with
            | Some did ->
                let blockIds = sortedBlocks |> List.map (fun b -> b.BlockId)
                let withoutDragged = blockIds |> List.filter (fun id -> id <> did)
                let insertPos = calculateInsertIndex gapIndex renderItems
                let draggedOrigPos = blockIds |> List.findIndex (fun id -> id = did)
                let adjustedPos = if draggedOrigPos < insertPos then insertPos - 1 else insertPos
                let adjustedPos = min adjustedPos withoutDragged.Length
                let before = withoutDragged |> List.take adjustedPos
                let after = withoutDragged |> List.skip adjustedPos
                let newOrder = before @ [did] @ after
                onReorderBlocks newOrder
                // Ungroup if the dragged block was in a RowGroup
                match onUngroupBlock with
                | Some ungroupFn ->
                    let draggedBlock = sortedBlocks |> List.tryFind (fun b -> b.BlockId = did)
                    match draggedBlock with
                    | Some b when b.RowGroup.IsSome -> ungroupFn did
                    | _ -> ()
                | None -> ()
            | None -> ()
        setDraggedId None
        setDropLocation None

    let renderSingleBlock (block: ContentBlockDto) =
        match editingBlock with
        | Some eb when eb.BlockId = block.BlockId ->
            Html.div [
                prop.key block.BlockId
                prop.className "group relative py-1"
                prop.children [
                    renderEditingBlock block eb setEditingBlock saveEditing cancelEditing
                ]
            ]
        | _ ->
            let dropZoneForBlock =
                match dropLocation with
                | Some (BlockDrop (bid, zone)) when bid = block.BlockId -> Some zone
                | _ -> None
            let borderClass =
                match dropZoneForBlock, onGroupBlocks with
                | Some Left, Some _ -> " border-l-4 border-primary/60"
                | Some Right, Some _ -> " border-r-4 border-primary/60"
                | Some _, None -> " border-t-2 border-primary/60"
                | None, _ -> ""
            Html.div [
                prop.key block.BlockId
                prop.className (
                    "group relative py-1" +
                    borderClass +
                    (if draggedId = Some block.BlockId then " opacity-40" else ""))
                prop.draggable true
                prop.onDragStart (fun e ->
                    e.dataTransfer.effectAllowed <- "move"
                    e.dataTransfer.setData("text/plain", block.BlockId) |> ignore
                    // Defer state update so the React re-render (which expands gap
                    // drop targets) doesn't cause a layout shift that cancels the
                    // browser's drag operation.
                    emitJsExpr (fun () -> setDraggedId (Some block.BlockId)) "setTimeout($0, 0)")
                prop.onDragEnd (fun _ ->
                    setDraggedId None
                    setDropLocation None)
                prop.onDragOver (fun e ->
                    if e.dataTransfer.files.length > 0 then
                        e.preventDefault ()
                        e.dataTransfer.dropEffect <- "copy"
                    elif draggedId.IsSome && draggedId <> Some block.BlockId then
                        e.preventDefault ()
                        e.dataTransfer.dropEffect <- "move"
                        if onGroupBlocks.IsSome then
                            let zone = detectDropZone e
                            setDropLocation (Some (BlockDrop (block.BlockId, zone)))
                        else
                            setDropLocation (Some (BlockDrop (block.BlockId, Left))))
                prop.onDragLeave (fun _ ->
                    setDropLocation None)
                prop.onDrop (fun e ->
                    e.preventDefault ()
                    if onGroupBlocks.IsSome then
                        // Detect zone directly — React state from onDragOver may be stale
                        let zone = detectDropZone e
                        handleDrop block.BlockId zone e
                    else
                        // No grouping — handle file drops and simple reorder
                        if draggedId.IsNone && e.dataTransfer.files.length > 0 then
                            match onUploadScreenshot with
                            | Some uploadFn ->
                                let file = e.dataTransfer.files.[0]
                                let fileType : string = emitJsExpr file "$0.type"
                                if fileType.StartsWith("image/") then
                                    readFileAsBytes file (Some block.BlockId) uploadFn
                            | None -> ()
                        else
                            // Simple reorder: insert dragged block before target
                            match draggedId with
                            | Some did when did <> block.BlockId ->
                                let blockIds = sortedBlocks |> List.map (fun b -> b.BlockId)
                                let withoutDragged = blockIds |> List.filter (fun id -> id <> did)
                                let newOrder =
                                    withoutDragged
                                    |> List.collect (fun id ->
                                        if id = block.BlockId then [did; id]
                                        else [id])
                                onReorderBlocks newOrder
                            | _ -> ()
                        setDraggedId None
                        setDropLocation None)
                prop.children [
                    // Drag handle (overhanging left side)
                    // Force visible when menu is open so the handle doesn't vanish
                    let handleVisible = menuBlockId = Some block.BlockId
                    Html.div [
                        prop.className (
                            "absolute -left-7 top-1 transition-opacity z-10" +
                            (if handleVisible then " opacity-100"
                             else " opacity-0 group-hover:opacity-100"))
                        prop.children [
                            Html.button [
                                prop.className "w-5 h-5 flex items-center justify-center text-base-content/30 hover:text-base-content/60 cursor-grab transition-colors"
                                prop.onClick (fun e ->
                                    e.stopPropagation ()
                                    if handleVisible then
                                        setMenuBlockId None
                                    else
                                        setMenuBlockId (Some block.BlockId))
                                prop.title "Drag to reorder, click for options"
                                prop.children [ Icons.gripVertical () ]
                            ]
                        ]
                    ]
                    // Context menu — rendered as sibling (not inside the
                    // opacity-controlled handle) so backdrop-filter works
                    if menuBlockId = Some block.BlockId then
                        let ungroupOption =
                            match block.RowGroup, onUngroupBlock with
                            | Some _, Some ungroupFn -> Some (fun () -> ungroupFn block.BlockId)
                            | _ -> None
                        contextMenu
                            block
                            (fun () -> onRemoveBlock block.BlockId)
                            (fun newType -> onChangeBlockType block.BlockId newType)
                            ungroupOption
                            (fun () -> setMenuBlockId None)
                    // Block content — click to edit (not for screenshots)
                    if block.BlockType = "screenshot" then
                        Html.div [
                            prop.children [ renderBlockContent block ]
                        ]
                    else
                        Html.div [
                            prop.className "cursor-text"
                            prop.onClick (fun _ -> startEditing block)
                            prop.children [ renderBlockContent block ]
                        ]
                ]
            ]

    let renderGap (gapIndex: int) =
        let isActive =
            match dropLocation with
            | Some (GapDrop gi) when gi = gapIndex -> true
            | _ -> false
        let isSuppressed = shouldSuppressGap gapIndex renderItems draggedId
        Html.div [
            prop.key $"gap-{gapIndex}"
            prop.className (
                "relative" +
                (if draggedId.IsSome && not isSuppressed then " py-2" else ""))
            prop.onDragOver (fun e ->
                if not isSuppressed then
                    e.preventDefault ()
                    if e.dataTransfer.files.length > 0 then
                        e.dataTransfer.dropEffect <- "copy"
                    else
                        e.dataTransfer.dropEffect <- "move"
                    setDropLocation (Some (GapDrop gapIndex)))
            prop.onDragLeave (fun _ ->
                setDropLocation None)
            prop.onDrop (fun e ->
                e.preventDefault ()
                handleGapDrop gapIndex e)
            prop.children [
                if isActive then
                    Html.div [
                        prop.className "absolute left-0 right-0 top-1/2 -translate-y-1/2 flex items-center"
                        prop.children [
                            Html.div [
                                prop.className "w-2 h-2 rounded-full bg-success -ml-1 flex-shrink-0"
                            ]
                            Html.div [
                                prop.className "h-[3px] bg-success rounded-full flex-grow"
                            ]
                        ]
                    ]
            ]
        ]

    Html.div [
        prop.className "space-y-1"
        prop.children [
            renderGap 0
            for i in 0 .. renderItems.Length - 1 do
                match renderItems.[i] with
                | SingleBlock block ->
                    renderSingleBlock block
                | RowPair (left, right) ->
                    Html.div [
                        prop.key $"row-{left.BlockId}-{right.BlockId}"
                        prop.className "flex flex-col sm:flex-row gap-4"
                        prop.children [
                            Html.div [
                                prop.className "flex-1 min-w-0"
                                prop.children [ renderSingleBlock left ]
                            ]
                            Html.div [
                                prop.className "flex-1 min-w-0"
                                prop.children [ renderSingleBlock right ]
                            ]
                        ]
                    ]
                renderGap (i + 1)

            // Always-visible new block placeholder
            Html.div [
                prop.className "py-1"
                prop.onDragOver (fun e ->
                    if e.dataTransfer.files.length > 0 then
                        e.preventDefault ()
                        e.dataTransfer.dropEffect <- "copy")
                prop.onDrop (fun e ->
                    match onUploadScreenshot with
                    | Some uploadFn when e.dataTransfer.files.length > 0 ->
                        e.preventDefault ()
                        let file = e.dataTransfer.files.[0]
                        let fileType : string = emitJsExpr file "$0.type"
                        if fileType.StartsWith("image/") then
                            readFileAsBytes file None uploadFn
                    | _ -> ())
                prop.children [
                    Html.input [
                        prop.className "w-full bg-transparent outline-none text-sm text-base-content/80 placeholder:text-base-content/20 placeholder:italic"
                        prop.placeholder "new block"
                        prop.value inputText
                        prop.onChange setInputText
                        prop.onKeyDown (fun e ->
                            match e.key with
                            | "Enter" ->
                                e.preventDefault ()
                                saveNewBlock inputText
                            | _ -> ())
                        prop.onPaste (fun (e: Browser.Types.ClipboardEvent) ->
                            smartPasteHandler inputText setInputText e)
                    ]
                ]
            ]
        ]
    ]
