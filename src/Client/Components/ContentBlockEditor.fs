module Mediatheca.Client.Components.ContentBlockEditor

open Fable.Core.JsInterop
open Feliz
open Mediatheca.Shared

type private EditingBlock = {
    BlockId: string
    Content: string
}

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

[<ReactComponent>]
let view
    (blocks: ContentBlockDto list)
    (onAddBlock: AddContentBlockRequest -> unit)
    (onUpdateBlock: string -> UpdateContentBlockRequest -> unit)
    (onRemoveBlock: string -> unit)
    (onChangeBlockType: string -> string -> unit)
    (onReorderBlocks: string list -> unit)
    =
    let inputText, setInputText = React.useState("")
    let editingBlock, setEditingBlock = React.useState<EditingBlock option>(None)
    let menuBlockId, setMenuBlockId = React.useState<string option>(None)
    let draggedId, setDraggedId = React.useState<string option>(None)
    let dropTargetId, setDropTargetId = React.useState<string option>(None)

    let sortedBlocks = blocks |> List.sortBy (fun b -> b.Position)

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

    let handleDrop (targetBlockId: string) =
        match draggedId with
        | Some did when did <> targetBlockId ->
            let blockIds = sortedBlocks |> List.map (fun b -> b.BlockId)
            let withoutDragged = blockIds |> List.filter (fun id -> id <> did)
            let newOrder =
                withoutDragged
                |> List.collect (fun id ->
                    if id = targetBlockId then [did; id]
                    else [id])
            onReorderBlocks newOrder
        | _ -> ()
        setDraggedId None
        setDropTargetId None

    Html.div [
        prop.className "space-y-1"
        prop.children [
            for block in sortedBlocks do
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
                    let isDragOver = dropTargetId = Some block.BlockId
                    Html.div [
                        prop.key block.BlockId
                        prop.className (
                            "group relative py-1" +
                            (if isDragOver then " border-t-2 border-primary/60" else "") +
                            (if draggedId = Some block.BlockId then " opacity-40" else ""))
                        prop.draggable true
                        prop.onDragStart (fun e ->
                            e.dataTransfer.effectAllowed <- "move"
                            e.dataTransfer.setData("text/plain", block.BlockId) |> ignore
                            setDraggedId (Some block.BlockId))
                        prop.onDragEnd (fun _ ->
                            setDraggedId None
                            setDropTargetId None)
                        prop.onDragOver (fun e ->
                            e.preventDefault ()
                            e.dataTransfer.dropEffect <- "move"
                            setDropTargetId (Some block.BlockId))
                        prop.onDragLeave (fun _ ->
                            setDropTargetId None)
                        prop.onDrop (fun e ->
                            e.preventDefault ()
                            handleDrop block.BlockId)
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
                                contextMenu
                                    block
                                    (fun () -> onRemoveBlock block.BlockId)
                                    (fun newType -> onChangeBlockType block.BlockId newType)
                                    (fun () -> setMenuBlockId None)
                            // Block content — click to edit
                            Html.div [
                                prop.className "cursor-text"
                                prop.onClick (fun _ -> startEditing block)
                                prop.children [ renderBlockContent block ]
                            ]
                        ]
                    ]

            // Always-visible new block placeholder
            Html.div [
                prop.className "py-1"
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
