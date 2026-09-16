module Mediatheca.Client.Pages.BookDetail.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.BookDetail.Types
open Mediatheca.Client.Pages.BookDetail.Format
open Mediatheca.Client
open Mediatheca.Client.Components

let private formatDateOnly (date: string) =
    match date.IndexOf('T') with
    | -1 -> date
    | i -> date.[..i-1]

let private panelCard (children: ReactElement list) =
    Html.div [
        prop.className (DesignSystem.velvetCard + " p-6")
        prop.children children
    ]

// ── Personal rating (mirrors GameDetail/MovieDetail's card-based rating) ──

type private RatingOption = {
    Value: int
    Name: string
    Description: string
    Icon: unit -> ReactElement
    ColorClass: string
}

let private ratingOptions: RatingOption list = [
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
            panelCard [
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

// ── Status control (GameDetail's HeroStatus shape, BookStatus's 4 cases) ──

/// Books' own status badge — reuses the `status-badge`/`status-badge-*` CSS
/// vocabulary GameDetail's `LifecycleStatus` pattern established (same
/// colors: Finished borrows the "done" hue `Retired` already owns), but
/// keeps Books' own label text. `DesignSystem.statusBadge` can't be reused
/// directly here: its label is hardcoded per `LifecycleStatus` case
/// ("Retired"), and the task's acceptance criteria require the segmented
/// control to literally read "Finished" — a book is finished, not retired.
let private bookStatusBadgeClass (status: BookStatus) =
    match status with
    | BookStatus.Backlog -> "status-badge status-badge-backlog"
    | BookStatus.InFocus -> "status-badge status-badge-in-focus " + DesignSystem.goldLeafSweep
    | BookStatus.Finished -> "status-badge status-badge-retired"
    | BookStatus.Abandoned -> "status-badge status-badge-abandoned"

let private bookStatusLabel (status: BookStatus) =
    match status with
    | BookStatus.Backlog -> "Backlog"
    | BookStatus.InFocus -> "In focus"
    | BookStatus.Finished -> "Finished"
    | BookStatus.Abandoned -> "Abandoned"

let private bookStatusBadge (status: BookStatus) : ReactElement =
    Html.span [
        prop.className (bookStatusBadgeClass status)
        prop.text (bookStatusLabel status)
    ]

[<ReactComponent>]
let private HeroStatus (currentStatus: BookStatus, isOpen: bool, dispatch: Msg -> unit) =
    let triggerRef = React.useElementRef()
    let pos, setPos = React.useState {| top = 0.0; left = 0.0 |}
    let allStatuses = [ BookStatus.Backlog; BookStatus.InFocus; BookStatus.Finished; BookStatus.Abandoned ]

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
                            bookStatusBadge currentStatus
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
                                prop.onClick (fun _ -> dispatch (Set_book_status status))
                                prop.children [
                                    bookStatusBadge status
                                ]
                            ]
                    ]
                ]
        ]
    ]

// ── Progress card ──

let private sourceLabel (source: ProgressSource) =
    match source with
    | ProgressSource.Audible -> "Audible"
    | ProgressSource.Goodreads -> "Goodreads"
    | ProgressSource.Manual -> "Manual"

let private positionLabel (position: ReadingPosition option) =
    match position with
    | Some (Page (page, Some total)) -> $"page {page} of {total}"
    | Some (Page (page, None)) -> $"page {page}"
    | Some (Minutes (minutes, Some total)) -> $"{minutes} of {total} min"
    | Some (Minutes (minutes, None)) -> $"{minutes} min"
    | None -> ""

