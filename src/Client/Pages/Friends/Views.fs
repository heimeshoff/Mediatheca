module Mediatheca.Client.Pages.Friends.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Friends.Types
open Mediatheca.Client
open Mediatheca.Client.Components

let private friendCard (friend: Mediatheca.Shared.FriendListItem) (_dispatch: Msg -> unit) =
    Html.a [
        prop.href (Router.format ("friends", friend.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("friends", friend.Slug))
        prop.children [
            Daisy.card [
                card.sm
                prop.className "card-hover bg-base-100 shadow-md cursor-pointer"
                prop.children [
                    Daisy.cardBody [
                        prop.className "items-center text-center p-5"
                        prop.children [
                            Daisy.avatar [
                                prop.children [
                                    Html.div [
                                        prop.className "w-16 h-16 rounded-full bg-base-300 ring-2 ring-base-300"
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
                            Html.h3 [
                                prop.className "card-title text-sm font-semibold mt-3"
                                prop.text friend.Name
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pageContainer + " " + DesignSystem.animateFadeIn)
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display text-gradient-primary"
                        prop.text "Friends"
                    ]
                    Daisy.button.button [
                        button.primary
                        prop.className "gap-2"
                        prop.onClick (fun _ -> dispatch Toggle_add_form)
                        prop.children [
                            Html.span [ prop.text (if model.ShowAddForm then "Cancel" else "+ Add Friend") ]
                        ]
                    ]
                ]
            ]
            // Add friend form
            if model.ShowAddForm then
                Daisy.card [
                    prop.className ("bg-base-100 shadow-md mb-6 " + DesignSystem.animateScaleIn)
                    prop.children [
                        Daisy.cardBody [
                            prop.children [
                                Html.h3 [
                                    prop.className "font-bold mb-3"
                                    prop.text "Add a Friend"
                                ]
                                Html.div [
                                    prop.className "flex gap-2"
                                    prop.children [
                                        Daisy.input [
                                            prop.className "flex-1"
                                            prop.placeholder "Friend name..."
                                            prop.value model.AddForm.Name
                                            prop.onChange (Add_form_name_changed >> dispatch)
                                            prop.onKeyDown (fun e ->
                                                if e.key = "Enter" then dispatch Submit_add_friend
                                            )
                                        ]
                                        Daisy.button.button [
                                            button.primary
                                            prop.onClick (fun _ -> dispatch Submit_add_friend)
                                            prop.text "Add"
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
            else if List.isEmpty model.Friends then
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
                                            svg.d "M15 19.128a9.38 9.38 0 0 0 2.625.372 9.337 9.337 0 0 0 4.121-.952 4.125 4.125 0 0 0-7.533-2.493M15 19.128v-.003c0-1.113-.285-2.16-.786-3.07M15 19.128v.106A12.318 12.318 0 0 1 8.624 21c-2.331 0-4.512-.645-6.374-1.766l-.001-.109a6.375 6.375 0 0 1 11.964-3.07M12 6.375a3.375 3.375 0 1 1-6.75 0 3.375 3.375 0 0 1 6.75 0Zm8.25 2.25a2.625 2.625 0 1 1-5.25 0 2.625 2.625 0 0 1 5.25 0Z"
                                        ]
                                    ]
                                ]
                            ]
                        ]
                        Html.p [
                            prop.className "text-base-content/50 font-medium"
                            prop.text "No friends yet."
                        ]
                        Html.p [
                            prop.className "mt-2 text-base-content/30 text-sm"
                            prop.text "Add a friend to get started."
                        ]
                    ]
                ]
            else
                Html.div [
                    prop.className ("grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4 " + DesignSystem.animateFadeIn)
                    prop.children [
                        for friend in model.Friends do
                            friendCard friend dispatch
                    ]
                ]
        ]
    ]
