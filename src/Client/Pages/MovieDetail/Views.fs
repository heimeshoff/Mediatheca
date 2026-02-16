module Mediatheca.Client.Pages.MovieDetail.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.MovieDetail.Types
open Mediatheca.Client.Components

let private formatDateOnly (date: string) =
    match date.IndexOf('T') with
    | -1 -> date
    | i -> date.[..i-1]

let private sectionHeader (title: string) =
    Html.h2 [
        prop.className "text-2xl font-bold font-display mb-6 flex items-center gap-2"
        prop.children [
            Html.span [ prop.className "w-1 h-6 bg-primary rounded-full inline-block" ]
            Html.text title
        ]
    ]

let private starRating (rating: float) =
    let stars = rating / 2.0
    let fullStars = int stars
    let hasHalf = stars - float fullStars >= 0.5
    Html.div [
        prop.className "flex items-center gap-1 text-warning"
        prop.children [
            for _ in 1 .. fullStars do
                Html.span [ prop.className "text-sm"; prop.text "\u2605" ]
            if hasHalf then
                Html.span [ prop.className "text-sm opacity-50"; prop.text "\u2605" ]
            for _ in 1 .. (5 - fullStars - (if hasHalf then 1 else 0)) do
                Html.span [ prop.className "text-sm text-base-content/20"; prop.text "\u2605" ]
            Html.span [
                prop.className "ml-1 text-base-content font-semibold text-sm"
                prop.text $"%.1f{rating}"
            ]
        ]
    ]

let private detailCard (label: string) (value: string) =
    Html.div [
        prop.className "bg-base-100/50 backdrop-blur-sm p-4 rounded-xl border border-base-content/5"
        prop.children [
            Html.span [
                prop.className "block text-base-content/40 text-xs uppercase font-bold tracking-widest mb-1"
                prop.text label
            ]
            Html.span [
                prop.className "font-medium"
                prop.text value
            ]
        ]
    ]

type private RatingOption = {
    Value: int
    Name: string
    Description: string
    Icon: unit -> ReactElement
    ColorClass: string
}

let private ratingOptions : RatingOption list = [
    { Value = 0; Name = "Unrated"; Description = "No rating yet"; Icon = Icons.questionCircle; ColorClass = "text-base-content/50" }
    { Value = 1; Name = "Waste"; Description = "Waste of time"; Icon = Icons.thumbsDown; ColorClass = "text-red-400" }
    { Value = 2; Name = "Meh"; Description = "Didn't click, uninspiring"; Icon = Icons.minusCircle; ColorClass = "text-orange-400" }
    { Value = 3; Name = "Decent"; Description = "Watchable, even if not life-changing"; Icon = Icons.handOkay; ColorClass = "text-yellow-400" }
    { Value = 4; Name = "Entertaining"; Description = "Strong craft, enjoyable"; Icon = Icons.thumbsUp; ColorClass = "text-lime-400" }
    { Value = 5; Name = "Outstanding"; Description = "Absolutely brilliant, stays with you"; Icon = Icons.trophy; ColorClass = "text-amber-400" }
]

let private getRatingOption (rating: int option) =
    let r = rating |> Option.defaultValue 0
    ratingOptions |> List.find (fun opt -> opt.Value = r)

