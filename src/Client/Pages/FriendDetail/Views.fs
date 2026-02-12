module Mediatheca.Client.Pages.FriendDetail.Views

open Fable.Core.JsInterop
open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.FriendDetail.Types
open Mediatheca.Client.Components

let private readFileAsBytes (file: Browser.Types.File) (onDone: byte array * string -> unit) =
    let reader = Browser.Dom.FileReader.Create()
    reader.onload <- fun _ ->
        let result: obj = unbox reader.result
        let uint8Array: byte array = emitJsExpr result "new Uint8Array($0)"
        onDone (uint8Array, file.name)
    reader.readAsArrayBuffer(file)

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
            prop.className "p-4 lg:p-6 animate-fade-in"
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
                            ]
                        ]
                    ]
                ]
            ]
        ]
