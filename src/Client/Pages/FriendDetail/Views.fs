module Mediatheca.Client.Pages.FriendDetail.Views

open Fable.Core.JsInterop
open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.FriendDetail.Types
open Mediatheca.Client
open Mediatheca.Client.Components

let private readFileAsBytes (file: Browser.Types.File) (onDone: byte array * string -> unit) =
    let reader = Browser.Dom.FileReader.Create()
    reader.onload <- fun _ ->
        let result: obj = unbox reader.result
        let uint8Array: byte array = emitJsExpr result "new Uint8Array($0)"
        onDone (uint8Array, file.name)
    reader.readAsArrayBuffer(file)

let private movieCard (movie: FriendMovieItem) =
    Html.a [
        prop.href (Router.format ("movies", movie.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("movies", movie.Slug))
        prop.children [
            Html.div [
                prop.className "flex items-center gap-3 p-3 rounded-lg bg-base-200/50 hover:bg-base-200 transition-colors cursor-pointer"
                prop.children [
                    PosterCard.thumbnail movie.PosterRef movie.Name
                    Html.div [
                        prop.children [
                            Html.p [
                                prop.className "font-medium text-sm leading-tight"
                                prop.text movie.Name
                            ]
                            Html.p [
                                prop.className "text-xs text-base-content/50"
                                prop.text (string movie.Year)
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private watchedMovieCard (movie: FriendWatchedItem) =
    Html.a [
        prop.href (Router.format ("movies", movie.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("movies", movie.Slug))
        prop.children [
            Html.div [
                prop.className "flex items-center gap-3 p-3 rounded-lg bg-base-200/50 hover:bg-base-200 transition-colors cursor-pointer"
                prop.children [
                    PosterCard.thumbnail movie.PosterRef movie.Name
                    Html.div [
                        prop.className "flex-1 min-w-0"
                        prop.children [
                            Html.p [
                                prop.className "font-medium text-sm leading-tight"
                                prop.text movie.Name
                            ]
                            Html.p [
                                prop.className "text-xs text-base-content/50"
                                prop.text (movie.Dates |> String.concat ", ")
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private watchedMovieSection (movies: FriendWatchedItem list) =
    if List.isEmpty movies then
        Html.none
    else
        Html.div [
            prop.className "mt-6"
            prop.children [
                Html.h3 [
                    prop.className "text-lg font-bold font-display mb-3"
                    prop.text "Watched Together"
                ]
                Html.div [
                    prop.className "flex flex-col gap-2"
                    prop.children [
                        for movie in movies do
                            watchedMovieCard movie
                    ]
                ]
            ]
        ]

let private movieSection (title: string) (movies: FriendMovieItem list) =
    if List.isEmpty movies then
        Html.none
    else
        Html.div [
            prop.className "mt-6"
            prop.children [
                Html.h3 [
                    prop.className "text-lg font-bold font-display mb-3"
                    prop.text title
                ]
                Html.div [
                    prop.className "flex flex-col gap-2"
                    prop.children [
                        for movie in movies do
                            movieCard movie
                    ]
                ]
            ]
        ]

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
        let fileInputId = "friend-detail-image-upload"
        Html.div [
            prop.className (DesignSystem.pagePadding + " " + DesignSystem.animateFadeIn)
            prop.children [
                // Back button
                Html.div [
                    prop.className "mb-4"
                    prop.children [
                        Daisy.button.button [
                            button.ghost
                            button.sm
                            prop.onClick (fun _ ->
                                emitJsStatement () "window.history.back()")
                            prop.text "< Back"
                        ]
                    ]
                ]
                Daisy.card [
                    prop.className "bg-base-100 shadow-xl relative"
                    prop.children [
                        // Trash icon in top right
                        Daisy.button.button [
                            button.ghost
                            button.sm
                            button.circle
                            prop.className "absolute top-4 right-4 text-base-content/40 hover:text-error"
                            prop.onClick (fun _ -> dispatch Remove_friend)
                            prop.children [ Icons.trash () ]
                        ]
                        Daisy.cardBody [
                            prop.className "p-8"
                            prop.children [
                                Html.div [
                                    prop.className "flex items-center gap-6"
                                    prop.children [
                                        // Clickable avatar for image upload
                                        Daisy.avatar [
                                            prop.children [
                                                Html.div [
                                                    prop.className "w-24 h-24 rounded-full bg-base-300 ring-2 ring-base-300 cursor-pointer transition-all duration-300 hover:ring-primary/50"
                                                    prop.onClick (fun _ ->
                                                        let input = Browser.Dom.document.getElementById(fileInputId)
                                                        if not (isNull input) then input.click())
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
                                        Html.input [
                                            prop.id fileInputId
                                            prop.type' "file"
                                            prop.accept "image/*"
                                            prop.className "hidden"
                                            prop.onChange (fun (e: Browser.Types.Event) ->
                                                let input: Browser.Types.HTMLInputElement = unbox e.target
                                                let files = input.files
                                                if files.length > 0 then
                                                    let file = files.[0]
                                                    readFileAsBytes file (fun (bytes, filename) ->
                                                        dispatch (Upload_friend_image (bytes, filename))))
                                        ]
                                        // Name: inline editable
                                        Html.div [
                                            prop.children [
                                                if model.IsEditing then
                                                    Daisy.input [
                                                        prop.className "text-2xl font-bold font-display w-full"
                                                        prop.autoFocus true
                                                        prop.value model.EditForm.Name
                                                        prop.onChange (Edit_name_changed >> dispatch)
                                                        prop.onKeyDown (fun e ->
                                                            if e.key = "Enter" then dispatch Submit_update
                                                            elif e.key = "Escape" then dispatch Cancel_editing)
                                                        prop.onBlur (fun _ -> dispatch Submit_update)
                                                    ]
                                                else
                                                    Html.h2 [
                                                        prop.className "text-2xl font-bold font-display cursor-pointer hover:text-primary transition-colors"
                                                        prop.onClick (fun _ -> dispatch Start_editing)
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
                                // Movie sections
                                match model.FriendMovies with
                                | Some movies ->
                                    movieSection "Recommended" movies.RecommendedMovies
                                    movieSection "Want to Watch Together" movies.WantToWatchMovies
                                    watchedMovieSection movies.WatchedMovies
                                | None -> ()
                            ]
                        ]
                    ]
                ]
                if model.ShowRemoveConfirm then
                    ModalPanel.viewWithFooter
                        "Remove Friend"
                        (fun () -> dispatch Cancel_remove_friend)
                        [
                            Html.p [
                                prop.text $"Do you really want to remove {friend.Name}? All recommendations and watch history references will be removed."
                            ]
                        ]
                        [
                            Daisy.button.button [
                                button.ghost
                                prop.onClick (fun _ -> dispatch Cancel_remove_friend)
                                prop.text "Cancel"
                            ]
                            Daisy.button.button [
                                button.error
                                prop.onClick (fun _ -> dispatch Confirm_remove_friend)
                                prop.text "Remove"
                            ]
                        ]
            ]
        ]
