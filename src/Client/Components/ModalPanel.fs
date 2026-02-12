module Mediatheca.Client.Components.ModalPanel

open Feliz

let viewCustom
    (title: string)
    (onClose: unit -> unit)
    (headerExtra: ReactElement list)
    (content: ReactElement list)
    (footer: ReactElement list) =
    Html.div [
        prop.className "fixed inset-0 z-50 flex justify-center items-start pt-[10vh]"
        prop.children [
            Html.div [
                prop.className "absolute inset-0 bg-black/30"
                prop.onClick (fun _ -> onClose ())
            ]
            Html.div [
                prop.className "relative w-full max-w-2xl mx-4 max-h-[70vh] flex flex-col bg-base-100/70 backdrop-blur-xl rounded-2xl shadow-2xl border border-base-content/10 overflow-hidden animate-fade-in"
                prop.children [
                    Html.div [
                        prop.className "p-5 pb-0"
                        prop.children [
                            Html.div [
                                prop.className "flex items-center justify-between mb-4"
                                prop.children [
                                    Html.h3 [
                                        prop.className "font-bold text-lg font-display"
                                        prop.text title
                                    ]
                                ]
                            ]
                            yield! headerExtra
                        ]
                    ]
                    Html.div [
                        prop.className "flex-1 overflow-y-auto px-5 pb-5"
                        prop.children content
                    ]
                    if not (List.isEmpty footer) then
                        Html.div [
                            prop.className "flex justify-end gap-2 px-5 py-4 border-t border-base-content/10"
                            prop.children footer
                        ]
                ]
            ]
        ]
    ]

let view (title: string) (onClose: unit -> unit) (content: ReactElement list) =
    viewCustom title onClose [] content []

let viewWithFooter (title: string) (onClose: unit -> unit) (content: ReactElement list) (footer: ReactElement list) =
    viewCustom title onClose [] content footer
