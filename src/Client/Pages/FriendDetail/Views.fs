module Mediatheca.Client.Pages.FriendDetail.Views

open Fable.Core
open Fable.Core.JsInterop
open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.FriendDetail.Types
open Mediatheca.Client
open Mediatheca.Client.Components

let private formatDateOnly (date: string) =
    match date.IndexOf('T') with
    | -1 -> date
    | i -> date.[..i-1]

let private readFileAsBytes (file: Browser.Types.File) (onDone: byte array * string -> unit) =
    let reader = Browser.Dom.FileReader.Create()
    reader.onload <- fun _ ->
        let result: obj = unbox reader.result
        let uint8Array: byte array = emitJsExpr result "new Uint8Array($0)"
        onDone (uint8Array, file.name)
    reader.readAsArrayBuffer(file)

let private routeForMedia (mediaType: MediaType) = match mediaType with | Movie -> "movies" | Series -> "series" | Game -> "games"

let private mediaListRow (onRemove: (string * string) -> unit) (item: EntryList.EntryItem) =
    Html.div [
        prop.className "flex items-center gap-3 p-3 rounded-xl bg-base-100 hover:bg-base-200/80 transition-colors group"
        prop.children [
            Html.a [
                prop.href (Router.format (item.RoutePrefix, item.Slug))
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate (item.RoutePrefix, item.Slug))
                prop.className "flex items-center gap-3 flex-1 min-w-0 cursor-pointer"
                prop.children [
                    Html.div [
                        prop.className "flex-none"
                        prop.children [ PosterCard.thumbnail item.PosterRef item.Name ]
                    ]
                    Html.div [
                        prop.className "flex-1 min-w-0"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center gap-2"
                                prop.children [
                                    Html.p [
                                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                        prop.text item.Name
                                    ]
                                    if item.RoutePrefix = "series" then
                                        Html.span [
                                            prop.className "badge badge-xs badge-outline badge-warning flex-none"
                                            prop.text "Series"
                                        ]
                                    elif item.RoutePrefix = "games" then
                                        Html.span [
                                            prop.className "badge badge-xs badge-outline badge-info flex-none"
                                            prop.text "Game"
                                        ]
                                ]
                            ]
                            Html.p [
                                prop.className "text-xs text-base-content/50"
                                prop.text (string item.Year)
                            ]
                        ]
                    ]
                ]
            ]
            Html.button [
                prop.className "text-base-content/30 opacity-0 group-hover:opacity-100 transition-opacity text-xs hover:text-error flex-none"
                prop.onClick (fun _ -> onRemove (item.Slug, item.RoutePrefix))
                prop.text "\u00D7"
            ]
        ]
    ]

