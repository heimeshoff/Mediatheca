/// Grow / shared-element FLIP motion primitive (ADR-0073). Design-system owns
/// this vocabulary; a consuming BC (e.g. intelligence's expandable dashboard
/// cards) decides *where* it fires and wires the snapshot-on-click, the
/// container ref, and the effect that plays the plan.
///
/// Split, per ADR-0073 §9: `Flip.plan` is the pure arithmetic core — every
/// judgement (intersection of the two key sets, the sub-pixel threshold, the
/// fade-vs-stretch rule) lives there and is unit-tested (`Motion.test.fs`).
/// `Flip.snapshot` / `Flip.play` / `growSurface` are the DOM shell — they
/// touch `getBoundingClientRect`, WAAPI `Element.animate`, and `matchMedia`,
/// and are deliberately untested (same division as
/// `LocalCopyRemovalDialog`'s pure phase machine over an untested async
/// shell).
module Mediatheca.Client.Motion

open Fable.Core.JsInterop
open Feliz
open Browser.Types

/// A viewport-coordinate box — a plain `getBoundingClientRect()` snapshot, no
/// scroll offset added. The caller settles any scroll *before* taking the
/// after-snapshot (ADR-0073 §1), so the travel that plays is exactly the
/// on-screen journey, scroll shift included.
type Box = {
    Left: float
    Top: float
    Width: float
    Height: float
}

/// One key's FLIP travel: translate by (Dx, Dy) from its old on-screen
/// position to its new one. `FadeIn` is true when the box's measured size
/// changed between the two snapshots (e.g. a list row re-flowing into a grid
/// tile) — translate alone would visibly stretch such content, so the caller
/// layers an opacity fade on top of the travel instead of scaling.
type FlipMove = {
    Key: string
    Dx: float
    Dy: float
    FadeIn: bool
}

/// `--duration-grow` mirrored from index.css. WAAPI's `Element.animate` takes
/// a duration in milliseconds and cannot read a CSS custom property without a
/// `getComputedStyle` round trip, so the value is duplicated here by hand.
/// Keep this in sync with index.css's `--duration-grow` (0.5s = 500ms).
let growDurationMs = 500.0

/// `--ease-grow` mirrored from index.css, see `growDurationMs`'s comment.
let growEasing = "cubic-bezier(0.16, 1, 0.3, 1)"

/// Moves under this threshold (in both axes) are treated as "didn't move" —
/// sub-pixel layout noise, not a real travel worth animating.
let private subPixelThreshold = 0.5

/// The DOM attribute `Flip.snapshot` reads a key from; `flipKey` is the only
/// place that writes it, so the two cannot drift.
let private flipKeyAttr = "data-flip-key"

/// True when the user's OS/browser requests reduced motion. `Flip.play` and
/// `growSurface` short-circuit to no-ops in that case — the freeze-at-the-
/// final-state stance from `.gold-sweep`/`.in-focus-frame`
/// (design-system-bky6v) reached here by construction, not a `@media`
/// override. Untested — touches `matchMedia`; see the module doc comment.
let prefersReducedMotion () : bool =
    emitJsExpr () "(typeof window !== 'undefined' && window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches)"

