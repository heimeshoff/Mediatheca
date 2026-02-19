module Mediatheca.Client.Pages.Movies.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Movies.Types
open Mediatheca.Client
open Mediatheca.Client.Components

let private inFocusBadge =
    Html.div [
        prop.className "absolute top-1.5 right-1.5 z-10 w-6 h-6 rounded-full bg-primary/80 backdrop-blur-sm flex items-center justify-center text-primary-content shadow-md border border-primary/30"
        prop.title "In Focus"
        prop.children [
            Html.span [ prop.className "w-3.5 h-3.5"; prop.children [ Icons.crosshairSmFilled () ] ]
        ]
    ]

let private movieCard (movie: Mediatheca.Shared.MovieListItem) =
    let badge = if movie.InFocus then Some inFocusBadge else None
    PosterCard.view movie.Slug movie.Name movie.Year movie.PosterRef badge

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pageContainer + " " + DesignSystem.animateFadeIn)
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display text-gradient-primary"
                        prop.text "Movies"
                    ]
                    Daisy.button.button [
                        button.primary
                        prop.className "gap-2"
                        prop.onClick (fun _ -> dispatch Open_tmdb_search)
                        prop.children [
                            Html.span [ prop.text "+" ]
                            Html.span [ prop.text "Add Movie" ]
                        ]
                    ]
                ]
            ]
            // Search bar
            Html.div [
                prop.className "relative mb-6"
                prop.children [
                    Html.div [
                        prop.className "absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none text-base-content/40"
                        prop.children [
                            Svg.svg [
                                svg.className "w-5 h-5"
                                svg.fill "none"
                                svg.viewBox (0, 0, 24, 24)
                                svg.stroke "currentColor"
                                svg.custom ("strokeWidth", 1.5)
                                svg.children [
                                    Svg.path [
                                        svg.custom ("strokeLinecap", "round")
                                        svg.custom ("strokeLinejoin", "round")
                                        svg.d "m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z"
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Daisy.input [
                        prop.className "w-full pl-10"
                        prop.placeholder "Search movies..."
                        prop.value model.SearchQuery
                        prop.onChange (Search_changed >> dispatch)
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
            else
                let filtered =
                    model.Movies
                    |> List.filter (fun m ->
                        model.SearchQuery = "" ||
                        m.Name.ToLowerInvariant().Contains(model.SearchQuery.ToLowerInvariant())
                    )
                if List.isEmpty filtered then
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
                                                svg.d "M3.375 19.5h17.25m-17.25 0a1.125 1.125 0 0 1-1.125-1.125M3.375 19.5h1.5C5.496 19.5 6 18.996 6 18.375m-3.75 0V5.625m0 12.75v-1.5c0-.621.504-1.125 1.125-1.125m18.375 2.625V5.625m0 12.75c0 .621-.504 1.125-1.125 1.125m1.125-1.125v-1.5c0-.621-.504-1.125-1.125-1.125m0 3.75h-1.5A1.125 1.125 0 0 1 18 18.375M20.625 4.5H3.375m17.25 0c.621 0 1.125.504 1.125 1.125M20.625 4.5h-1.5C18.504 4.5 18 5.004 18 5.625m3.75 0v1.5c0 .621-.504 1.125-1.125 1.125M3.375 4.5c-.621 0-1.125.504-1.125 1.125M3.375 4.5h1.5C5.496 4.5 6 5.004 6 5.625m-3.75 0v1.5c0 .621.504 1.125 1.125 1.125m0 0h1.5m-1.5 0c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125m1.5-3.75C5.496 8.25 6 7.746 6 7.125v-1.5M4.875 8.25C5.496 8.25 6 8.754 6 9.375v1.5c0 .621-.504 1.125-1.125 1.125m0 0h1.5m-1.5 0c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125M6 12h1.5m-1.5 0c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125m0 0h1.5m7.5-12h1.5m-1.5 0c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125m1.5-3.75C18.504 5.625 18 6.129 18 6.75v1.5m1.125-3.75c.621 0 1.125.504 1.125 1.125v1.5c0 .621-.504 1.125-1.125 1.125m0 0h-1.5m1.5 0c.621 0 1.125.504 1.125 1.125v1.5c0 .621-.504 1.125-1.125 1.125m-1.5-3.75C18.504 8.25 18 8.754 18 9.375v1.5m1.125-3.75c-.621 0-1.125.504-1.125 1.125v1.5c0 .621.504 1.125 1.125 1.125m0 0h-1.5m1.5 0c.621 0 1.125.504 1.125 1.125v1.5c0 .621-.504 1.125-1.125 1.125M18 12h-1.5m1.5 0c.621 0 1.125.504 1.125 1.125v1.5c0 .621-.504 1.125-1.125 1.125"
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            Html.p [
                                prop.className "text-base-content/50 font-medium"
                                prop.text "No movies found."
                            ]
                            Html.p [
                                prop.className "mt-2 text-base-content/30 text-sm"
                                prop.text "Add a movie from TMDB to get started."
                            ]
                        ]
                    ]
                else
                    Html.div [
                        prop.className ("grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4 " + DesignSystem.animateFadeIn)
                        prop.children [
                            for movie in filtered do
                                movieCard movie
                        ]
                    ]
        ]
    ]
