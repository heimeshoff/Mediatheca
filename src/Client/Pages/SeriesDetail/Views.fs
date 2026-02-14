module Mediatheca.Client.Pages.SeriesDetail.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.SeriesDetail.Types
open Mediatheca.Client
open Mediatheca.Client.Components

// ── Helpers ──

let private sectionHeader (title: string) =
    Html.h2 [
        prop.className (DesignSystem.sectionHeader + " font-bold mb-6 flex items-center gap-2")
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

let private statusBadge (status: SeriesStatus) =
    let (color, label) =
        match status with
        | Returning -> (badge.success, "Returning")
        | Ended -> (badge.ghost, "Ended")
        | Canceled -> (badge.error, "Canceled")
        | InProduction -> (badge.warning, "In Production")
        | Planned -> (badge.info, "Planned")
        | UnknownStatus -> (badge.ghost, "Unknown")
    Daisy.badge [ badge.sm; color; prop.text label ]

let private glassCard (children: ReactElement list) =
    Html.div [
        prop.className (DesignSystem.glassCard + " p-6")
        prop.children children
    ]

let private friendAvatar (size: string) (fr: FriendRef) (extraClass: string) =
    Html.div [
        prop.className $"{size} rounded-full overflow-hidden flex items-center justify-center cursor-pointer {extraClass}"
        prop.title fr.Name
        prop.onClick (fun e ->
            e.stopPropagation()
            Router.navigate ("friends", fr.Slug))
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

// ── Rating ──

type private RatingOption = {
    Value: int
    Name: string
    Description: string
    Icon: unit -> ReactElement
    ColorClass: string
}

let private ratingOptions : RatingOption list = [
    { Value = 0; Name = "Unrated"; Description = "No rating yet"; Icon = Icons.questionCircle; ColorClass = "text-base-content/50" }
    { Value = 1; Name = "Waste"; Description = "Waste of time"; Icon = Icons.thumbsDown; ColorClass = "text-error" }
    { Value = 2; Name = "Meh"; Description = "Didn't click, uninspiring"; Icon = Icons.minusCircle; ColorClass = "text-secondary" }
    { Value = 3; Name = "Decent"; Description = "Watchable, even if not life-changing"; Icon = Icons.handOkay; ColorClass = "text-warning" }
    { Value = 4; Name = "Entertaining"; Description = "Strong craft, enjoyable"; Icon = Icons.thumbsUp; ColorClass = "text-success" }
    { Value = 5; Name = "Outstanding"; Description = "Absolutely brilliant, stays with you"; Icon = Icons.trophy; ColorClass = "text-primary" }
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
                                    prop.onClick (fun _ -> dispatch (Set_rating opt.Value))
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
                                prop.onClick (fun _ -> dispatch (Set_rating 0))
                                prop.children [
                                    Html.span [
                                        prop.className "w-5 h-5 text-base-content/40"
                                        prop.children [ Icons.questionCircle () ]
                                    ]
                                    Html.span [ prop.className "font-medium text-base-content/50"; prop.text "Clear rating" ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

// ── Cast ──

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

// ── Friend Manager (modal) ──

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
                prop.className "text-base-content/50 py-2 text-sm"
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

// ── Episodes Tab ──

let private episodeCard
    (seasonNumber: int)
    (episode: EpisodeDto)
    (isNextEpisode: bool)
    (model: Model)
    (dispatch: Msg -> unit) =
    let borderClass =
        if isNextEpisode then "border-primary/50 ring-1 ring-primary/30"
        else "border-base-content/8"
    let opacityClass =
        if episode.IsWatched then "" else "opacity-80"
    let isEditingDate = model.EditingEpisodeDate = Some (seasonNumber, episode.EpisodeNumber)
    Html.div [
        prop.className $"bg-base-100/50 backdrop-blur-sm border {borderClass} rounded-xl overflow-hidden transition-all {opacityClass}"
        prop.children [
            Html.div [
                prop.className "flex gap-4 p-4"
                prop.children [
                    // Episode still image
                    Html.div [
                        prop.className "w-36 h-20 flex-shrink-0 rounded-lg overflow-hidden bg-base-300"
                        prop.children [
                            match episode.StillRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt episode.Name
                                    prop.className "w-full h-full object-cover"
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex items-center justify-center w-full h-full text-base-content/20"
                                    prop.children [ Icons.tv () ]
                                ]
                        ]
                    ]
                    // Episode info
                    Html.div [
                        prop.className "flex-grow min-w-0"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center gap-2 mb-1"
                                prop.children [
                                    if isNextEpisode then
                                        Daisy.badge [
                                            badge.sm
                                            badge.primary
                                            prop.text "NEXT"
                                        ]
                                    Html.span [
                                        prop.className "text-xs font-bold text-primary uppercase tracking-wider"
                                        prop.text $"E{episode.EpisodeNumber:D2}"
                                    ]
                                    match episode.Runtime with
                                    | Some r ->
                                        Html.span [
                                            prop.className "text-xs text-base-content/40"
                                            prop.text $"{r}m"
                                        ]
                                    | None -> ()
                                ]
                            ]
                            Html.h4 [
                                prop.className "font-semibold text-sm mb-1 line-clamp-1"
                                prop.text episode.Name
                            ]
                            if not (System.String.IsNullOrWhiteSpace episode.Overview) then
                                Html.p [
                                    prop.className "text-xs text-base-content/50 line-clamp-2"
                                    prop.text episode.Overview
                                ]
                        ]
                    ]
                    // Watch toggle button + date
                    Html.div [
                        prop.className "flex flex-col items-center justify-center flex-shrink-0 gap-1"
                        prop.children [
                            Html.button [
                                prop.className (
                                    "w-10 h-10 rounded-full flex items-center justify-center transition-all cursor-pointer " +
                                    (if episode.IsWatched then
                                        "bg-success text-success-content hover:bg-success/80"
                                     else
                                        "bg-base-content/10 text-base-content/40 hover:bg-primary/20 hover:text-primary"))
                                prop.onClick (fun _ ->
                                    dispatch (Toggle_episode_watched (seasonNumber, episode.EpisodeNumber, episode.IsWatched)))
                                prop.children [
                                    if episode.IsWatched then
                                        Svg.svg [
                                            svg.className "w-5 h-5"
                                            svg.fill "none"
                                            svg.viewBox (0, 0, 24, 24)
                                            svg.stroke "currentColor"
                                            svg.custom ("strokeWidth", 2.5)
                                            svg.children [
                                                Svg.path [
                                                    svg.custom ("strokeLinecap", "round")
                                                    svg.custom ("strokeLinejoin", "round")
                                                    svg.d "m4.5 12.75 6 6 9-13.5"
                                                ]
                                            ]
                                        ]
                                    else
                                        Svg.svg [
                                            svg.className "w-5 h-5"
                                            svg.fill "none"
                                            svg.viewBox (0, 0, 24, 24)
                                            svg.stroke "currentColor"
                                            svg.custom ("strokeWidth", 1.5)
                                            svg.children [
                                                Svg.circle [
                                                    svg.cx 12
                                                    svg.cy 12
                                                    svg.r 9
                                                ]
                                            ]
                                        ]
                                ]
                            ]
                            // Watched date display
                            if episode.IsWatched then
                                if isEditingDate then
                                    Daisy.input [
                                        prop.className "w-28"
                                        input.xs
                                        prop.type' "date"
                                        prop.autoFocus true
                                        prop.value (episode.WatchedDate |> Option.defaultValue "")
                                        prop.onChange (fun (v: string) ->
                                            dispatch (Update_episode_date (seasonNumber, episode.EpisodeNumber, v)))
                                        prop.onBlur (fun _ ->
                                            dispatch Cancel_edit_episode_date)
                                        prop.onKeyDown (fun e ->
                                            if e.key = "Escape" then
                                                dispatch Cancel_edit_episode_date)
                                    ]
                                else
                                    Html.span [
                                        prop.className "text-[10px] text-base-content/40 cursor-pointer hover:text-primary transition-colors"
                                        prop.onClick (fun _ -> dispatch (Edit_episode_date (seasonNumber, episode.EpisodeNumber)))
                                        prop.text (episode.WatchedDate |> Option.defaultValue "No date")
                                    ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private seasonSidebar (seasons: SeasonDto list) (selectedSeason: int) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "space-y-1"
        prop.children [
            for season in seasons do
                let isSelected = season.SeasonNumber = selectedSeason
                let watchedCount = season.WatchedCount
                let totalCount = season.Episodes.Length
                Html.button [
                    prop.className (
                        "w-full text-left px-4 py-3 rounded-lg transition-all cursor-pointer " +
                        (if isSelected then "bg-primary/15 text-primary border border-primary/30"
                         else "hover:bg-base-content/5 text-base-content/70"))
                    prop.onClick (fun _ -> dispatch (Select_season season.SeasonNumber))
                    prop.children [
                        Html.div [
                            prop.className "flex items-center justify-between"
                            prop.children [
                                Html.span [
                                    prop.className "font-semibold text-sm"
                                    prop.text season.Name
                                ]
                                Html.span [
                                    prop.className "text-xs text-base-content/40"
                                    prop.text $"{watchedCount}/{totalCount}"
                                ]
                            ]
                        ]
                        // Progress bar
                        if totalCount > 0 then
                            Html.div [
                                prop.className "mt-2 h-1 bg-base-content/10 rounded-full overflow-hidden"
                                prop.children [
                                    Html.div [
                                        prop.className "h-full bg-primary rounded-full transition-all"
                                        prop.style [ style.width (length.percent (float watchedCount / float totalCount * 100.0)) ]
                                    ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

let private sessionAvatar (friends: FriendRef list) =
    if List.isEmpty friends then
        // Personal — single user silhouette
        Html.div [
            prop.className "w-10 h-10 rounded-full bg-primary/15 flex items-center justify-center text-primary flex-shrink-0"
            prop.children [
                Svg.svg [
                    svg.className "w-5 h-5"
                    svg.fill "none"
                    svg.viewBox (0, 0, 24, 24)
                    svg.stroke "currentColor"
                    svg.custom ("strokeWidth", 1.5)
                    svg.children [
                        Svg.path [
                            svg.custom ("strokeLinecap", "round")
                            svg.custom ("strokeLinejoin", "round")
                            svg.d "M15.75 6a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0ZM4.501 20.118a7.5 7.5 0 0 1 14.998 0A17.933 17.933 0 0 1 12 21.75c-2.676 0-5.216-.584-7.499-1.632Z"
                        ]
                    ]
                ]
            ]
        ]
    else
        // Friends — overlapping avatars
        Html.div [
            prop.className "flex -space-x-2 flex-shrink-0"
            prop.children [
                for fr in friends |> List.truncate 3 do
                    friendAvatar "w-10 h-10" fr "bg-secondary/20 text-sm font-bold text-secondary border-2 border-base-100"
            ]
        ]

let private rewatchSessionPanel (series: SeriesDetail) (model: Model) (dispatch: Msg -> unit) =
    let totalEpisodes = series.Seasons |> List.sumBy (fun s -> s.Episodes.Length)
    Html.div [
        prop.className "mb-6"
        prop.children [
            // Header
            Html.div [
                prop.className "flex items-center justify-between mb-4"
                prop.children [
                    Html.h4 [
                        prop.className "text-sm font-bold uppercase tracking-wider text-base-content/60"
                        prop.text "Watch Sessions"
                    ]
                    Html.button [
                        prop.className "w-7 h-7 rounded-full bg-primary flex items-center justify-center text-primary-content hover:scale-110 transition-transform text-sm font-bold cursor-pointer"
                        prop.onClick (fun _ -> dispatch Create_rewatch_session)
                        prop.text "+"
                    ]
                ]
            ]
            // Horizontal session cards
            Html.div [
                prop.className "flex gap-3 overflow-x-auto pb-2"
                prop.children [
                    for session in series.RewatchSessions do
                        let isSelected = session.RewatchId = model.SelectedRewatchId || (session.IsDefault && model.SelectedRewatchId = "default")
                        let pct =
                            if totalEpisodes = 0 then 0.0
                            else float session.WatchedCount / float totalEpisodes * 100.0
                        let sessionName =
                            if List.isEmpty session.Friends then "Personal"
                            else session.Friends |> List.map (fun f -> f.Name) |> String.concat ", "
                        Html.div [
                            prop.className (
                                "group relative flex-shrink-0 w-52 p-4 rounded-xl transition-all cursor-pointer border " +
                                DesignSystem.glassSubtle + " " +
                                (if isSelected then "border-primary/30 bg-primary/10"
                                 else "border-base-content/8 hover:border-base-content/15"))
                            prop.onClick (fun _ -> dispatch (Select_rewatch session.RewatchId))
                            prop.children [
                                // Avatar + Name row
                                Html.div [
                                    prop.className "flex items-center gap-3 mb-3"
                                    prop.children [
                                        sessionAvatar session.Friends
                                        Html.div [
                                            prop.className "flex-grow min-w-0"
                                            prop.children [
                                                Html.div [
                                                    prop.className "flex items-center justify-between gap-1"
                                                    prop.children [
                                                        Html.span [
                                                            prop.className "font-semibold text-sm truncate"
                                                            prop.text sessionName
                                                        ]
                                                        // Three-dots context menu
                                                        Html.div [
                                                            prop.className "relative flex-shrink-0"
                                                            prop.children [
                                                                Html.button [
                                                                    prop.className "w-6 h-6 flex items-center justify-center text-base-content/40 hover:text-base-content transition-colors cursor-pointer rounded-full hover:bg-base-content/10"
                                                                    prop.onClick (fun e ->
                                                                        e.stopPropagation()
                                                                        dispatch (Toggle_session_menu session.RewatchId))
                                                                    prop.children [
                                                                        Svg.svg [
                                                                            svg.className "w-4 h-4"
                                                                            svg.fill "currentColor"
                                                                            svg.viewBox (0, 0, 20, 20)
                                                                            svg.children [
                                                                                Svg.path [
                                                                                    svg.d "M10 6a2 2 0 1 1 0-4 2 2 0 0 1 0 4ZM10 12a2 2 0 1 1 0-4 2 2 0 0 1 0 4ZM10 18a2 2 0 1 1 0-4 2 2 0 0 1 0 4Z"
                                                                                ]
                                                                            ]
                                                                        ]
                                                                    ]
                                                                ]
                                                                // Dropdown menu
                                                                if model.SessionMenuOpen = Some session.RewatchId then
                                                                    Html.div [
                                                                        prop.className "absolute right-0 top-full mt-1 z-50 min-w-[160px] rating-dropdown py-1"
                                                                        prop.children [
                                                                            Html.button [
                                                                                prop.className "w-full flex items-center gap-2.5 px-3 py-2 text-sm text-left hover:bg-base-content/10 transition-colors cursor-pointer"
                                                                                prop.onClick (fun e ->
                                                                                    e.stopPropagation()
                                                                                    dispatch Close_session_menu
                                                                                    dispatch (Open_friend_picker (Session_friend_picker session.RewatchId)))
                                                                                prop.children [
                                                                                    Html.span [
                                                                                        prop.className "w-4 h-4 text-base-content/60"
                                                                                        prop.children [ Icons.friends () ]
                                                                                    ]
                                                                                    Html.span [ prop.text "Manage friends" ]
                                                                                ]
                                                                            ]
                                                                            if not session.IsDefault then
                                                                                Html.button [
                                                                                    prop.className "w-full flex items-center gap-2.5 px-3 py-2 text-sm text-left hover:bg-error/15 text-error/70 hover:text-error transition-colors cursor-pointer"
                                                                                    prop.onClick (fun e ->
                                                                                        e.stopPropagation()
                                                                                        dispatch Close_session_menu
                                                                                        dispatch (Remove_rewatch_session session.RewatchId))
                                                                                    prop.children [
                                                                                        Html.span [
                                                                                            prop.className "w-4 h-4"
                                                                                            prop.children [ Icons.trash () ]
                                                                                        ]
                                                                                        Html.span [ prop.text "Delete session" ]
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
                                // Progress
                                Html.div [
                                    prop.children [
                                        Html.div [
                                            prop.className "flex items-center justify-between mb-1.5"
                                            prop.children [
                                                Html.span [
                                                    prop.className "text-xs text-base-content/50"
                                                    prop.text "Progress"
                                                ]
                                                Html.span [
                                                    prop.className "text-xs font-semibold text-base-content/70"
                                                    prop.text (sprintf "%.0f%%" pct)
                                                ]
                                            ]
                                        ]
                                        Html.div [
                                            prop.className "h-1.5 bg-base-content/10 rounded-full overflow-hidden"
                                            prop.children [
                                                Html.div [
                                                    prop.className "h-full bg-primary rounded-full transition-all"
                                                    prop.style [ style.width (length.percent pct) ]
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

let private episodesTab (series: SeriesDetail) (model: Model) (dispatch: Msg -> unit) =
    let selectedSeason =
        series.Seasons
        |> List.tryFind (fun s -> s.SeasonNumber = model.SelectedSeason)
    Html.div [
        prop.className "space-y-6"
        prop.children [
            // Watch sessions at the top, full width
            if series.RewatchSessions.Length > 0 then
                rewatchSessionPanel series model dispatch
            Html.div [
                prop.className "grid grid-cols-1 lg:grid-cols-12 gap-6"
                prop.children [
                    // Season sidebar
                    Html.div [
                        prop.className "lg:col-span-3"
                        prop.children [
                            seasonSidebar series.Seasons model.SelectedSeason dispatch
                        ]
                    ]
                    // Episode list
                    Html.div [
                        prop.className "lg:col-span-9"
                        prop.children [
                            match selectedSeason with
                            | None ->
                                Html.p [
                                    prop.className "text-base-content/50"
                                    prop.text "Select a season to view episodes."
                                ]
                            | Some season ->
                                Html.div [
                                    prop.className "space-y-4"
                                    prop.children [
                                        // Season header
                                        Html.div [
                                            prop.className "flex items-center justify-between mb-2"
                                            prop.children [
                                                Html.div [
                                                    prop.children [
                                                        Html.h3 [
                                                            prop.className "text-xl font-bold font-display"
                                                            prop.text season.Name
                                                        ]
                                                        Html.p [
                                                            prop.className "text-sm text-base-content/50"
                                                            prop.text $"{season.Episodes.Length} Episodes Total"
                                                        ]
                                                    ]
                                                ]
                                                // Season trailer + Mark all / Unmark all
                                                Html.div [
                                                    prop.className "flex items-center gap-3"
                                                    prop.children [
                                                        match model.SeasonTrailerKeys |> Map.tryFind season.SeasonNumber with
                                                        | Some key ->
                                                            Html.button [
                                                                prop.className "inline-flex items-center gap-1.5 bg-red-600/90 hover:bg-red-600 text-white px-3 py-1.5 rounded-full text-xs font-semibold transition-colors cursor-pointer"
                                                                prop.onClick (fun _ -> dispatch (Open_trailer key))
                                                                prop.children [
                                                                    Icons.play ()
                                                                    Html.span [ prop.text "Trailer" ]
                                                                ]
                                                            ]
                                                        | None -> ()
                                                        let allWatched = season.Episodes |> List.forall (fun e -> e.IsWatched)
                                                        if allWatched then
                                                            Daisy.button.button [
                                                                button.sm
                                                                button.ghost
                                                                prop.className "text-error/70 hover:text-error"
                                                                prop.onClick (fun _ -> dispatch (Mark_season_unwatched season.SeasonNumber))
                                                                prop.text "Unmark All"
                                                            ]
                                                        else
                                                            Daisy.button.button [
                                                                button.sm
                                                                button.primary
                                                                button.outline
                                                                prop.onClick (fun _ -> dispatch (Mark_season_watched season.SeasonNumber))
                                                                prop.text "Mark All Watched"
                                                            ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                        // Find next episode for highlighting
                                        let nextEp =
                                            season.Episodes
                                            |> List.tryFind (fun e -> not e.IsWatched)
                                        // Episode cards with "Coming Next" divider
                                        let mutable shownDivider = false
                                        for ep in season.Episodes do
                                            let isNext =
                                                match nextEp with
                                                | Some n -> n.EpisodeNumber = ep.EpisodeNumber
                                                | None -> false
                                            if isNext && not shownDivider && ep.EpisodeNumber > 1 then
                                                shownDivider <- true
                                                Html.div [
                                                    prop.className "flex items-center gap-3 py-2"
                                                    prop.children [
                                                        Html.div [ prop.className "flex-grow h-px bg-primary/30" ]
                                                        Html.span [
                                                            prop.className "text-xs font-bold text-primary uppercase tracking-wider"
                                                            prop.text "Coming Next"
                                                        ]
                                                        Html.div [ prop.className "flex-grow h-px bg-primary/30" ]
                                                    ]
                                                ]
                                            episodeCard season.SeasonNumber ep isNext model dispatch
                                    ]
                                ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Overview Tab ──

let private overviewTab (series: SeriesDetail) (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-10"
        prop.children [
            // Left column: Synopsis + Notes
            Html.div [
                prop.className "lg:col-span-8 space-y-10"
                prop.children [
                    // Synopsis
                    if not (System.String.IsNullOrWhiteSpace series.Overview) then
                        Html.section [
                            prop.children [
                                sectionHeader "Synopsis"
                                Html.p [
                                    prop.className "text-base-content/70 leading-relaxed text-lg"
                                    prop.text series.Overview
                                ]
                            ]
                        ]
                    // Notes (Content Blocks)
                    Html.section [
                        prop.children [
                            ContentBlockEditor.view
                                series.ContentBlocks
                                (fun req -> dispatch (Add_content_block req))
                                (fun bid req -> dispatch (Update_content_block (bid, req)))
                                (fun bid -> dispatch (Remove_content_block bid))
                                (fun bid blockType -> dispatch (Change_content_block_type (bid, blockType)))
                                (fun blockIds -> dispatch (Reorder_content_blocks blockIds))
                        ]
                    ]
                    // Cast
                    if not (List.isEmpty series.Cast) then
                        Html.section [
                            prop.children [
                                sectionHeader "Cast"
                                Html.div [
                                    prop.className "flex gap-6 overflow-x-auto pb-4 lg:flex-wrap lg:overflow-x-visible"
                                    prop.children [
                                        for c in series.Cast do
                                            castCard c
                                    ]
                                ]
                            ]
                        ]
                ]
            ]
            // Right column: Social
            Html.div [
                prop.className "lg:col-span-4 space-y-6"
                prop.children [
                    // Personal Rating
                    personalRatingCard series.PersonalRating model.IsRatingOpen dispatch
                    // Recommended By card
                    glassCard [
                        Html.div [
                            prop.className "flex items-center justify-between mb-4"
                            prop.children [
                                Html.h3 [ prop.className "text-lg font-bold"; prop.text "Recommended By" ]
                                Html.button [
                                    prop.className "w-8 h-8 rounded-full bg-primary flex items-center justify-center text-primary-content hover:scale-110 transition-transform text-sm font-bold"
                                    prop.onClick (fun _ -> dispatch (Open_friend_picker Recommend_picker))
                                    prop.text "+"
                                ]
                            ]
                        ]
                        if List.isEmpty series.RecommendedBy then
                            Html.p [
                                prop.className "text-base-content/40 text-sm"
                                prop.text "No recommendations yet"
                            ]
                        else
                            Html.div [
                                prop.className "space-y-3"
                                prop.children [
                                    for fr in series.RecommendedBy do
                                        Html.div [
                                            prop.className "flex items-center justify-between group p-2 rounded-lg hover:bg-base-content/5 transition-colors"
                                            prop.children [
                                                Html.div [
                                                    prop.className "flex items-center gap-3"
                                                    prop.children [
                                                        friendAvatar "w-9 h-9" fr "bg-primary/20 text-sm font-bold text-primary"
                                                        Html.a [
                                                            prop.className "font-medium text-sm cursor-pointer hover:text-primary transition-colors"
                                                            prop.href (Router.format ("friends", fr.Slug))
                                                            prop.onClick (fun e ->
                                                                e.preventDefault()
                                                                Router.navigate ("friends", fr.Slug))
                                                            prop.text fr.Name
                                                        ]
                                                    ]
                                                ]
                                                Html.button [
                                                    prop.className "text-base-content/30 opacity-0 group-hover:opacity-100 transition-opacity text-xs hover:text-error"
                                                    prop.onClick (fun _ -> dispatch (Remove_recommendation fr.Slug))
                                                    prop.text "\u00D7"
                                                ]
                                            ]
                                        ]
                                ]
                            ]
                    ]
                    // Want to Watch With card
                    glassCard [
                        Html.div [
                            prop.className "flex items-center justify-between mb-4"
                            prop.children [
                                Html.h3 [ prop.className "text-lg font-bold"; prop.text "Watch With" ]
                                Html.button [
                                    prop.className "w-8 h-8 rounded-full bg-primary flex items-center justify-center text-primary-content hover:scale-110 transition-transform text-sm font-bold"
                                    prop.onClick (fun _ -> dispatch (Open_friend_picker Watch_with_picker))
                                    prop.text "+"
                                ]
                            ]
                        ]
                        Html.p [
                            prop.className "text-base-content/40 text-sm mb-4"
                            prop.text "Friends who want to watch this with you"
                        ]
                        if List.isEmpty series.WantToWatchWith then
                            Html.p [
                                prop.className "text-base-content/30 text-sm italic"
                                prop.text "No one yet"
                            ]
                        else
                            Html.div [
                                prop.className "space-y-3"
                                prop.children [
                                    for fr in series.WantToWatchWith do
                                        Html.div [
                                            prop.className "flex items-center justify-between group p-2 rounded-lg hover:bg-base-content/5 transition-colors"
                                            prop.children [
                                                Html.div [
                                                    prop.className "flex items-center gap-3"
                                                    prop.children [
                                                        friendAvatar "w-9 h-9" fr "bg-secondary/20 text-sm font-bold text-secondary"
                                                        Html.a [
                                                            prop.className "font-medium text-sm cursor-pointer hover:text-primary transition-colors"
                                                            prop.href (Router.format ("friends", fr.Slug))
                                                            prop.onClick (fun e ->
                                                                e.preventDefault()
                                                                Router.navigate ("friends", fr.Slug))
                                                            prop.text fr.Name
                                                        ]
                                                    ]
                                                ]
                                                Html.button [
                                                    prop.className "text-base-content/30 opacity-0 group-hover:opacity-100 transition-opacity text-xs hover:text-error"
                                                    prop.onClick (fun _ -> dispatch (Remove_watch_with fr.Slug))
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
                    // Remove series
                    Html.div [
                        prop.className "pt-4"
                        prop.children [
                            if model.ConfirmingRemove then
                                Html.div [
                                    prop.className "bg-error/10 border border-error/30 rounded-xl p-4 space-y-3"
                                    prop.children [
                                        Html.p [
                                            prop.className "text-sm font-semibold text-error"
                                            prop.text "Are you sure you want to remove this series?"
                                        ]
                                        Html.div [
                                            prop.className "flex gap-2"
                                            prop.children [
                                                Daisy.button.button [
                                                    button.error
                                                    button.sm
                                                    prop.className "flex-1"
                                                    prop.onClick (fun _ -> dispatch Remove_series)
                                                    prop.text "Yes, remove"
                                                ]
                                                Daisy.button.button [
                                                    button.ghost
                                                    button.sm
                                                    prop.className "flex-1"
                                                    prop.onClick (fun _ -> dispatch Cancel_remove_series)
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
                                    prop.onClick (fun _ -> dispatch Confirm_remove_series)
                                    prop.text "Remove Series"
                                ]
                        ]
                    ]
                ]
            ]
        ]
    ]

// ── Tab Bar ──

let private tabBar (activeTab: SeriesTab) (dispatch: Msg -> unit) =
    let tabs = [
        (Overview, "Overview")
        (Episodes, "Episodes")
    ]
    Html.div [
        prop.className "flex gap-1 border-b border-base-content/10 mb-8"
        prop.children [
            for (tab, label) in tabs do
                Html.button [
                    prop.className (
                        "px-5 py-3 text-sm font-semibold transition-all cursor-pointer " +
                        (if tab = activeTab then
                            "text-primary border-b-2 border-primary"
                         else
                            "text-base-content/50 hover:text-base-content"))
                    prop.onClick (fun _ -> dispatch (Set_tab tab))
                    prop.text label
                ]
        ]
    ]

// ── Main View ──

let view (model: Model) (dispatch: Msg -> unit) =
    match model.IsLoading, model.Detail with
    | true, _ ->
        Html.div [
            prop.className "flex justify-center py-12"
            prop.children [
                Daisy.loading [ loading.spinner; loading.lg ]
            ]
        ]
    | false, None ->
        PageContainer.view "Series Not Found" [
            Html.p [
                prop.className "text-base-content/70"
                prop.text "The series you're looking for doesn't exist."
            ]
            Html.a [
                prop.className "link link-primary mt-4 inline-block"
                prop.href (Router.format "series")
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate "series"
                )
                prop.text "Back to Series"
            ]
        ]
    | false, Some series ->
        Html.div [
            prop.children [
                // ── Hero Section ──
                Html.div [
                    prop.className "relative h-72 lg:h-[500px] w-full overflow-hidden"
                    prop.children [
                        // Backdrop image
                        Html.div [
                            prop.className "absolute inset-0"
                            prop.children [
                                match series.BackdropRef with
                                | Some ref ->
                                    Html.img [
                                        prop.src $"/images/{ref}"
                                        prop.alt series.Name
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
                                    prop.onClick (fun _ -> Router.navigate "series")
                                    prop.text "\u2190 Back"
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
                                        // Series Poster (desktop)
                                        Html.div [
                                            prop.className "hidden lg:block w-52 h-80 flex-shrink-0 rounded-xl overflow-hidden shadow-2xl border border-base-content/10"
                                            prop.children [
                                                match series.PosterRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}"
                                                        prop.alt series.Name
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.tv () ]
                                                    ]
                                            ]
                                        ]
                                        // Mobile poster (smaller)
                                        Html.div [
                                            prop.className "lg:hidden w-28 h-44 flex-shrink-0 rounded-lg overflow-hidden shadow-xl border border-base-content/10"
                                            prop.children [
                                                match series.PosterRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}"
                                                        prop.alt series.Name
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.tv () ]
                                                    ]
                                            ]
                                        ]
                                        // Title & Meta
                                        Html.div [
                                            prop.className "flex-grow pb-2"
                                            prop.children [
                                                // Genre badges + status + rating
                                                Html.div [
                                                    prop.className "flex flex-wrap items-center gap-3 mb-3"
                                                    prop.children [
                                                        statusBadge series.Status
                                                        for genre in series.Genres |> List.truncate 3 do
                                                            Html.span [
                                                                prop.className "bg-primary/80 px-3 py-1 rounded text-xs font-bold tracking-wider uppercase text-primary-content"
                                                                prop.text genre
                                                            ]
                                                        match series.TmdbRating with
                                                        | Some r -> starRating r
                                                        | None -> ()
                                                    ]
                                                ]
                                                // Title
                                                Html.h1 [
                                                    prop.className "text-3xl lg:text-5xl font-bold font-display tracking-tight mb-2"
                                                    prop.text series.Name
                                                ]
                                                // Year, runtime, seasons
                                                Html.div [
                                                    prop.className "flex items-center gap-3 text-base-content/50 mb-4"
                                                    prop.children [
                                                        Html.span [ prop.text (string series.Year) ]
                                                        match series.EpisodeRuntime with
                                                        | Some r ->
                                                            Html.span [ prop.className "text-base-content/30"; prop.text "\u00B7" ]
                                                            Html.span [ prop.text $"{r} min/ep" ]
                                                        | None -> ()
                                                        Html.span [ prop.className "text-base-content/30"; prop.text "\u00B7" ]
                                                        Html.span [ prop.text $"{series.Seasons.Length} Seasons" ]
                                                    ]
                                                ]
                                                // Trailer button
                                                match model.TrailerKey with
                                                | Some key ->
                                                    Html.button [
                                                        prop.className "inline-flex items-center gap-2 bg-red-600/90 hover:bg-red-600 text-white px-4 py-2 rounded-full text-sm font-semibold transition-colors cursor-pointer"
                                                        prop.onClick (fun _ -> dispatch (Open_trailer key))
                                                        prop.children [
                                                            Icons.play ()
                                                            Html.span [ prop.text "Play Trailer" ]
                                                        ]
                                                    ]
                                                | None -> ()
                                            ]
                                        ]
                                        // Next Up card (bottom-right of hero, desktop only)
                                        let nextUp =
                                            series.Seasons
                                            |> List.tryPick (fun s ->
                                                s.Episodes
                                                |> List.tryFind (fun e -> not e.IsWatched)
                                                |> Option.map (fun e -> s.SeasonNumber, e))
                                        match nextUp with
                                        | Some (sNum, ep) ->
                                            Html.div [
                                                prop.className "hidden lg:block flex-shrink-0"
                                                prop.children [
                                                    Html.div [
                                                        prop.className (DesignSystem.glassCard + " p-4 min-w-[180px]")
                                                        prop.children [
                                                            Html.p [
                                                                prop.className "text-xs font-bold text-primary uppercase tracking-wider mb-1"
                                                                prop.text "Next Up"
                                                            ]
                                                            Html.p [
                                                                prop.className "font-semibold text-sm"
                                                                prop.text $"Season {sNum}, Episode {ep.EpisodeNumber}"
                                                            ]
                                                            Html.p [
                                                                prop.className "text-xs text-base-content/50 line-clamp-1"
                                                                prop.text ep.Name
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        | None -> ()
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                // ── Tab Bar + Content ──
                Html.div [
                    prop.className "max-w-6xl mx-auto px-4 lg:px-8 pt-4 lg:pt-6 pb-8 lg:pb-12"
                    prop.children [
                        tabBar model.ActiveTab dispatch
                        match model.ActiveTab with
                        | Overview -> overviewTab series model dispatch
                        | Episodes -> episodesTab series model dispatch
                    ]
                ]
                // Friend picker modals
                match model.ShowFriendPicker with
                | Some Recommend_picker ->
                    FriendManager
                        "Recommended By"
                        model.Friends
                        series.RecommendedBy
                        (fun slug -> dispatch (Add_recommendation slug))
                        (fun slug -> dispatch (Remove_recommendation slug))
                        (fun name -> dispatch (Add_friend_and_recommend name))
                        (fun () -> dispatch Close_friend_picker)
                | Some Watch_with_picker ->
                    FriendManager
                        "Want to Watch With"
                        model.Friends
                        series.WantToWatchWith
                        (fun slug -> dispatch (Add_watch_with slug))
                        (fun slug -> dispatch (Remove_watch_with slug))
                        (fun name -> dispatch (Add_friend_and_watch_with name))
                        (fun () -> dispatch Close_friend_picker)
                | Some (Session_friend_picker rewatchId) ->
                    let sessionFriends =
                        series.RewatchSessions
                        |> List.tryFind (fun s -> s.RewatchId = rewatchId)
                        |> Option.map (fun s -> s.Friends)
                        |> Option.defaultValue []
                    FriendManager
                        "Session Friends"
                        model.Friends
                        sessionFriends
                        (fun slug -> dispatch (Add_rewatch_friend (rewatchId, slug)))
                        (fun slug -> dispatch (Remove_rewatch_friend (rewatchId, slug)))
                        (fun name -> dispatch (Add_friend_and_add_to_session (rewatchId, name)))
                        (fun () -> dispatch Close_friend_picker)
                | None -> ()
                // Trailer modal
                match model.ShowTrailer with
                | Some key ->
                    Html.div [
                        prop.className "fixed inset-0 z-50 flex items-center justify-center"
                        prop.children [
                            Html.div [
                                prop.className "absolute inset-0 bg-black/80"
                                prop.onClick (fun _ -> dispatch Close_trailer)
                            ]
                            Html.div [
                                prop.className "relative w-full max-w-4xl mx-4 aspect-video"
                                prop.children [
                                    Html.iframe [
                                        prop.className "w-full h-full rounded-xl"
                                        prop.src $"https://www.youtube.com/embed/{key}?autoplay=1&rel=0"
                                        prop.custom ("allow", "autoplay; encrypted-media")
                                        prop.custom ("allowFullScreen", true)
                                    ]
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
