module Mediatheca.Client.Pages.Movies.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Movies.Types
open Mediatheca.Client.Components

let private ratingColor (rating: float) =
    if rating >= 7.5 then "badge-success"
    elif rating >= 5.0 then "badge-warning"
    else "badge-error"

let private movieCard (movie: Mediatheca.Shared.MovieListItem) =
    Html.a [
        prop.href (Router.format ("movies", movie.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("movies", movie.Slug)
        )
        prop.children [
            Daisy.card [
                card.sm
                prop.className "poster-card card-hover bg-base-100 shadow-md cursor-pointer overflow-hidden"
                prop.children [
                    Html.figure [
                        prop.className "relative aspect-[2/3] bg-base-300 overflow-hidden"
                        prop.children [
                            match movie.PosterRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt movie.Name
                                    prop.className "w-full h-full object-cover transition-transform duration-300 group-hover:scale-105"
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex items-center justify-center w-full h-full text-base-content/30"
                                    prop.children [
                                        Icons.movie ()
                                    ]
                                ]
                            // Rating badge overlay
                            match movie.TmdbRating with
                            | Some rating ->
                                Html.div [
                                    prop.className $"absolute top-2 right-2 badge badge-sm {ratingColor rating} font-bold shadow-lg"
                                    prop.text $"%.1f{rating}"
                                ]
                            | None -> ()
                            // Bottom gradient overlay
                            Html.div [
                                prop.className "poster-overlay absolute inset-x-0 bottom-0 h-1/2 bg-gradient-to-t from-black/70 to-transparent"
                            ]
                            Html.div [
                                prop.className "poster-overlay absolute inset-x-0 bottom-0 p-3"
                                prop.children [
                                    Html.p [
                                        prop.className "text-white text-xs font-medium line-clamp-2 drop-shadow-md"
                                        prop.text movie.Name
                                    ]
                                    Html.p [
                                        prop.className "text-white/70 text-xs mt-0.5"
                                        prop.text (string movie.Year)
                                    ]
                                ]
                            ]
                        ]
                    ]
                    Daisy.cardBody [
                        prop.children [
                            Html.h3 [
                                prop.className "card-title text-sm font-semibold line-clamp-1"
                                prop.text movie.Name
                            ]
                            Html.div [
                                prop.className "flex items-center gap-2 text-xs text-base-content/50"
                                prop.children [
                                    Html.span [ prop.text (string movie.Year) ]
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]

let private allGenres (movies: Mediatheca.Shared.MovieListItem list) =
    movies
    |> List.collect (fun m -> m.Genres)
    |> List.distinct
    |> List.sort

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className "p-4 lg:p-6 animate-fade-in"
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
            // Search and filter bar
            Html.div [
                prop.className "flex flex-col sm:flex-row gap-3 mb-6"
                prop.children [
                    Html.div [
                        prop.className "relative flex-1"
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
                    Html.div [
                        prop.className "flex gap-1.5 flex-wrap"
                        prop.children [
                            Daisy.button.button [
                                if model.GenreFilter.IsNone then button.primary else button.ghost
                                button.sm
                                prop.className (if model.GenreFilter.IsNone then "" else "hover:bg-base-300/60")
                                prop.onClick (fun _ -> dispatch (Genre_filter_changed None))
                                prop.text "All"
                            ]
                            for genre in allGenres model.Movies do
                                Daisy.button.button [
                                    if model.GenreFilter = Some genre then button.primary else button.ghost
                                    button.sm
                                    prop.className (if model.GenreFilter = Some genre then "" else "hover:bg-base-300/60")
                                    prop.onClick (fun _ -> dispatch (Genre_filter_changed (Some genre)))
                                    prop.text genre
                                ]
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
            else
                let filtered =
                    model.Movies
                    |> List.filter (fun m ->
                        let matchesSearch =
                            model.SearchQuery = "" ||
                            m.Name.ToLowerInvariant().Contains(model.SearchQuery.ToLowerInvariant())
                        let matchesGenre =
                            match model.GenreFilter with
                            | None -> true
                            | Some g -> m.Genres |> List.contains g
                        matchesSearch && matchesGenre
                    )
                if List.isEmpty filtered then
                    Html.div [
                        prop.className "text-center py-20 animate-fade-in"
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
                        prop.className "grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4 stagger-grid"
                        prop.children [
                            for movie in filtered do
                                movieCard movie
                        ]
                    ]
            // TMDB Search Modal
            match model.TmdbSearch with
            | Some searchModel ->
                TmdbSearchModal.view searchModel (Tmdb_search_msg >> dispatch)
            | None -> ()
        ]
    ]
