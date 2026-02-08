module Mediatheca.Client.Components.ContentBlockEditor

open Feliz
open Feliz.DaisyUI
open Mediatheca.Shared

type private EditingBlock = {
    BlockId: string
    Content: string
    ImageRef: string
    Url: string
    Caption: string
}

type private AddingBlockType =
    | Adding_text
    | Adding_image
    | Adding_link

type private AddingBlock = {
    BlockType: AddingBlockType
    Content: string
    ImageRef: string
    Url: string
    Caption: string
}

let private emptyAdding (blockType: AddingBlockType) = {
    BlockType = blockType
    Content = ""
    ImageRef = ""
    Url = ""
    Caption = ""
}

let private editingFromBlock (block: ContentBlockDto) = {
    BlockId = block.BlockId
    Content = block.Content
    ImageRef = block.ImageRef |> Option.defaultValue ""
    Url = block.Url |> Option.defaultValue ""
    Caption = block.Caption |> Option.defaultValue ""
}

let private optionIfNotEmpty (s: string) =
    if System.String.IsNullOrWhiteSpace(s) then None else Some s

let private textBlockView
    (block: ContentBlockDto)
    (onEdit: unit -> unit)
    (onRemove: unit -> unit) =
    Html.div [
        prop.className "bg-base-200 rounded-lg p-4 group"
        prop.children [
            Html.p [
                prop.className "text-base-content/80 whitespace-pre-wrap"
                prop.text block.Content
            ]
            Html.div [
                prop.className "flex gap-1 mt-2 opacity-0 group-hover:opacity-100 transition-opacity"
                prop.children [
                    Daisy.button.button [
                        button.xs
                        button.ghost
                        prop.onClick (fun _ -> onEdit ())
                        prop.text "Edit"
                    ]
                    Daisy.button.button [
                        button.xs
                        button.ghost
                        prop.onClick (fun _ -> onRemove ())
                        prop.text "Delete"
                    ]
                ]
            ]
        ]
    ]

let private imageBlockView
    (block: ContentBlockDto)
    (onEdit: unit -> unit)
    (onRemove: unit -> unit) =
    Html.div [
        prop.className "bg-base-200 rounded-lg p-4 group"
        prop.children [
            match block.ImageRef with
            | Some ref ->
                Html.img [
                    prop.src $"/images/{ref}"
                    prop.alt (block.Caption |> Option.defaultValue "Image")
                    prop.className "rounded-lg max-w-full max-h-64 object-contain"
                ]
            | None ->
                Html.div [
                    prop.className "w-full h-32 bg-base-300 rounded-lg flex items-center justify-center text-base-content/30"
                    prop.text "No image"
                ]
            match block.Caption with
            | Some caption ->
                Html.p [
                    prop.className "text-sm text-base-content/60 mt-2 italic"
                    prop.text caption
                ]
            | None -> ()
            if not (System.String.IsNullOrWhiteSpace(block.Content)) then
                Html.p [
                    prop.className "text-base-content/80 mt-2"
                    prop.text block.Content
                ]
            Html.div [
                prop.className "flex gap-1 mt-2 opacity-0 group-hover:opacity-100 transition-opacity"
                prop.children [
                    Daisy.button.button [
                        button.xs
                        button.ghost
                        prop.onClick (fun _ -> onEdit ())
                        prop.text "Edit"
                    ]
                    Daisy.button.button [
                        button.xs
                        button.ghost
                        prop.onClick (fun _ -> onRemove ())
                        prop.text "Delete"
                    ]
                ]
            ]
        ]
    ]

let private linkBlockView
    (block: ContentBlockDto)
    (onEdit: unit -> unit)
    (onRemove: unit -> unit) =
    Html.div [
        prop.className "bg-base-200 rounded-lg p-4 group"
        prop.children [
            match block.Url with
            | Some url ->
                Html.a [
                    prop.href url
                    prop.target "_blank"
                    prop.rel "noopener noreferrer"
                    prop.className "link link-primary font-semibold"
                    prop.text url
                ]
            | None -> ()
            if not (System.String.IsNullOrWhiteSpace(block.Content)) then
                Html.p [
                    prop.className "text-base-content/80 mt-1"
                    prop.text block.Content
                ]
            match block.Caption with
            | Some caption ->
                Html.p [
                    prop.className "text-sm text-base-content/60 mt-1 italic"
                    prop.text caption
                ]
            | None -> ()
            Html.div [
                prop.className "flex gap-1 mt-2 opacity-0 group-hover:opacity-100 transition-opacity"
                prop.children [
                    Daisy.button.button [
                        button.xs
                        button.ghost
                        prop.onClick (fun _ -> onEdit ())
                        prop.text "Edit"
                    ]
                    Daisy.button.button [
                        button.xs
                        button.ghost
                        prop.onClick (fun _ -> onRemove ())
                        prop.text "Delete"
                    ]
                ]
            ]
        ]
    ]

