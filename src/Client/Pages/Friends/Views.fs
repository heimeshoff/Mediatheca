module Mediatheca.Client.Pages.Friends.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Friends.Types
open Mediatheca.Client.Components

let private friendCard (friend: Mediatheca.Shared.FriendListItem) =
    Html.a [
        prop.href (Router.format ("friends", friend.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("friends", friend.Slug)
        )
        prop.children [
            Daisy.card [
                card.sm
                prop.className "bg-base-100 shadow-md hover:shadow-xl transition-shadow cursor-pointer"
                prop.children [
                    Daisy.cardBody [
                        prop.className "items-center text-center"
                        prop.children [
                            Daisy.avatar [
                                prop.children [
                                    Html.div [
                                        prop.className "w-16 h-16 rounded-full bg-base-300"
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
                                prop.className "card-title text-sm font-semibold mt-2"
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
        prop.className "p-4 lg:p-6"
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display"
                        prop.text "Friends"
                    ]
                    Daisy.button.button [
                        button.primary
                        prop.onClick (fun _ -> dispatch ToggleAddForm)
                        prop.text (if model.ShowAddForm then "Cancel" else "Add Friend")
                    ]
                ]
            ]
            // Add friend form
            if model.ShowAddForm then
                Daisy.card [
                    prop.className "bg-base-100 shadow-md mb-6"
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
                                            prop.onChange (AddFormNameChanged >> dispatch)
                                            prop.onKeyDown (fun e ->
                                                if e.key = "Enter" then dispatch SubmitAddFriend
                                            )
                                        ]
                                        Daisy.button.button [
                                            button.primary
                                            prop.onClick (fun _ -> dispatch SubmitAddFriend)
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
                    prop.className "text-center py-12 text-base-content/50"
                    prop.children [
                        Html.p [ prop.text "No friends yet." ]
                        Html.p [
                            prop.className "mt-2"
                            prop.text "Add a friend to get started."
                        ]
                    ]
                ]
            else
                Html.div [
                    prop.className "grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4"
                    prop.children [
                        for friend in model.Friends do
                            friendCard friend
                    ]
                ]
        ]
    ]