let private watchedMediaListRow (watchedBySlug: Map<string, FriendWatchedItem>) (item: EntryList.EntryItem) =
    Html.a [
        prop.href (Router.format (item.RoutePrefix, item.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate (item.RoutePrefix, item.Slug))
        prop.children [
            Html.div [
                prop.className "flex items-center gap-3 p-3 rounded-xl bg-base-100 hover:bg-base-200/80 transition-colors cursor-pointer group"
                prop.children [
                    Html.div [
                        prop.className "flex-none"
                        prop.children [ PosterCard.thumbnail item.PosterRef item.Name ]
                    ]
                    Html.div [
                        prop.className "flex-1 min-w-0"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center gap-2"
                                prop.children [
                                    Html.p [
                                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                                        prop.text item.Name
                                    ]
                                    if item.RoutePrefix = "series" then
                                        Html.span [
                                            prop.className "badge badge-xs badge-outline badge-warning flex-none"
                                            prop.text "Series"
                                        ]
                                    elif item.RoutePrefix = "games" then
                                        Html.span [
                                            prop.className "badge badge-xs badge-outline badge-info flex-none"
                                            prop.text "Game"
                                        ]
                                ]
                            ]
                            Html.p [
                                prop.className "text-xs text-base-content/50"
                                prop.text (
                                    match Map.tryFind item.Slug watchedBySlug with
                                    | Some w when not (List.isEmpty w.Dates) -> w.Dates |> List.map formatDateOnly |> String.concat ", "
                                    | _ -> string item.Year
                                )
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private friendMediaItems (items: FriendMediaItem list) : EntryList.EntryItem list =
    items |> List.map (fun m -> {
        EntryList.EntryItem.Slug = m.Slug
        Name = m.Name
        Year = m.Year
        PosterRef = m.PosterRef
        Rating = None
        RoutePrefix = routeForMedia m.MediaType
    })

let private friendWatchedItems (items: FriendWatchedItem list) : EntryList.EntryItem list =
    items |> List.map (fun m -> {
        EntryList.EntryItem.Slug = m.Slug
        Name = m.Name
        Year = m.Year
        PosterRef = m.PosterRef
        Rating = None
        RoutePrefix = routeForMedia m.MediaType
    })

let private sectionHeader (title: string) (count: int) (isCollapsed: bool) (onToggle: unit -> unit) =
    Html.div [
        prop.className "flex items-center gap-2 cursor-pointer select-none mt-6 mb-3"
        prop.onClick (fun _ -> onToggle ())
        prop.children [
            Html.h3 [
                prop.className "text-lg font-bold font-display"
                prop.text title
            ]
            Html.span [
                prop.className "text-sm text-base-content/50"
                prop.text $"({count})"
            ]
            Html.span [
                prop.className "text-base-content/40 transition-transform duration-200"
                prop.children [
                    if isCollapsed then Icons.chevronDown ()
                    else Icons.chevronUp ()
                ]
            ]
        ]
    ]

let private mediaSection (title: string) (isCollapsed: bool) (onToggle: unit -> unit) (onRemove: (string * string) -> unit) (viewSettings: ViewSettings option) (onSettingsChanged: (ViewSettings -> unit) option) (items: FriendMediaItem list) =
    if List.isEmpty items then
        Html.none
    else
        Html.div [
            prop.children [
                sectionHeader title (List.length items) isCollapsed onToggle
                if not isCollapsed then
                    EntryList.view {
                        Items = friendMediaItems items
                        RenderListRow = mediaListRow onRemove
                        ShowWatchOrder = false
                        InitialSettings = viewSettings
                        OnSettingsChanged = onSettingsChanged
                    }
            ]
        ]

let private watchedMediaSection (isCollapsed: bool) (onToggle: unit -> unit) (viewSettings: ViewSettings option) (onSettingsChanged: (ViewSettings -> unit) option) (items: FriendWatchedItem list) =
    if List.isEmpty items then
        Html.none
    else
        let watchedBySlug =
            items |> List.map (fun m -> m.Slug, m) |> Map.ofList
        Html.div [
            prop.children [
                sectionHeader "Watched Together" (List.length items) isCollapsed onToggle
                if not isCollapsed then
                    EntryList.view {
                        Items = friendWatchedItems items
                        RenderListRow = watchedMediaListRow watchedBySlug
                        ShowWatchOrder = false
                        InitialSettings = viewSettings
                        OnSettingsChanged = onSettingsChanged
                    }
            ]
        ]

[<ReactComponent>]
let private CropEditor (imageUrl: string) (initialOffset: float * float) (initialZoom: float) (onSave: float * float * float -> unit) (onClose: unit -> unit) =
    let offsetX, setOffsetX = React.useState (fst initialOffset)
    let offsetY, setOffsetY = React.useState (snd initialOffset)
    let zoom, setZoom = React.useState initialZoom
    let isDragging = React.useRef false
    let lastPos = React.useRef (0.0, 0.0)

    let handleMouseDown (e: Browser.Types.MouseEvent) =
        e.preventDefault ()
        isDragging.current <- true
        lastPos.current <- (e.clientX, e.clientY)

    let handleMouseMove (e: Browser.Types.MouseEvent) =
        if isDragging.current then
            let (lx, ly) = lastPos.current
            let dx = e.clientX - lx
            let dy = e.clientY - ly
            lastPos.current <- (e.clientX, e.clientY)
            setOffsetX (fun ox -> ox + dx / zoom)
            setOffsetY (fun oy -> oy + dy / zoom)

    let handleMouseUp _ =
        isDragging.current <- false

    let handleWheel (e: Browser.Types.WheelEvent) =
        e.preventDefault ()
        let delta = if e.deltaY < 0.0 then 0.1 else -0.1
        setZoom (fun z -> max 0.5 (min 5.0 (z + delta)))

    // Touch support
    let lastTouchPos = React.useRef (0.0, 0.0)
    let lastTouchDist = React.useRef 0.0

    let handleTouchStart (e: Browser.Types.TouchEvent) =
        e.preventDefault ()
        let touches : obj = e?touches
        let len : int = touches?length
        if len = 1 then
            let t0 : obj = touches?(0)
            let cx : float = t0?clientX
            let cy : float = t0?clientY
            isDragging.current <- true
            lastTouchPos.current <- (cx, cy)
        elif len = 2 then
            let t0 : obj = touches?(0)
            let t1 : obj = touches?(1)
            let x0 : float = t0?clientX
            let y0 : float = t0?clientY
            let x1 : float = t1?clientX
            let y1 : float = t1?clientY
            lastTouchDist.current <- sqrt ((x1 - x0) ** 2.0 + (y1 - y0) ** 2.0)

    let handleTouchMove (e: Browser.Types.TouchEvent) =
        e.preventDefault ()
        let touches : obj = e?touches
        let len : int = touches?length
        if len = 1 && isDragging.current then
            let t0 : obj = touches?(0)
            let cx : float = t0?clientX
            let cy : float = t0?clientY
            let (lx, ly) = lastTouchPos.current
            let dx = cx - lx
            let dy = cy - ly
            lastTouchPos.current <- (cx, cy)
            setOffsetX (fun ox -> ox + dx / zoom)
            setOffsetY (fun oy -> oy + dy / zoom)
        elif len = 2 then
            let t0 : obj = touches?(0)
            let t1 : obj = touches?(1)
            let x0 : float = t0?clientX
            let y0 : float = t0?clientY
            let x1 : float = t1?clientX
            let y1 : float = t1?clientY
            let dist = sqrt ((x1 - x0) ** 2.0 + (y1 - y0) ** 2.0)
            if lastTouchDist.current > 0.0 then
                let scale = dist / lastTouchDist.current
                setZoom (fun z -> max 0.5 (min 5.0 (z * scale)))
            lastTouchDist.current <- dist

    let handleTouchEnd _ =
        isDragging.current <- false
        lastTouchDist.current <- 0.0

    // Prevent default on wheel for the container
    let containerRef = React.useRef<Browser.Types.HTMLElement option> None
    React.useEffect (fun () ->
        match containerRef.current with
        | Some el ->
            let handler : obj -> unit = fun e -> emitJsExpr e "$0.preventDefault()"
            emitJsExpr (el, handler) "$0.addEventListener('wheel', $1, { passive: false })"
            React.createDisposable (fun () ->
                emitJsExpr (el, handler) "$0.removeEventListener('wheel', $1)")
        | None -> React.createDisposable ignore
    )

    Html.div [
        prop.className DesignSystem.modalContainer
        prop.children [
            Html.div [
                prop.className DesignSystem.modalBackdrop
                prop.onClick (fun _ -> onClose ())
            ]
            Html.div [
                prop.className ("relative w-full max-w-md mx-4 flex flex-col " + DesignSystem.modalPanel)
                prop.children [
                    Html.div [
                        prop.className "p-5 pb-3"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-between mb-2"
                                prop.children [
                                    Html.h3 [
                                        prop.className "font-bold text-lg font-display"
                                        prop.text "Crop Image"
                                    ]
                                ]
                            ]
                            Html.p [
                                prop.className "text-sm text-base-content/50"
                                prop.text "Drag to pan, scroll to zoom"
                            ]
                        ]
                    ]
                    // Crop area
                    Html.div [
                        prop.className "flex justify-center px-5 pb-4"
                        prop.children [
                            Html.div [
                                prop.ref (fun el -> containerRef.current <- (if isNull el then None else Some (unbox el)))
                                prop.className "relative w-64 h-64 rounded-full overflow-hidden cursor-grab active:cursor-grabbing select-none ring-2 ring-primary/30"
                                prop.style [
                                    style.touchAction.none
                                ]
                                prop.onMouseDown (fun e -> handleMouseDown e)
                                prop.onMouseMove (fun e -> handleMouseMove e)
                                prop.onMouseUp handleMouseUp
                                prop.onMouseLeave handleMouseUp
                                prop.onWheel (fun e -> handleWheel e)
                                prop.onTouchStart (fun e -> handleTouchStart e)
                                prop.onTouchMove (fun e -> handleTouchMove e)
                                prop.onTouchEnd (fun e -> handleTouchEnd e)
                                prop.children [
                                    Html.img [
                                        prop.src imageUrl
                                        prop.className "absolute pointer-events-none"
                                        prop.draggable false
                                        prop.style [
                                            style.width (length.percent 100)
                                            style.height (length.percent 100)
                                            style.objectFit.cover
                                            style.objectPosition $"{50.0 + offsetX}% {50.0 + offsetY}%"
                                            style.transform $"scale({zoom})"
                                            style.transformOrigin "center center"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                    // Footer
                    Html.div [
                        prop.className "flex justify-end gap-2 px-5 py-4 border-t border-base-content/10"
                        prop.children [
                            Daisy.button.button [
                                button.ghost
                                button.sm
                                prop.onClick (fun _ -> onClose ())
                                prop.text "Cancel"
                            ]
                            Daisy.button.button [
                                button.primary
                                button.sm
                                prop.onClick (fun _ -> onSave (offsetX, offsetY, zoom))
                                prop.text "Save"
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    match model.IsLoading, model.Friend with
    | true, _ ->
        Html.div [
            prop.className "flex justify-center py-12"
            prop.children [
                Daisy.loading [ loading.spinner; loading.lg ]
            ]
        ]
    | false, None ->
        PageContainer.view "Friend Not Found" [
            Html.p [
                prop.className "text-base-content/70"
                prop.text "The friend you're looking for doesn't exist."
            ]
            Html.a [
                prop.className "link link-primary mt-4 inline-block"
                prop.href (Router.format "friends")
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate "friends"
                )
                prop.text "Back to Friends"
            ]
        ]
    | false, Some friend ->
        let fileInputId = "friend-detail-image-upload"
        Html.div [
            prop.className (DesignSystem.pageContainer + " " + DesignSystem.animateFadeIn)
            prop.children [
                // Back button
                Html.div [
                    prop.className "mb-4"
                    prop.children [
                        Daisy.button.button [
                            button.ghost
                            button.sm
                            prop.onClick (fun _ ->
                                emitJsStatement () "window.history.back()")
                            prop.text "< Back"
                        ]
                    ]
                ]
                Daisy.card [
                    prop.className "bg-base-100 shadow-xl relative"
                    prop.children [
                        // Action menu in top right
                        Html.div [
                            prop.className "absolute top-4 right-4"
                            prop.children [
                                ActionMenu.view [
                                    { Label = "Event Log"
                                      Icon = Some Icons.events
                                      OnClick = fun () -> dispatch Open_event_history
                                      IsDestructive = false }
                                    { Label = "Delete"
                                      Icon = Some Icons.trash
                                      OnClick = fun () -> dispatch Remove_friend
                                      IsDestructive = true }
                                ]
                            ]
                        ]
                        Daisy.cardBody [
                            prop.className "p-8"
                            prop.children [
                                Html.div [
                                    prop.className "flex items-center gap-6"
                                    prop.children [
                                        // Avatar with drag-and-drop and crop display
                                        Html.div [
                                            prop.className "relative"
                                            prop.children [
                                                Daisy.avatar [
                                                    prop.children [
                                                        Html.div [
                                                            prop.className (
                                                                "w-24 h-24 rounded-full bg-base-300 ring-2 cursor-pointer transition-all duration-300 overflow-hidden"
                                                                + (if model.IsDragOver then " ring-primary ring-4 scale-110" else " ring-base-300 hover:ring-primary/50"))
                                                            prop.onClick (fun _ ->
                                                                let input = Browser.Dom.document.getElementById(fileInputId)
                                                                if not (isNull input) then input.click())
                                                            prop.onDragOver (fun e ->
                                                                e.preventDefault ()
                                                                e.dataTransfer.dropEffect <- "copy"
                                                                dispatch (Set_drag_over true))
                                                            prop.onDragEnter (fun e ->
                                                                e.preventDefault ()
                                                                dispatch (Set_drag_over true))
                                                            prop.onDragLeave (fun _ ->
                                                                dispatch (Set_drag_over false))
                                                            prop.onDrop (fun e ->
                                                                e.preventDefault ()
                                                                dispatch (Set_drag_over false)
                                                                let files = e.dataTransfer.files
                                                                if files.length > 0 then
                                                                    let file = files.[0]
                                                                    readFileAsBytes file (fun (bytes, filename) ->
                                                                        dispatch (Upload_friend_image (bytes, filename))))
                                                            prop.children [
                                                                match friend.ImageRef with
                                                                | Some ref ->
                                                                    let cs = model.CropState
                                                                    Html.img [
                                                                        prop.src $"/images/{ref}"
                                                                        prop.alt friend.Name
                                                                        prop.className "w-full h-full object-cover"
                                                                        prop.style [
                                                                            style.objectPosition $"{50.0 + cs.OffsetX}% {50.0 + cs.OffsetY}%"
                                                                            style.transform $"scale({cs.Zoom})"
                                                                            style.transformOrigin "center center"
                                                                        ]
                                                                    ]
                                                                | None ->
                                                                    Html.div [
                                                                        prop.className "flex items-center justify-center w-full h-full text-base-content/30"
                                                                        prop.children [ Icons.friends () ]
                                                                    ]
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                                // Re-crop button (only if image exists)
                                                match friend.ImageRef with
                                                | Some _ ->
                                                    Html.button [
                                                        prop.className "absolute -bottom-1 -right-1 w-7 h-7 rounded-full bg-base-200 border border-base-content/20 flex items-center justify-center text-xs hover:bg-primary hover:text-primary-content transition-colors"
                                                        prop.title "Adjust crop"
                                                        prop.onClick (fun e ->
                                                            e.stopPropagation ()
                                                            dispatch Open_crop_modal)
                                                        prop.children [
                                                            Icons.edit ()
                                                        ]
                                                    ]
                                                | None -> ()
                                            ]
                                        ]
                                        Html.input [
                                            prop.id fileInputId
                                            prop.type' "file"
                                            prop.accept "image/*"
                                            prop.className "hidden"
                                            prop.onChange (fun (e: Browser.Types.Event) ->
                                                let input: Browser.Types.HTMLInputElement = unbox e.target
                                                let files = input.files
                                                if files.length > 0 then
                                                    let file = files.[0]
                                                    readFileAsBytes file (fun (bytes, filename) ->
                                                        dispatch (Upload_friend_image (bytes, filename))))
                                        ]
                                        // Name: inline editable
                                        Html.div [
                                            prop.children [
                                                if model.IsEditing then
                                                    Daisy.input [
                                                        prop.className "text-2xl font-bold font-display w-full"
                                                        prop.autoFocus true
                                                        prop.value model.EditForm.Name
                                                        prop.onChange (Edit_name_changed >> dispatch)
                                                        prop.onKeyDown (fun e ->
                                                            if e.key = "Enter" then dispatch Submit_update
                                                            elif e.key = "Escape" then dispatch Cancel_editing)
                                                        prop.onBlur (fun _ -> dispatch Submit_update)
                                                    ]
                                                else
                                                    Html.h2 [
                                                        prop.className "text-2xl font-bold font-display cursor-pointer hover:text-primary transition-colors"
                                                        prop.onClick (fun _ -> dispatch Start_editing)
                                                        prop.text friend.Name
                                                    ]
                                            ]
                                        ]
                                    ]
                                ]
                                match model.Error with
                                | Some err ->
                                    Daisy.alert [
                                        alert.error
                                        prop.className "mt-4"
                                        prop.text err
                                    ]
                                | None -> ()
                                // Media sections
                                match model.FriendMedia with
                                | Some media ->
                                    mediaSection "Recommended" (Set.contains "Recommended" model.CollapsedSections) (fun () -> dispatch (Toggle_section "Recommended")) (fun (slug, rp) -> dispatch (Remove_from_recommended (slug, rp))) (Map.tryFind "Recommended" model.SectionSettings) (Some (fun s -> dispatch (Save_section_settings ("Recommended", s)))) media.Recommended
                                    mediaSection "Pending" (Set.contains "Pending" model.CollapsedSections) (fun () -> dispatch (Toggle_section "Pending")) (fun (slug, rp) -> dispatch (Remove_from_pending (slug, rp))) (Map.tryFind "Pending" model.SectionSettings) (Some (fun s -> dispatch (Save_section_settings ("Pending", s)))) media.WantToWatch
                                    watchedMediaSection (Set.contains "Watched" model.CollapsedSections) (fun () -> dispatch (Toggle_section "Watched")) (Map.tryFind "Watched" model.SectionSettings) (Some (fun s -> dispatch (Save_section_settings ("Watched", s)))) media.Watched
                                | None -> ()
                            ]
                        ]
                    ]
                ]
                if model.ShowRemoveConfirm then
                    ModalPanel.viewWithFooter
                        "Remove Friend"
                        (fun () -> dispatch Cancel_remove_friend)
                        [
                            Html.p [
                                prop.text $"Do you really want to remove {friend.Name}? All recommendations and watch history references will be removed."
                            ]
                        ]
                        [
                            Daisy.button.button [
                                button.ghost
                                prop.onClick (fun _ -> dispatch Cancel_remove_friend)
                                prop.text "Cancel"
                            ]
                            Daisy.button.button [
                                button.error
                                prop.onClick (fun _ -> dispatch Confirm_remove_friend)
                                prop.text "Remove"
                            ]
                        ]
                // Event History Modal
                if model.ShowEventHistory then
                    EventHistoryModal.view $"Friend-{model.Slug}" (fun () -> dispatch Close_event_history)
                // Crop Modal
                if model.ShowCropModal then
                    match friend.ImageRef with
                    | Some ref ->
                        CropEditor
                            $"/images/{ref}"
                            (model.CropState.OffsetX, model.CropState.OffsetY)
                            model.CropState.Zoom
                            (fun (x, y, z) -> dispatch (Update_crop (x, y, z)); dispatch Save_crop)
                            (fun () -> dispatch Close_crop_modal)
                    | None -> ()
            ]
        ]
