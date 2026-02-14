module Mediatheca.Client.Components.PageContainer

open Feliz
open Feliz.DaisyUI
open Mediatheca.Client

let view (title: string) (children: ReactElement list) =
    Html.div [
        prop.className DesignSystem.pagePadding
        prop.children [
            Html.h1 [
                prop.className "text-2xl font-bold mb-6"
                prop.text title
            ]
            Daisy.card [
                prop.className "bg-base-100 shadow-xl"
                prop.children [
                    Daisy.cardBody [
                        prop.children children
                    ]
                ]
            ]
        ]
    ]