let private editBlockForm
    (editing: EditingBlock)
    (blockType: string)
    (onSave: EditingBlock -> unit)
    (onCancel: unit -> unit)
    (onChange: EditingBlock -> unit) =
    Html.div [
        prop.className "bg-base-200 rounded-lg p-4 space-y-3"
        prop.children [
            if blockType = "text" || blockType = "link" then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text (if blockType = "text" then "Content" else "Description")
                                ]
                            ]
                        ]
                        Daisy.textarea [
                            prop.className "w-full"
                            prop.placeholder (if blockType = "text" then "Enter text..." else "Enter description...")
                            prop.value editing.Content
                            prop.onChange (fun (v: string) -> onChange { editing with Content = v })
                        ]
                    ]
                ]
            if blockType = "image" then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "Image Reference"
                                ]
                            ]
                        ]
                        Daisy.input [
                            prop.className "w-full"
                            prop.placeholder "Image reference..."
                            prop.value editing.ImageRef
                            prop.onChange (fun (v: string) -> onChange { editing with ImageRef = v })
                        ]
                    ]
                ]
            if blockType = "image" then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "Description"
                                ]
                            ]
                        ]
                        Daisy.textarea [
                            prop.className "w-full"
                            prop.placeholder "Enter description..."
                            prop.value editing.Content
                            prop.onChange (fun (v: string) -> onChange { editing with Content = v })
                        ]
                    ]
                ]
            if blockType = "link" then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "URL"
                                ]
                            ]
                        ]
                        Daisy.input [
                            prop.className "w-full"
                            prop.placeholder "https://..."
                            prop.value editing.Url
                            prop.onChange (fun (v: string) -> onChange { editing with Url = v })
                        ]
                    ]
                ]
            if blockType = "image" || blockType = "link" then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "Caption"
                                ]
                            ]
                        ]
                        Daisy.input [
                            prop.className "w-full"
                            prop.placeholder "Optional caption..."
                            prop.value editing.Caption
                            prop.onChange (fun (v: string) -> onChange { editing with Caption = v })
                        ]
                    ]
                ]
            Html.div [
                prop.className "flex gap-2 justify-end"
                prop.children [
                    Daisy.button.button [
                        button.sm
                        button.ghost
                        prop.onClick (fun _ -> onCancel ())
                        prop.text "Cancel"
                    ]
                    Daisy.button.button [
                        button.sm
                        button.primary
                        prop.onClick (fun _ -> onSave editing)
                        prop.text "Save"
                    ]
                ]
            ]
        ]
    ]

