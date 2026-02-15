module Mediatheca.Client.Pages.Dashboard.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Client.Pages.Dashboard.Types
open Mediatheca.Client
open Mediatheca.Client.Components

let private statCard (icon: unit -> ReactElement) (label: string) (value: string) (color: string) (delay: int) =
    Html.div [
        prop.className $"{DesignSystem.statGlow} bg-base-100 rounded-2xl p-6 shadow-md card-hover"
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
                                prop.text value
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

let private formatWatchTime (minutes: int) =
    if minutes = 0 then "0h"
    elif minutes < 60 then $"{minutes}m"
    else
        let hours = minutes / 60
        let mins = minutes % 60
        if mins = 0 then $"{hours}h"
        else $"{hours}h {mins}m"

let private recentSeriesCard (series: Mediatheca.Shared.RecentSeriesItem) =
    Html.a [
        prop.href (Router.format ("series", series.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", series.Slug)
        )
        prop.className "flex items-center gap-3 p-3 rounded-xl hover:bg-base-300/50 transition-colors cursor-pointer group"
        prop.children [
            PosterCard.thumbnail series.PosterRef series.Name
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "font-semibold text-sm truncate group-hover:text-primary transition-colors"
                        prop.text series.Name
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/50"
                        prop.children [
                            Html.span [ prop.text (string series.Year) ]
                            Html.span [
                                prop.text $" \u00B7 {series.WatchedEpisodeCount}/{series.EpisodeCount} eps"
                            ]
                        ]
                    ]
                    match series.NextUp with
                    | Some nextUp ->
                        Html.p [
                            prop.className "text-xs text-primary/70 mt-0.5"
                            prop.text $"Next: S{nextUp.SeasonNumber}E{nextUp.EpisodeNumber} {nextUp.EpisodeName}"
                        ]
                    | None -> ()
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
            PosterCard.thumbnail movie.PosterRef movie.Name
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

let private activityItem (item: Mediatheca.Shared.RecentActivityItem) =
    Html.div [
        prop.className "flex items-center gap-3 py-2"
        prop.children [
            Html.div [
                prop.className "w-2 h-2 rounded-full bg-primary/50 flex-none"
            ]
            Html.div [
                prop.className "flex-1 min-w-0"
                prop.children [
                    Html.p [
                        prop.className "text-sm truncate"
                        prop.text item.Description
                    ]
                    Html.p [
                        prop.className "text-xs text-base-content/40"
                        prop.text (
                            try
                                let dt = System.DateTimeOffset.Parse(item.Timestamp)
                                dt.LocalDateTime.ToString("MMM d, HH:mm")
                            with _ -> item.Timestamp
                        )
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (_dispatch: Msg -> unit) =
    let stats = model.Stats |> Option.defaultValue { MovieCount = 0; SeriesCount = 0; GameCount = 0; FriendCount = 0; CatalogCount = 0; WatchSessionCount = 0; TotalWatchTimeMinutes = 0; SeriesWatchTimeMinutes = 0; TotalPlayTimeMinutes = 0 }
    Html.div [
        prop.className DesignSystem.animateFadeIn
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
                prop.className (DesignSystem.pagePadding + " -mt-6 relative z-10")
                prop.children [
                    // Stats grid
                    Html.div [
                        prop.className ("grid grid-cols-2 lg:grid-cols-5 gap-4 mb-8 " + DesignSystem.staggerGrid)
                        prop.children [
                            statCard Icons.movie "Movies" (string stats.MovieCount) "text-primary" 0
                            statCard Icons.tv "Series" (string stats.SeriesCount) "text-warning" 100
                            statCard Icons.friends "Friends" (string stats.FriendCount) "text-secondary" 200
                            statCard Icons.catalog "Catalogs" (string stats.CatalogCount) "text-accent" 300
                            statCard Icons.events "Watch Time" (formatWatchTime (stats.TotalWatchTimeMinutes + stats.SeriesWatchTimeMinutes)) "text-info" 400
                        ]
                    ]

                    Html.div [
                        prop.className "grid grid-cols-1 lg:grid-cols-2 gap-6"
                        prop.children [
                            // Recent movies section
                            Html.div [
                                prop.className DesignSystem.animateFadeInUp
                                prop.children [
                                    if not (List.isEmpty model.RecentMovies) then
                                        Html.div [
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
                                            prop.className ("text-center py-12 " + DesignSystem.animateFadeIn)
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

                            // Recent series section
                            Html.div [
                                prop.className DesignSystem.animateFadeInUp
                                prop.children [
                                    if not (List.isEmpty model.RecentSeries) then
                                        Html.div [
                                            prop.children [
                                                Html.div [
                                                    prop.className "flex items-center justify-between mb-4"
                                                    prop.children [
                                                        Html.h2 [
                                                            prop.className "text-lg font-bold font-display"
                                                            prop.text "Recent Series"
                                                        ]
                                                        Html.a [
                                                            prop.href (Router.format "series")
                                                            prop.onClick (fun e ->
                                                                e.preventDefault()
                                                                Router.navigate "series"
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
                                                                for s in model.RecentSeries do
                                                                    recentSeriesCard s
                                                            ]
                                                        ]
                                                    ]
                                                ]
                                            ]
                                        ]
                                ]
                            ]
                        ]
                    ]

                    // Continue Watching section (series with next-up episodes)
                    let continueWatching = model.RecentSeries |> List.filter (fun s -> s.NextUp.IsSome && s.WatchedEpisodeCount > 0)
                    if not (List.isEmpty continueWatching) then
                        Html.div [
                            prop.className ("mt-6 " + DesignSystem.animateFadeInUp)
                            prop.children [
                                Html.h2 [
                                    prop.className "text-lg font-bold font-display mb-4"
                                    prop.text "Continue Watching"
                                ]
                                Daisy.card [
                                    prop.className "bg-base-100 shadow-md"
                                    prop.children [
                                        Daisy.cardBody [
                                            prop.className "p-2"
                                            prop.children [
                                                for s in continueWatching do
                                                    recentSeriesCard s
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]

                    // Recent activity section
                    if not (List.isEmpty model.RecentActivity) then
                        Html.div [
                            prop.className ("mt-6 " + DesignSystem.animateFadeInUp)
                            prop.children [
                                Html.div [
                                    prop.className "flex items-center justify-between mb-4"
                                    prop.children [
                                        Html.h2 [
                                            prop.className "text-lg font-bold font-display"
                                            prop.text "Recent Activity"
                                        ]
                                        Html.a [
                                            prop.href (Router.format "events")
                                            prop.onClick (fun e ->
                                                e.preventDefault()
                                                Router.navigate "events"
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
                                            prop.className "p-4"
                                            prop.children [
                                                for item in model.RecentActivity |> List.truncate 8 do
                                                    activityItem item
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                        ]
                ]
            ]
        ]
    ]