let private progressHistoryRow (model: Model) (dispatch: Msg -> unit) (row: ReadingProgressDto) =
    let key = row.ObservedOn, row.Source
    Html.div [
        prop.key ($"{row.ObservedOn}-{sourceLabel row.Source}")
        prop.className "flex items-center justify-between py-2 border-b border-base-content/5 last:border-0 group/row"
        prop.children [
            Html.div [
                prop.className "flex items-center gap-3"
                prop.children [
                    Html.span [ prop.className "text-xs font-mono text-base-content/50"; prop.text (formatDateOnly row.ObservedOn) ]
                    Html.span [ prop.className "text-sm font-mono font-semibold"; prop.text (string row.Percent + "%") ]
                    Html.span [ prop.className "text-xs uppercase tracking-wide text-base-content/40"; prop.text (sourceLabel row.Source) ]
                    if positionLabel row.Position <> "" then
                        Html.span [ prop.className "text-xs text-base-content/40 font-mono"; prop.text (positionLabel row.Position) ]
                ]
            ]
            if model.ConfirmingRemoveObservation = Some key then
                Html.div [
                    prop.className "flex items-center gap-2"
                    prop.children [
                        Html.button [
                            prop.className "text-xs text-error font-semibold cursor-pointer"
                            prop.onClick (fun _ -> dispatch (Remove_observation (row.ObservedOn, row.Source)))
                            prop.text "Remove"
                        ]
                        Html.button [
                            prop.className "text-xs text-base-content/50 cursor-pointer"
                            prop.onClick (fun _ -> dispatch Cancel_remove_observation)
                            prop.text "Cancel"
                        ]
                    ]
                ]
            else
                Html.button [
                    prop.className "opacity-0 group-hover/row:opacity-100 transition-opacity text-base-content/30 hover:text-error text-xs"
                    prop.title "Remove observation"
                    prop.onClick (fun _ -> dispatch (Confirm_remove_observation (row.ObservedOn, row.Source)))
                    prop.children [ Icons.trash () ]
                ]
        ]
    ]

let private progressCard (book: BookDetail) (model: Model) (dispatch: Msg -> unit) =
    let fraction = float book.ProgressPercent / 100.0
    let history = book.ProgressHistory |> List.sortByDescending (fun r -> r.ObservedOn)
    panelCard [
        Html.div [
            prop.className "flex items-center justify-between mb-4"
            prop.children [
                Html.h3 [ prop.className "text-lg font-bold"; prop.text "Reading Progress" ]
                Daisy.button.button [
                    button.ghost
                    button.sm
                    prop.onClick (fun _ -> dispatch Open_progress_popover)
                    prop.text "Update progress"
                ]
            ]
        ]
        DesignSystem.progressContinuous fraction
        Html.div [
            prop.className "flex items-center gap-3 mt-3 mb-2"
            prop.children [
                Html.span [ prop.className "font-mono text-2xl font-bold"; prop.text (string book.ProgressPercent + "%") ]
                match book.ProgressSource with
                | Some source ->
                    Html.span [ prop.className "text-xs uppercase tracking-wide text-base-content/40 font-sans"; prop.text (sourceLabel source) ]
                | None -> ()
                match book.ProgressObservedOn with
                | Some observedOn ->
                    Html.span [ prop.className "text-xs text-base-content/40 font-mono"; prop.text $"observed {formatDateOnly observedOn}" ]
                | None -> ()
            ]
        ]
        if not (List.isEmpty history) then
            Html.div [
                prop.className "mt-4"
                prop.children [
                    Html.p [
                        prop.className "text-xs font-bold text-base-content/40 uppercase tracking-wider mb-2"
                        prop.text "History"
                    ]
                    Html.div [
                        prop.children [ for row in history -> progressHistoryRow model dispatch row ]
                    ]
                ]
            ]
    ]