let private addBlockForm
    (adding: AddingBlock)
    (onSave: AddingBlock -> unit)
    (onCancel: unit -> unit)
    (onChange: AddingBlock -> unit) =
    let blockTypeStr =
        match adding.BlockType with
        | Adding_text -> "text"
        | Adding_image -> "image"
        | Adding_link -> "link"
    let label =
        match adding.BlockType with
        | Adding_text -> "New Text Block"
        | Adding_image -> "New Image Block"
        | Adding_link -> "New Link Block"
    Html.div [
        prop.className "bg-base-200 rounded-lg p-4 space-y-3"
        prop.children [
            Html.h3 [
                prop.className "font-semibold text-sm"
                prop.text label
            ]
            if adding.BlockType = Adding_text || adding.BlockType = Adding_link then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text (if adding.BlockType = Adding_text then "Content" else "Description")
                                ]
                            ]
                        ]
                        Daisy.textarea [
                            prop.className "w-full"
                            prop.placeholder (if adding.BlockType = Adding_text then "Enter text..." else "Enter description...")
                            prop.value adding.Content
                            prop.onChange (fun (v: string) -> onChange { adding with Content = v })
                        ]
                    ]
                ]
            if adding.BlockType = Adding_image then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "Image Reference"
                                ]
                            ]
                        ]
                        Daisy.input [
                            prop.className "w-full"
                            prop.placeholder "Image reference..."
                            prop.value adding.ImageRef
                            prop.onChange (fun (v: string) -> onChange { adding with ImageRef = v })
                        ]
                    ]
                ]
            if adding.BlockType = Adding_image then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "Description"
                                ]
                            ]
                        ]
                        Daisy.textarea [
                            prop.className "w-full"
                            prop.placeholder "Enter description..."
                            prop.value adding.Content
                            prop.onChange (fun (v: string) -> onChange { adding with Content = v })
                        ]
                    ]
                ]
            if adding.BlockType = Adding_link then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "URL"
                                ]
                            ]
                        ]
                        Daisy.input [
                            prop.className "w-full"
                            prop.placeholder "https://..."
                            prop.value adding.Url
                            prop.onChange (fun (v: string) -> onChange { adding with Url = v })
                        ]
                    ]
                ]
            if adding.BlockType = Adding_image || adding.BlockType = Adding_link then
                Html.div [
                    prop.children [
                        Html.label [
                            prop.className "label"
                            prop.children [
                                Html.span [
                                    prop.className "label-text"
                                    prop.text "Caption"
                                ]
                            ]
                        ]
                        Daisy.input [
                            prop.className "w-full"
                            prop.placeholder "Optional caption..."
                            prop.value adding.Caption
                            prop.onChange (fun (v: string) -> onChange { adding with Caption = v })
                        ]
                    ]
                ]
            Html.div [
                prop.className "flex gap-2 justify-end"
                prop.children [
                    Daisy.button.button [
                        button.sm
                        button.ghost
                        prop.onClick (fun _ -> onCancel ())
                        prop.text "Cancel"
                    ]
                    Daisy.button.button [
                        button.sm
                        button.primary
                        prop.onClick (fun _ -> onSave adding)
                        prop.text "Add"
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
    let editingBlockId, setEditingBlockId = React.useState<string option>(None)
    let editingBlock, setEditingBlock = React.useState<EditingBlock option>(None)
    let addingBlock, setAddingBlock = React.useState<AddingBlock option>(None)

    let sortedBlocks = blocks |> List.sortBy (fun b -> b.Position)

    let startEditing (block: ContentBlockDto) =
        setEditingBlockId (Some block.BlockId)
        setEditingBlock (Some (editingFromBlock block))
        setAddingBlock None

    let cancelEditing () =
        setEditingBlockId None
        setEditingBlock None

    let saveEditing (eb: EditingBlock) (blockType: string) =
        let req : UpdateContentBlockRequest = {
            Content = eb.Content
            ImageRef = optionIfNotEmpty eb.ImageRef
            Url = optionIfNotEmpty eb.Url
            Caption = optionIfNotEmpty eb.Caption
        }
        onUpdateBlock eb.BlockId req
        cancelEditing ()

    let startAdding (blockType: AddingBlockType) =
        setAddingBlock (Some (emptyAdding blockType))
        cancelEditing ()

    let cancelAdding () =
        setAddingBlock None

    let saveAdding (ab: AddingBlock) =
        let blockTypeStr =
            match ab.BlockType with
            | Adding_text -> "text"
            | Adding_image -> "image"
            | Adding_link -> "link"
        let req : AddContentBlockRequest = {
            BlockType = blockTypeStr
            Content = ab.Content
            ImageRef = optionIfNotEmpty ab.ImageRef
            Url = optionIfNotEmpty ab.Url
            Caption = optionIfNotEmpty ab.Caption
        }
        onAddBlock req
        cancelAdding ()

    Html.div [
        prop.className "space-y-3"
        prop.children [
            if List.isEmpty sortedBlocks && Option.isNone addingBlock then
                Html.p [
                    prop.className "text-base-content/50 text-sm"
                    prop.text "No notes yet. Add a text block, image, or link."
                ]

            for block in sortedBlocks do
                match editingBlockId, editingBlock with
                | Some eid, Some eb when eid = block.BlockId ->
                    editBlockForm
                        eb
                        block.BlockType
                        (fun eb -> saveEditing eb block.BlockType)
                        cancelEditing
                        (fun eb -> setEditingBlock (Some eb))
                | _ ->
                    match block.BlockType with
                    | "text" ->
                        textBlockView block (fun () -> startEditing block) (fun () -> onRemoveBlock block.BlockId)
                    | "image" ->
                        imageBlockView block (fun () -> startEditing block) (fun () -> onRemoveBlock block.BlockId)
                    | "link" ->
                        linkBlockView block (fun () -> startEditing block) (fun () -> onRemoveBlock block.BlockId)
                    | _ ->
                        textBlockView block (fun () -> startEditing block) (fun () -> onRemoveBlock block.BlockId)

            // Add block form or buttons
            match addingBlock with
            | Some ab ->
                addBlockForm
                    ab
                    saveAdding
                    cancelAdding
                    (fun ab -> setAddingBlock (Some ab))
            | None ->
                Html.div [
                    prop.className "flex gap-2"
                    prop.children [
                        Daisy.button.button [
                            button.sm
                            button.ghost
                            prop.onClick (fun _ -> startAdding Adding_text)
                            prop.text "+ Text"
                        ]
                        Daisy.button.button [
                            button.sm
                            button.ghost
                            prop.onClick (fun _ -> startAdding Adding_image)
                            prop.text "+ Image"
                        ]
                        Daisy.button.button [
                            button.sm
                            button.ghost
                            prop.onClick (fun _ -> startAdding Adding_link)
                            prop.text "+ Link"
                        ]
                    ]
                ]
        ]
    ]
