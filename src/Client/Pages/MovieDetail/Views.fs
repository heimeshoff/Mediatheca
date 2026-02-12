module Mediatheca.Client.Pages.MovieDetail.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.MovieDetail.Types
open Mediatheca.Client.Components

let private castCard (cast: CastMemberDto) =
    Html.div [
        prop.className "flex-none w-24"
        prop.children [
            Html.div [
                prop.className "aspect-[2/3] rounded-lg bg-base-300 overflow-hidden mb-1"
                prop.children [
                    match cast.ImageRef with
                    | Some ref ->
                        Html.img [
                            prop.src $"/images/{ref}"
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
                prop.className "text-xs font-semibold line-clamp-1"
                prop.text cast.Name
            ]
            Html.p [
                prop.className "text-xs text-base-content/60 line-clamp-1"
                prop.text cast.Role
            ]
        ]
    ]

let private friendListRow (friend: FriendListItem) (onClick: unit -> unit) =
    Html.div [
        prop.className "flex items-center gap-3 p-2 rounded-lg hover:bg-base-200 cursor-pointer"
        prop.onClick (fun _ -> onClick ())
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

let private friendPicker
    (allFriends: FriendListItem list)
    (excludeSlugs: string list)
    (onSelect: string -> unit)
    (onClose: unit -> unit) =
    let available = allFriends |> List.filter (fun f -> not (List.contains f.Slug excludeSlugs))
    ModalPanel.view "Select a Friend" onClose [
        if List.isEmpty available then
            Html.p [
                prop.className "text-base-content/60 py-4"
                prop.text "No friends available. Add friends first."
            ]
        else
            Html.div [
                prop.className "space-y-2"
                prop.children [
                    for friend in available do
                        friendListRow friend (fun () -> onSelect friend.Slug)
                ]
            ]
    ]

let private friendChip (friendRef: FriendRef) (onRemove: string -> unit) =
    Daisy.badge [
        badge.lg
        prop.className "gap-1"
        prop.children [
            Html.a [
                prop.href (Router.format ("friends", friendRef.Slug))
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate ("friends", friendRef.Slug)
                )
                prop.text friendRef.Name
            ]
            Html.button [
                prop.className "btn btn-ghost btn-xs"
                prop.onClick (fun _ -> onRemove friendRef.Slug)
                prop.text "x"
            ]
        ]
    ]

[<ReactComponent>]
let private RecommendationManager
    (allFriends: FriendListItem list)
    (recommendedBy: FriendRef list)
    (onAdd: string -> unit)
    (onRemove: string -> unit)
    (onAddNew: string -> unit)
    (onClose: unit -> unit) =
    let searchText, setSearchText = React.useState("")
    let highlightedIndex, setHighlightedIndex = React.useState(0)
    let recommendedSlugs = recommendedBy |> List.map (fun f -> f.Slug) |> Set.ofList
    let available =
        allFriends
        |> List.filter (fun f ->
            not (Set.contains f.Slug recommendedSlugs) &&
            (searchText = "" || f.Name.ToLowerInvariant().Contains(searchText.ToLowerInvariant())))
    let availableArr = available |> List.toArray
    let trimmedSearch = searchText.Trim()
    let hasExactMatch = allFriends |> List.exists (fun f -> f.Name.ToLowerInvariant() = trimmedSearch.ToLowerInvariant())
    let showAddContact = trimmedSearch <> "" && not hasExactMatch
    let totalItems = availableArr.Length + (if showAddContact then 1 else 0)

    let headerExtra = [
        if not (List.isEmpty recommendedBy) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-4"
                prop.children [
                    for fr in recommendedBy do
                        Daisy.badge [
                            badge.lg
                            badge.primary
                            prop.className "gap-1"
                            prop.children [
                                Html.span [ prop.text fr.Name ]
                                Html.button [
                                    prop.className "btn btn-ghost btn-xs"
                                    prop.onClick (fun _ -> onRemove fr.Slug)
                                    prop.text "x"
                                ]
                            ]
                        ]
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

    ModalPanel.viewCustom "Recommended By" onClose headerExtra content []

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
                // Backdrop hero
                Html.div [
                    prop.className "relative h-64 lg:h-80 bg-base-300 overflow-hidden"
                    prop.children [
                        match movie.BackdropRef with
                        | Some ref ->
                            Html.img [
                                prop.src $"/images/{ref}"
                                prop.alt movie.Name
                                prop.className "w-full h-full object-cover opacity-50"
                            ]
                        | None -> ()
                        Html.div [
                            prop.className "absolute inset-0 bg-gradient-to-t from-base-300 to-transparent"
                        ]
                        // Back button
                        Html.div [
                            prop.className "absolute top-4 left-4"
                            prop.children [
                                Daisy.button.button [
                                    button.ghost
                                    button.sm
                                    prop.className "text-base-content"
                                    prop.onClick (fun _ -> Router.navigate "movies")
                                    prop.text "< Back"
                                ]
                            ]
                        ]
                    ]
                ]
                // Movie info card
                Html.div [
                    prop.className "px-4 lg:px-6 -mt-32 relative z-10"
                    prop.children [
                        Html.div [
                            prop.className "flex flex-col md:flex-row gap-6"
                            prop.children [
                                // Poster
                                Html.div [
                                    prop.className "w-40 flex-none"
                                    prop.children [
                                        Html.div [
                                            prop.className "aspect-[2/3] rounded-lg overflow-hidden shadow-xl bg-base-200"
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
                                                        prop.className "flex items-center justify-center w-full h-full text-base-content/30"
                                                        prop.children [ Icons.movie () ]
                                                    ]
                                            ]
                                        ]
                                    ]
                                ]
                                // Info
                                Html.div [
                                    prop.className "flex-1"
                                    prop.children [
                                        Html.h1 [
                                            prop.className "text-3xl font-bold font-display"
                                            prop.text movie.Name
                                        ]
                                        Html.div [
                                            prop.className "flex items-center gap-3 mt-2 text-base-content/60"
                                            prop.children [
                                                Html.span [ prop.text (string movie.Year) ]
                                                match movie.Runtime with
                                                | Some r ->
                                                    Html.span [ prop.text $"{r} min" ]
                                                | None -> ()
                                                match movie.TmdbRating with
                                                | Some r ->
                                                    Html.span [ prop.text $"%.1f{r} / 10" ]
                                                | None -> ()
                                            ]
                                        ]
                                        Html.div [
                                            prop.className "flex flex-wrap gap-2 mt-3"
                                            prop.children [
                                                for genre in movie.Genres do
                                                    Daisy.badge [
                                                        badge.outline
                                                        prop.text genre
                                                    ]
                                            ]
                                        ]
                                        Daisy.badge [
                                            badge.lg
                                            prop.className "gap-2 cursor-pointer select-none hover:badge-primary mt-2"
                                            prop.onClick (fun _ -> dispatch (Open_friend_picker Recommend_picker))
                                            prop.children [
                                                Icons.recommendedBy ()
                                                if not (List.isEmpty movie.RecommendedBy) then
                                                    Html.span [
                                                        prop.className "font-semibold"
                                                        prop.text (movie.RecommendedBy |> List.map (fun fr -> fr.Name) |> String.concat ", ")
                                                    ]
                                            ]
                                        ]
                                        Html.p [
                                            prop.className "mt-4 text-base-content/80 leading-relaxed"
                                            prop.text movie.Overview
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                // Cast horizontal scroll
                if not (List.isEmpty movie.Cast) then
                    Html.div [
                        prop.className "px-4 lg:px-6 mt-8"
                        prop.children [
                            Html.h2 [
                                prop.className "text-lg font-bold font-display mb-3"
                                prop.text "Cast"
                            ]
                            Html.div [
                                prop.className "flex gap-3 overflow-x-auto pb-2"
                                prop.children [
                                    for c in movie.Cast do
                                        castCard c
                                ]
                            ]
                        ]
                    ]
                // Watch History section
                Html.div [
                    prop.className "px-4 lg:px-6 mt-8"
                    prop.children [
                        Html.div [
                            prop.className "flex items-center justify-between mb-3"
                            prop.children [
                                Html.h2 [
                                    prop.className "text-lg font-bold font-display"
                                    prop.text "Watch History"
                                ]
                                Daisy.button.button [
                                    button.sm
                                    button.ghost
                                    prop.onClick (fun _ -> dispatch Open_record_session)
                                    prop.text "+ Record Session"
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "flex flex-wrap gap-2"
                            prop.children [
                                for session in movie.WatchSessions do
                                    Daisy.card [
                                        prop.className "bg-base-200 shadow-sm w-auto"
                                        prop.children [
                                            Daisy.cardBody [
                                                prop.className "p-3 flex-row items-center gap-2"
                                                prop.children [
                                                    Html.span [
                                                        prop.className "font-semibold text-sm whitespace-nowrap"
                                                        prop.text session.Date
                                                    ]
                                                    if not (List.isEmpty session.Friends) then
                                                        Html.div [
                                                            prop.className "flex flex-wrap gap-1"
                                                            prop.children [
                                                                for friend in session.Friends do
                                                                    Daisy.badge [
                                                                        badge.sm
                                                                        prop.text friend.Name
                                                                    ]
                                                            ]
                                                        ]
                                                ]
                                            ]
                                        ]
                                    ]
                                if not (List.isEmpty movie.WantToWatchWith) then
                                    Daisy.card [
                                        prop.className "bg-base-200 shadow-sm w-auto border border-dashed border-base-content/20"
                                        prop.children [
                                            Daisy.cardBody [
                                                prop.className "p-3 flex-row items-center gap-2"
                                                prop.children [
                                                    Html.span [
                                                        prop.className "text-sm text-base-content/60 whitespace-nowrap"
                                                        prop.text "Watch with"
                                                    ]
                                                    Html.div [
                                                        prop.className "flex flex-wrap gap-1"
                                                        prop.children [
                                                            for fr in movie.WantToWatchWith do
                                                                Daisy.badge [
                                                                    badge.sm
                                                                    prop.className "gap-1"
                                                                    prop.children [
                                                                        Html.span [ prop.text fr.Name ]
                                                                        Html.button [
                                                                            prop.className "text-xs opacity-60 hover:opacity-100"
                                                                            prop.onClick (fun _ -> dispatch (Remove_want_to_watch_with fr.Slug))
                                                                            prop.text "x"
                                                                        ]
                                                                    ]
                                                                ]
                                                        ]
                                                    ]
                                                    Daisy.badge [
                                                        badge.sm
                                                        prop.className "cursor-pointer select-none hover:badge-primary"
                                                        prop.onClick (fun _ -> dispatch (Open_friend_picker Watch_with_picker))
                                                        prop.text "+"
                                                    ]
                                                ]
                                            ]
                                        ]
                                    ]
                                else
                                    Daisy.badge [
                                        badge.lg
                                        prop.className "cursor-pointer select-none hover:badge-primary"
                                        prop.onClick (fun _ -> dispatch (Open_friend_picker Watch_with_picker))
                                        prop.text "Want to watch with"
                                    ]
                            ]
                        ]
                    ]
                ]
                // Notes section
                Html.div [
                    prop.className "px-4 lg:px-6 mt-8"
                    prop.children [
                        Html.h2 [
                            prop.className "text-lg font-bold font-display mb-3"
                            prop.text "Notes"
                        ]
                        ContentBlockEditor.view
                            movie.ContentBlocks
                            (fun req -> dispatch (Add_content_block req))
                            (fun bid req -> dispatch (Update_content_block (bid, req)))
                            (fun bid -> dispatch (Remove_content_block bid))
                    ]
                ]
                // Error display
                match model.Error with
                | Some err ->
                    Html.div [
                        prop.className "px-4 lg:px-6 mt-4"
                        prop.children [
                            Daisy.alert [
                                alert.error
                                prop.text err
                            ]
                        ]
                    ]
                | None -> ()
                // Actions
                Html.div [
                    prop.className "px-4 lg:px-6 mt-8 mb-8"
                    prop.children [
                        Daisy.button.button [
                            button.error
                            button.outline
                            prop.onClick (fun _ -> dispatch Remove_movie)
                            prop.text "Remove Movie"
                        ]
                    ]
                ]
                // Friend picker modal
                match model.ShowFriendPicker with
                | Some Recommend_picker ->
                    RecommendationManager
                        model.AllFriends
                        movie.RecommendedBy
                        (fun slug -> dispatch (Recommend_friend slug))
                        (fun slug -> dispatch (Remove_recommendation slug))
                        (fun name -> dispatch (Add_friend_and_recommend name))
                        (fun () -> dispatch Close_friend_picker)
                | Some Watch_with_picker ->
                    let excludeSlugs = movie.WantToWatchWith |> List.map (fun f -> f.Slug)
                    friendPicker model.AllFriends excludeSlugs
                        (fun slug -> dispatch (Want_to_watch_with slug))
                        (fun () -> dispatch Close_friend_picker)
                | None -> ()
                // Record session modal
                if model.ShowRecordSession then
                    ModalPanel.viewWithFooter
                        "Record Watch Session"
                        (fun () -> dispatch Close_record_session)
                        [
                            Html.div [
                                prop.className "space-y-4"
                                prop.children [
                                    // Date input
                                    Html.div [
                                        prop.children [
                                            Html.label [
                                                prop.className "label"
                                                prop.children [
                                                    Html.span [ prop.className "label-text"; prop.text "Date" ]
                                                ]
                                            ]
                                            Daisy.input [
                                                prop.className "w-full"
                                                prop.type' "date"
                                                prop.value model.SessionForm.Date
                                                prop.onChange (fun (v: string) -> dispatch (Session_date_changed v))
                                            ]
                                        ]
                                    ]
                                    // Friend multi-select
                                    if not (List.isEmpty model.AllFriends) then
                                        Html.div [
                                            prop.children [
                                                Html.label [
                                                    prop.className "label"
                                                    prop.children [
                                                        Html.span [ prop.className "label-text"; prop.text "Watched with" ]
                                                    ]
                                                ]
                                                Html.div [
                                                    prop.className "flex flex-wrap gap-2"
                                                    prop.children [
                                                        for friend in model.AllFriends do
                                                            let isSelected = model.SessionForm.SelectedFriends.Contains friend.Slug
                                                            Daisy.badge [
                                                                if isSelected then badge.primary
                                                                badge.lg
                                                                prop.className "cursor-pointer select-none"
                                                                prop.onClick (fun _ -> dispatch (Toggle_session_friend friend.Slug))
                                                                prop.text friend.Name
                                                            ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                        [
                            Daisy.button.button [
                                prop.onClick (fun _ -> dispatch Close_record_session)
                                prop.text "Cancel"
                            ]
                            Daisy.button.button [
                                button.primary
                                prop.onClick (fun _ -> dispatch Submit_record_session)
                                prop.text "Record Session"
                            ]
                        ]
            ]
        ]
