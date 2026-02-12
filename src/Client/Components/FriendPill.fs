module Mediatheca.Client.Components.FriendPill

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared

let view (friend: FriendRef) =
    Daisy.badge [
        badge.lg
        prop.className "cursor-pointer hover:badge-primary"
        prop.onClick (fun e ->
            e.preventDefault()
            e.stopPropagation()
            Router.navigate ("friends", friend.Slug))
        prop.children [
            Html.a [
                prop.href (Router.format ("friends", friend.Slug))
                prop.onClick (fun e -> e.preventDefault())
                prop.text friend.Name
            ]
        ]
    ]

let viewWithRemove (friend: FriendRef) (onRemove: string -> unit) =
    Daisy.badge [
        badge.lg
        prop.className "gap-1"
        prop.children [
            Html.a [
                prop.className "cursor-pointer hover:text-primary"
                prop.href (Router.format ("friends", friend.Slug))
                prop.onClick (fun e ->
                    e.preventDefault()
                    e.stopPropagation()
                    Router.navigate ("friends", friend.Slug))
                prop.text friend.Name
            ]
            Html.button [
                prop.className "btn btn-ghost btn-xs"
                prop.onClick (fun e ->
                    e.stopPropagation()
                    onRemove friend.Slug)
                prop.text "x"
            ]
        ]
    ]

let viewInline (friend: FriendRef) =
    Html.a [
        prop.className "font-semibold cursor-pointer hover:text-primary"
        prop.href (Router.format ("friends", friend.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            e.stopPropagation()
            Router.navigate ("friends", friend.Slug))
        prop.text friend.Name
    ]
