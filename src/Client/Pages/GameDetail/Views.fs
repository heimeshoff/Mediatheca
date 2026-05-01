module Mediatheca.Client.Pages.GameDetail.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.GameDetail.Types
open Mediatheca.Client
open Mediatheca.Client.Components

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

let private glassCard (children: ReactElement list) =
    Html.div [
        prop.className "bg-base-100/50 backdrop-blur-xl border border-base-content/8 p-6 rounded-xl"
        prop.children children
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

let private statusBadgeClass (status: GameStatus) =
    match status with
    | Backlog -> "badge-ghost"
    | InFocus -> "badge-info"
    | Completed -> "badge-success"
    | Abandoned -> "badge-error"
    | OnHold -> "badge-warning"
    | Dismissed -> "badge-neutral"

let private statusLabel (status: GameStatus) =
    match status with
    | Backlog -> "Backlog"
    | InFocus -> "In Focus"
    | Completed -> "Completed"
    | Abandoned -> "Abandoned"
    | OnHold -> "On Hold"
    | Dismissed -> "Dismissed"

let private formatPlayTime (minutes: int) =
    if minutes = 0 then "No sessions"
    elif minutes < 60 then $"{minutes}m"
    else
        let h = minutes / 60
        let m = minutes % 60
        if m = 0 then $"{h}h"
        else $"{h}h {m}m"

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
    { Value = 3; Name = "Decent"; Description = "Enjoyable, even if not life-changing"; Icon = Icons.handOkay; ColorClass = "text-yellow-400" }
    { Value = 4; Name = "Entertaining"; Description = "Strong craft, enjoyable"; Icon = Icons.thumbsUp; ColorClass = "text-lime-400" }
    { Value = 5; Name = "Outstanding"; Description = "Absolutely brilliant, stays with you"; Icon = Icons.trophy; ColorClass = "text-amber-400" }
]

let private getRatingOption (rating: int option) =
    let r = rating |> Option.defaultValue 0
    ratingOptions |> List.find (fun opt -> opt.Value = r)

let private personalRatingCard (rating: int option) (isOpen: bool) (dispatch: Msg -> unit) =
    let currentOption = getRatingOption rating
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

[<ReactComponent>]
let private HeroRating (rawgRating: float option, personalRating: int option, isOpen: bool, dispatch: Msg -> unit) =
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
                                match rawgRating with
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
                Html.div [
                    prop.className "fixed inset-0 z-[200]"
                    prop.onClick (fun _ -> dispatch Toggle_rating_dropdown)
                ]
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

