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

let private textBlock
    (block: ContentBlockDto)
    (onEdit: unit -> unit)
    (onRemove: unit -> unit) =
    Html.div [
        prop.key block.BlockId
        prop.className "group relative py-1"
        prop.children [
            Html.p [
                prop.className "text-sm text-base-content/80 whitespace-pre-wrap"
                prop.children [ renderContent (getDisplayContent block) ]
            ]
            Html.div [
                prop.className "absolute top-1 right-0 flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
                prop.children [
                    Html.button [
                        prop.className "w-6 h-6 rounded flex items-center justify-center text-xs text-base-content/40 hover:text-primary hover:bg-primary/10 transition-colors cursor-pointer"
                        prop.onClick (fun _ -> onEdit ())
                        prop.title "Edit"
                        prop.text "\u270E"
                    ]
                    Html.button [
                        prop.className "w-6 h-6 rounded flex items-center justify-center text-xs text-base-content/40 hover:text-error hover:bg-error/10 transition-colors cursor-pointer"
                        prop.onClick (fun _ -> onRemove ())
                        prop.title "Delete"
                        prop.text "\u00D7"
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
    =
    let inputText, setInputText = React.useState("")
    let editingBlock, setEditingBlock = React.useState<EditingBlock option>(None)

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

    Html.div [
        prop.className "space-y-1"
        prop.children [
            for block in sortedBlocks do
                match editingBlock with
                | Some eb when eb.BlockId = block.BlockId ->
                    Html.div [
                        prop.key block.BlockId
                        prop.className "py-1"
                        prop.children [
                            Html.input [
                                prop.className "w-full bg-transparent outline-none text-sm text-base-content/80"
                                prop.autoFocus true
                                prop.value eb.Content
                                prop.onChange (fun (v: string) -> setEditingBlock (Some { eb with Content = v }))
                                prop.onKeyDown (fun e ->
                                    match e.key with
                                    | "Enter" ->
                                        e.preventDefault ()
                                        saveEditing eb
                                    | "Escape" -> cancelEditing ()
                                    | _ -> ())
                                prop.onPaste (fun (e: Browser.Types.ClipboardEvent) ->
                                    smartPasteHandler eb.Content (fun v -> setEditingBlock (Some { eb with Content = v })) e)
                            ]
                        ]
                    ]
                | _ ->
                    textBlock block (fun () -> startEditing block) (fun () -> onRemoveBlock block.BlockId)

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
