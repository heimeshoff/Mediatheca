module Mediatheca.Client.Components.EntryList

open Feliz
open Mediatheca.Client
open Mediatheca.Client.Components

// ── Public types ──

type EntryItem = {
    Slug: string
    Name: string
    Year: int
    PosterRef: string option
    Rating: float option
}

type Props = {
    Items: EntryItem list
    RenderListRow: EntryItem -> ReactElement
}

// ── Internal types ──

type private Layout = Gallery | List

type private SortField = ByReleaseDate | ByName | ByRating
type private SortDirection = Ascending | Descending

type private SortState = {
    Field: SortField
    Direction: SortDirection
}

// ── Sort helpers ──

let private defaultDirectionFor field =
    match field with
    | ByName -> Ascending
    | ByReleaseDate -> Descending
    | ByRating -> Descending

let private sortFieldLabel field =
    match field with
    | ByReleaseDate -> "Release Date"
    | ByName -> "Name"
    | ByRating -> "Rating"

let private sortEntries (sort: SortState) (entries: EntryItem list) =
    let sorted =
        match sort.Field with
        | ByReleaseDate -> entries |> List.sortBy (fun e -> e.Year)
        | ByName -> entries |> List.sortBy (fun e -> e.Name.ToLowerInvariant())
        | ByRating -> entries |> List.sortBy (fun e -> e.Rating |> Option.defaultValue 0.0)
    match sort.Direction with
    | Ascending -> sorted
    | Descending -> sorted |> List.rev

// ── Sub-components ──

let private layoutToggle (active: Layout) (onSwitch: Layout -> unit) =
    Html.div [
        prop.className "flex items-center gap-1 bg-base-200/50 rounded-lg p-1"
        prop.children [
            Html.button [
                prop.className (
                    "flex items-center justify-center w-8 h-8 rounded-md transition-all duration-200 "
                    + (if active = Gallery then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch Gallery)
                prop.children [ Icons.viewGrid () ]
            ]
            Html.button [
                prop.className (
                    "flex items-center justify-center w-8 h-8 rounded-md transition-all duration-200 "
                    + (if active = List then "bg-primary/15 text-primary shadow-sm" else "text-base-content/50 hover:text-base-content")
                )
                prop.onClick (fun _ -> onSwitch List)
                prop.children [ Icons.viewList () ]
            ]
        ]
    ]

let private directionIcon dir =
    match dir with
    | Ascending -> Icons.chevronDown ()
    | Descending -> Icons.chevronUp ()

/// Sort button with absolute-position glassmorphic dropdown.
/// Uses position:absolute so backdrop-filter isn't broken by
/// ancestor compositing layers (e.g. animate-fade-in).
[<ReactComponent>]
let private SortButton (sort: SortState, onSort: SortState -> unit) =
    let isOpen, setIsOpen = React.useState false

    let selectField field =
        if sort.Field = field then
            let newDir = if sort.Direction = Ascending then Descending else Ascending
            onSort { sort with Direction = newDir }
        else
            onSort { Field = field; Direction = defaultDirectionFor field }
        setIsOpen false

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
                        for field in [ ByReleaseDate; ByName; ByRating ] do
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

let private galleryView (entries: EntryItem list) =
    Html.div [
        prop.className (DesignSystem.movieGrid + " " + DesignSystem.staggerGrid)
        prop.children [
            for entry in entries do
                PosterCard.view entry.Slug entry.Name entry.Year entry.PosterRef None
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
    let layout, setLayout = React.useState Gallery
    let sort, setSort = React.useState { Field = ByReleaseDate; Direction = Descending }

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
                            SortButton (sort, setSort)
                            layoutToggle layout setLayout
                        ]
                    ]
                ]
            ]
            match layout with
            | Gallery -> galleryView sorted
            | List -> listView props.RenderListRow sorted
        ]
    ]
