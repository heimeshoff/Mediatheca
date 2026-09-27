module Mediatheca.Client.Components.PosterFit

open Browser.Types

/// games-q7vnd: the poster frame's authored aspect ratio
/// (`.poster-image-container`'s `aspect-ratio: 2/3` in index.css), as
/// width/height.
let targetRatio = 2.0 / 3.0

/// How far a cover's natural aspect ratio may stray from `targetRatio` and
/// still count as "close enough to 2:3" -- kept `object-fit: cover`. Named
/// tolerance per the task brief ("within about 10% of 0.667 counts as
/// 2:3", worker-picked exact value): a 10% band (0.600-0.733) is too wide
/// -- it swallows real NES box art (~0.71-0.73, US NES box ratio ~5x7in =
/// 0.714), which must switch to `Contain`. 5% narrows the cover band to
/// 0.633-0.700: wide enough to absorb Steam library art's minor crop
/// variance (600x900 = 0.667 sits comfortably inside), narrow enough that
/// a landscape SNES box (~1.4), a genuine portrait NES box (~0.71-0.73),
/// and a square RomM thumbnail (1.0) all land outside it and get `Contain`.
let tolerance = 0.05

/// The two CSS fits a poster image can render with.
[<RequireQualifiedAccess>]
type Fit =
    | Cover
    | Contain

/// Pure decision: given an image's decoded natural pixel size, is it close
/// enough to the 2:3 poster frame to crop-fill it (`Cover`, today's
/// default), or does it need to show in full, letterboxed (`Contain`)?
/// Malformed dimensions (zero or negative -- an image that hasn't decoded)
/// fall back to `Cover` rather than divide by zero.
let decide (naturalWidth: float) (naturalHeight: float) : Fit =
    if naturalWidth <= 0.0 || naturalHeight <= 0.0 then
        Fit.Cover
    else
        let ratio = naturalWidth / naturalHeight
        let low = targetRatio * (1.0 - tolerance)
        let high = targetRatio * (1.0 + tolerance)
        if ratio >= low && ratio <= high then Fit.Cover else Fit.Contain

/// The shared whole-cover modifier (books-r8cfn's `.poster-image--contain`,
/// `DesignSystem.posterImageContain`) -- kept as a bare string here so this
/// module never needs to depend on `DesignSystem` just to name its own
/// output class.
let private containClass = "poster-image--contain"

/// Wires the decision to a real `<img>`: reads its decoded natural size on
/// `load` and, only when it is clearly off the 2:3 frame, adds the shared
/// `poster-image--contain` modifier directly on the DOM node. Direct DOM
/// mutation rather than React/Elmish state, because the decision only
/// needs to run once per image and never needs to re-render the rest of
/// the card. Movie/series art and Steam's near-2:3 covers add nothing and
/// stay on `DesignSystem.posterImage`'s default `cover`.
let onImageLoad (ev: Event) : unit =
    let img: HTMLImageElement = unbox ev.target
    match decide (float img.naturalWidth) (float img.naturalHeight) with
    | Fit.Contain ->
        if not (img.className.Contains containClass) then
            img.className <- img.className + " " + containClass
    | Fit.Cover ->
        if img.className.Contains containClass then
            img.className <-
                img.className.Replace(" " + containClass, "").Replace(containClass, "").Trim()
