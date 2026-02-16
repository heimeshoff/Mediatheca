module Mediatheca.Client.Components.EntryList

open Feliz
open Mediatheca.Shared
open Mediatheca.Client
open Mediatheca.Client.Components

// ── Public types ──

type EntryItem = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    Rating: float option
    RoutePrefix: string
}

type Props = {
    Items: EntryItem list
    RenderListRow: EntryItem -> ReactElement
    ShowWatchOrder: bool
    InitialSettings: ViewSettings option
    OnSettingsChanged: (ViewSettings -> unit) option
}

// ── Internal types ──

type private SortState = {
    Field: ViewSortField
    Direction: ViewSortDirection
}

// ── Sort helpers ──

let private defaultDirectionFor field =
    match field with
    | ViewSortField.ByName -> ViewSortDirection.Ascending
    | ViewSortField.ByReleaseDate -> ViewSortDirection.Descending
    | ViewSortField.ByRating -> ViewSortDirection.Descending
    | ViewSortField.ByWatchOrder -> ViewSortDirection.Ascending

let private sortFieldLabel field =
    match field with
    | ViewSortField.ByReleaseDate -> "Release Date"
    | ViewSortField.ByName -> "Name"
    | ViewSortField.ByRating -> "Rating"
    | ViewSortField.ByWatchOrder -> "Watch Order"

let private sortEntries (sort: SortState) (entries: EntryItem list) =
    let sorted =
        match sort.Field with
        | ViewSortField.ByReleaseDate -> entries |> List.sortBy (fun e -> e.Year)
        | ViewSortField.ByName -> entries |> List.sortBy (fun e -> e.Name.ToLowerInvariant())
        | ViewSortField.ByRating -> entries |> List.sortBy (fun e -> e.Rating |> Option.defaultValue 0.0)
        | ViewSortField.ByWatchOrder -> entries // preserve original order from server
    match sort.Direction with
    | ViewSortDirection.Ascending -> sorted
    | ViewSortDirection.Descending -> sorted |> List.rev

// ── Sub-components ──

