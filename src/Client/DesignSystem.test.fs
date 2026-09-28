/// design-system-k4tw8 / design-system-v3qh6: every poster/hero-card hover
/// surface must go through the *same* CSS mechanism the reference poster
/// cards use (`.poster-card:hover .poster-image-container` /
/// `.group:hover .poster-hover-scale`'s shared `transform: scale(1.05)`)
/// rather than a second, separately-declared Tailwind `group-hover:scale-*`
/// utility (which Tailwind 4 compiles to the standalone CSS `scale`
/// property, not `transform` -- a second, textually-separate hover-zoom
/// code path). Rendered to static markup (no DOM needed, matching this
/// suite's `environment: "node"` Vitest config) so the assertions are on
/// the actual class lists `filmstripRow` / `nextEpisodeHeroCard` emit, not
/// hand-copied strings.
module Mediatheca.Client.DesignSystemTests

open Fable.Core.JsInterop
open Fable.Mocha
open Mediatheca.Client.DesignSystem

let private renderToStaticMarkup : Feliz.ReactElement -> string =
    import "renderToStaticMarkup" "react-dom/server"

let private item : FilmstripItem = {
    Key = "alien"
    PosterRef = None
    Title = "Alien"
    Meta = "1h57"
    Href = None
    OnNavigate = None
    InFocusBadge = None
    JellyfinButton = None
}

let private noProgress : SeriesProgressProps = {
    SeasonsTouched = []
    ActiveSeasonIndex = None
    CurrentSeasonWatched = []
    CurrentSeasonNextUpIndex = None
    IsComplete = false
}

let private heroCardProps : NextEpisodeHeroCardProps = {
    SeriesName = "Severance"
    EpisodeLabel = None
    BackdropRef = None
    PosterRef = None
    Progress = noProgress
    WatchedWith = []
    JellyfinButton = None
}

let designSystemTests =
    testList "DesignSystem poster/hero-card hover mechanism" [

        testCase "filmstripRow's poster box carries poster-hover-scale and not the old Tailwind hover-scale utilities" <| fun () ->
            let html = renderToStaticMarkup (filmstripRow [ item ])
            Expect.stringContains html "poster-hover-scale" "the poster box shares the .poster-hover-scale hover-scale rule"
            Expect.isFalse (html.Contains "group-hover:scale") "the separate Tailwind group-hover:scale-* utility (compiles to the standalone `scale` property) must be gone"
            Expect.isFalse (html.Contains "transition-transform") "the separate Tailwind transition-transform utility must be gone -- the transition now lives on .poster-hover-scale in index.css"

        testCase "nextEpisodeHeroCard's root carries poster-hover-scale and not the old Tailwind hover-scale utilities" <| fun () ->
            let html = renderToStaticMarkup (nextEpisodeHeroCard heroCardProps)
            Expect.stringContains html "poster-hover-scale" "the hero card shares the .poster-hover-scale hover-scale rule"
            Expect.isFalse (html.Contains "group-hover:scale") "the separate Tailwind group-hover:scale-* utility (compiles to the standalone `scale` property) must be gone"
            Expect.isFalse (html.Contains "transition-transform") "the separate Tailwind transition-transform utility must be gone -- the transition now lives on .poster-hover-scale in index.css"
    ]

Mocha.runTests designSystemTests |> ignore
