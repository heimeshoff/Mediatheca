module Mediatheca.Client.Pages.Movies.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Movies.Types
open Mediatheca.Client.Components

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
                prop.className "bg-base-100 shadow-md hover:shadow-xl transition-shadow cursor-pointer"
                prop.children [
                    Html.figure [
                        prop.className "aspect-[2/3] bg-base-300"
                        prop.children [
                            match movie.PosterRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt movie.Name
                                    prop.className "w-full h-full object-cover"
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex items-center justify-center w-full h-full text-base-content/30"
                                    prop.children [
                                        Icons.movie ()
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
                                prop.className "flex items-center gap-2 text-xs text-base-content/60"
                                prop.children [
                                    Html.span [ prop.text (string movie.Year) ]
                                    match movie.TmdbRating with
                                    | Some rating ->
                                        Html.span [ prop.text $"%.1f{rating}" ]
                                    | None -> ()
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
        prop.className "p-4 lg:p-6"
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display"
                        prop.text "Movies"
                    ]
                    Daisy.button.button [
                        button.primary
                        prop.onClick (fun _ -> dispatch Open_tmdb_search)
                        prop.text "Add Movie"
                    ]
                ]
            ]
            // Search and filter bar
            Html.div [
                prop.className "flex flex-col sm:flex-row gap-3 mb-6"
                prop.children [
                    Daisy.input [
                        prop.className "flex-1"
                        prop.placeholder "Search movies..."
                        prop.value model.SearchQuery
                        prop.onChange (Search_changed >> dispatch)
                    ]
                    Html.div [
                        prop.className "flex gap-2 flex-wrap"
                        prop.children [
                            Daisy.button.button [
                                if model.GenreFilter.IsNone then button.primary else button.ghost
                                button.sm
                                prop.onClick (fun _ -> dispatch (Genre_filter_changed None))
                                prop.text "All"
                            ]
                            for genre in allGenres model.Movies do
                                Daisy.button.button [
                                    if model.GenreFilter = Some genre then button.primary else button.ghost
                                    button.sm
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
                        prop.className "text-center py-12 text-base-content/50"
                        prop.children [
                            Html.p [ prop.text "No movies found." ]
                            Html.p [
                                prop.className "mt-2"
                                prop.text "Add a movie from TMDB to get started."
                            ]
                        ]
                    ]
                else
                    Html.div [
                        prop.className "grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4"
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