let private layoutToggle (active: ViewLayout) (onSwitch: ViewLayout -> unit) =
    Html.div [
        prop.className "flex items-center gap-1 bg-base-200/50 rounded-lg p-1"
        prop.children [
            Html.button [
                prop.className (
                    "flex items-center justify-center w-8 h-8 rounded-md transition-all duration-200 "
                    + (if active = ViewLayout.Gallery then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch ViewLayout.Gallery)
                prop.children [ Icons.viewGrid () ]
            ]
            Html.button [
                prop.className (
                    "flex items-center justify-center w-8 h-8 rounded-md transition-all duration-200 "
                    + (if active = ViewLayout.List then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch ViewLayout.List)
                prop.children [ Icons.viewList () ]
            ]
        ]
    ]

let private sizeToggle (active: ViewGallerySize) (onSwitch: ViewGallerySize -> unit) =
    Html.div [
        prop.className "flex items-center gap-1 bg-base-200/50 rounded-lg p-1"
        prop.children [
            Html.button [
                prop.className (
                    "flex items-center justify-center w-8 h-8 rounded-md transition-all duration-200 text-xs font-bold "
                    + (if active = ViewGallerySize.Normal then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch ViewGallerySize.Normal)
                prop.title "Normal size"
                prop.text "L"
            ]
            Html.button [
                prop.className (
                    "flex items-center justify-center w-8 h-8 rounded-md transition-all duration-200 text-xs font-bold "
                    + (if active = ViewGallerySize.Medium then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch ViewGallerySize.Medium)
                prop.title "Medium size"
                prop.text "M"
            ]
        ]
    ]

let private directionIcon dir =
    match dir with
    | ViewSortDirection.Ascending -> Icons.chevronDown ()
    | ViewSortDirection.Descending -> Icons.chevronUp ()

/// Sort button with absolute-position glassmorphic dropdown.
/// Uses position:absolute so backdrop-filter isn't broken by
/// ancestor compositing layers (e.g. animate-fade-in).
[<ReactComponent>]
let private SortButton (sort: SortState, onSort: SortState -> unit, showWatchOrder: bool) =
    let isOpen, setIsOpen = React.useState false

    let selectField field =
        if sort.Field = field then
            let newDir = if sort.Direction = ViewSortDirection.Ascending then ViewSortDirection.Descending else ViewSortDirection.Ascending
            onSort { sort with Direction = newDir }
        else
            onSort { Field = field; Direction = defaultDirectionFor field }
        setIsOpen false

    let fields =
        [ ViewSortField.ByReleaseDate; ViewSortField.ByName; ViewSortField.ByRating ] @ (if showWatchOrder then [ ViewSortField.ByWatchOrder ] else [])

    Html.div [
        prop.className "relative"
        prop.children [
            Html.button [
                prop.className (
                    "flex items-center justify-center w-8 h-8 rounded-lg transition-all duration-200 "
                    + (if isOpen then "bg-primary/15 text-primary" else "text-base-content/50 hover:text-base-content bg-base-200/50")
                )
                prop.onClick (fun _ -> setIsOpen (not isOpen))
                prop.children [ Icons.arrowsUpDown () ]
            ]
            if isOpen then
                // Click-away backdrop
                Html.div [
                    prop.className "fixed inset-0 z-40"
                    prop.onClick (fun _ -> setIsOpen false)
                ]
                // Dropdown — absolute position so backdrop-filter works
                Html.div [
                    prop.className (DesignSystem.glassDropdown + " absolute top-full right-0 mt-2 z-50 w-48 p-1.5")
                    prop.children [
                        for field in fields do
                            let isActive = sort.Field = field
                            let dir = if isActive then sort.Direction else defaultDirectionFor field
                            Html.button [
                                prop.className (
                                    "rating-dropdown-item w-full "
                                    + (if isActive then "rating-dropdown-item-active" else "")
                                )
                                prop.onClick (fun _ -> selectField field)
                                prop.children [
                                    Html.span [
                                        prop.className "flex-1 text-sm"
                                        prop.text (sortFieldLabel field)
                                    ]
                                    Html.span [
                                        prop.className (if isActive then "text-primary" else "text-base-content/30")
                                        prop.children [ directionIcon dir ]
                                    ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

let private galleryView (size: ViewGallerySize) (entries: EntryItem list) =
    let gridClass = match size with ViewGallerySize.Normal -> DesignSystem.movieGrid | ViewGallerySize.Medium -> DesignSystem.movieGridMedium
    Html.div [
        prop.className (gridClass + " " + DesignSystem.animateFadeIn)
        prop.children [
            for entry in entries do
                let navSlug =
                    if entry.Slug.Contains(":") then entry.Slug.Split(':').[0]
                    else entry.Slug
                PosterCard.viewForRoute entry.RoutePrefix navSlug entry.Name entry.Year entry.PosterRef None
        ]
    ]

let private listView (renderRow: EntryItem -> ReactElement) (entries: EntryItem list) =
    Html.div [
        prop.className ("bg-base-200/50 rounded-xl p-2 flex flex-col gap-1 " + DesignSystem.animateFadeIn)
        prop.children [
            for entry in entries do
                renderRow entry
        ]
    ]

// ── Main component ──

[<ReactComponent>]
let view (props: Props) =
    let layout, setLayout = React.useState ViewLayout.Gallery
    let gallerySize, setGallerySize = React.useState ViewGallerySize.Normal
    let defaultSort =
        if props.ShowWatchOrder then { Field = ViewSortField.ByWatchOrder; Direction = ViewSortDirection.Ascending }
        else { Field = ViewSortField.ByReleaseDate; Direction = ViewSortDirection.Descending }
    let sort, setSort = React.useState defaultSort
    let appliedInitial = React.useRef false

    // Apply initial settings once when they arrive
    React.useEffect (fun () ->
        match props.InitialSettings with
        | Some s when not appliedInitial.current ->
            appliedInitial.current <- true
            setLayout s.Layout
            setGallerySize s.GallerySize
            setSort { Field = s.SortField; Direction = s.SortDirection }
        | _ -> ()
    , [| box props.InitialSettings |])

    let notifyChange (l: ViewLayout) (gs: ViewGallerySize) (s: SortState) =
        match props.OnSettingsChanged with
        | Some cb ->
            cb { SortField = s.Field; SortDirection = s.Direction; Layout = l; GallerySize = gs }
        | None -> ()

    let setLayoutAndNotify l =
        setLayout l
        notifyChange l gallerySize sort

    let setGallerySizeAndNotify gs =
        setGallerySize gs
        notifyChange layout gs sort

    let setSortAndNotify s =
        setSort s
        notifyChange layout gallerySize s

    let sorted = sortEntries sort props.Items

    Html.div [
        prop.className "flex flex-col gap-4"
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between"
                prop.children [
                    Html.p [
                        prop.className DesignSystem.secondaryText
                        prop.text $"{props.Items.Length} entries"
                    ]
                    Html.div [
                        prop.className "flex items-center gap-2"
                        prop.children [
                            SortButton (sort, setSortAndNotify, props.ShowWatchOrder)
                            if layout = ViewLayout.Gallery then
                                sizeToggle gallerySize setGallerySizeAndNotify
                            layoutToggle layout setLayoutAndNotify
                        ]
                    ]
                ]
            ]
            match layout with
            | ViewLayout.Gallery -> galleryView gallerySize sorted
            | ViewLayout.List -> listView props.RenderListRow sorted
        ]
    ]
