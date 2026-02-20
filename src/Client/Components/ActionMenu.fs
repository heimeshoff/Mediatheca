module Mediatheca.Client.Components.ActionMenu

open Feliz
open Fable.Core.JsInterop

type ActionMenuItem = {
    Label: string
    Icon: (unit -> ReactElement) option
    OnClick: unit -> unit
    IsDestructive: bool
}

/// Hover-reveal action menu with glassmorphism dropdown.
/// Renders as a sibling structure to avoid backdrop-filter nesting issues.
[<ReactComponent>]
let view (items: ActionMenuItem list) =
    let isOpen, setIsOpen = React.useState false
    let menuRef = React.useRef<Browser.Types.HTMLElement option>(None)

    // Close on click outside
    React.useEffect((fun () ->
        let handler (e: Browser.Types.Event) =
            match menuRef.current with
            | Some el ->
                let target = e.target :?> Browser.Types.HTMLElement
                if not (el.contains(target)) then
                    setIsOpen false
            | None -> ()
        let listener = handler
        Browser.Dom.document.addEventListener("mousedown", listener)
        { new System.IDisposable with
            member _.Dispose() =
                Browser.Dom.document.removeEventListener("mousedown", listener)
        }
    ), [| isOpen :> obj |])

    Html.div [
        prop.ref (fun el ->
            if not (isNull el) then menuRef.current <- Some (el :?> Browser.Types.HTMLElement))
        prop.className "relative"
        prop.children [
            // Trigger button
            Html.button [
                prop.className "w-8 h-8 flex items-center justify-center rounded-full text-base-content/50 hover:text-base-content hover:bg-base-content/10 transition-all cursor-pointer"
                prop.onClick (fun e ->
                    e.stopPropagation()
                    setIsOpen (not isOpen))
                prop.children [
                    // Ellipsis icon (three dots vertical)
                    Html.svg [
                        prop.className "w-5 h-5"
                        prop.custom ("viewBox", "0 0 20 20")
                        prop.custom ("fill", "currentColor")
                        prop.children [
                            Html.path [
                                prop.d "M10 6a2 2 0 110-4 2 2 0 010 4zm0 6a2 2 0 110-4 2 2 0 010 4zm0 6a2 2 0 110-4 2 2 0 010 4z"
                            ]
                        ]
                    ]
                ]
            ]
            // Dropdown menu (sibling to button, not child of any blurred element)
            if isOpen then
                // Click-away backdrop
                Html.div [
                    prop.className "fixed inset-0 z-[200]"
                    prop.onClick (fun _ -> setIsOpen false)
                ]
                Html.div [
                    prop.className "absolute right-0 top-full mt-1 z-[201] min-w-[180px] rating-dropdown"
                    prop.children [
                        for item in items do
                            Html.button [
                                prop.className (
                                    "w-full flex items-center gap-2.5 px-3 py-2.5 rounded-lg text-sm font-medium transition-all text-left cursor-pointer "
                                    + if item.IsDestructive then
                                        "text-error/80 hover:text-error hover:bg-error/10"
                                      else
                                        "text-base-content/80 hover:text-base-content hover:bg-base-content/10"
                                )
                                prop.onClick (fun e ->
                                    e.stopPropagation()
                                    setIsOpen false
                                    item.OnClick ())
                                prop.children [
                                    match item.Icon with
                                    | Some iconFn ->
                                        Html.span [
                                            prop.className "w-4 h-4 flex-shrink-0"
                                            prop.children [ iconFn () ]
                                        ]
                                    | None -> ()
                                    Html.span [ prop.text item.Label ]
                                ]
                            ]
                    ]
                ]
        ]
    ]

/// Hero-positioned action menu (top-right, hover-reveal like "Change backdrop" button)
[<ReactComponent>]
let heroView (items: ActionMenuItem list) =
    let isOpen, setIsOpen = React.useState false
    let menuRef = React.useRef<Browser.Types.HTMLElement option>(None)

    React.useEffect((fun () ->
        let handler (e: Browser.Types.Event) =
            match menuRef.current with
            | Some el ->
                let target = e.target :?> Browser.Types.HTMLElement
                if not (el.contains(target)) then
                    setIsOpen false
            | None -> ()
        let listener = handler
        Browser.Dom.document.addEventListener("mousedown", listener)
        { new System.IDisposable with
            member _.Dispose() =
                Browser.Dom.document.removeEventListener("mousedown", listener)
        }
    ), [| isOpen :> obj |])

    Html.div [
        prop.ref (fun el ->
            if not (isNull el) then menuRef.current <- Some (el :?> Browser.Types.HTMLElement))
        prop.className "relative"
        prop.children [
            // Trigger button - glass style matching "Change backdrop"
            Html.button [
                prop.className "w-9 h-9 flex items-center justify-center rounded-full text-base-content backdrop-blur-sm bg-base-300/30 hover:bg-base-300/50 transition-all cursor-pointer"
                prop.onClick (fun e ->
                    e.stopPropagation()
                    setIsOpen (not isOpen))
                prop.children [
                    Html.svg [
                        prop.className "w-5 h-5"
                        prop.custom ("viewBox", "0 0 20 20")
                        prop.custom ("fill", "currentColor")
                        prop.children [
                            Html.path [
                                prop.d "M10 6a2 2 0 110-4 2 2 0 010 4zm0 6a2 2 0 110-4 2 2 0 010 4zm0 6a2 2 0 110-4 2 2 0 010 4z"
                            ]
                        ]
                    ]
                ]
            ]
            if isOpen then
                Html.div [
                    prop.className "fixed inset-0 z-[200]"
                    prop.onClick (fun _ -> setIsOpen false)
                ]
                Html.div [
                    prop.className "absolute right-0 top-full mt-1 z-[201] min-w-[180px] rating-dropdown"
                    prop.children [
                        for item in items do
                            Html.button [
                                prop.className (
                                    "w-full flex items-center gap-2.5 px-3 py-2.5 rounded-lg text-sm font-medium transition-all text-left cursor-pointer "
                                    + if item.IsDestructive then
                                        "text-error/80 hover:text-error hover:bg-error/10"
                                      else
                                        "text-base-content/80 hover:text-base-content hover:bg-base-content/10"
                                )
                                prop.onClick (fun e ->
                                    e.stopPropagation()
                                    setIsOpen false
                                    item.OnClick ())
                                prop.children [
                                    match item.Icon with
                                    | Some iconFn ->
                                        Html.span [
                                            prop.className "w-4 h-4 flex-shrink-0"
                                            prop.children [ iconFn () ]
                                        ]
                                    | None -> ()
                                    Html.span [ prop.text item.Label ]
                                ]
                            ]
                    ]
                ]
        ]
    ]
