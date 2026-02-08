module Mediatheca.Client.Pages.Dashboard.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Dashboard.Types
open Mediatheca.Client.Components

let private statCard (icon: unit -> ReactElement) (label: string) (value: int) (color: string) (delay: int) =
    Html.div [
        prop.className $"stat-glow bg-base-100 rounded-2xl p-6 shadow-md card-hover"
        prop.style [ style.custom ("animationDelay", $"{delay}ms") ]
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between"
                prop.children [
                    Html.div [
                        prop.children [
                            Html.p [
                                prop.className "text-base-content/50 text-sm font-medium uppercase tracking-wide"
                                prop.text label
                            ]
                            Html.p [
                                prop.className $"text-4xl font-bold font-display mt-1 {color}"
                                prop.text (string value)
                            ]
                        ]
                    ]
                    Html.div [
                        prop.className $"p-3 rounded-xl bg-base-300/50 {color}"
                        prop.children [ icon () ]
                    ]
                ]
            ]
        ]
    ]

let private recentMovieCard (movie: Mediatheca.Shared.MovieListItem) =
    Html.a [
        prop.href (Router.format ("movies", movie.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("movies", movie.Slug)
        )
        prop.className "flex items-center gap-3 p-3 rounded-xl hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            Html.div [
                prop.className "w-10 h-14 rounded-lg overflow-hidden bg-base-300 flex-none"
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
                            prop.className "flex items-center justify-center w-full h-full text-base-content/20"
                            prop.children [ Icons.movie () ]
                        ]
                ]
            ]
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                        prop.text movie.Name
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/50"
                        prop.children [
                            Html.span [ prop.text (string movie.Year) ]
                            match movie.TmdbRating with
                            | Some r ->
                                Html.span [ prop.text $" \u00B7 %.1f{r}" ]
                            | None -> ()
                        ]
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (_dispatch: Msg -> unit) =
    Html.div [
        prop.className "animate-fade-in"
        prop.children [
            // Hero section
            Html.div [
                prop.className "relative overflow-hidden bg-gradient-to-br from-base-200 via-base-200 to-primary/10 px-6 py-10 lg:px-10 lg:py-14"
                prop.children [
                    // Decorative circles
                    Html.div [
                        prop.className "absolute -top-20 -right-20 w-64 h-64 rounded-full bg-primary/5 blur-3xl"
                    ]
                    Html.div [
                        prop.className "absolute -bottom-10 -left-10 w-48 h-48 rounded-full bg-accent/5 blur-3xl"
                    ]
                    Html.div [
                        prop.className "relative"
                        prop.children [
                            Html.h1 [
                                prop.className "text-3xl lg:text-4xl font-bold font-display text-gradient-primary"
                                prop.text "Dashboard"
                            ]
                            Html.p [
                                prop.className "mt-2 text-base-content/60 text-lg"
                                prop.text "Your personal media collection at a glance."
                            ]
                        ]
                    ]
                ]
            ]

            Html.div [
                prop.className "p-4 lg:p-6 -mt-6 relative z-10"
                prop.children [
                    // Stats grid
                    Html.div [
                        prop.className "grid grid-cols-2 lg:grid-cols-4 gap-4 mb-8 stagger-grid"
                        prop.children [
                            statCard Icons.movie "Movies" model.MovieCount "text-primary" 0
                            statCard Icons.friends "Friends" model.FriendCount "text-secondary" 100
                        ]
                    ]

                    // Recent movies section
                    if not (List.isEmpty model.RecentMovies) then
                        Html.div [
                            prop.className "animate-fade-in-up"
                            prop.children [
                                Html.div [
                                    prop.className "flex items-center justify-between mb-4"
                                    prop.children [
                                        Html.h2 [
                                            prop.className "text-lg font-bold font-display"
                                            prop.text "Recent Movies"
                                        ]
                                        Html.a [
                                            prop.href (Router.format "movies")
                                            prop.onClick (fun e ->
                                                e.preventDefault()
                                                Router.navigate "movies"
                                            )
                                            prop.className "text-sm text-primary hover:text-primary/80 transition-colors font-medium"
                                            prop.text "View all"
                                        ]
                                    ]
                                ]
                                Daisy.card [
                                    prop.className "bg-base-100 shadow-md"
                                    prop.children [
                                        Daisy.cardBody [
                                            prop.className "p-2"
                                            prop.children [
                                                for movie in model.RecentMovies do
                                                    recentMovieCard movie
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                    else if not model.IsLoading then
                        Html.div [
                            prop.className "text-center py-12 animate-fade-in"
                            prop.children [
                                Html.div [
                                    prop.className "text-base-content/15 mb-4"
                                    prop.children [ Icons.mediatheca () ]
                                ]
                                Html.p [
                                    prop.className "text-base-content/40 font-medium"
                                    prop.text "Your library is empty."
                                ]
                                Html.p [
                                    prop.className "text-base-content/30 text-sm mt-1"
                                    prop.text "Head to Movies to start building your collection."
                                ]
                            ]
                        ]
                ]
            ]
        ]
    ]
