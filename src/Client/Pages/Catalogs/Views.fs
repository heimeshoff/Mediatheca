module Mediatheca.Client.Pages.Catalogs.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Catalogs.Types
open Mediatheca.Client
open Mediatheca.Client.Components

let private catalogCard (catalog: Mediatheca.Shared.CatalogListItem) =
    Html.a [
        prop.href (Router.format ("catalogs", catalog.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("catalogs", catalog.Slug)
        )
        prop.children [
            Daisy.card [
                prop.className "card-hover bg-base-100 shadow-md cursor-pointer h-full"
                prop.children [
                    Daisy.cardBody [
                        prop.className "p-5"
                        prop.children [
                            Html.div [
                                prop.className "flex items-start gap-3"
                                prop.children [
                                    Html.div [
                                        prop.className "p-2 rounded-lg bg-primary/10 text-primary flex-none"
                                        prop.children [ Icons.catalog () ]
                                    ]
                                    Html.div [
                                        prop.className "flex-1 min-w-0"
                                        prop.children [
                                            Html.h3 [
                                                prop.className "font-bold text-base truncate"
                                                prop.text catalog.Name
                                            ]
                                            if catalog.Description <> "" then
                                                Html.p [
                                                    prop.className "text-sm text-base-content/50 mt-1 line-clamp-2"
                                                    prop.text catalog.Description
                                                ]
                                        ]
                                    ]
                                ]
                            ]
                            Html.div [
                                prop.className "flex items-center gap-2 mt-3 pt-3 border-t border-base-300/50"
                                prop.children [
                                    Daisy.badge [
                                        badge.ghost
                                        prop.className "badge-sm"
                                        prop.text $"{catalog.EntryCount} movies"
                                    ]
                                    if catalog.IsSorted then
                                        Daisy.badge [
                                            badge.info
                                            badge.outline
                                            prop.className "badge-sm"
                                            prop.text "Sorted"
                                        ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pagePadding + " " + DesignSystem.animateFadeIn)
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display text-gradient-primary"
                        prop.text "Catalogs"
                    ]
                    Daisy.button.button [
                        button.primary
                        prop.className "gap-2"
                        prop.onClick (fun _ -> dispatch Toggle_create_form)
                        prop.children [
                            Html.span [ prop.text (if model.ShowCreateForm then "Cancel" else "+ New Catalog") ]
                        ]
                    ]
                ]
            ]
            // Create catalog form
            if model.ShowCreateForm then
                Daisy.card [
                    prop.className ("bg-base-100 shadow-md mb-6 " + DesignSystem.animateScaleIn)
                    prop.children [
                        Daisy.cardBody [
                            prop.children [
                                Html.h3 [
                                    prop.className "font-bold mb-3"
                                    prop.text "New Catalog"
                                ]
                                Html.div [
                                    prop.className "flex flex-col gap-3"
                                    prop.children [
                                        Daisy.input [
                                            prop.placeholder "Catalog name..."
                                            prop.value model.CreateForm.Name
                                            prop.onChange (Create_form_name_changed >> dispatch)
                                            prop.onKeyDown (fun e ->
                                                if e.key = "Enter" then dispatch Submit_create_catalog
                                            )
                                        ]
                                        Daisy.textarea [
                                            prop.placeholder "Description (optional)..."
                                            prop.className "textarea-sm"
                                            prop.value model.CreateForm.Description
                                            prop.onChange (Create_form_description_changed >> dispatch)
                                        ]
                                        Html.label [
                                            prop.className "flex items-center gap-2 cursor-pointer"
                                            prop.children [
                                                Daisy.checkbox [
                                                    checkbox.primary
                                                    checkbox.sm
                                                    prop.isChecked model.CreateForm.IsSorted
                                                    prop.onChange (fun (v: bool) -> dispatch (Create_form_sorted_changed v))
                                                ]
                                                Html.span [
                                                    prop.className "text-sm"
                                                    prop.text "Sorted (entries have a specific order)"
                                                ]
                                            ]
                                        ]
                                        Daisy.button.button [
                                            button.primary
                                            prop.onClick (fun _ -> dispatch Submit_create_catalog)
                                            prop.text "Create"
                                        ]
                                    ]
                                ]
                                match model.Error with
                                | Some err ->
                                    Daisy.alert [
                                        alert.error
                                        prop.className "mt-2"
                                        prop.text err
                                    ]
                                | None -> ()
                            ]
                        ]
                    ]
                ]
            if model.IsLoading then
                Html.div [
                    prop.className "flex justify-center py-12"
                    prop.children [
                        Daisy.loading [ loading.spinner; loading.lg ]
                    ]
                ]
            else if List.isEmpty model.Catalogs then
                Html.div [
                    prop.className ("text-center py-20 " + DesignSystem.animateFadeIn)
                    prop.children [
                        Html.div [
                            prop.className "text-base-content/20 mb-4"
                            prop.children [
                                Svg.svg [
                                    svg.className "w-16 h-16 mx-auto"
                                    svg.fill "none"
                                    svg.viewBox (0, 0, 24, 24)
                                    svg.stroke "currentColor"
                                    svg.custom ("strokeWidth", 1)
                                    svg.children [
                                        Svg.path [
                                            svg.custom ("strokeLinecap", "round")
                                            svg.custom ("strokeLinejoin", "round")
                                            svg.d "M12 6.042A8.967 8.967 0 0 0 6 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 0 1 6 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 0 1 6-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0 0 18 18a8.967 8.967 0 0 0-6 2.292m0-14.25v14.25"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                        Html.p [
                            prop.className "text-base-content/50 font-medium"
                            prop.text "No catalogs yet."
                        ]
                        Html.p [
                            prop.className "mt-2 text-base-content/30 text-sm"
                            prop.text "Create a catalog to organize your movies."
                        ]
                    ]
                ]
            else
                Html.div [
                    prop.className ("grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4 " + DesignSystem.staggerGrid)
                    prop.children [
                        for catalog in model.Catalogs do
                            catalogCard catalog
                    ]
                ]
        ]
    ]
