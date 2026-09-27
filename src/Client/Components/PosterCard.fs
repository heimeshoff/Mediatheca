module Mediatheca.Client.Components.PosterCard

open Feliz
open Feliz.Router
open Mediatheca.Client

/// Poster card for grid display (Movies page).
/// Renders a 2/3 aspect-ratio poster with hover effects (shine, shadow lift, info overlay).
let view
    (slug: string)
    (name: string)
    (year: int)
    (posterRef: string option)
    (ratingBadge: ReactElement option)
    =
    Html.a [
        prop.href (Router.format ("movies", slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("movies", slug))
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " group relative cursor-pointer w-full")
                prop.children [
                    Html.div [
                        prop.className DesignSystem.posterImageContainer
                        prop.children [
                            match posterRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt name
                                    prop.className DesignSystem.posterImage
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center w-full h-full text-base-content/20 px-3 gap-2"
                                    prop.children [
                                        Icons.movie ()
                                        Html.p [
                                            prop.className "text-xs text-base-content/40 font-medium text-center line-clamp-2"
                                            prop.text name
                                        ]
                                    ]
                                ]

                            // Optional rating badge (top-right)
                            match ratingBadge with
                            | Some badge -> badge
                            | None -> ()

                            // Shine effect
                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                ]
            ]
        ]
    ]

/// Poster card with configurable route prefix (e.g. "movies" or "series").
/// games-q7vnd: only when `routePrefix` is "games" does the image wire
/// `PosterFit.onImageLoad`, so off-ratio box art (RomM's NES/SNES covers,
/// RAWG's 16:9 screenshots) gets the shared `.poster-image--contain`
/// letterbox treatment once its decoded size proves it isn't close to 2:3.
/// Movie and series art authored at 2:3 never runs the check and keeps
/// `posterImage`'s default `cover`.
let viewForRoute
    (routePrefix: string)
    (slug: string)
    (name: string)
    (year: int)
    (posterRef: string option)
    (ratingBadge: ReactElement option)
    =
    Html.a [
        prop.href (Router.format (routePrefix, slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate (routePrefix, slug))
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " group relative cursor-pointer w-full")
                prop.children [
                    Html.div [
                        prop.className DesignSystem.posterImageContainer
                        prop.children [
                            match posterRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt name
                                    prop.className DesignSystem.posterImage
                                    if routePrefix = "games" then
                                        prop.onLoad PosterFit.onImageLoad
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center w-full h-full text-base-content/20 px-3 gap-2"
                                    prop.children [
                                        Icons.movie ()
                                        Html.p [
                                            prop.className "text-xs text-base-content/40 font-medium text-center line-clamp-2"
                                            prop.text name
                                        ]
                                    ]
                                ]

                            match ratingBadge with
                            | Some badge -> badge
                            | None -> ()

                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                ]
            ]
        ]
    ]

/// Small poster thumbnail for list/row layouts (Dashboard, FriendDetail, CatalogDetail).
let thumbnail (posterRef: string option) (alt: string) =
    Html.div [
        prop.className "w-10 h-14 rounded-lg overflow-hidden bg-base-300 flex-shrink-0"
        prop.children [
            match posterRef with
            | Some ref ->
                Html.img [
                    prop.src $"/images/{ref}"
                    prop.alt alt
                    prop.className "w-full h-full object-cover"
                ]
            | None ->
                Html.div [
                    prop.className "flex items-center justify-center w-full h-full text-base-content/20"
                    prop.children [ Icons.movie () ]
                ]
        ]
    ]
