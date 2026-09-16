module Mediatheca.Client.Components.CatalogManager

open Feliz
open Feliz.DaisyUI
open Mediatheca.Shared

/// The "Add to Catalog" picker modal, shared by every media detail page
/// (Movie, Series, Game, Book — books-f3sb2). Lifted out of three
/// near-identical private copies (`Pages/MovieDetail/Views.fs`,
/// `Pages/SeriesDetail/Views.fs`, `Pages/GameDetail/Views.fs`) that differed
/// only in the "already in all catalogs" noun, which is now `mediaNoun`.
/// Paper overlay (ADR-0016) comes from `ModalPanel.viewCustom`, unchanged.
[<ReactComponent>]
let CatalogManager
    (allCatalogs: CatalogListItem list)
    (currentCatalogs: CatalogRef list)
    (mediaNoun: string)
    (onAdd: string -> unit)
    (onRemove: string -> string -> unit)
    (onCreateNew: string -> unit)
    (onClose: unit -> unit) =
    let searchText, setSearchText = React.useState("")
    let highlightedIndex, setHighlightedIndex = React.useState(0)
    let selectedSlugs = currentCatalogs |> List.map (fun c -> c.Slug) |> Set.ofList
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
        if not (List.isEmpty currentCatalogs) then
            Html.div [
                prop.className "flex flex-wrap gap-2 mb-4"
                prop.children [
                    for cat in currentCatalogs do
                        Html.span [
                            prop.className "inline-flex items-center gap-1.5 bg-transparent border border-base-content/20 text-base-content/70 px-3 py-1 rounded-full text-sm font-semibold transition-colors hover:border-base-content/40"
                            prop.children [
                                Html.span [ prop.text cat.Name ]
                                Html.button [
                                    prop.className "text-base-content/40 hover:text-error transition-colors cursor-pointer ml-0.5"
                                    prop.onClick (fun e ->
                                        e.stopPropagation()
                                        onRemove cat.Slug cat.EntryId)
                                    prop.text "×"
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
                    elif trimmedSearch = "" then $"{mediaNoun} already in all catalogs."
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