[<ReactComponent>]
let private HeroStatus (currentStatus: GameStatus, isOpen: bool, dispatch: Msg -> unit) =
    let triggerRef = React.useElementRef()
    let pos, setPos = React.useState {| top = 0.0; left = 0.0 |}
    let allStatuses = [ Backlog; InFocus; Completed; Abandoned; OnHold; Dismissed ]

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
                    Html.button [
                        prop.className "cursor-pointer hover:opacity-80 transition-opacity"
                        prop.onClick (fun _ -> dispatch Toggle_status_dropdown)
                        prop.children [
                            Daisy.badge [
                                prop.className (statusBadgeClass currentStatus)
                                prop.text (statusLabel currentStatus)
                            ]
                        ]
                    ]
                ]
            ]
            if isOpen then
                Html.div [
                    prop.className "fixed inset-0 z-[200]"
                    prop.onClick (fun _ -> dispatch Toggle_status_dropdown)
                ]
                Html.div [
                    prop.className "fixed z-[201] rating-dropdown"
                    prop.style [ style.top (int pos.top); style.left (int pos.left) ]
                    prop.children [
                        for status in allStatuses do
                            let isActive = status = currentStatus
                            let itemClass =
                                if isActive then "rating-dropdown-item rating-dropdown-item-active"
                                else "rating-dropdown-item"
                            Html.button [
                                prop.className itemClass
                                prop.onClick (fun _ -> dispatch (Set_game_status status))
                                prop.children [
                                    Daisy.badge [
                                        badge.sm
                                        prop.className (statusBadgeClass status)
                                        prop.text (statusLabel status)
                                    ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

[<ReactComponent>]
let private PlayModePicker
    (allPlayModes: string list)
    (currentModes: string list)
    (onAdd: string -> unit)
    (onRemove: string -> unit)
    (onClose: unit -> unit) =
    let searchText, setSearchText = React.useState("")
    let highlightedIndex, setHighlightedIndex = React.useState(0)
    let currentSet = currentModes |> Set.ofList
    let available =
        allPlayModes
        |> List.filter (fun m ->
            not (Set.contains m currentSet) &&
            (searchText = "" || m.ToLowerInvariant().Contains(searchText.ToLowerInvariant())))
    let availableArr = available |> List.toArray
    let trimmedSearch = searchText.Trim()
    let hasExactMatch = allPlayModes |> List.exists (fun m -> m.ToLowerInvariant() = trimmedSearch.ToLowerInvariant())
    let showCreateNew = trimmedSearch <> "" && not hasExactMatch
    let totalItems = availableArr.Length + (if showCreateNew then 1 else 0)

    let headerExtra = [
        if not (List.isEmpty currentModes) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-4"
                prop.children [
                    for mode in currentModes do
                        Html.span [
                            prop.className "inline-flex items-center gap-1.5 bg-transparent border border-base-content/20 text-base-content/70 px-3 py-1 rounded-full text-sm font-semibold transition-colors hover:border-base-content/40"
                            prop.children [
                                Html.span [ prop.text mode ]
                                Html.button [
                                    prop.className "text-base-content/40 hover:text-error transition-colors cursor-pointer ml-0.5"
                                    prop.onClick (fun e ->
                                        e.stopPropagation()
                                        onRemove mode)
                                    prop.text "\u00D7"
                                ]
                            ]
                        ]
                ]
            ]
        Daisy.input [
            prop.className "w-full mb-4"
            prop.type' "text"
            prop.placeholder "Search play modes..."
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
                        onAdd availableArr.[highlightedIndex]
                        setSearchText ""
                        setHighlightedIndex 0
                    elif showCreateNew && highlightedIndex = availableArr.Length then
                        onAdd trimmedSearch
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
                    if List.isEmpty allPlayModes && trimmedSearch = "" then "No play modes found across games."
                    elif trimmedSearch = "" then "All known play modes already assigned."
                    else "No matches found."
                )
            ]
        else
            Html.div [
                prop.className "space-y-1"
                prop.children [
                    for i in 0 .. availableArr.Length - 1 do
                        let mode = availableArr.[i]
                        let isHighlighted = (i = highlightedIndex)
                        Html.div [
                            prop.className (
                                "flex items-center gap-3 p-2 rounded-lg cursor-pointer " +
                                (if isHighlighted then "bg-primary/20" else "hover:bg-base-200"))
                            prop.onClick (fun _ -> onAdd mode)
                            prop.children [
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300 flex items-center justify-center text-base-content/40"
                                    prop.children [ Icons.gamepad () ]
                                ]
                                Html.span [ prop.className "font-semibold"; prop.text mode ]
                            ]
                        ]
                    if showCreateNew then
                        let isHighlighted = (highlightedIndex = availableArr.Length)
                        Html.div [
                            prop.className (
                                "flex items-center gap-3 p-2 rounded-lg cursor-pointer " +
                                (if isHighlighted then "bg-primary/20" else "hover:bg-base-200"))
                            prop.onClick (fun _ ->
                                onAdd trimmedSearch
                                setSearchText ""
                                setHighlightedIndex 0)
                            prop.children [
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300 flex items-center justify-center text-base-content/40 text-lg"
                                    prop.text "+"
                                ]
                                Html.span [
                                    prop.className "font-semibold"
                                    prop.text $"Add \"{trimmedSearch}\""
                                ]
                            ]
                        ]
                ]
            ]
    ]

    ModalPanel.viewCustom "Play Modes" onClose headerExtra content []

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

[<ReactComponent>]
let private CatalogManager
    (allCatalogs: CatalogListItem list)
    (gameCatalogs: CatalogRef list)
    (onAdd: string -> unit)
    (onRemove: string -> string -> unit)
    (onCreateNew: string -> unit)
    (onClose: unit -> unit) =
    let searchText, setSearchText = React.useState("")
    let highlightedIndex, setHighlightedIndex = React.useState(0)
    let selectedSlugs = gameCatalogs |> List.map (fun c -> c.Slug) |> Set.ofList
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
        if not (List.isEmpty gameCatalogs) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-4"
                prop.children [
                    for cat in gameCatalogs do
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
                    elif trimmedSearch = "" then "Game already in all catalogs."
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

// Stable DOM id the ConnectSteamPicker uses to anchor itself to the
// Connect button. Kept in one place so button + picker stay in sync.
let private connectSteamTriggerId = "connect-steam-trigger"

/// Button rendered inside the Links section. The picker popover is NOT nested
/// here — it's rendered at the top level of `view` to avoid the
/// `backdrop-filter` nesting bug (Links uses `glassCard` which has blur).
let private connectSteamButton (connectState: ConnectSteamState) (dispatch: Msg -> unit) =
    let buttonClass =
        "flex items-center gap-3 p-2 rounded-lg hover:bg-base-content/5 transition-colors text-sm font-medium text-base-content/50 hover:text-primary w-full text-left"
    Html.div [
        prop.id connectSteamTriggerId
        prop.children [
            match connectState with
            | Searching ->
                Html.button [
                    prop.disabled true
                    prop.className buttonClass
                    prop.children [
                        Icons.gamepad ()
                        Html.span [ prop.text "Searching Steam…" ]
                        Html.span [ prop.className "ml-auto loading loading-spinner loading-xs text-base-content/40" ]
                    ]
                ]
            | Attaching _ ->
                Html.button [
                    prop.disabled true
                    prop.className buttonClass
                    prop.children [
                        Icons.gamepad ()
                        Html.span [ prop.text "Attaching…" ]
                        Html.span [ prop.className "ml-auto loading loading-spinner loading-xs text-base-content/40" ]
                    ]
                ]
            | _ ->
                Html.button [
                    prop.className buttonClass
                    prop.onClick (fun _ -> dispatch Connect_steam_requested)
                    prop.children [
                        Icons.gamepad ()
                        Html.span [ prop.text "Connect with Steam" ]
                    ]
                ]
            match connectState with
            | Failed err ->
                Html.div [
                    prop.className "mt-2 flex items-start gap-2 text-xs text-error/80 px-2"
                    prop.children [
                        Html.span [ prop.className "flex-1"; prop.text err ]
                        Html.button [
                            prop.className "text-base-content/50 hover:text-base-content underline"
                            prop.onClick (fun _ -> dispatch Connect_steam_dismissed)
                            prop.text "Dismiss"
                        ]
                    ]
                ]
            | _ -> ()
        ]
    ]

/// Glassmorphic popover listing Steam candidates. Rendered at the root of
/// `view` (outside any `glassCard` / `backdrop-filter` ancestor) so its own
/// `rating-dropdown` blur is applied against the page, not against the parent
/// card — see CLAUDE.md "nested backdrop-filter" gotcha.
[<ReactComponent>]
let private ConnectSteamPicker (candidates: SteamSearchResult list, dispatch: Msg -> unit) =
    let pos, setPos = React.useState {| top = 0.0; left = 0.0; width = 0.0 |}

    React.useEffect ((fun () ->
        let doc = Browser.Dom.document
        let el = doc.getElementById connectSteamTriggerId
        if not (isNull el) then
            let rect = el.getBoundingClientRect()
            setPos {| top = rect.bottom + 8.0; left = rect.left; width = rect.width |}
    ), [||])

    let popoverMinWidth = max 320.0 pos.width

    Html.div [
        prop.children [
            // Full-screen catcher — click outside to dismiss
            Html.div [
                prop.className "fixed inset-0 z-[200]"
                prop.onClick (fun _ -> dispatch Connect_steam_dismissed)
            ]
            Html.div [
                prop.className "fixed z-[201] rating-dropdown p-2"
                prop.style [
                    style.top (int pos.top)
                    style.left (int pos.left)
                    style.minWidth (length.px (int popoverMinWidth))
                    style.maxWidth (length.px 420)
                ]
                prop.children [
                    Html.div [
                        prop.className "px-2 py-1 text-[11px] uppercase tracking-wider text-base-content/50 font-semibold"
                        prop.text "Choose the matching Steam title"
                    ]
                    for c in candidates do
                        Html.div [
                            prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-content/5 transition-colors"
                            prop.children [
                                match c.HeaderImageUrl with
                                | Some url ->
                                    Html.img [
                                        prop.src url
                                        prop.alt c.Name
                                        prop.className "w-20 h-10 object-cover rounded flex-shrink-0 bg-base-300"
                                    ]
                                | None ->
                                    Html.div [
                                        prop.className "w-20 h-10 rounded flex-shrink-0 bg-base-300 flex items-center justify-center text-base-content/30"
                                        prop.children [ Icons.gamepad () ]
                                    ]
                                Html.div [
                                    prop.className "flex-1 min-w-0"
                                    prop.children [
                                        Html.div [ prop.className "text-sm font-medium truncate"; prop.text c.Name ]
                                        Html.div [
                                            prop.className "text-xs text-base-content/50"
                                            prop.text (
                                                match c.ReleaseYear with
                                                | Some y -> sprintf "%d" y
                                                | None -> "Unknown year")
                                        ]
                                    ]
                                ]
                                Html.button [
                                    prop.className "btn btn-xs btn-primary"
                                    prop.onClick (fun _ -> dispatch (Steam_candidate_chosen c.AppId))
                                    prop.text "Choose"
                                ]
                            ]
                        ]
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) (onBack: unit -> unit) =
    match model.IsLoading, model.Game with
    | true, _ ->
        Html.div [
            prop.className "flex justify-center py-12"
            prop.children [
                Daisy.loading [ loading.spinner; loading.lg ]
            ]
        ]
    | false, None ->
        PageContainer.view "Game Not Found" [
            Html.p [
                prop.className "text-base-content/70"
                prop.text "The game you're looking for doesn't exist."
            ]
            Html.a [
                prop.className "link link-primary mt-4 inline-block"
                prop.href (Router.format "games")
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate "games"
                )
                prop.text "Back to Games"
            ]
        ]
    | false, Some game ->
        Html.div [
            prop.children [
                // Hero Section
                Html.div [
                    prop.className "relative h-72 lg:h-[500px] w-full overflow-hidden group/hero"
                    prop.children [
                        // Backdrop image
                        Html.div [
                            prop.className "absolute inset-0"
                            prop.children [
                                match game.BackdropRef with
                                | Some ref ->
                                    Html.img [
                                        prop.src $"/images/{ref}?v={model.ImageVersion}"
                                        prop.alt game.Name
                                        prop.className "w-full h-full object-cover"
                                    ]
                                | None ->
                                    match game.CoverRef with
                                    | Some ref ->
                                        Html.img [
                                            prop.src $"/images/{ref}?v={model.ImageVersion}"
                                            prop.alt game.Name
                                            prop.className "w-full h-full object-cover blur-xl scale-125"
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
                                    prop.onClick (fun _ -> onBack ())
                                    prop.text "\u2190 Back"
                                ]
                            ]
                        ]
                        // Top-right action buttons (hover-reveal)
                        Html.div [
                            prop.className "absolute top-4 right-4 z-10 opacity-0 group-hover/hero:opacity-100 transition-opacity flex items-center gap-2"
                            prop.children [
                                Daisy.button.button [
                                    button.ghost
                                    button.sm
                                    prop.className "text-base-content backdrop-blur-sm bg-base-300/30"
                                    prop.onClick (fun _ -> dispatch (Open_image_picker Backdrop_picker))
                                    prop.text "Change backdrop"
                                ]
                                ActionMenu.heroView [
                                    { Label = "Event Log"
                                      Icon = Some Icons.events
                                      OnClick = fun () -> dispatch Open_event_history
                                      IsDestructive = false }
                                    { Label = "Remove Game"
                                      Icon = Some Icons.trash
                                      OnClick = fun () -> dispatch Confirm_remove_game
                                      IsDestructive = true }
                                ]
                            ]
                        ]
                        // Remove-game confirmation popover (anchored under the action menu)
                        if model.ConfirmingRemove then
                            Html.div [
                                prop.className "absolute top-16 right-4 z-[250] rating-dropdown p-4"
                                prop.style [ style.minWidth 260 ]
                                prop.children [
                                    Html.p [
                                        prop.className "text-sm font-semibold text-error mb-3"
                                        prop.text "Remove this game?"
                                    ]
                                    Html.div [
                                        prop.className "flex gap-2"
                                        prop.children [
                                            Daisy.button.button [
                                                button.error
                                                button.sm
                                                prop.className "flex-1"
                                                prop.onClick (fun _ -> dispatch Remove_game)
                                                prop.text "Yes, remove"
                                            ]
                                            Daisy.button.button [
                                                button.ghost
                                                button.sm
                                                prop.className "flex-1"
                                                prop.onClick (fun _ -> dispatch Cancel_remove_game)
                                                prop.text "Cancel"
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        // Hero content at bottom
                        Html.div [
                            prop.className "relative h-full flex items-end pb-6 lg:pb-8 px-4 lg:px-8"
                            prop.children [
                                Html.div [
                                    prop.className "flex gap-6 lg:gap-10 items-end w-full max-w-6xl mx-auto"
                                    prop.children [
                                        // Cover image (desktop)
                                        Html.div [
                                            prop.className "hidden lg:block w-52 h-80 flex-shrink-0 rounded-xl overflow-hidden shadow-2xl border border-base-content/10 cursor-pointer group/cover relative"
                                            prop.onClick (fun _ -> dispatch (Open_image_picker Cover_picker))
                                            prop.children [
                                                match game.CoverRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}?v={model.ImageVersion}"
                                                        prop.alt game.Name
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.gamepad () ]
                                                    ]
                                                Html.div [
                                                    prop.className "absolute inset-0 bg-black/50 opacity-0 group-hover/cover:opacity-100 transition-opacity flex items-center justify-center"
                                                    prop.children [
                                                        Html.span [ prop.className "text-white text-sm font-semibold"; prop.text "Change cover" ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                        // Cover image (mobile)
                                        Html.div [
                                            prop.className "lg:hidden w-28 h-44 flex-shrink-0 rounded-lg overflow-hidden shadow-xl border border-base-content/10 cursor-pointer group/cover relative"
                                            prop.onClick (fun _ -> dispatch (Open_image_picker Cover_picker))
                                            prop.children [
                                                match game.CoverRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}?v={model.ImageVersion}"
                                                        prop.alt game.Name
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.gamepad () ]
                                                    ]
                                                Html.div [
                                                    prop.className "absolute inset-0 bg-black/50 opacity-0 group-hover/cover:opacity-100 transition-opacity flex items-center justify-center"
                                                    prop.children [
                                                        Html.span [ prop.className "text-white text-xs font-semibold"; prop.text "Change" ]
                                                    ]
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
                                                        for genre in game.Genres |> List.truncate 3 do
                                                            Html.span [
                                                                prop.className "bg-primary/80 px-3 py-1 rounded text-xs font-bold tracking-wider uppercase text-primary-content"
                                                                prop.text genre
                                                            ]
                                                        HeroStatus (game.Status, model.IsStatusOpen, dispatch)
                                                        HeroRating (game.RawgRating, game.PersonalRating, model.IsRatingOpen, dispatch)
                                                    ]
                                                ]
                                                // Title
                                                Html.h1 [
                                                    prop.className "text-3xl lg:text-5xl font-bold font-display tracking-tight mb-2"
                                                    prop.text game.Name
                                                ]
                                                // Year & Play time
                                                Html.div [
                                                    prop.className "flex items-center gap-3 text-base-content/60 mb-4"
                                                    prop.children [
                                                        Html.span [ prop.text (string game.Year) ]
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
                // Tab Bar + Content
                Html.div [
                    prop.className "max-w-6xl mx-auto px-4 lg:px-8 pt-4 lg:pt-6 pb-8 lg:pb-12"
                    prop.children [
                        // Tab bar
                        Html.div [
                            prop.className "flex gap-1 border-b border-base-content/10 mb-8"
                            prop.children [
                                for (tab, label) in [ (Overview, "Overview"); (Journal, "Journal") ] do
                                    Html.button [
                                        prop.className (
                                            "px-5 py-3 text-sm font-semibold transition-all cursor-pointer " +
                                            (if tab = model.ActiveTab then
                                                "text-primary border-b-2 border-primary"
                                             else
                                                "text-base-content/50 hover:text-base-content"))
                                        prop.onClick (fun _ -> dispatch (Set_tab tab))
                                        prop.text label
                                    ]
                            ]
                        ]
                        // Tab content
                        match model.ActiveTab with
                        | Overview ->
                            let visibleTrailers =
                                model.Trailers
                                |> List.filter (fun t -> not (Set.contains t.VideoUrl model.FailedTrailerUrls))
                            let selectedTrailer =
                                match model.PlayingTrailerUrl with
                                | Some url ->
                                    visibleTrailers |> List.tryFind (fun t -> t.VideoUrl = url)
                                | None -> None
                            Html.div [
                                prop.className "grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-10"
                                prop.children [
                                    // Left Column: Details
                                    Html.div [
                                        prop.className "lg:col-span-8 space-y-10"
                                        prop.children [
                                            // Trailers: video port on top, thumbnail strip below (only if >1 trailer)
                                            if not (List.isEmpty visibleTrailers) then
                                                Html.section [
                                                    prop.children [
                                                        sectionHeader "Trailers"
                                                        // Main video port
                                                        Html.div [
                                                            prop.className (
                                                                if List.length visibleTrailers > 1 then
                                                                    "relative w-full aspect-video rounded-xl overflow-hidden glass-card mb-4"
                                                                else
                                                                    "relative w-full aspect-video rounded-xl overflow-hidden glass-card")
                                                            prop.children [
                                                                match selectedTrailer with
                                                                | Some trailer ->
                                                                    Mediatheca.Client.Components.HlsVideo.view
                                                                        trailer.VideoUrl
                                                                        "absolute inset-0 w-full h-full object-contain bg-black"
                                                                        (trailer.ThumbnailUrl |> Option.defaultValue "")
                                                                        (fun () -> dispatch (Trailer_errored trailer.VideoUrl))
                                                                | None -> ()
                                                            ]
                                                        ]
                                                        // Thumbnail strip — only when there are multiple trailers
                                                        if List.length visibleTrailers > 1 then
                                                            Html.div [
                                                                prop.className "flex gap-3 overflow-x-auto p-1.5 pb-3 -m-1.5"
                                                                prop.children [
                                                                    for trailer in visibleTrailers do
                                                                        let isSelected =
                                                                            model.PlayingTrailerUrl = Some trailer.VideoUrl
                                                                        Html.button [
                                                                            prop.key trailer.VideoUrl
                                                                            prop.className (
                                                                                "relative flex-none w-40 aspect-video rounded-lg overflow-hidden cursor-pointer transition-all group/thumb "
                                                                                + (if isSelected then "ring-2 ring-primary ring-offset-2 ring-offset-base-300"
                                                                                   else "opacity-70 hover:opacity-100"))
                                                                            prop.title (trailer.Title |> Option.defaultValue "Trailer")
                                                                            prop.onClick (fun _ ->
                                                                                dispatch (Play_trailer_inline trailer.VideoUrl))
                                                                            prop.children [
                                                                                match trailer.ThumbnailUrl with
                                                                                | Some thumb ->
                                                                                    Html.img [
                                                                                        prop.className "absolute inset-0 w-full h-full object-cover"
                                                                                        prop.src thumb
                                                                                        prop.alt (trailer.Title |> Option.defaultValue "Trailer")
                                                                                        prop.custom ("loading", "lazy")
                                                                                    ]
                                                                                | None ->
                                                                                    Html.div [
                                                                                        prop.className "absolute inset-0 bg-base-300"
                                                                                    ]
                                                                                if not isSelected then
                                                                                    Html.div [
                                                                                        prop.className "absolute inset-0 bg-gradient-to-t from-black/60 to-black/10 group-hover/thumb:from-black/40"
                                                                                    ]
                                                                                    Html.div [
                                                                                        prop.className "absolute inset-0 flex items-center justify-center"
                                                                                        prop.children [
                                                                                            Html.span [
                                                                                                prop.className "w-9 h-9 rounded-full bg-white/85 group-hover/thumb:bg-white text-black flex items-center justify-center shadow [&>svg]:w-4 [&>svg]:h-4 [&>svg]:ml-0.5"
                                                                                                prop.children [ Icons.play () ]
                                                                                            ]
                                                                                        ]
                                                                                    ]
                                                                            ]
                                                                        ]
                                                                ]
                                                            ]
                                                    ]
                                                ]
                                            // Catalogs
                                            Html.div [
                                                prop.className "flex flex-wrap items-center gap-2"
                                                prop.children [
                                                    Html.button [
                                                        prop.className "w-9 h-9 rounded-full bg-base-100/50 backdrop-blur-sm border border-base-content/15 hover:bg-base-100/70 text-base-content/50 hover:text-base-content flex items-center justify-center transition-colors cursor-pointer"
                                                        prop.onClick (fun _ -> dispatch Open_catalog_picker)
                                                        prop.children [
                                                            Html.span [ prop.className "[&>svg]:w-5 [&>svg]:h-5"; prop.children [ Icons.catalog () ] ]
                                                        ]
                                                    ]
                                                    for cat in model.GameCatalogs do
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
                                            // Description (expandable)
                                            if not (System.String.IsNullOrWhiteSpace game.Description) || not (System.String.IsNullOrWhiteSpace game.ShortDescription) then
                                                Html.section [
                                                    prop.children [
                                                        sectionHeader "Description"
                                                        let hasShort = not (System.String.IsNullOrWhiteSpace game.ShortDescription)
                                                        let hasFull = not (System.String.IsNullOrWhiteSpace game.Description)
                                                        let showShort = hasShort && hasFull && not model.IsDescriptionExpanded
                                                        if showShort then
                                                            Html.div [
                                                                prop.children [
                                                                    Html.p [
                                                                        prop.className "text-base-content/70 leading-relaxed text-lg"
                                                                        prop.text game.ShortDescription
                                                                    ]
                                                                    Html.button [
                                                                        prop.className "text-primary text-sm font-medium mt-2 cursor-pointer hover:underline"
                                                                        prop.onClick (fun _ -> dispatch Toggle_description_expanded)
                                                                        prop.text "Read more\u2026"
                                                                    ]
                                                                ]
                                                            ]
                                                        elif hasFull then
                                                            Html.div [
                                                                prop.children [
                                                                    Html.p [
                                                                        prop.className "text-base-content/70 leading-relaxed text-lg"
                                                                        prop.text game.Description
                                                                    ]
                                                                    if hasShort then
                                                                        Html.button [
                                                                            prop.className "text-primary text-sm font-medium mt-2 cursor-pointer hover:underline"
                                                                            prop.onClick (fun _ -> dispatch Toggle_description_expanded)
                                                                            prop.text "Show less"
                                                                        ]
                                                                ]
                                                            ]
                                                        else
                                                            Html.p [
                                                                prop.className "text-base-content/70 leading-relaxed text-lg"
                                                                prop.text game.ShortDescription
                                                            ]
                                                    ]
                                                ]
                                        ]
                                    ]
                                    // Right Column: Social & Activity
                                    Html.div [
                                        prop.className "lg:col-span-4 space-y-6"
                                        prop.children [
                                            // External Links
                                            glassCard [
                                                Html.h3 [ prop.className "text-lg font-bold mb-4"; prop.text "Links" ]
                                                Html.div [
                                                    prop.className "space-y-3"
                                                    prop.children [
                                                        match game.SteamAppId with
                                                        | Some appId ->
                                                            // Linked state: Steam Store link + small refresh button to re-run
                                                            // the search and pick a different App ID. Both share the
                                                            // connectSteamTriggerId so the picker popover anchors here.
                                                            let isBusy =
                                                                match model.ConnectSteamState with
                                                                | Searching | Attaching _ -> true
                                                                | _ -> false
                                                            Html.div [
                                                                prop.id connectSteamTriggerId
                                                                prop.children [
                                                                    Html.div [
                                                                        prop.className "flex items-center gap-1"
                                                                        prop.children [
                                                                            Html.a [
                                                                                prop.className "flex-1 flex items-center gap-3 p-2 rounded-lg hover:bg-base-content/5 transition-colors text-sm font-medium text-base-content/70 hover:text-primary"
                                                                                prop.href $"https://store.steampowered.com/app/{appId}/"
                                                                                prop.target "_blank"
                                                                                prop.rel "noopener noreferrer"
                                                                                prop.children [
                                                                                    Icons.gamepad ()
                                                                                    Html.span [ prop.text "Steam Store" ]
                                                                                    Html.span [ prop.className "ml-auto text-base-content/30"; prop.children [ Icons.externalLink () ] ]
                                                                                ]
                                                                            ]
                                                                            Html.button [
                                                                                prop.className (
                                                                                    "btn btn-ghost btn-xs btn-circle text-base-content/50 hover:text-primary transition-colors"
                                                                                    + (if isBusy then " animate-spin" else "")
                                                                                )
                                                                                prop.disabled isBusy
                                                                                prop.title "Re-link to a different Steam title"
                                                                                prop.onClick (fun _ -> dispatch Connect_steam_requested)
                                                                                prop.children [ Icons.arrowPathSm () ]
                                                                            ]
                                                                        ]
                                                                    ]
                                                                    match model.ConnectSteamState with
                                                                    | Failed err ->
                                                                        Html.div [
                                                                            prop.className "mt-2 flex items-start gap-2 text-xs text-error/80 px-2"
                                                                            prop.children [
                                                                                Html.span [ prop.className "flex-1"; prop.text err ]
                                                                                Html.button [
                                                                                    prop.className "text-base-content/50 hover:text-base-content underline"
                                                                                    prop.onClick (fun _ -> dispatch Connect_steam_dismissed)
                                                                                    prop.text "Dismiss"
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    | _ -> ()
                                                                ]
                                                            ]
                                                        | None ->
                                                            connectSteamButton model.ConnectSteamState dispatch
                                                        match game.WebsiteUrl with
                                                        | Some url ->
                                                            Html.a [
                                                                prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-content/5 transition-colors text-sm font-medium text-base-content/70 hover:text-primary"
                                                                prop.href url
                                                                prop.target "_blank"
                                                                prop.rel "noopener noreferrer"
                                                                prop.children [
                                                                    Icons.globe ()
                                                                    Html.span [ prop.text "Official Website" ]
                                                                    Html.span [ prop.className "ml-auto text-base-content/30"; prop.children [ Icons.externalLink () ] ]
                                                                ]
                                                            ]
                                                        | None -> ()
                                                        Html.a [
                                                            prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-content/5 transition-colors text-sm font-medium text-base-content/70 hover:text-primary"
                                                            prop.href $"https://howlongtobeat.com/?q={System.Uri.EscapeDataString(game.Name)}"
                                                            prop.target "_blank"
                                                            prop.rel "noopener noreferrer"
                                                            prop.children [
                                                                Icons.hourglass ()
                                                                Html.span [ prop.text "HowLongToBeat" ]
                                                                Html.span [ prop.className "ml-auto text-base-content/30"; prop.children [ Icons.externalLink () ] ]
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                            // HowLongToBeat
                                            glassCard [
                                                Html.div [
                                                    prop.className "flex items-center justify-between mb-4"
                                                    prop.children [
                                                        Html.h3 [
                                                            prop.className "text-lg font-bold flex items-center gap-2"
                                                            prop.children [
                                                                Icons.hourglass ()
                                                                Html.text "HowLongToBeat"
                                                            ]
                                                        ]
                                                        Html.button [
                                                            prop.className (
                                                                "btn btn-ghost btn-xs btn-circle text-base-content/50 hover:text-primary transition-colors"
                                                                + (if model.HltbFetching then " animate-spin" else "")
                                                            )
                                                            prop.disabled model.HltbFetching
                                                            prop.title "Refresh HowLongToBeat data"
                                                            prop.onClick (fun _ -> dispatch Fetch_hltb)
                                                            prop.children [ Icons.arrowPathSm () ]
                                                        ]
                                                    ]
                                                ]
                                                match game.HltbHours with
                                                | Some mainHours ->
                                                    let totalPlayHours = float game.TotalPlayTimeMinutes / 60.0
                                                    let mainPlusHours = game.HltbMainPlusHours
                                                    let completionistHours = game.HltbCompletionistHours
                                                    // Find the max value for scaling bars
                                                    let allValues =
                                                        [ yield mainHours
                                                          yield totalPlayHours
                                                          match mainPlusHours with Some h -> yield h | None -> ()
                                                          match completionistHours with Some h -> yield h | None -> () ]
                                                    let maxHours = allValues |> List.max |> max 1.0
                                                    let barPercent (h: float) = min 100.0 (h / maxHours * 100.0)
                                                    // Build list of bars: (label, hours, colorClass, valueColorClass)
                                                    let bars =
                                                        [ yield ("Main Story", mainHours, "bg-blue-500", "text-blue-400")
                                                          match mainPlusHours with Some h -> yield ("Main + Extra", h, "bg-blue-500", "text-blue-400") | None -> ()
                                                          match completionistHours with Some h -> yield ("Completionist", h, "bg-blue-500", "text-blue-400") | None -> ()
                                                          if totalPlayHours > 0.0 then yield ("Your Time", totalPlayHours, "bg-emerald-500", "text-emerald-400") ]
                                                        |> List.sortBy (fun (_, h, _, _) -> h)
                                                    Html.div [
                                                        prop.className "space-y-3"
                                                        prop.children [
                                                            for (label, hours, barColor, valueColor) in bars do
                                                                Html.div [
                                                                    prop.className "space-y-1"
                                                                    prop.children [
                                                                        Html.div [
                                                                            prop.className "flex justify-between text-xs text-base-content/60"
                                                                            prop.children [
                                                                                Html.span [ prop.text label ]
                                                                                Html.span [ prop.className $"font-semibold {valueColor}"; prop.text $"%.1f{hours}h" ]
                                                                            ]
                                                                        ]
                                                                        Html.div [
                                                                            prop.className "w-full bg-base-content/10 rounded-full h-2.5 overflow-hidden"
                                                                            prop.children [
                                                                                Html.div [
                                                                                    prop.className $"h-full rounded-full {barColor} transition-all duration-500"
                                                                                    prop.style [ style.width (length.percent (barPercent hours)) ]
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    ]
                                                                ]
                                                        ]
                                                    ]
                                                | None ->
                                                    if model.HltbFetching then
                                                        Html.div [
                                                            prop.className "flex items-center gap-2"
                                                            prop.children [
                                                                Html.span [ prop.className "loading loading-spinner loading-sm text-primary" ]
                                                                Html.span [
                                                                    prop.className "text-sm text-base-content/60"
                                                                    prop.text "Fetching from HowLongToBeat..."
                                                                ]
                                                            ]
                                                        ]
                                                    elif model.HltbNoData then
                                                        Html.p [
                                                            prop.className "text-sm text-base-content/40 italic"
                                                            prop.text "No HLTB data available for this game"
                                                        ]
                                                    else
                                                        Html.p [
                                                            prop.className "text-sm text-base-content/40 italic"
                                                            prop.text "Click refresh to fetch from HowLongToBeat"
                                                        ]
                                            ]
                                            // Play History
                                            let editState = model.PlaySessionEditState
                                            let isCardVisible =
                                                not (List.isEmpty model.PlaySessions) || editState <> EditIdle
                                            if isCardVisible then
                                                let editingId =
                                                    match editState with
                                                    | Editing (id, _) -> Some id
                                                    | _ -> None
                                                let isSaving = editState = Saving
                                                let validateDraft (draft: PlaySessionDraft) =
                                                    if System.String.IsNullOrWhiteSpace(draft.Date) then false
                                                    else
                                                        match System.Int32.TryParse(draft.MinutesText) with
                                                        | true, m -> m > 0 && m <= 24 * 60
                                                        | _ -> false
                                                let renderDraftEditor (draft: PlaySessionDraft) =
                                                    let saveDisabled = isSaving || not (validateDraft draft)
                                                    Html.div [
                                                        prop.className "py-2 border-b border-base-content/5 last:border-0"
                                                        prop.children [
                                                            Html.div [
                                                                prop.className "flex items-center gap-2 flex-wrap"
                                                                prop.children [
                                                                    Html.input [
                                                                        prop.type' "date"
                                                                        prop.className "input input-xs input-bordered bg-base-100/50"
                                                                        prop.value draft.Date
                                                                        prop.onChange (fun (v: string) -> dispatch (Session_draft_date_changed v))
                                                                    ]
                                                                    Html.input [
                                                                        prop.type' "number"
                                                                        prop.placeholder "min"
                                                                        prop.className "input input-xs input-bordered bg-base-100/50 w-20"
                                                                        prop.value draft.MinutesText
                                                                        prop.min 1
                                                                        prop.max (24 * 60)
                                                                        prop.onChange (fun (v: string) -> dispatch (Session_draft_minutes_changed v))
                                                                    ]
                                                                    Daisy.button.button [
                                                                        button.primary
                                                                        button.xs
                                                                        prop.disabled saveDisabled
                                                                        prop.onClick (fun _ -> dispatch Session_draft_save)
                                                                        prop.text "Save"
                                                                    ]
                                                                    Daisy.button.button [
                                                                        button.ghost
                                                                        button.xs
                                                                        prop.disabled isSaving
                                                                        prop.onClick (fun _ -> dispatch Session_draft_cancel)
                                                                        prop.text "Cancel"
                                                                    ]
                                                                ]
                                                            ]
                                                            match editState with
                                                            | EditFailed err ->
                                                                Html.p [
                                                                    prop.className "text-xs text-error mt-1"
                                                                    prop.text err
                                                                ]
                                                            | _ -> ()
                                                        ]
                                                    ]
                                                glassCard [
                                                    Html.div [
                                                        prop.className "flex items-center justify-between mb-4"
                                                        prop.children [
                                                            Html.h3 [ prop.className "text-lg font-bold"; prop.text "Play History" ]
                                                            match editState with
                                                            | EditIdle | EditFailed _ ->
                                                                Html.button [
                                                                    prop.className "w-8 h-8 rounded-full bg-primary flex items-center justify-center text-primary-content hover:scale-110 transition-transform text-sm font-bold cursor-pointer"
                                                                    prop.title "Add play session"
                                                                    prop.onClick (fun _ -> dispatch Add_session_clicked)
                                                                    prop.text "+"
                                                                ]
                                                            | _ -> ()
                                                        ]
                                                    ]
                                                    Html.div [
                                                        prop.className "space-y-2"
                                                        prop.children [
                                                            // Inline editor for "Adding" appears at the top
                                                            match editState with
                                                            | Adding draft -> renderDraftEditor draft
                                                            | _ -> ()
                                                            for session in model.PlaySessions |> List.truncate 10 do
                                                                if Some session.Id = editingId then
                                                                    match editState with
                                                                    | Editing (_, draft) -> renderDraftEditor draft
                                                                    | _ -> ()
                                                                else
                                                                    Html.div [
                                                                        prop.className "flex items-center justify-between py-1.5 border-b border-base-content/5 last:border-0 group"
                                                                        prop.children [
                                                                            Html.div [
                                                                                prop.className "flex items-center gap-2"
                                                                                prop.children [
                                                                                    Html.span [
                                                                                        prop.className "text-sm text-base-content/60"
                                                                                        prop.text session.Date
                                                                                    ]
                                                                                    if session.Source = Manual then
                                                                                        Html.span [
                                                                                            prop.className "text-[10px] uppercase tracking-wider text-base-content/40 border border-base-content/15 rounded px-1.5 py-0.5"
                                                                                            prop.text "manual"
                                                                                        ]
                                                                                ]
                                                                            ]
                                                                            Html.div [
                                                                                prop.className "flex items-center gap-2"
                                                                                prop.children [
                                                                                    Html.span [
                                                                                        prop.className "text-sm font-semibold text-primary"
                                                                                        prop.text (formatPlayTime session.MinutesPlayed)
                                                                                    ]
                                                                                    Html.div [
                                                                                        prop.className "flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity"
                                                                                        prop.children [
                                                                                            Html.button [
                                                                                                prop.className "p-1 rounded hover:bg-base-content/10 text-base-content/60 hover:text-base-content cursor-pointer"
                                                                                                prop.title "Edit session"
                                                                                                prop.onClick (fun _ -> dispatch (Edit_session_clicked session))
                                                                                                prop.children [ Icons.edit () ]
                                                                                            ]
                                                                                            Html.button [
                                                                                                prop.className "p-1 rounded hover:bg-error/15 text-base-content/60 hover:text-error cursor-pointer"
                                                                                                prop.title "Delete session"
                                                                                                prop.onClick (fun _ -> dispatch (Delete_session_requested session.Id))
                                                                                                prop.children [
                                                                                                    Svg.svg [
                                                                                                        svg.className "w-4 h-4"
                                                                                                        svg.fill "none"
                                                                                                        svg.viewBox (0, 0, 24, 24)
                                                                                                        svg.stroke "currentColor"
                                                                                                        svg.custom ("strokeWidth", 1.5)
                                                                                                        svg.children [
                                                                                                            Svg.path [
                                                                                                                svg.custom ("strokeLinecap", "round")
                                                                                                                svg.custom ("strokeLinejoin", "round")
                                                                                                                svg.d "m14.74 9-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 0 1-2.244 2.077H8.084a2.25 2.25 0 0 1-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 0 0-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 0 1 3.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 0 0-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 0 0-7.5 0"
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
                                                        ]
                                                    ]
                                                    // Inline error from EditFailed when not currently in a draft
                                                    match editState with
                                                    | EditFailed err when editingId.IsNone ->
                                                        Html.p [
                                                            prop.className "text-xs text-error mt-2"
                                                            prop.text err
                                                        ]
                                                    | _ -> ()
                                                ]

                                            // Delete confirmation modal (glassmorphic, sibling of any blurred parent)
                                            match model.PendingDelete with
                                            | Some pendingId ->
                                                let pendingSession =
                                                    model.PlaySessions
                                                    |> List.tryFind (fun s -> s.Id = pendingId)
                                                Html.div [
                                                    prop.className "fixed inset-0 z-[300] flex items-center justify-center p-4"
                                                    prop.onClick (fun _ -> dispatch Delete_session_cancelled)
                                                    prop.onKeyDown (fun e ->
                                                        if e.key = "Escape" then dispatch Delete_session_cancelled)
                                                    prop.children [
                                                        // Dim backdrop
                                                        Html.div [
                                                            prop.className "absolute inset-0 bg-base-300/40"
                                                        ]
                                                        // Glassmorphic dialog
                                                        Html.div [
                                                            prop.className "relative rating-dropdown p-6 max-w-sm w-full"
                                                            prop.onClick (fun e -> e.stopPropagation())
                                                            prop.children [
                                                                Html.h3 [
                                                                    prop.className "text-lg font-bold text-error mb-2"
                                                                    prop.text "Delete this play session?"
                                                                ]
                                                                match pendingSession with
                                                                | Some s ->
                                                                    Html.p [
                                                                        prop.className "text-sm text-base-content/70 mb-4"
                                                                        prop.text $"{s.Date} · {formatPlayTime s.MinutesPlayed}"
                                                                    ]
                                                                | None -> ()
                                                                Html.div [
                                                                    prop.className "flex gap-2"
                                                                    prop.children [
                                                                        Daisy.button.button [
                                                                            button.error
                                                                            button.sm
                                                                            prop.className "flex-1"
                                                                            prop.onClick (fun _ -> dispatch Delete_session_confirmed)
                                                                            prop.text "Delete"
                                                                        ]
                                                                        Daisy.button.button [
                                                                            button.ghost
                                                                            button.sm
                                                                            prop.className "flex-1"
                                                                            prop.onClick (fun _ -> dispatch Delete_session_cancelled)
                                                                            prop.text "Cancel"
                                                                        ]
                                                                    ]
                                                                ]
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            | None -> ()

                                            // Friends (consolidated)
                                            let hasOwnership = game.IsOwnedByMe || not (List.isEmpty game.FamilyOwners)
                                            let hasRecommended = not (List.isEmpty game.RecommendedBy)
                                            let hasPending = not (List.isEmpty game.WantToPlayWith)
                                            let hasPlayedWith = not (List.isEmpty game.PlayedWith)
                                            let isEmpty = not hasOwnership && not hasRecommended && not hasPending && not hasPlayedWith
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
                                                        // Owned By sub-section
                                                        if hasOwnership then
                                                            Html.div [
                                                                prop.className "mb-4"
                                                                prop.children [
                                                                    Html.div [
                                                                        prop.className "flex items-baseline gap-2 mb-2"
                                                                        prop.children [
                                                                            Html.p [
                                                                                prop.className "text-xs font-bold text-base-content/40 uppercase tracking-wider"
                                                                                prop.text "Owned By"
                                                                            ]
                                                                            match game.SteamLibraryDate with
                                                                            | Some date ->
                                                                                Html.span [
                                                                                    prop.className "text-xs text-base-content/30"
                                                                                    prop.text $"since {date}"
                                                                                ]
                                                                            | None -> ()
                                                                        ]
                                                                    ]
                                                                    Html.div [
                                                                        prop.className "flex flex-wrap gap-2"
                                                                        prop.children [
                                                                            if game.IsOwnedByMe then
                                                                                Daisy.badge [ badge.lg; badge.primary; prop.className "font-semibold"; prop.text "Me" ]
                                                                            for fr in game.FamilyOwners do
                                                                                FriendPill.view fr
                                                                        ]
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
                                                                            for fr in game.RecommendedBy do
                                                                                FriendPill.view fr
                                                                        ]
                                                                    ]
                                                                ]
                                                            ]
                                                        // Pending sub-section
                                                        if hasPending then
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
                                                                            for fr in game.WantToPlayWith do
                                                                                FriendPill.view fr
                                                                        ]
                                                                    ]
                                                                ]
                                                            ]
                                                        // Played With sub-section
                                                        if hasPlayedWith then
                                                            Html.div [
                                                                prop.children [
                                                                    Html.p [
                                                                        prop.className "text-xs font-bold text-base-content/40 uppercase tracking-wider mb-2"
                                                                        prop.text "Played With"
                                                                    ]
                                                                    Html.div [
                                                                        prop.className "flex flex-wrap gap-2"
                                                                        prop.children [
                                                                            for fr in game.PlayedWith do
                                                                                FriendPill.view fr
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
                                                                        dispatch Toggle_ownership)
                                                                    prop.children [
                                                                        Html.span [
                                                                            prop.className "text-base-content/60"
                                                                            prop.text (if game.IsOwnedByMe then "Remove Ownership" else "Mark as Owned")
                                                                        ]
                                                                    ]
                                                                ]
                                                                Html.div [ prop.className "border-t border-base-content/10 my-1" ]
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
                                                                        dispatch (Open_friend_picker Play_with_picker))
                                                                    prop.children [
                                                                        Html.span [ prop.className "text-base-content/60"; prop.text "Pending" ]
                                                                    ]
                                                                ]
                                                                Html.button [
                                                                    prop.className "w-full flex items-center gap-3 px-3 py-2.5 text-sm text-left hover:bg-base-content/10 transition-colors cursor-pointer"
                                                                    prop.onClick (fun _ ->
                                                                        dispatch Close_friends_menu
                                                                        dispatch (Open_friend_picker Played_with_picker))
                                                                    prop.children [
                                                                        Html.span [ prop.className "text-base-content/60"; prop.text "Played With" ]
                                                                    ]
                                                                ]
                                                            ]
                                                        ]
                                                ]
                                            ]
                                            // Play Modes
                                            glassCard [
                                                Html.div [
                                                    prop.className "flex items-center justify-between mb-4"
                                                    prop.children [
                                                        Html.h3 [ prop.className "text-lg font-bold"; prop.text "Play Modes" ]
                                                        Html.button [
                                                            prop.className "w-8 h-8 rounded-full bg-primary flex items-center justify-center text-primary-content hover:scale-110 transition-transform text-sm font-bold cursor-pointer"
                                                            prop.onClick (fun _ -> dispatch Toggle_play_mode_picker)
                                                            prop.text "+"
                                                        ]
                                                    ]
                                                ]
                                                if List.isEmpty game.PlayModes then
                                                    Html.p [
                                                        prop.className "text-base-content/30 text-sm italic"
                                                        prop.text "No play modes yet"
                                                    ]
                                                else
                                                    Html.div [
                                                        prop.className "flex flex-wrap gap-2"
                                                        prop.children [
                                                            for playMode in game.PlayModes do
                                                                Html.span [
                                                                    prop.className "inline-flex items-center gap-1.5 bg-base-100/50 border border-base-content/15 px-3 py-1.5 rounded-lg text-sm font-medium group/mode"
                                                                    prop.children [
                                                                        Html.span [ prop.text playMode ]
                                                                        Html.button [
                                                                            prop.className "text-base-content/30 hover:text-error transition-colors cursor-pointer opacity-0 group-hover/mode:opacity-100"
                                                                            prop.onClick (fun _ -> dispatch (Remove_play_mode playMode))
                                                                            prop.text "\u00D7"
                                                                        ]
                                                                    ]
                                                                ]
                                                        ]
                                                    ]
                                            ]
                                            // Error display
                                            match model.Error with
                                            | Some err ->
                                                Daisy.alert [
                                                    alert.error
                                                    prop.text err
                                                ]
                                            | None -> ()
                                        ]
                                    ]
                                ]
                            ]
                        | Journal ->
                            Html.section [
                                prop.children [
                                    ContentBlockEditor.view
                                        game.ContentBlocks
                                        (fun req -> dispatch (Add_content_block req))
                                        (fun bid req -> dispatch (Update_content_block (bid, req)))
                                        (fun bid -> dispatch (Remove_content_block bid))
                                        (fun bid blockType -> dispatch (Change_content_block_type (bid, blockType)))
                                        (fun blockIds -> dispatch (Reorder_content_blocks blockIds))
                                        (Some (fun data filename insertBefore -> dispatch (Upload_screenshot (data, filename, insertBefore))))
                                        (Some (fun leftId rightId -> dispatch (Group_content_blocks (leftId, rightId))))
                                        (Some (fun blockId -> dispatch (Ungroup_content_block blockId)))
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
                        game.RecommendedBy
                        (fun slug -> dispatch (Recommend_friend slug))
                        (fun slug -> dispatch (Remove_recommendation slug))
                        (fun name -> dispatch (Add_friend_and_recommend name))
                        (fun () -> dispatch Close_friend_picker)
                | Some Play_with_picker ->
                    FriendManager
                        "Pending"
                        model.AllFriends
                        game.WantToPlayWith
                        (fun slug -> dispatch (Want_to_play_with slug))
                        (fun slug -> dispatch (Remove_want_to_play_with slug))
                        (fun name -> dispatch (Add_friend_and_play_with name))
                        (fun () -> dispatch Close_friend_picker)
                | Some Played_with_picker ->
                    FriendManager
                        "Played With"
                        model.AllFriends
                        game.PlayedWith
                        (fun slug -> dispatch (Add_played_with slug))
                        (fun slug -> dispatch (Remove_played_with slug))
                        (fun name -> dispatch (Add_friend_and_played_with name))
                        (fun () -> dispatch Close_friend_picker)
                | None -> ()
                // Catalog picker modal
                if model.ShowCatalogPicker then
                    CatalogManager
                        model.AllCatalogs
                        model.GameCatalogs
                        (fun slug -> dispatch (Add_to_catalog slug))
                        (fun slug entryId -> dispatch (Remove_from_catalog (slug, entryId)))
                        (fun name -> dispatch (Create_catalog_and_add name))
                        (fun () -> dispatch Close_catalog_picker)
                // Play mode picker modal
                if model.ShowPlayModePicker then
                    PlayModePicker
                        model.AllPlayModes
                        game.PlayModes
                        (fun mode -> dispatch (Add_play_mode mode))
                        (fun mode -> dispatch (Remove_play_mode mode))
                        (fun () -> dispatch Toggle_play_mode_picker)
                // Image picker modal
                match model.ShowImagePicker with
                | Some pickerKind ->
                    let title = match pickerKind with Cover_picker -> "Choose Cover Image" | Backdrop_picker -> "Choose Backdrop Image"
                    let isCoverPicker = match pickerKind with Cover_picker -> true | Backdrop_picker -> false
                    let filtered =
                        if isCoverPicker then
                            model.ImageCandidates
                            |> List.filter (fun c -> not c.IsCurrent || c.IsCover)
                            |> List.sortByDescending (fun c -> c.IsCurrent, c.IsCover)
                        else
                            model.ImageCandidates
                            |> List.filter (fun c -> not c.IsCover)
                            |> List.sortByDescending (fun c -> c.IsCurrent)
                    let content = [
                        if model.IsSelectingImage then
                            Html.div [
                                prop.className "flex flex-col items-center justify-center py-12 gap-3"
                                prop.children [
                                    Daisy.loading [ loading.spinner; loading.lg ]
                                    Html.p [ prop.className "text-base-content/60"; prop.text "Downloading image..." ]
                                ]
                            ]
                        elif model.IsLoadingImages then
                            Html.div [
                                prop.className "flex justify-center py-12"
                                prop.children [ Daisy.loading [ loading.spinner; loading.lg ] ]
                            ]
                        elif List.isEmpty model.ImageCandidates then
                            Html.p [
                                prop.className "text-base-content/60 py-8 text-center"
                                prop.text "No image sources available. Add a Steam App ID or RAWG ID to this game first."
                            ]
                        elif List.isEmpty filtered then
                            Html.p [
                                prop.className "text-base-content/60 py-8 text-center"
                                prop.text "No matching images found."
                            ]
                        else
                            Html.div [
                                prop.className $"grid grid-cols-2 sm:grid-cols-3 gap-3"
                                prop.children [
                                    for candidate in filtered do
                                        let imgUrl =
                                            if candidate.IsCurrent then $"{candidate.Url}?v={model.ImageVersion}"
                                            else candidate.Url
                                        Html.button [
                                            prop.className (
                                                if candidate.IsCurrent then
                                                    "group/thumb rounded-lg overflow-hidden border-2 border-primary ring-1 ring-primary/30 bg-base-200 cursor-default opacity-80"
                                                else
                                                    "group/thumb rounded-lg overflow-hidden border border-base-content/10 hover:border-primary/50 transition-colors cursor-pointer bg-base-200"
                                            )
                                            if not candidate.IsCurrent then
                                                prop.onClick (fun _ -> dispatch (Select_image candidate.Url))
                                            prop.children [
                                                Html.div [
                                                    prop.className (
                                                        "relative " + (if isCoverPicker then "aspect-[2/3]" else "aspect-video")
                                                    )
                                                    prop.children [
                                                        Html.img [
                                                            prop.src imgUrl
                                                            prop.alt candidate.Label
                                                            prop.className "w-full h-full object-cover"
                                                        ]
                                                        if candidate.IsCurrent then
                                                            Html.span [
                                                                prop.className "absolute top-1.5 left-1.5 px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider bg-primary text-primary-content rounded"
                                                                prop.text "Current"
                                                            ]
                                                    ]
                                                ]
                                                Html.div [
                                                    prop.className "p-2"
                                                    prop.children [
                                                        Html.p [ prop.className "text-xs font-medium truncate"; prop.text candidate.Label ]
                                                        Html.p [ prop.className "text-xs text-base-content/50"; prop.text candidate.Source ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                ]
                            ]
                    ]
                    ModalPanel.view title (fun () -> dispatch Close_image_picker) content
                | None -> ()
                // Event History Modal
                if model.ShowEventHistory then
                    EventHistoryModal.view $"Game-{model.Slug}" (fun () -> dispatch Close_event_history)
                // Connect-with-Steam candidate picker — rendered at view root so
                // its rating-dropdown backdrop-filter isn't nested under the
                // Links glassCard's backdrop-blur (see CLAUDE.md gotcha).
                match model.ConnectSteamState with
                | ShowingCandidates candidates ->
                    ConnectSteamPicker (candidates, dispatch)
                | _ -> ()
            ]
        ]
