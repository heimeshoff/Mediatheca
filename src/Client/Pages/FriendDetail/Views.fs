module Mediatheca.Client.Pages.FriendDetail.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.FriendDetail.Types
open Mediatheca.Client.Components

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
        Html.div [
            prop.className "p-4 lg:p-6 animate-fade-in"
            prop.children [
                // Back button
                Html.div [
                    prop.className "mb-4"
                    prop.children [
                        Daisy.button.button [
                            button.ghost
                            button.sm
                            prop.onClick (fun e ->
                                e.preventDefault()
                                Fable.Core.JS.Constructors.Window.Create().history.back())
                            prop.text "< Back"
                        ]
                    ]
                ]
                Daisy.card [
                    prop.className "bg-base-100 shadow-xl"
                    prop.children [
                        Daisy.cardBody [
                            prop.className "p-8"
                            prop.children [
                                if model.IsEditing then
                                    // Edit form
                                    Html.h2 [
                                        prop.className "card-title font-display mb-6"
                                        prop.text "Edit Friend"
                                    ]
                                    Html.div [
                                        prop.className "form-control mb-6"
                                        prop.children [
                                            Html.label [
                                                prop.className "label"
                                                prop.children [
                                                    Html.span [
                                                        prop.className "label-text"
                                                        prop.text "Name"
                                                    ]
                                                ]
                                            ]
                                            Daisy.input [
                                                prop.value model.EditForm.Name
                                                prop.onChange (Edit_name_changed >> dispatch)
                                            ]
                                        ]
                                    ]
                                    match model.Error with
                                    | Some err ->
                                        Daisy.alert [
                                            alert.error
                                            prop.className "mb-4"
                                            prop.text err
                                        ]
                                    | None -> ()
                                    Html.div [
                                        prop.className "flex gap-3 pt-2"
                                        prop.children [
                                            Daisy.button.button [
                                                button.primary
                                                prop.onClick (fun _ -> dispatch Submit_update)
                                                prop.text "Save"
                                            ]
                                            Daisy.button.button [
                                                button.ghost
                                                prop.onClick (fun _ -> dispatch Cancel_editing)
                                                prop.text "Cancel"
                                            ]
                                        ]
                                    ]
                                else
                                    // View
                                    Html.div [
                                        prop.className "flex items-center gap-6"
                                        prop.children [
                                            Daisy.avatar [
                                                prop.children [
                                                    Html.div [
                                                        prop.className "w-24 h-24 rounded-full bg-base-300 ring-2 ring-base-300"
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
                                            Html.div [
                                                prop.children [
                                                    Html.h2 [
                                                        prop.className "text-2xl font-bold font-display"
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
                                    // Divider + action buttons
                                    Html.hr [
                                        prop.className "border-base-300/50 my-6"
                                    ]
                                    Html.div [
                                        prop.className "flex gap-3"
                                        prop.children [
                                            Daisy.button.button [
                                                button.primary
                                                button.outline
                                                prop.onClick (fun _ -> dispatch Start_editing)
                                                prop.text "Edit"
                                            ]
                                            Daisy.button.button [
                                                button.error
                                                button.outline
                                                prop.onClick (fun _ -> dispatch Remove_friend)
                                                prop.text "Remove"
                                            ]
                                        ]
                                    ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