let private progressPopover (book: BookDetail) (draft: ProgressDraft) (dispatch: Msg -> unit) =
    let tabButton (label: string) (tab: ProgressTab) =
        Daisy.button.button [
            (if draft.Tab = tab then button.primary else button.ghost)
            button.sm
            prop.onClick (fun _ -> dispatch (Set_progress_tab tab))
            prop.text label
        ]
    ModalPanel.view "Update progress" (fun () -> dispatch Close_progress_popover) [
        Html.div [
            prop.className "flex gap-2 mb-4"
            prop.children [ tabButton "Percent" ByPercentTab; tabButton "Page" ByPageTab ]
        ]
        match draft.Tab with
        | ByPercentTab ->
            Html.div [
                prop.className "form-control mb-3"
                prop.children [
                    Daisy.label [ prop.className "label"; prop.children [ Html.span [ prop.className "label-text"; prop.text "Percent complete" ] ] ]
                    Daisy.input [
                        prop.type' "number"
                        prop.className "font-mono"
                        prop.value draft.PercentText
                        prop.onChange (fun (v: string) -> dispatch (Set_progress_percent_text v))
                    ]
                ]
            ]
        | ByPageTab ->
            Html.div [
                prop.className "grid grid-cols-2 gap-3 mb-3"
                prop.children [
                    Html.div [
                        prop.className "form-control"
                        prop.children [
                            Daisy.label [ prop.className "label"; prop.children [ Html.span [ prop.className "label-text"; prop.text "Page" ] ] ]
                            Daisy.input [
                                prop.type' "number"
                                prop.className "font-mono"
                                prop.value draft.PageText
                                prop.onChange (fun (v: string) -> dispatch (Set_progress_page_text v))
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className "form-control"
                        prop.children [
                            Daisy.label [ prop.className "label"; prop.children [ Html.span [ prop.className "label-text"; prop.text "Of total pages" ] ] ]
                            Daisy.input [
                                prop.type' "number"
                                prop.className "font-mono"
                                prop.value draft.TotalText
                                prop.onChange (fun (v: string) -> dispatch (Set_progress_total_text v))
                            ]
                        ]
                    ]
                ]
            ]
        Html.div [
            prop.className "form-control mt-3"
            prop.children [
                Daisy.label [ prop.className "label"; prop.children [ Html.span [ prop.className "label-text"; prop.text "Observed on" ] ] ]
                Daisy.input [
                    prop.type' "date"
                    prop.className "font-mono"
                    prop.value draft.DateText
                    prop.onChange (fun (v: string) -> dispatch (Set_progress_date_text v))
                ]
            ]
        ]
        match draft.Error with
        | Some err ->
            Daisy.alert [ alert.error; prop.className "mt-3"; prop.text err ]
        | None -> ()
        Html.div [
            prop.className "flex justify-end mt-4"
            prop.children [
                Daisy.button.button [
                    button.primary
                    prop.onClick (fun _ -> dispatch Submit_progress)
                    prop.text "Save"
                ]
            ]
        ]
    ]

// ── Details / Links cards ──

let private detailsCard (book: BookDetail) =
    let hasMeta =
        [ book.Publisher; book.PublishedDate; book.Language ] |> List.exists Option.isSome
    panelCard [
        Html.h3 [ prop.className "text-lg font-bold mb-4"; prop.text "Details" ]
        match book.Description with
        | Some d when not (System.String.IsNullOrWhiteSpace d) ->
            Html.p [ prop.className "text-base-content/70 leading-relaxed mb-4"; prop.text d ]
        | _ -> ()
        if hasMeta then
            Html.p [
                prop.className "text-sm text-base-content/60 mb-3 font-mono"
                prop.text (
                    [ book.Publisher; book.PublishedDate; book.Language ]
                    |> List.choose id
                    |> String.concat " · "
                )
            ]
        if not (List.isEmpty book.Subjects) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-3"
                prop.children [
                    for subject in book.Subjects do
                        Html.span [
                            prop.className "bg-base-content/10 text-base-content/70 px-3 py-1 rounded-full text-xs font-semibold"
                            prop.text subject
                        ]
                ]
            ]
        match book.AverageRating with
        | Some rating ->
            Html.p [
                prop.className "text-sm text-base-content/60 font-mono"
                prop.text $"Average rating %.1f{rating}"
            ]
        | None -> ()
    ]

let private linksCard (book: BookDetail) =
    let audibleLink = book.AudibleAsin |> Option.map (fun asin -> "Audible", $"https://www.audible.de/pd/{asin}")
    let goodreadsLink = book.GoodreadsBookId |> Option.map (fun gid -> "Goodreads", $"https://www.goodreads.com/book/show/{gid}")
    let openLibraryLink = book.OpenLibraryWorkKey |> Option.map (fun workKey -> "Open Library", $"https://openlibrary.org{workKey}")
    let links = [ audibleLink; goodreadsLink; openLibraryLink ] |> List.choose id
    if List.isEmpty links then
        Html.none
    else
        panelCard [
            Html.h3 [ prop.className "text-lg font-bold mb-4"; prop.text "Links" ]
            Html.div [
                prop.className "flex flex-col gap-2"
                prop.children [
                    for (label, href) in links do
                        Html.a [
                            prop.key label
                            prop.href href
                            prop.target "_blank"
                            prop.rel "noopener noreferrer"
                            prop.className "inline-flex items-center gap-2 text-base-content/70 hover:text-primary transition-colors text-sm font-semibold"
                            prop.children [
                                Html.span [ prop.className "w-4 h-4"; prop.children [ Icons.externalLink () ] ]
                                Html.span [ prop.text label ]
                            ]
                        ]
                ]
            ]
        ]

// ── Friends (recommended-by only, per the task's What section) ──

[<ReactComponent>]
let private FriendManager
    (allFriends: FriendListItem list)
    (recommendedBy: FriendRef list)
    (onAdd: string -> unit)
    (onRemove: string -> unit)
    (onAddNew: string -> unit)
    (onClose: unit -> unit) =
    let searchText, setSearchText = React.useState("")
    let selectedSlugs = recommendedBy |> List.map (fun f -> f.Slug) |> Set.ofList
    let available =
        allFriends
        |> List.filter (fun f ->
            not (Set.contains f.Slug selectedSlugs) &&
            (searchText = "" || f.Name.ToLowerInvariant().Contains(searchText.ToLowerInvariant())))
    let trimmedSearch = searchText.Trim()
    let hasExactMatch = allFriends |> List.exists (fun f -> f.Name.ToLowerInvariant() = trimmedSearch.ToLowerInvariant())
    let showAddContact = trimmedSearch <> "" && not hasExactMatch

    let headerExtra = [
        if not (List.isEmpty recommendedBy) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-4"
                prop.children [ for fr in recommendedBy -> FriendPill.viewWithRemove fr onRemove ]
            ]
        Daisy.input [
            prop.className "w-full mb-4"
            prop.type' "text"
            prop.placeholder "Search friends..."
            prop.autoFocus true
            prop.value searchText
            prop.onChange (fun (v: string) -> setSearchText v)
            prop.onKeyDown (fun e ->
                match e.key with
                | "Enter" ->
                    e.preventDefault()
                    match available with
                    | first :: _ ->
                        onAdd first.Slug
                        setSearchText ""
                    | [] ->
                        if showAddContact then
                            onAddNew trimmedSearch
                            setSearchText ""
                | "Escape" -> onClose ()
                | _ -> ())
        ]
    ]

    let content = [
        if List.isEmpty available && not showAddContact then
            Html.p [
                prop.className "text-base-content/60 py-2 text-sm"
                prop.text (
                    if List.isEmpty allFriends && trimmedSearch = "" then "No friends available. Add friends first."
                    elif trimmedSearch = "" then "All friends already recommending this book."
                    else "No matches found."
                )
            ]
        else
            Html.div [
                prop.className "space-y-1"
                prop.children [
                    for friend in available do
                        Html.div [
                            prop.key friend.Slug
                            prop.className "flex items-center gap-3 p-2 rounded-lg cursor-pointer hover:bg-base-200"
                            prop.onClick (fun _ -> onAdd friend.Slug)
                            prop.children [
                                Html.span [ prop.className "font-semibold"; prop.text friend.Name ]
                            ]
                        ]
                    if showAddContact then
                        Html.div [
                            prop.className "flex items-center gap-3 p-2 rounded-lg cursor-pointer hover:bg-base-200"
                            prop.onClick (fun _ ->
                                onAddNew trimmedSearch
                                setSearchText "")
                            prop.children [
                                Html.div [
                                    prop.className "w-10 h-10 rounded-full bg-base-300 flex items-center justify-center text-base-content/40 text-lg"
                                    prop.text "+"
                                ]
                                Html.span [ prop.className "font-semibold"; prop.text $"Add contact \"{trimmedSearch}\"" ]
                            ]
                        ]
                ]
            ]
    ]

    ModalPanel.viewCustom "Recommended By" onClose headerExtra content []

