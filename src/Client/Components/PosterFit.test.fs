/// games-q7vnd: client-side coverage for `PosterFit.decide`, the pure rule
/// that decides whether a game poster's loaded cover art is close enough to
/// the 2:3 poster frame to stay `object-fit: cover` (Steam's 600x900
/// library art) or needs `object-fit: contain` instead (RomM's off-ratio
/// NES/SNES box art, RAWG's 16:9 screenshots).
module Mediatheca.Client.Components.PosterFitTests

open Fable.Mocha
open Mediatheca.Client.Components.PosterFit

let posterFitTests =
    testList "games-q7vnd: PosterFit.decide" [

        testCase "a true 2:3 cover (Steam library art, 600x900) stays cover" <| fun () ->
            let result = decide 600.0 900.0
            Expect.equal result Fit.Cover "600x900 is exactly the 2:3 target ratio"

        testCase "a landscape SNES-like box (e.g. 1280x920, ratio ~1.39) switches to contain" <| fun () ->
            let result = decide 1280.0 920.0
            Expect.equal result Fit.Contain "a landscape cover is far outside the 2:3 tolerance band"

        testCase "a genuine portrait-but-wide NES-like box (e.g. 512x712, ratio ~0.719) switches to contain" <| fun () ->
            // Real US NES box art ratio is ~0.71-0.73 (box is ~5x7in = 0.714).
            // 512x712 = 0.7191, further from 0.667 than the 5% cover band
            // (0.633-0.700) allows -- must switch to contain, not stay cover.
            let result = decide 512.0 712.0
            Expect.equal result Fit.Contain "a genuine portrait NES box ratio (~0.719) is outside the 2:3 tolerance band"

        testCase "a square cover (e.g. 900x900) switches to contain" <| fun () ->
            let result = decide 900.0 900.0
            Expect.equal result Fit.Contain "a square cover (ratio 1.0) is far outside the 2:3 tolerance band"

        testCase "a cover just inside the named 5% tolerance stays cover" <| fun () ->
            // targetRatio = 2/3 = 0.6667; 5% above that is exactly 0.7.
            // 700x1000 = 0.7, right at the edge -- still counts as cover.
            let result = decide 700.0 1000.0
            Expect.equal result Fit.Cover "a ratio right at the edge of the named tolerance still counts as 2:3"

        testCase "a cover just outside the named 5% tolerance switches to contain" <| fun () ->
            // 701x1000 = 0.701, just past the 5% band.
            let result = decide 701.0 1000.0
            Expect.equal result Fit.Contain "a ratio just past the named tolerance no longer counts as 2:3"

        testCase "malformed (zero) dimensions fall back to cover rather than dividing by zero" <| fun () ->
            let result = decide 0.0 0.0
            Expect.equal result Fit.Cover "zero dimensions must not throw and must fall back to today's default"
    ]

Mocha.runTests posterFitTests |> ignore