module Flip =

    /// Pure core: for every key present in BOTH maps (a key present in only
    /// one — appeared or disappeared between snapshots — produces no move),
    /// compute the translate that would carry `before`'s box onto `after`'s
    /// box, drop it if both axes are under the sub-pixel threshold, and flag
    /// `FadeIn` when the box's width or height changed. The inversion
    /// convention: `Dx`/`Dy` is `before - after`, so a caller starts the
    /// element at `translate(Dx, Dy)` (visually where it used to be) and
    /// animates to `translate(0)` / `none` (its real, new position).
    let plan (before: Map<string, Box>) (after: Map<string, Box>) : FlipMove list =
        before
        |> Map.toList
        |> List.choose (fun (key, beforeBox) ->
            match Map.tryFind key after with
            | None -> None
            | Some afterBox ->
                let dx = beforeBox.Left - afterBox.Left
                let dy = beforeBox.Top - afterBox.Top
                let sizeChanged =
                    abs (beforeBox.Width - afterBox.Width) >= subPixelThreshold
                    || abs (beforeBox.Height - afterBox.Height) >= subPixelThreshold
                // A size-only change (no position change) still needs to fade in, so
                // only the position component is subject to the sub-pixel threshold —
                // a box that merely resized in place is not "didn't move".
                if abs dx < subPixelThreshold && abs dy < subPixelThreshold && not sizeChanged then
                    None
                else
                    Some { Key = key; Dx = dx; Dy = dy; FadeIn = sizeChanged })

    /// DOM shell: read every `[data-flip-key]` descendant of `root` in
    /// viewport coordinates. Untested — see the module doc comment.
    let snapshot (root: Element) : Map<string, Box> =
        let nodes: Element[] = emitJsExpr root "Array.from($0.querySelectorAll('[data-flip-key]'))"
        nodes
        |> Array.map (fun el ->
            let key: string = emitJsExpr el "$0.getAttribute('data-flip-key')"
            let rect: {| left: float; top: float; width: float; height: float |} =
                emitJsExpr el "$0.getBoundingClientRect()"
            key, { Left = rect.left; Top = rect.top; Width = rect.width; Height = rect.height })
        |> Map.ofArray

    /// DOM shell: invert each planned move on its matching
    /// `[data-flip-key="…"]` descendant of `root` via WAAPI `Element.animate`
    /// — translate-only (`translate(dx,dy) -> none`), with an opacity
    /// `0 -> 1` fade layered on top for moves whose box size changed
    /// (`FadeIn`), over `growDurationMs`/`growEasing`, `fill: "none"` so the
    /// element snaps back to its untransformed state with no cleanup step and
    /// no inline-style residue.
    ///
    /// Returns the live animation handles (there is no typed WAAPI
    /// `Animation` binding in this codebase's Fable.Browser.Dom, so they are
    /// carried as `obj`) — callers keep them in a ref and `cancel()` them
    /// (via `Motion.cancel`) on re-entry, so spam-clicking expand/collapse
    /// cannot leave a stray transform behind. No-ops (returns `[]`) under
    /// `prefersReducedMotion` — items are simply already at their final
    /// positions. Untested — see the module doc comment.
    let play (root: Element) (moves: FlipMove list) : obj list =
        if prefersReducedMotion () then
            []
        else
            moves
            |> List.choose (fun move ->
                let el: Element = emitJsExpr (root, move.Key) "$0.querySelector('[data-flip-key=\"' + $1 + '\"]')"
                if isNull el then
                    None
                else
                    let anim: obj =
                        emitJsExpr (el, move.Dx, move.Dy, move.FadeIn, growDurationMs, growEasing)
                            "$0.animate($3 ? [{ transform: 'translate(' + $1 + 'px, ' + $2 + 'px)', opacity: 0 }, { transform: 'none', opacity: 1 }] : [{ transform: 'translate(' + $1 + 'px, ' + $2 + 'px)' }, { transform: 'none' }], { duration: $4, easing: $5, fill: 'none' })"
                    Some anim)

/// `Motion.flipKey key` emits both the React `key` (`prop.key`) and the DOM
/// `data-flip-key` attribute `Flip.snapshot`/`Flip.play` read, from one
/// value, so the React key and the FLIP key cannot drift apart
/// (ADR-0073 §7). Pure — a plain property list, no DOM touched.
let flipKey (key: string) : IReactProperty list =
    [ prop.key key
      prop.custom (flipKeyAttr, key) ]

/// DOM shell: animate a surface's measured `height` from `fromHeightPx` to
/// `toHeightPx` with `overflow: hidden` — the card-box half of the grow (item
/// travel is `Flip.play`'s job; this only grows the container). Returns the
/// live animation handle, or `None` under `prefersReducedMotion`. Untested —
/// see the module doc comment.
let growSurface (el: Element) (fromHeightPx: float) (toHeightPx: float) : obj option =
    if prefersReducedMotion () then
        None
    else
        let anim: obj =
            emitJsExpr (el, fromHeightPx, toHeightPx, growDurationMs, growEasing)
                "$0.animate([{ height: $1 + 'px', overflow: 'hidden' }, { height: $2 + 'px', overflow: 'hidden' }], { duration: $3, easing: $4, fill: 'none' })"
        Some anim

/// Cancel a live animation handle returned by `Flip.play`/`growSurface`.
/// Safe to call on any value, including `null`/`undefined`. Untested — see
/// the module doc comment.
let cancel (anim: obj) : unit =
    emitJsExpr anim "$0 && $0.cancel && $0.cancel()" |> ignore