let private friendsCard (book: BookDetail) (dispatch: Msg -> unit) =
    panelCard [
        Html.div [
            prop.className "flex items-center justify-between mb-4"
            prop.children [
                Html.h3 [ prop.className "text-lg font-bold"; prop.text "Recommended By" ]
                Html.button [
                    prop.className "w-8 h-8 rounded-full bg-primary flex items-center justify-center text-primary-content hover:scale-110 transition-transform text-sm font-bold cursor-pointer"
                    prop.onClick (fun _ -> dispatch Open_friend_picker)
                    prop.text "+"
                ]
            ]
        ]
        if List.isEmpty book.RecommendedBy then
            Html.p [ prop.className "text-base-content/30 text-sm italic"; prop.text "No recommendations yet" ]
        else
            Html.div [
                prop.className "flex flex-wrap gap-2"
                prop.children [ for fr in book.RecommendedBy -> FriendPill.view fr ]
            ]
    ]

// ── Format picker ──

let private formatLabel (format: BookFormat) =
    match format with
    | BookFormat.Audiobook -> "Audiobook"
    | BookFormat.Print -> "Print"
    | BookFormat.Ebook -> "Ebook"
    | BookFormat.Unknown -> "Unknown"

let private formatPicker (current: BookFormat) (dispatch: Msg -> unit) =
    ModalPanel.view "Change format" (fun () -> dispatch Close_format_picker) [
        Html.div [
            prop.className "space-y-1"
            prop.children [
                for format in [ BookFormat.Audiobook; BookFormat.Print; BookFormat.Ebook; BookFormat.Unknown ] do
                    let isActive = format = current
                    Html.button [
                        prop.key (formatLabel format)
                        prop.className (
                            "w-full flex items-center gap-3 p-2 rounded-lg cursor-pointer text-left "
                            + (if isActive then "bg-primary/20" else "hover:bg-base-200"))
                        prop.onClick (fun _ -> dispatch (Set_format format))
                        prop.text (formatLabel format)
                    ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) (onBack: unit -> unit) =
    match model.IsLoading, model.Book with
    | true, _ ->
        Html.div [
            prop.className "flex justify-center py-12"
            prop.children [ Daisy.loading [ loading.spinner; loading.lg ] ]
        ]
    | false, None ->
        PageContainer.view "Book Not Found" [
            Html.p [ prop.className "text-base-content/70"; prop.text "The book you're looking for doesn't exist." ]
            Html.a [
                prop.className "link link-primary mt-4 inline-block"
                prop.href (Router.format "")
                prop.onClick (fun e ->
                    e.preventDefault()
                    Router.navigate "")
                prop.text "Back to Dashboard"
            ]
        ]
    | false, Some book ->
        let canRefresh = book.OpenLibraryWorkKey.IsSome || book.Isbn13.IsSome
        Html.div [
            prop.children [
                // ── Hero ──
                Html.div [
                    prop.className "relative min-h-72 lg:h-[420px] w-full overflow-hidden"
                    prop.children [
                        Html.div [
                            prop.className "absolute inset-0 bg-gradient-to-t from-base-300 via-base-300/40 to-base-300/80"
                        ]
                        Html.div [
                            prop.className "absolute top-4 left-4 z-10"
                            prop.children [
                                Daisy.button.button [
                                    button.ghost
                                    button.sm
                                    prop.className "text-base-content bg-base-300/30"
                                    prop.onClick (fun _ -> onBack ())
                                    prop.text "← Back"
                                ]
                            ]
                        ]
                        Html.div [
                            prop.className "absolute top-4 right-4 z-10 opacity-0 hover:opacity-100 transition-opacity"
                            prop.children [
                                let refreshItem: ActionMenu.ActionMenuItem list =
                                    if canRefresh then
                                        [ { Label = "Refresh metadata"
                                            Icon = Some Icons.arrowPath
                                            OnClick = fun () -> dispatch Refresh_metadata
                                            IsDestructive = false } ]
                                    else []
                                let restItems: ActionMenu.ActionMenuItem list = [
                                    { Label = "Change format"
                                      Icon = Some Icons.book
                                      OnClick = fun () -> dispatch Open_format_picker
                                      IsDestructive = false }
                                    { Label = "Event history"
                                      Icon = Some Icons.events
                                      OnClick = fun () -> dispatch Open_event_history
                                      IsDestructive = false }
                                    { Label = "Remove book"
                                      Icon = Some Icons.trash
                                      OnClick = fun () -> dispatch Confirm_remove_book
                                      IsDestructive = true }
                                ]
                                ActionMenu.heroView (refreshItem @ restItems)
                            ]
                        ]
                        Html.div [
                            prop.className "relative min-h-72 lg:h-full flex items-end pb-6 lg:pb-8 px-4 lg:px-8"
                            prop.children [
                                Html.div [
                                    prop.className "flex gap-6 lg:gap-10 items-end w-full max-w-6xl mx-auto"
                                    prop.children [
                                        Html.div [
                                            prop.className "hidden lg:block w-52 h-80 flex-shrink-0 rounded-xl overflow-hidden shadow-2xl border border-base-content/10"
                                            prop.children [
                                                match book.CoverRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}"
                                                        prop.alt book.Title
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.book () ]
                                                    ]
                                            ]
                                        ]
                                        Html.div [
                                            prop.className "lg:hidden w-28 h-44 flex-shrink-0 rounded-lg overflow-hidden shadow-xl border border-base-content/10"
                                            prop.children [
                                                match book.CoverRef with
                                                | Some ref ->
                                                    Html.img [
                                                        prop.src $"/images/{ref}"
                                                        prop.alt book.Title
                                                        prop.className "w-full h-full object-cover"
                                                    ]
                                                | None ->
                                                    Html.div [
                                                        prop.className "flex items-center justify-center w-full h-full bg-base-200 text-base-content/30"
                                                        prop.children [ Icons.book () ]
                                                    ]
                                            ]
                                        ]
                                        Html.div [
                                            prop.className "flex-grow pb-2"
                                            prop.children [
                                                Html.div [
                                                    prop.className "flex flex-wrap items-center gap-3 mb-3"
                                                    prop.children [
                                                        Html.span [
                                                            prop.className "bg-primary/80 px-3 py-1 rounded text-xs font-bold tracking-wider uppercase text-primary-content"
                                                            prop.text (formatLabel book.Format)
                                                        ]
                                                        HeroStatus (book.Status, model.IsStatusOpen, dispatch)
                                                    ]
                                                ]
                                                Html.h1 [
                                                    prop.className "text-3xl lg:text-5xl font-bold font-display tracking-tight mb-2"
                                                    prop.text book.Title
                                                ]
                                                Html.div [
                                                    prop.className "flex items-center gap-3 text-base-content/60 mb-2"
                                                    prop.children [
                                                        if not (List.isEmpty book.Authors) then
                                                            Html.span [ prop.text (String.concat ", " book.Authors) ]
                                                        match book.Year with
                                                        | Some y ->
                                                            Html.span [ prop.className "text-base-content/30"; prop.text "·" ]
                                                            Html.span [ prop.text (string y) ]
                                                        | None -> ()
                                                        match lengthLine book.Format book.RuntimeMinutes book.PageCount book.Narrators with
                                                        | Some line ->
                                                            Html.span [ prop.className "text-base-content/30"; prop.text "·" ]
                                                            Html.span [ prop.className "font-mono text-sm"; prop.text line ]
                                                        | None -> ()
                                                    ]
                                                ]
                                                match seriesLine book.SeriesName book.SeriesPosition with
                                                | Some line ->
                                                    Html.p [ prop.className "text-base-content/50 italic mb-2"; prop.text line ]
                                                | None -> ()
                                                match book.Status, book.FinishedAt with
                                                | BookStatus.Finished, Some finishedAt ->
                                                    Html.p [
                                                        prop.className "text-sm text-base-content/50 font-mono"
                                                        prop.text $"finished {finishedAt}"
                                                    ]
                                                | _ -> ()
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                // ── Content grid ──
                Html.div [
                    prop.className "max-w-6xl mx-auto px-4 lg:px-8 pt-4 lg:pt-6 pb-8 lg:pb-12"
                    prop.children [
                        Html.div [
                            prop.className "grid grid-cols-1 lg:grid-cols-12 gap-8 lg:gap-10"
                            prop.children [
                                // ── Left column ──
                                Html.div [
                                    prop.className "lg:col-span-8 space-y-6"
                                    prop.children [
                                        // Catalogs
                                        Html.div [
                                            prop.className "flex flex-wrap items-center gap-2"
                                            prop.children [
                                                // Add to catalog button
                                                Html.button [
                                                    prop.className "w-9 h-9 rounded-full bg-base-100/50 border border-base-content/15 hover:bg-base-100/70 text-base-content/50 hover:text-base-content flex items-center justify-center transition-colors cursor-pointer"
                                                    prop.onClick (fun _ -> dispatch Open_catalog_picker)
                                                    prop.children [
                                                        Html.span [ prop.className "[&>svg]:w-5 [&>svg]:h-5"; prop.children [ Icons.catalog () ] ]
                                                    ]
                                                ]
                                                // Selected catalog pills
                                                for cat in model.BookCatalogs do
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
                                                                prop.text "×"
                                                            ]
                                                        ]
                                                    ]
                                            ]
                                        ]
                                        progressCard book model dispatch
                                        personalRatingCard book.PersonalRating model.IsRatingOpen dispatch
                                        detailsCard book
                                        linksCard book
                                        match model.Error with
                                        | Some err -> Daisy.alert [ alert.error; prop.text err ]
                                        | None -> ()
                                        if model.ConfirmingRemove then
                                            Html.div [
                                                prop.className "bg-error/10 border border-error/30 rounded-xl p-4 space-y-3"
                                                prop.children [
                                                    Html.p [
                                                        prop.className "text-sm font-semibold text-error"
                                                        prop.text "Are you sure you want to remove this book?"
                                                    ]
                                                    Html.div [
                                                        prop.className "flex gap-2"
                                                        prop.children [
                                                            Daisy.button.button [
                                                                button.error
                                                                button.sm
                                                                prop.className "flex-1"
                                                                prop.onClick (fun _ -> dispatch Remove_book)
                                                                prop.text "Yes, remove"
                                                            ]
                                                            Daisy.button.button [
                                                                button.ghost
                                                                button.sm
                                                                prop.className "flex-1"
                                                                prop.onClick (fun _ -> dispatch Cancel_remove_book)
                                                                prop.text "Cancel"
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                    ]
                                ]
                                // ── Right column: friends + notes ──
                                Html.div [
                                    prop.className "lg:col-span-4 space-y-6"
                                    prop.children [
                                        friendsCard book dispatch
                                        Html.div [
                                            prop.children [
                                                NotesEditor.view Book model.Slug
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    ]
                ]
                if model.ShowProgressPopover then
                    progressPopover book model.ProgressDraft dispatch
                if model.ShowFriendPicker then
                    FriendManager
                        model.AllFriends
                        book.RecommendedBy
                        (fun slug -> dispatch (Recommend_friend slug))
                        (fun slug -> dispatch (Remove_recommendation slug))
                        (fun name -> dispatch (Add_friend_and_recommend name))
                        (fun () -> dispatch Close_friend_picker)
                if model.ShowFormatPicker then
                    formatPicker book.Format dispatch
                if model.ShowEventHistory then
                    EventHistoryModal.view $"Book-{model.Slug}" (fun () -> dispatch Close_event_history)
                // Catalog picker modal
                if model.ShowCatalogPicker then
                    CatalogManager.CatalogManager
                        model.AllCatalogs
                        model.BookCatalogs
                        "Book"
                        (fun slug -> dispatch (Add_to_catalog slug))
                        (fun slug entryId -> dispatch (Remove_from_catalog (slug, entryId)))
                        (fun name -> dispatch (Create_catalog_and_add name))
                        (fun () -> dispatch Close_catalog_picker)
            ]
        ]
