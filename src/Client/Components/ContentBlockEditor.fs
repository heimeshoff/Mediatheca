module Mediatheca.Client.Components.ContentBlockEditor

open Fable.Core.JsInterop
open Feliz
open Mediatheca.Shared

type private EditingBlock = {
    BlockId: string
    Content: string
    Url: string
}

let private editingFromBlock (block: ContentBlockDto) = {
    BlockId = block.BlockId
    Content = block.Content
    Url = block.Url |> Option.defaultValue ""
}

let private optionIfNotEmpty (s: string) =
    if System.String.IsNullOrWhiteSpace(s) then None else Some s

let private isUrl (text: string) =
    let trimmed = text.Trim()
    System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^https?://[^\s]+$")

let private cardClass = "bg-base-100/50 backdrop-blur-sm p-4 rounded-xl border border-base-content/5"

let private textNoteCard
    (block: ContentBlockDto)
    (onEdit: unit -> unit)
    (onRemove: unit -> unit) =
    Html.div [
        prop.key block.BlockId
        prop.className (cardClass + " group relative")
        prop.children [
            Html.p [
                prop.className "text-sm text-base-content/80 whitespace-pre-wrap"
                prop.text block.Content
            ]
            Html.div [
                prop.className "absolute top-2 right-2 flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
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

let private linkNoteCard
    (block: ContentBlockDto)
    (onEdit: unit -> unit)
    (onRemove: unit -> unit) =
    Html.div [
        prop.key block.BlockId
        prop.className (cardClass + " group relative")
        prop.children [
            match block.Url with
            | Some url ->
                Html.a [
                    prop.href url
                    prop.target "_blank"
                    prop.rel "noopener noreferrer"
                    prop.className "text-sm link link-primary underline"
                    prop.text (if System.String.IsNullOrWhiteSpace(block.Content) then url else block.Content)
                ]
            | None ->
                Html.p [
                    prop.className "text-sm text-base-content/80"
                    prop.text block.Content
                ]
            Html.div [
                prop.className "absolute top-2 right-2 flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
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
    let isAdding, setIsAdding = React.useState(false)
    let inputText, setInputText = React.useState("")
    let editingBlockId, setEditingBlockId = React.useState<string option>(None)
    let editingBlock, setEditingBlock = React.useState<EditingBlock option>(None)
    // For link editing: separate fields
    let editUrl, setEditUrl = React.useState("")

    let sortedBlocks = blocks |> List.sortBy (fun b -> b.Position)

    let startEditing (block: ContentBlockDto) =
        setEditingBlockId (Some block.BlockId)
        let eb = editingFromBlock block
        setEditingBlock (Some eb)
        setEditUrl eb.Url
        setIsAdding false

    let cancelEditing () =
        setEditingBlockId None
        setEditingBlock None
        setEditUrl ""

    let saveEditing (eb: EditingBlock) (url: string) =
        let req : UpdateContentBlockRequest = {
            Content = eb.Content
            ImageRef = None
            Url = optionIfNotEmpty url
            Caption = None
        }
        onUpdateBlock eb.BlockId req
        cancelEditing ()

    let cancelAdding () =
        setIsAdding false
        setInputText ""

    let saveNewBlock (text: string) =
        let trimmed = text.Trim()
        if not (System.String.IsNullOrWhiteSpace trimmed) then
            if isUrl trimmed then
                let req : AddContentBlockRequest = {
                    BlockType = "link"
                    Content = ""
                    ImageRef = None
                    Url = Some trimmed
                    Caption = None
                }
                onAddBlock req
            else
                let req : AddContentBlockRequest = {
                    BlockType = "text"
                    Content = trimmed
                    ImageRef = None
                    Url = None
                    Caption = None
                }
                onAddBlock req
            cancelAdding ()

    let saveAsLink (displayText: string) (url: string) =
        let req : AddContentBlockRequest = {
            BlockType = "link"
            Content = displayText
            ImageRef = None
            Url = Some (url.Trim())
            Caption = None
        }
        onAddBlock req
        cancelAdding ()

    Html.div [
        prop.className "space-y-3"
        prop.children [
            for block in sortedBlocks do
                match editingBlockId, editingBlock with
                | Some eid, Some eb when eid = block.BlockId ->
                    if block.BlockType = "link" then
                        // Link editing: two fields
                        Html.div [
                            prop.key block.BlockId
                            prop.className (cardClass + " space-y-2")
                            prop.children [
                                Html.input [
                                    prop.className "w-full bg-transparent outline-none text-sm"
                                    prop.placeholder "Display text..."
                                    prop.autoFocus true
                                    prop.value eb.Content
                                    prop.onChange (fun (v: string) -> setEditingBlock (Some { eb with Content = v }))
                                    prop.onKeyDown (fun e ->
                                        match e.key with
                                        | "Escape" -> cancelEditing ()
                                        | _ -> ())
                                ]
                                Html.input [
                                    prop.className "w-full bg-transparent outline-none text-sm text-base-content/60"
                                    prop.placeholder "URL..."
                                    prop.value editUrl
                                    prop.onChange (fun (v: string) -> setEditUrl v)
                                    prop.onKeyDown (fun e ->
                                        match e.key with
                                        | "Enter" ->
                                            e.preventDefault ()
                                            saveEditing eb editUrl
                                        | "Escape" -> cancelEditing ()
                                        | _ -> ())
                                ]
                                Html.div [
                                    prop.className "flex gap-2 justify-end"
                                    prop.children [
                                        Html.button [
                                            prop.className "text-xs text-base-content/40 hover:text-base-content cursor-pointer"
                                            prop.onClick (fun _ -> cancelEditing ())
                                            prop.text "Cancel"
                                        ]
                                        Html.button [
                                            prop.className "text-xs text-primary hover:text-primary-focus cursor-pointer"
                                            prop.onClick (fun _ -> saveEditing eb editUrl)
                                            prop.text "Save"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    else
                        // Text editing: single field
                        Html.div [
                            prop.key block.BlockId
                            prop.className cardClass
                            prop.children [
                                Html.input [
                                    prop.className "w-full bg-transparent outline-none text-sm"
                                    prop.autoFocus true
                                    prop.value eb.Content
                                    prop.onChange (fun (v: string) -> setEditingBlock (Some { eb with Content = v }))
                                    prop.onKeyDown (fun e ->
                                        match e.key with
                                        | "Enter" ->
                                            e.preventDefault ()
                                            saveEditing eb ""
                                        | "Escape" -> cancelEditing ()
                                        | _ -> ())
                                ]
                            ]
                        ]
                | _ ->
                    match block.BlockType with
                    | "link" ->
                        linkNoteCard block (fun () -> startEditing block) (fun () -> onRemoveBlock block.BlockId)
                    | _ ->
                        textNoteCard block (fun () -> startEditing block) (fun () -> onRemoveBlock block.BlockId)

            // Add-note card
            if isAdding then
                Html.div [
                    prop.className cardClass
                    prop.children [
                        Html.input [
                            prop.className "w-full bg-transparent outline-none text-sm"
                            prop.placeholder "Add note..."
                            prop.autoFocus true
                            prop.value inputText
                            prop.onChange setInputText
                            prop.onKeyDown (fun e ->
                                match e.key with
                                | "Enter" ->
                                    e.preventDefault ()
                                    saveNewBlock inputText
                                | "Escape" -> cancelAdding ()
                                | _ -> ())
                            prop.onPaste (fun (e: Browser.Types.ClipboardEvent) ->
                                let clipboardText = e.clipboardData.getData("text")
                                if isUrl (clipboardText.Trim()) then
                                    let target : Browser.Types.HTMLInputElement = unbox e.target
                                    let selStart : int = emitJsExpr target "$0.selectionStart"
                                    let selEnd : int = emitJsExpr target "$0.selectionEnd"
                                    if selStart <> selEnd then
                                        e.preventDefault ()
                                        let selectedText = inputText.[selStart..selEnd - 1]
                                        saveAsLink selectedText (clipboardText.Trim()))
                        ]
                    ]
                ]
            else
                Html.div [
                    prop.className (cardClass + " cursor-pointer hover:border-base-content/20 transition-colors flex items-center justify-center min-h-[3rem]")
                    prop.onClick (fun _ -> setIsAdding true)
                    prop.children [
                        Html.span [
                            prop.className "text-xl text-base-content/30"
                            prop.text "+"
                        ]
                    ]
                ]
        ]
    ]
