/// design-system-k4tw8: the filmstrip poster box's hover-scale must go
/// through the *same* CSS mechanism the other dashboard poster cards use
/// (`.poster-card:hover .poster-image-container`'s `transform: scale(1.05)`)
/// rather than a second, separately-declared Tailwind `group-hover:scale-*`
/// utility (which Tailwind 4 compiles to the standalone CSS `scale`
/// property, not `transform` -- a second, textually-separate hover-zoom
/// code path). Rendered to static markup (no DOM needed, matching this
/// suite's `environment: "node"` Vitest config) so the assertion is on the
/// actual class list `filmstripRow` emits, not a hand-copied string.
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

let designSystemTests =
    testList "DesignSystem.filmstripRow poster hover mechanism" [

        testCase "the poster box carries filmstrip-poster and not the old Tailwind hover-scale utilities" <| fun () ->
            let html = renderToStaticMarkup (filmstripRow [ item ])
            Expect.stringContains html "filmstrip-poster" "the poster box shares the .filmstrip-poster hover-scale rule"
            Expect.isFalse (html.Contains "group-hover:scale") "the separate Tailwind group-hover:scale-* utility (compiles to the standalone `scale` property) must be gone"
            Expect.isFalse (html.Contains "transition-transform") "the separate Tailwind transition-transform utility must be gone -- the transition now lives on .filmstrip-poster in index.css"
    ]

Mocha.runTests designSystemTests |> ignore