[<ReactComponent>]
let private HeroRating (tmdbRating: float option, personalRating: int option, isOpen: bool, dispatch: Msg -> unit) =
    let triggerRef = React.useElementRef()
    let pos, setPos = React.useState {| top = 0.0; left = 0.0 |}
    let currentOption = getRatingOption personalRating
    let hasPersonalRating = personalRating.IsSome && personalRating.Value > 0

    React.useEffect ((fun () ->
        if isOpen then
            match triggerRef.current with
            | Some el ->
                let rect = el.getBoundingClientRect()
                setPos {| top = rect.bottom + 8.0; left = rect.left |}
            | None -> ()
    ), [| box isOpen |])

    Html.div [
        prop.className "relative"
        prop.children [
            Html.div [
                prop.ref triggerRef
                prop.children [
                    if hasPersonalRating then
                        Html.button [
                            prop.className $"flex items-center gap-1.5 cursor-pointer hover:opacity-80 transition-opacity {currentOption.ColorClass}"
                            prop.onClick (fun _ -> dispatch Toggle_rating_dropdown)
                            prop.children [
                                Html.span [
                                    prop.className "w-5 h-5"
                                    prop.children [ currentOption.Icon () ]
                                ]
                                Html.span [
                                    prop.className "text-sm font-semibold"
                                    prop.text currentOption.Name
                                ]
                            ]
                        ]
                    else
                        Html.button [
                            prop.className "cursor-pointer hover:opacity-80 transition-opacity"
                            prop.onClick (fun _ -> dispatch Toggle_rating_dropdown)
                            prop.children [
                                match tmdbRating with
                                | Some r -> starRating r
                                | None ->
                                    Html.span [
                                        prop.className "text-sm text-base-content/50 hover:text-primary transition-colors"
                                        prop.text "Rate"
                                    ]
                            ]
                        ]
                ]
            ]
            if isOpen then
                // Click-away backdrop
                Html.div [
                    prop.className "fixed inset-0 z-[200]"
                    prop.onClick (fun _ -> dispatch Toggle_rating_dropdown)
                ]
                // Dropdown — fixed position to escape overflow-hidden hero
                Html.div [
                    prop.className "fixed z-[201] rating-dropdown"
                    prop.style [ style.top (int pos.top); style.left (int pos.left) ]
                    prop.children [
                        for opt in ratingOptions do
                            if opt.Value > 0 then
                                let isActive = personalRating = Some opt.Value
                                let itemClass =
                                    if isActive then "rating-dropdown-item rating-dropdown-item-active"
                                    else "rating-dropdown-item"
                                Html.button [
                                    prop.className itemClass
                                    prop.onClick (fun _ -> dispatch (Set_personal_rating opt.Value))
                                    prop.children [
                                        Html.span [
                                            prop.className $"w-5 h-5 {opt.ColorClass}"
                                            prop.children [ opt.Icon () ]
                                        ]
                                        Html.div [
                                            prop.className "flex flex-col items-start"
                                            prop.children [
                                                Html.span [ prop.className "font-medium"; prop.text opt.Name ]
                                                Html.span [ prop.className "text-xs text-base-content/50"; prop.text opt.Description ]
                                            ]
                                        ]
                                    ]
                                ]
                        if personalRating.IsSome && personalRating.Value > 0 then
                            Html.button [
                                prop.className "rating-dropdown-item rating-dropdown-item-clear"
                                prop.onClick (fun _ -> dispatch (Set_personal_rating 0))
                                prop.children [
                                    Html.span [
                                        prop.className "w-5 h-5 text-base-content/40"
                                        prop.children [ Icons.questionCircle () ]
                                    ]
                                    Html.span [ prop.className "font-medium text-base-content/60"; prop.text "Clear rating" ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

let private glassCard (children: ReactElement list) =
    Html.div [
        prop.className "bg-base-100/50 backdrop-blur-xl border border-base-content/8 p-6 rounded-xl"
        prop.children children
    ]

let private personalRatingCard (rating: int option) (isOpen: bool) (dispatch: Msg -> unit) =
    let currentOption = getRatingOption rating
    // Outer container has position:relative but NO backdrop-filter,
    // so the dropdown's z-50 escapes the glassCard stacking context.
    Html.div [
        prop.className "relative"
        prop.children [
            glassCard [
                Html.div [
                    prop.className "flex items-center justify-between mb-4"
                    prop.children [
                        Html.h3 [ prop.className "text-lg font-bold"; prop.text "My Rating" ]
                    ]
                ]
                Html.button [
                    prop.className $"flex items-center gap-3 font-semibold text-lg cursor-pointer {currentOption.ColorClass} hover:opacity-80 transition-opacity"
                    prop.onClick (fun _ -> dispatch Toggle_rating_dropdown)
                    prop.children [
                        Html.span [
                            prop.className "w-6 h-6"
                            prop.children [ currentOption.Icon () ]
                        ]
                        Html.span [ prop.text currentOption.Name ]
                    ]
                ]
            ]
            if isOpen then
                Html.div [
                    prop.className "absolute top-full left-0 mt-2 z-50 rating-dropdown"
                    prop.children [
                        for opt in ratingOptions do
                            if opt.Value > 0 then
                                let isActive = rating = Some opt.Value
                                let itemClass =
                                    if isActive then "rating-dropdown-item rating-dropdown-item-active"
                                    else "rating-dropdown-item"
                                Html.button [
                                    prop.className itemClass
                                    prop.onClick (fun _ -> dispatch (Set_personal_rating opt.Value))
                                    prop.children [
                                        Html.span [
                                            prop.className $"w-5 h-5 {opt.ColorClass}"
                                            prop.children [ opt.Icon () ]
                                        ]
                                        Html.div [
                                            prop.className "flex flex-col items-start"
                                            prop.children [
                                                Html.span [ prop.className "font-medium"; prop.text opt.Name ]
                                                Html.span [ prop.className "text-xs text-base-content/50"; prop.text opt.Description ]
                                            ]
                                        ]
                                    ]
                                ]
                        if rating.IsSome && rating.Value > 0 then
                            Html.button [
                                prop.className "rating-dropdown-item rating-dropdown-item-clear"
                                prop.onClick (fun _ -> dispatch (Set_personal_rating 0))
                                prop.children [
                                    Html.span [
                                        prop.className "w-5 h-5 text-base-content/40"
                                        prop.children [ Icons.questionCircle () ]
                                    ]
                                    Html.span [ prop.className "font-medium text-base-content/60"; prop.text "Clear rating" ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

let private castCard (cast: CastMemberDto) =
    Html.div [
        prop.className "flex-shrink-0 text-center w-24"
        prop.children [
            Html.div [
                prop.className "w-24 h-24 rounded-full overflow-hidden mb-3 border-2 border-primary/20 bg-base-300 mx-auto"
                prop.children [
                    match cast.ImageRef with
                    | Some ref ->
                        let src = if ref.StartsWith("http") then ref else $"/images/{ref}"
                        Html.img [
                            prop.src src
                            prop.alt cast.Name
                            prop.className "w-full h-full object-cover"
                        ]
                    | None ->
                        Html.div [
                            prop.className "flex items-center justify-center w-full h-full text-base-content/30 text-xs"
                            prop.text "?"
                        ]
                ]
            ]
            Html.p [
                prop.className "font-medium text-sm line-clamp-1"
                prop.text cast.Name
            ]
            Html.p [
                prop.className "text-base-content/50 text-xs line-clamp-1"
                prop.text cast.Role
            ]
        ]
    ]

let private crewCard (crew: CrewMemberDto) =
    Html.div [
        prop.className "flex-shrink-0 text-center w-24"
        prop.children [
            Html.div [
                prop.className "w-24 h-24 rounded-full overflow-hidden mb-3 border-2 border-secondary/20 bg-base-300 mx-auto"
                prop.children [
                    match crew.ImageRef with
                    | Some ref ->
                        let src = if ref.StartsWith("http") then ref else $"/images/{ref}"
                        Html.img [
                            prop.src src
                            prop.alt crew.Name
                            prop.className "w-full h-full object-cover"
                        ]
                    | None ->
                        Html.div [
                            prop.className "flex items-center justify-center w-full h-full text-base-content/30 text-xs"
                            prop.text "?"
                        ]
                ]
            ]
            Html.p [
                prop.className "font-medium text-sm line-clamp-1"
                prop.text crew.Name
            ]
            Html.p [
                prop.className "text-base-content/50 text-xs line-clamp-1"
                prop.text crew.Job
            ]
        ]
    ]

let private friendAvatar (size: string) (fr: FriendRef) (extraClass: string) =
    Html.div [
        prop.className $"{size} rounded-full overflow-hidden flex items-center justify-center {extraClass}"
        prop.children [
            match fr.ImageRef with
            | Some ref ->
                Html.img [
                    prop.src $"/images/{ref}"
                    prop.alt fr.Name
                    prop.className "w-full h-full object-cover"
                ]
            | None ->
                Html.span [
                    prop.className "w-full h-full flex items-center justify-center"
                    prop.text (fr.Name.[0..0].ToUpper())
                ]
        ]
    ]

[<ReactComponent>]
let private CatalogManager
    (allCatalogs: CatalogListItem list)
    (movieCatalogs: CatalogRef list)
    (onAdd: string -> unit)
    (onRemove: string -> string -> unit)
    (onCreateNew: string -> unit)
    (onClose: unit -> unit) =
    let searchText, setSearchText = React.useState("")
    let highlightedIndex, setHighlightedIndex = React.useState(0)
    let selectedSlugs = movieCatalogs |> List.map (fun c -> c.Slug) |> Set.ofList
    let available =
        allCatalogs
        |> List.filter (fun c ->
            not (Set.contains c.Slug selectedSlugs) &&
            (searchText = "" || c.Name.ToLowerInvariant().Contains(searchText.ToLowerInvariant())))
    let availableArr = available |> List.toArray
    let trimmedSearch = searchText.Trim()
    let hasExactMatch = allCatalogs |> List.exists (fun c -> c.Name.ToLowerInvariant() = trimmedSearch.ToLowerInvariant())
    let showCreateNew = trimmedSearch <> "" && not hasExactMatch
    let totalItems = availableArr.Length + (if showCreateNew then 1 else 0)

    let headerExtra = [
        if not (List.isEmpty movieCatalogs) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-4"
                prop.children [
                    for cat in movieCatalogs do
                        Html.span [
                            prop.className "inline-flex items-center gap-1.5 bg-transparent border border-base-content/20 text-base-content/70 px-3 py-1 rounded-full text-sm font-semibold transition-colors hover:border-base-content/40"
                            prop.children [
                                Html.span [ prop.text cat.Name ]
                                Html.button [
                                    prop.className "text-base-content/40 hover:text-error transition-colors cursor-pointer ml-0.5"
                                    prop.onClick (fun e ->
                                        e.stopPropagation()
                                        onRemove cat.Slug cat.EntryId)
                                    prop.text "\u00D7"
                                ]
                            ]
                        ]
                ]
            ]
        Daisy.input [
            prop.className "w-full mb-4"
            prop.type' "text"
            prop.placeholder "Search catalogs..."
            prop.autoFocus true
            prop.value searchText
            prop.onChange (fun (v: string) ->
                setSearchText v
                setHighlightedIndex 0)
            prop.onKeyDown (fun e ->
                match e.key with
                | "ArrowDown" ->
                    e.preventDefault()
                    if totalItems > 0 then
                        setHighlightedIndex (min (highlightedIndex + 1) (totalItems - 1))
                | "ArrowUp" ->
                    e.preventDefault()
                    setHighlightedIndex (max (highlightedIndex - 1) 0)
                | "Enter" ->
                    e.preventDefault()
                    if highlightedIndex >= 0 && highlightedIndex < availableArr.Length then
                        onAdd availableArr.[highlightedIndex].Slug
                        setSearchText ""
                        setHighlightedIndex 0
                    elif showCreateNew && highlightedIndex = availableArr.Length then
                        onCreateNew trimmedSearch
                        setSearchText ""
                        setHighlightedIndex 0
                | "Escape" -> onClose ()
                | _ -> ())
        ]
    ]

    let content = [
        if totalItems = 0 && not showCreateNew then
            Html.p [
                prop.className "text-base-content/60 py-2 text-sm"
                prop.text (
                    if List.isEmpty allCatalogs && trimmedSearch = "" then "No catalogs yet. Create one below."
                    elif trimmedSearch = "" then "Movie already in all catalogs."
                    else "No matches found."
                )
            ]
        else
            Html.div [
                prop.className "space-y-1"
                prop.children [
                    for i in 0 .. availableArr.Length - 1 do
                        let catalog = availableArr.[i]
                        let isHighlighted = (i = highlightedIndex)
                        Html.div [
                            prop.className (
                                "flex items-center gap-3 p-2 rounded-lg cursor-pointer " +
                                (if isHighlighted then "bg-primary/20" else "hover:bg-base-200"))
                            prop.onClick (fun _ -> onAdd catalog.Slug)
                            prop.children [
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300 flex items-center justify-center text-base-content/40"
                                    prop.children [ Icons.catalog () ]
                                ]
                                Html.div [
                                    prop.className "flex flex-col"
                                    prop.children [
                                        Html.span [ prop.className "font-semibold"; prop.text catalog.Name ]
                                        if catalog.Description <> "" then
                                            Html.span [ prop.className "text-xs text-base-content/50"; prop.text catalog.Description ]
                                    ]
                                ]
                            ]
                        ]
                    if showCreateNew then
                        let isHighlighted = (highlightedIndex = availableArr.Length)
                        Html.div [
                            prop.className (
                                "flex items-center gap-3 p-2 rounded-lg cursor-pointer " +
                                (if isHighlighted then "bg-primary/20" else "hover:bg-base-200"))
                            prop.onClick (fun _ ->
                                onCreateNew trimmedSearch
                                setSearchText ""
                                setHighlightedIndex 0)
                            prop.children [
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300 flex items-center justify-center text-base-content/40 text-lg"
                                    prop.text "+"
                                ]
                                Html.span [
                                    prop.className "font-semibold"
                                    prop.text $"Create catalog \"{trimmedSearch}\""
                                ]
                            ]
                        ]
                ]
            ]
    ]

    ModalPanel.viewCustom "Add to Catalog" onClose headerExtra content []

[<ReactComponent>]
let private FriendManager
    (title: string)
    (allFriends: FriendListItem list)
    (selectedFriends: FriendRef list)
    (onAdd: string -> unit)
    (onRemove: string -> unit)
    (onAddNew: string -> unit)
    (onClose: unit -> unit) =
    let searchText, setSearchText = React.useState("")
    let highlightedIndex, setHighlightedIndex = React.useState(0)
    let selectedSlugs = selectedFriends |> List.map (fun f -> f.Slug) |> Set.ofList
    let available =
        allFriends
        |> List.filter (fun f ->
            not (Set.contains f.Slug selectedSlugs) &&
            (searchText = "" || f.Name.ToLowerInvariant().Contains(searchText.ToLowerInvariant())))
    let availableArr = available |> List.toArray
    let trimmedSearch = searchText.Trim()
    let hasExactMatch = allFriends |> List.exists (fun f -> f.Name.ToLowerInvariant() = trimmedSearch.ToLowerInvariant())
    let showAddContact = trimmedSearch <> "" && not hasExactMatch
    let totalItems = availableArr.Length + (if showAddContact then 1 else 0)

    let headerExtra = [
        if not (List.isEmpty selectedFriends) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-4"
                prop.children [
                    for fr in selectedFriends do
                        FriendPill.viewWithRemove fr onRemove
                ]
            ]
        Daisy.input [
            prop.className "w-full mb-4"
            prop.type' "text"
            prop.placeholder "Search friends..."
            prop.autoFocus true
            prop.value searchText
            prop.onChange (fun (v: string) ->
                setSearchText v
                setHighlightedIndex 0)
            prop.onKeyDown (fun e ->
                match e.key with
                | "ArrowDown" ->
                    e.preventDefault()
                    if totalItems > 0 then
                        setHighlightedIndex (min (highlightedIndex + 1) (totalItems - 1))
                | "ArrowUp" ->
                    e.preventDefault()
                    setHighlightedIndex (max (highlightedIndex - 1) 0)
                | "Enter" ->
                    e.preventDefault()
                    if highlightedIndex >= 0 && highlightedIndex < availableArr.Length then
                        onAdd availableArr.[highlightedIndex].Slug
                        setSearchText ""
                        setHighlightedIndex 0
                    elif showAddContact && highlightedIndex = availableArr.Length then
                        onAddNew trimmedSearch
                        setSearchText ""
                        setHighlightedIndex 0
                | "Escape" -> onClose ()
                | _ -> ())
        ]
    ]

    let content = [
        if totalItems = 0 && not showAddContact then
            Html.p [
                prop.className "text-base-content/60 py-2 text-sm"
                prop.text (
                    if List.isEmpty allFriends && trimmedSearch = "" then "No friends available. Add friends first."
                    elif trimmedSearch = "" then "All friends already added."
                    else "No matches found."
                )
            ]
        else
            Html.div [
                prop.className "space-y-1"
                prop.children [
                    for i in 0 .. availableArr.Length - 1 do
                        let friend = availableArr.[i]
                        let isHighlighted = (i = highlightedIndex)
                        Html.div [
                            prop.className (
                                "flex items-center gap-3 p-2 rounded-lg cursor-pointer " +
                                (if isHighlighted then "bg-primary/20" else "hover:bg-base-200"))
                            prop.onClick (fun _ -> onAdd friend.Slug)
                            prop.children [
                                Daisy.avatar [
                                    prop.children [
                                        Html.div [
                                            prop.className "w-10 rounded-full bg-base-300"
                                            prop.children [
                                                match friend.ImageRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}"
                                                        prop.alt friend.Name
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
                                Html.span [ prop.className "font-semibold"; prop.text friend.Name ]
                            ]
                        ]
                    if showAddContact then
                        let isHighlighted = (highlightedIndex = availableArr.Length)
                        Html.div [
                            prop.className (
                                "flex items-center gap-3 p-2 rounded-lg cursor-pointer " +
                                (if isHighlighted then "bg-primary/20" else "hover:bg-base-200"))
                            prop.onClick (fun _ ->
                                onAddNew trimmedSearch
                                setSearchText ""
                                setHighlightedIndex 0)
                            prop.children [
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300 flex items-center justify-center text-base-content/40 text-lg"
                                    prop.text "+"
                                ]
                                Html.span [
                                    prop.className "font-semibold"
                                    prop.text $"Add contact \"{trimmedSearch}\""
                                ]
                            ]
                        ]
                ]
            ]
    ]

    ModalPanel.viewCustom title onClose headerExtra content []

let private friendsCard (movie: MovieDetail) (model: Model) (dispatch: Msg -> unit) =
    let hasRecommended = not (List.isEmpty movie.RecommendedBy)
    let hasWatchWith = not (List.isEmpty movie.WantToWatchWith)
    let hasSessions = not (List.isEmpty movie.WatchSessions)
    let isEmpty = not hasRecommended && not hasWatchWith && not hasSessions
    Html.div [
        prop.className "relative"
        prop.children [
            glassCard [
                // Header
                Html.div [
                    prop.className "flex items-center justify-between mb-4"
                    prop.children [
                        Html.h3 [ prop.className "text-lg font-bold"; prop.text "Friends" ]
                        Html.button [
                            prop.className "w-8 h-8 rounded-full bg-primary flex items-center justify-center text-primary-content hover:scale-110 transition-transform text-sm font-bold cursor-pointer"
                            prop.onClick (fun _ -> dispatch Toggle_friends_menu)
                            prop.text "+"
                        ]
                    ]
                ]
                // Recommended By sub-section
                if hasRecommended then
                    Html.div [
                        prop.className "mb-4"
                        prop.children [
                            Html.p [
                                prop.className "text-xs font-bold text-base-content/40 uppercase tracking-wider mb-2"
                                prop.text "Recommended By"
                            ]
                            Html.div [
                                prop.className "flex flex-wrap gap-2"
                                prop.children [
                                    for fr in movie.RecommendedBy do
                                        FriendPill.view fr
                                ]
                            ]
                        ]
                    ]
                // Pending sub-section
                if hasWatchWith then
                    Html.div [
                        prop.className "mb-4"
                        prop.children [
                            Html.p [
                                prop.className "text-xs font-bold text-base-content/40 uppercase tracking-wider mb-2"
                                prop.text "Pending"
                            ]
                            Html.div [
                                prop.className "flex flex-wrap gap-2"
                                prop.children [
                                    for fr in movie.WantToWatchWith do
                                        FriendPill.view fr
                                ]
                            ]
                        ]
                    ]
                // Watch History sub-section
                if hasSessions then
                    Html.div [
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-between mb-2"
                                prop.children [
                                    Html.p [
                                        prop.className "text-xs font-bold text-base-content/40 uppercase tracking-wider"
                                        prop.text "Watch History"
                                    ]
                                    Html.span [
                                        prop.className "text-primary text-xs font-bold uppercase tracking-widest"
                                        prop.text (
                                            let n = movie.WatchSessions.Length
                                            if n = 1 then "1 Session"
                                            else $"{n} Sessions"
                                        )
                                    ]
                                ]
                            ]
                            Html.div [
                                prop.className "space-y-5 relative before:absolute before:left-[11px] before:top-2 before:bottom-2 before:w-[2px] before:bg-base-content/10"
                                prop.children [
                                    for i in 0 .. movie.WatchSessions.Length - 1 do
                                        let session = movie.WatchSessions.[i]
                                        Html.div [
                                            prop.key session.SessionId
                                            prop.className "group/session relative pl-8"
                                            prop.children [
                                                // Timeline dot
                                                Html.div [
                                                    prop.className "absolute left-0 top-1 w-6 h-6 flex items-center justify-center"
                                                    prop.children [
                                                        Html.div [
                                                            prop.className (
                                                                "w-3 h-3 rounded-full border-4 border-base-300 z-10 " +
                                                                (if i = 0 then "bg-primary" else "bg-base-content/30"))
                                                        ]
                                                    ]
                                                ]
                                                // Session content
                                                Html.div [
                                                    prop.children [
                                                        // Date (editable)
                                                        Html.div [
                                                            prop.className "flex items-center gap-2 mb-1"
                                                            prop.children [
                                                                if model.EditingSessionDate = Some session.SessionId then
                                                                    Daisy.input [
                                                                        prop.className "w-36"
                                                                        input.sm
                                                                        prop.type' "date"
                                                                        prop.autoFocus true
                                                                        prop.value (if session.Date.Length > 10 then session.Date.Substring(0, 10) else session.Date)
                                                                        prop.onChange (fun (v: string) ->
                                                                            dispatch (Update_session_date (session.SessionId, v)))
                                                                        prop.onBlur (fun _ ->
                                                                            dispatch (Update_session_date (session.SessionId, session.Date)))
                                                                        prop.onKeyDown (fun e ->
                                                                            if e.key = "Escape" then
                                                                                dispatch (Update_session_date (session.SessionId, session.Date)))
                                                                    ]
                                                                else
                                                                    Html.span [
                                                                        prop.className "text-xs font-bold text-base-content/40 uppercase tracking-tight cursor-pointer hover:text-primary transition-colors"
                                                                        prop.onClick (fun _ -> dispatch (Edit_session_date session.SessionId))
                                                                        prop.text (formatDateOnly session.Date)
                                                                    ]
                                                                Html.button [
                                                                    prop.className "opacity-0 group-hover/session:opacity-100 transition-opacity text-base-content/30 hover:text-error text-xs"
                                                                    prop.title "Remove session"
                                                                    prop.onClick (fun _ -> dispatch (Remove_watch_session session.SessionId))
                                                                    prop.children [ Icons.trash () ]
                                                                ]
                                                            ]
                                                        ]
                                                        // Session friends (pills instead of overlapping avatars)
                                                        Html.div [
                                                            prop.className "flex flex-wrap gap-2 items-center"
                                                            prop.children [
                                                                for fr in session.Friends do
                                                                    FriendPill.view fr
                                                                Html.button [
                                                                    prop.className "w-6 h-6 rounded-full bg-base-content/10 flex items-center justify-center text-[10px] text-base-content/40 hover:bg-primary/30 hover:text-primary transition-colors"
                                                                    prop.onClick (fun _ -> dispatch (Open_friend_picker (Session_friend_picker session.SessionId)))
                                                                    prop.text "+"
                                                                ]
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                    ]
                // Empty state
                if isEmpty then
                    Html.p [
                        prop.className "text-base-content/30 text-sm italic"
                        prop.text "No friend activity yet"
                    ]
            ]
            // Context menu dropdown
            if model.IsFriendsMenuOpen then
                Html.div [
                    prop.className "fixed inset-0 z-40"
                    prop.onClick (fun _ -> dispatch Close_friends_menu)
                ]
                Html.div [
                    prop.className "absolute top-12 right-6 z-50 rating-dropdown py-1 min-w-[200px]"
                    prop.children [
                        Html.button [
                            prop.className "w-full flex items-center gap-3 px-3 py-2.5 text-sm text-left hover:bg-base-content/10 transition-colors cursor-pointer"
                            prop.onClick (fun _ ->
                                dispatch Close_friends_menu
                                dispatch (Open_friend_picker Recommend_picker))
                            prop.children [
                                Html.span [ prop.className "text-base-content/60"; prop.text "Recommended By" ]
                            ]
                        ]
                        Html.button [
                            prop.className "w-full flex items-center gap-3 px-3 py-2.5 text-sm text-left hover:bg-base-content/10 transition-colors cursor-pointer"
                            prop.onClick (fun _ ->
                                dispatch Close_friends_menu
                                dispatch (Open_friend_picker Watch_with_picker))
                            prop.children [
                                Html.span [ prop.className "text-base-content/60"; prop.text "Pending" ]
                            ]
                        ]
                        Html.button [
                            prop.className "w-full flex items-center gap-3 px-3 py-2.5 text-sm text-left hover:bg-base-content/10 transition-colors cursor-pointer"
                            prop.onClick (fun _ ->
                                dispatch Close_friends_menu
                                dispatch Record_quick_session)
                            prop.children [
                                Html.span [ prop.className "text-base-content/60"; prop.text "Record Watch Session" ]
                            ]
                        ]
                    ]
                ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    match model.IsLoading, model.Movie with
    | true, _ ->
        Html.div [
            prop.className "flex justify-center py-12"
            prop.children [
                Daisy.loading [ loading.spinner; loading.lg ]
            ]
        ]
    | false, None ->
        PageContainer.view "Movie Not Found" [
            Html.p [
                prop.className "text-base-content/70"
                prop.text "The movie you're looking for doesn't exist."
            ]
            Html.a [
                prop.className "link link-primary mt-4 inline-block"
                prop.href (Router.format "movies")
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate "movies"
                )
                prop.text "Back to Movies"
            ]
        ]
    | false, Some movie ->
        Html.div [
            prop.children [
                // ── Hero Section ──
                Html.div [
                    prop.className "relative min-h-72 lg:h-[500px] w-full overflow-hidden"
                    prop.children [
                        // Backdrop image
                        Html.div [
                            prop.className "absolute inset-0"
                            prop.children [
                                match movie.BackdropRef with
                                | Some ref ->
                                    Html.img [
                                        prop.src $"/images/{ref}"
                                        prop.alt movie.Name
                                        prop.className "w-full h-full object-cover"
                                    ]
                                | None -> ()
                                // Gradient overlay
                                Html.div [
                                    prop.className "absolute inset-0 bg-gradient-to-t from-base-300 via-base-300/40 to-base-300/80"
                                ]
                            ]
                        ]
                        // Back button
                        Html.div [
                            prop.className "absolute top-4 left-4 z-10"
                            prop.children [
                                Daisy.button.button [
                                    button.ghost
                                    button.sm
                                    prop.className "text-base-content backdrop-blur-sm bg-base-300/30"
                                    prop.onClick (fun _ -> Router.navigate "movies")
                                    prop.text "\u2190 Back"
                                ]
                            ]
                        ]
                        // Hero content at bottom
                        Html.div [
                            prop.className "relative min-h-72 lg:h-full flex items-end pb-6 lg:pb-8 px-4 lg:px-8"
                            prop.children [
                                Html.div [
                                    prop.className "flex gap-6 lg:gap-10 items-end w-full max-w-6xl mx-auto"
                                    prop.children [
                                        // Movie Poster
                                        Html.div [
                                            prop.className "hidden lg:block w-52 h-80 flex-shrink-0 rounded-xl overflow-hidden shadow-2xl border border-base-content/10"
                                            prop.children [
                                                match movie.PosterRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}"
                                                        prop.alt movie.Name
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.movie () ]
                                                    ]
                                            ]
                                        ]
                                        // Mobile poster (smaller)
                                        Html.div [
                                            prop.className "lg:hidden w-28 h-44 flex-shrink-0 rounded-lg overflow-hidden shadow-xl border border-base-content/10"
                                            prop.children [
                                                match movie.PosterRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}"
                                                        prop.alt movie.Name
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.movie () ]
                                                    ]
                                            ]
                                        ]
                                        // Title & Meta
                                        Html.div [
                                            prop.className "flex-grow pb-2"
                                            prop.children [
                                                // Genre badges + Rating
                                                Html.div [
                                                    prop.className "flex flex-wrap items-center gap-3 mb-3"
                                                    prop.children [
                                                        for genre in movie.Genres |> List.truncate 3 do
                                                            Html.span [
                                                                prop.className "bg-primary/80 px-3 py-1 rounded text-xs font-bold tracking-wider uppercase text-primary-content"
                                                                prop.text genre
                                                            ]
                                                        HeroRating (movie.TmdbRating, movie.PersonalRating, model.IsRatingOpen, dispatch)
                                                    ]
                                                ]
                                                // Title
                                                Html.h1 [
                                                    prop.className "text-3xl lg:text-5xl font-bold font-display tracking-tight mb-2"
                                                    prop.text movie.Name
                                                ]
                                                // Year & Runtime
                                                Html.div [
                                                    prop.className "flex items-center gap-3 text-base-content/60 mb-4"
                                                    prop.children [
                                                        Html.span [ prop.text (string movie.Year) ]
                                                        match movie.Runtime with
                                                        | Some r ->
                                                            Html.span [ prop.className "text-base-content/30"; prop.text "\u00B7" ]
                                                            Html.span [ prop.text $"{r} min" ]
                                                        | None -> ()
                                                    ]
                                                ]
                                                // Trailer button
                                                match model.TrailerKey with
                                                | Some _ ->
                                                    Html.button [
                                                        prop.className "inline-flex items-center gap-2 bg-red-600/90 hover:bg-red-600 text-white px-4 py-2 rounded-full text-sm font-semibold transition-colors cursor-pointer"
                                                        prop.onClick (fun _ -> dispatch Open_trailer)
                                                        prop.children [
                                                            Icons.play ()
                                                            Html.span [ prop.text "Play Trailer" ]
                                                        ]
                                                    ]
                                                | None -> ()
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                // ── Content Grid ──
                Html.div [
                    prop.className "max-w-6xl mx-auto px-4 lg:px-8 pt-4 lg:pt-6 pb-8 lg:pb-12"
                    prop.children [
                        Html.div [
                            prop.className "grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-10"
                            prop.children [
                                // ── Left Column: Details ──
                                Html.div [
                                    prop.className "lg:col-span-8 space-y-10"
                                    prop.children [
                                        // Catalogs
                                        Html.div [
                                            prop.className "flex flex-wrap items-center gap-2"
                                            prop.children [
                                                // Add to catalog button
                                                Html.button [
                                                    prop.className "w-9 h-9 rounded-full bg-base-100/50 backdrop-blur-sm border border-base-content/15 hover:bg-base-100/70 text-base-content/50 hover:text-base-content flex items-center justify-center transition-colors cursor-pointer"
                                                    prop.onClick (fun _ -> dispatch Open_catalog_picker)
                                                    prop.children [
                                                        Html.span [ prop.className "[&>svg]:w-5 [&>svg]:h-5"; prop.children [ Icons.catalog () ] ]
                                                    ]
                                                ]
                                                // Selected catalog pills
                                                for cat in model.MovieCatalogs do
                                                    Html.span [
                                                        prop.className "inline-flex items-center gap-1.5 bg-transparent border border-base-content/20 text-base-content/70 px-3 py-1.5 rounded-full text-sm font-semibold transition-colors hover:border-base-content/40 group/pill"
                                                        prop.children [
                                                            Html.a [
                                                                prop.className "cursor-pointer hover:text-primary transition-colors"
                                                                prop.href (Feliz.Router.Router.format ("catalogs", cat.Slug))
                                                                prop.onClick (fun e ->
                                                                    e.preventDefault()
                                                                    Feliz.Router.Router.navigate ("catalogs", cat.Slug))
                                                                prop.text cat.Name
                                                            ]
                                                            Html.button [
                                                                prop.className "text-base-content/30 hover:text-error transition-colors cursor-pointer opacity-0 group-hover/pill:opacity-100"
                                                                prop.onClick (fun e ->
                                                                    e.stopPropagation()
                                                                    dispatch (Remove_from_catalog (cat.Slug, cat.EntryId)))
                                                                prop.text "\u00D7"
                                                            ]
                                                        ]
                                                    ]
                                            ]
                                        ]
                                        // Synopsis
                                        if not (System.String.IsNullOrWhiteSpace movie.Overview) then
                                            Html.section [
                                                prop.children [
                                                    sectionHeader "Synopsis"
                                                    Html.p [
                                                        prop.className "text-base-content/70 leading-relaxed text-lg"
                                                        prop.text movie.Overview
                                                    ]
                                                ]
                                            ]
                                        // Notes
                                        Html.section [
                                            prop.children [
                                                ContentBlockEditor.view
                                                    movie.ContentBlocks
                                                    (fun req -> dispatch (Add_content_block req))
                                                    (fun bid req -> dispatch (Update_content_block (bid, req)))
                                                    (fun bid -> dispatch (Remove_content_block bid))
                                                    (fun bid blockType -> dispatch (Change_content_block_type (bid, blockType)))
                                                    (fun blockIds -> dispatch (Reorder_content_blocks blockIds))
                                            ]
                                        ]
                                        // Cast
                                        if not (List.isEmpty movie.Cast) then
                                            let castToShow = model.FullCredits |> Option.map (fun c -> c.Cast) |> Option.defaultValue movie.Cast
                                            Html.section [
                                                prop.children [
                                                    sectionHeader "Cast"
                                                    Html.div [
                                                        prop.className "flex gap-6 overflow-x-auto pb-4 lg:flex-wrap lg:overflow-x-visible"
                                                        prop.children [
                                                            for c in castToShow do
                                                                castCard c
                                                        ]
                                                    ]
                                                    match model.FullCredits with
                                                    | None ->
                                                        Daisy.button.button [
                                                            button.ghost
                                                            button.sm
                                                            prop.className "mt-2"
                                                            prop.onClick (fun _ -> dispatch Load_full_credits)
                                                            prop.text "Load full cast & crew"
                                                        ]
                                                    | Some _ -> ()
                                                ]
                                            ]
                                        // Crew (only shown after loading full credits)
                                        match model.FullCredits with
                                        | Some credits when not (List.isEmpty credits.Crew) ->
                                            Html.section [
                                                prop.children [
                                                    sectionHeader "Crew"
                                                    Html.div [
                                                        prop.className "flex gap-6 overflow-x-auto pb-4 lg:flex-wrap lg:overflow-x-visible"
                                                        prop.children [
                                                            for c in credits.Crew do
                                                                crewCard c
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        | _ -> ()
                                    ]
                                ]
                                // ── Right Column: Social & Activity ──
                                Html.div [
                                    prop.className "lg:col-span-4 space-y-6"
                                    prop.children [
                                        // Friends card (merged Recommended By + Pending + Watch History)
                                        friendsCard movie model dispatch
                                        // Error display
                                        match model.Error with
                                        | Some err ->
                                            Daisy.alert [
                                                alert.error
                                                prop.text err
                                            ]
                                        | None -> ()
                                        // Remove movie
                                        Html.div [
                                            prop.className "pt-4"
                                            prop.children [
                                                if model.ConfirmingRemove then
                                                    Html.div [
                                                        prop.className "bg-error/10 border border-error/30 rounded-xl p-4 space-y-3"
                                                        prop.children [
                                                            Html.p [
                                                                prop.className "text-sm font-semibold text-error"
                                                                prop.text "Are you sure you want to remove this movie?"
                                                            ]
                                                            Html.div [
                                                                prop.className "flex gap-2"
                                                                prop.children [
                                                                    Daisy.button.button [
                                                                        button.error
                                                                        button.sm
                                                                        prop.className "flex-1"
                                                                        prop.onClick (fun _ -> dispatch Remove_movie)
                                                                        prop.text "Yes, remove"
                                                                    ]
                                                                    Daisy.button.button [
                                                                        button.ghost
                                                                        button.sm
                                                                        prop.className "flex-1"
                                                                        prop.onClick (fun _ -> dispatch Cancel_remove_movie)
                                                                        prop.text "Cancel"
                                                                    ]
                                                                ]
                                                            ]
                                                        ]
                                                    ]
                                                else
                                                    Daisy.button.button [
                                                        button.error
                                                        button.sm
                                                        prop.className "w-full"
                                                        prop.onClick (fun _ -> dispatch Confirm_remove_movie)
                                                        prop.text "Remove Movie"
                                                    ]
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                // Friend picker modals
                match model.ShowFriendPicker with
                | Some Recommend_picker ->
                    FriendManager
                        "Recommended By"
                        model.AllFriends
                        movie.RecommendedBy
                        (fun slug -> dispatch (Recommend_friend slug))
                        (fun slug -> dispatch (Remove_recommendation slug))
                        (fun name -> dispatch (Add_friend_and_recommend name))
                        (fun () -> dispatch Close_friend_picker)
                | Some Watch_with_picker ->
                    FriendManager
                        "Pending"
                        model.AllFriends
                        movie.WantToWatchWith
                        (fun slug -> dispatch (Want_to_watch_with slug))
                        (fun slug -> dispatch (Remove_want_to_watch_with slug))
                        (fun name -> dispatch (Add_friend_and_watch_with name))
                        (fun () -> dispatch Close_friend_picker)
                | Some (Session_friend_picker sessionId) ->
                    let sessionFriends =
                        movie.WatchSessions
                        |> List.tryFind (fun s -> s.SessionId = sessionId)
                        |> Option.map (fun s -> s.Friends)
                        |> Option.defaultValue []
                    FriendManager
                        "Watched With"
                        model.AllFriends
                        sessionFriends
                        (fun slug -> dispatch (Add_friend_to_session (sessionId, slug)))
                        (fun slug -> dispatch (Remove_friend_from_session (sessionId, slug)))
                        (fun name -> dispatch (Add_new_friend_to_session (sessionId, name)))
                        (fun () -> dispatch Close_friend_picker)
                | None -> ()
                // Catalog picker modal
                if model.ShowCatalogPicker then
                    CatalogManager
                        model.AllCatalogs
                        model.MovieCatalogs
                        (fun slug -> dispatch (Add_to_catalog slug))
                        (fun slug entryId -> dispatch (Remove_from_catalog (slug, entryId)))
                        (fun name -> dispatch (Create_catalog_and_add name))
                        (fun () -> dispatch Close_catalog_picker)
                // Trailer modal
                if model.ShowTrailer then
                    match model.TrailerKey with
                    | Some key ->
                        Html.div [
                            prop.className "fixed inset-0 z-50 flex items-center justify-center"
                            prop.children [
                                // Backdrop
                                Html.div [
                                    prop.className "absolute inset-0 bg-black/80"
                                    prop.onClick (fun _ -> dispatch Close_trailer)
                                ]
                                // Player container
                                Html.div [
                                    prop.className "relative w-full max-w-4xl mx-4 aspect-video"
                                    prop.children [
                                        Html.iframe [
                                            prop.className "w-full h-full rounded-xl"
                                            prop.src $"https://www.youtube.com/embed/{key}?autoplay=1&rel=0"
                                            prop.custom ("allow", "autoplay; encrypted-media")
                                            prop.custom ("allowFullScreen", true)
                                        ]
                                        // Close button
                                        Html.button [
                                            prop.className "absolute -top-10 right-0 text-white/70 hover:text-white text-2xl font-bold cursor-pointer"
                                            prop.onClick (fun _ -> dispatch Close_trailer)
                                            prop.text "\u00D7"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    | None -> ()
            ]
        ]
