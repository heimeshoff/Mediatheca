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
                                    prop.className "flex items-center justify-center w-full h-full text-base-content/20"
                                    prop.children [ Icons.movie () ]
                                ]

                            // Optional rating badge (top-right)
                            match ratingBadge with
                            | Some badge -> badge
                            | None -> ()

                            // Bottom gradient + title overlay (visible on hover)
                            Html.div [
                                prop.className "poster-overlay absolute inset-x-0 bottom-0 h-1/2 bg-gradient-to-t from-black/70 to-transparent"
                            ]
                            Html.div [
                                prop.className "poster-overlay absolute inset-x-0 bottom-0 p-3"
                                prop.children [
                                    Html.p [
                                        prop.className "text-white text-xs font-medium line-clamp-2 drop-shadow-md"
                                        prop.text name
                                    ]
                                    Html.p [
                                        prop.className "text-white/70 text-xs mt-0.5"
                                        prop.text (string year)
                                    ]
                                ]
                            ]

                            // Shine effect
                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                ]
            ]
        ]
    ]

/// Poster card with configurable route prefix (e.g. "movies" or "series").
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
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex items-center justify-center w-full h-full text-base-content/20"
                                    prop.children [ Icons.movie () ]
                                ]

                            match ratingBadge with
                            | Some badge -> badge
                            | None -> ()

                            Html.div [
                                prop.className "poster-overlay absolute inset-x-0 bottom-0 h-1/2 bg-gradient-to-t from-black/70 to-transparent"
                            ]
                            Html.div [
                                prop.className "poster-overlay absolute inset-x-0 bottom-0 p-3"
                                prop.children [
                                    Html.p [
                                        prop.className "text-white text-xs font-medium line-clamp-2 drop-shadow-md"
                                        prop.text name
                                    ]
                                    Html.p [
                                        prop.className "text-white/70 text-xs mt-0.5"
                                        prop.text (string year)
                                    ]
                                ]
                            ]

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
