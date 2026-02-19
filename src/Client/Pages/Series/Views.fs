module Mediatheca.Client.Pages.Series.Views

open Feliz
open Feliz.DaisyUI
open Feliz.Router
open Mediatheca.Shared
open Mediatheca.Client.Pages.Series.Types
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

let private seriesCard (series: SeriesListItem) =
    let progressText = $"{series.WatchedEpisodeCount}/{series.EpisodeCount} episodes"
    let isFinished = series.EpisodeCount > 0 && series.WatchedEpisodeCount >= series.EpisodeCount
    Html.a [
        prop.href (Router.format ("series", series.Slug))
        prop.onClick (fun e ->
            e.preventDefault()
            Router.navigate ("series", series.Slug))
        prop.children [
            Html.div [
                prop.className (DesignSystem.posterCard + " group relative cursor-pointer w-full")
                prop.children [
                    Html.div [
                        prop.className DesignSystem.posterImageContainer
                        prop.children [
                            match series.PosterRef with
                            | Some ref ->
                                Html.img [
                                    prop.src $"/images/{ref}"
                                    prop.alt series.Name
                                    prop.className DesignSystem.posterImage
                                ]
                            | None ->
                                Html.div [
                                    prop.className "flex flex-col items-center justify-center w-full h-full text-base-content/20 px-3 gap-2"
                                    prop.children [
                                        Icons.tv ()
                                        Html.p [
                                            prop.className "text-xs text-base-content/40 font-medium text-center line-clamp-2"
                                            prop.text series.Name
                                        ]
                                    ]
                                ]

                            // In Focus badge
                            if series.InFocus then inFocusBadge

                            // Shine effect
                            Html.div [ prop.className DesignSystem.posterShine ]
                        ]
                    ]
                    // Info below the poster
                    Html.div [
                        prop.className "flex items-baseline justify-between mt-2 px-1"
                        prop.children [
                            Html.p [
                                prop.className "text-xs text-base-content/50"
                                prop.text progressText
                            ]
                            if series.IsAbandoned then
                                Html.p [
                                    prop.className "text-xs text-error font-medium"
                                    prop.text "Abandoned"
                                ]
                            elif isFinished then
                                Html.p [
                                    prop.className "text-xs text-success font-medium"
                                    prop.text "Finished"
                                ]
                            else
                                match series.NextUp with
                                | Some n ->
                                    Html.p [
                                        prop.className "text-xs text-primary font-medium"
                                        prop.text $"Next S{n.SeasonNumber}E{n.EpisodeNumber}"
                                    ]
                                | None -> ()
                        ]
                    ]
                ]
            ]
        ]
    ]

let view (model: Model) (dispatch: Msg -> unit) =
    Html.div [
        prop.className (DesignSystem.pageContainer + " " + DesignSystem.animateFadeIn)
        prop.children [
            Html.div [
                prop.className "flex items-center justify-between mb-6"
                prop.children [
                    Html.h1 [
                        prop.className "text-2xl font-bold font-display text-gradient-primary"
                        prop.text "TV Series"
                    ]
                    Daisy.button.button [
                        button.primary
                        prop.className "gap-2"
                        prop.onClick (fun _ -> dispatch Open_tmdb_search)
                        prop.children [
                            Html.span [ prop.text "+" ]
                            Html.span [ prop.text "Add Series" ]
                        ]
                    ]
                ]
            ]
            // Search bar
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
                                prop.placeholder "Search series..."
                                prop.value model.SearchQuery
                                prop.onChange (Search_changed >> dispatch)
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
                    model.Series
                    |> List.filter (fun s ->
                        model.SearchQuery = "" ||
                        s.Name.ToLowerInvariant().Contains(model.SearchQuery.ToLowerInvariant())
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
                                                svg.d "M6 20.25h12m-7.5-3v3m3-3v3m-10.125-3h17.25c.621 0 1.125-.504 1.125-1.125V4.875c0-.621-.504-1.125-1.125-1.125H2.625c-.621 0-1.125.504-1.125 1.125v11.25c0 .621.504 1.125 1.125 1.125Z"
                                            ]
                                        ]
                                    ]
                                ]
                            ]
                            Html.p [
                                prop.className "text-base-content/50 font-medium"
                                prop.text "No series found."
                            ]
                            Html.p [
                                prop.className "mt-2 text-base-content/30 text-sm"
                                prop.text "Add a TV series from TMDB to get started."
                            ]
                        ]
                    ]
                else
                    Html.div [
                        prop.className ("grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4 " + DesignSystem.animateFadeIn)
                        prop.children [
                            for series in filtered do
                                seriesCard series
                        ]
                    ]
        ]
    ]
