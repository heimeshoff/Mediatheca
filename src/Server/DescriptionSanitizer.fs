namespace Mediatheca.Server

/// games-r1tx4: the ONE shared HTML-subset sanitizer for every
/// externally-sourced description this codebase stores in a cache-tier
/// column (ADR-0043/ADR-0045) -- Audible/Audnexus book summaries, Steam
/// `about_the_game`/`detailed_description`, and RAWG's `description`.
/// Lifted out of `Audible.fs` (books-nvnyk introduced it there first) so
/// Steam.fs/Rawg.fs can share the exact same implementation instead of
/// each keeping their own copy or a divergent one. Compiled ahead of
/// Audible.fs/Steam.fs/Rawg.fs in `Server.fsproj` so every one of them can
/// call it directly (same `Mediatheca.Server` namespace, no `open` needed
/// -- see the project CLAUDE.md's "sibling modules" gotcha).
///
/// Tags kept verbatim (attributes always dropped): p/br/b/strong/i/em/
/// ul/ol/li; every other tag unwraps to its own text content -- never
/// deleted, so a disallowed element's text (including `<script>`/`<style>`
/// bodies) survives as plain text rather than vanishing. Entities are left
/// alone; the client-side `RichText` renderer decodes them.
module DescriptionSanitizer =

    let private allowedTags =
        set [ "p"; "br"; "b"; "strong"; "i"; "em"; "ul"; "ol"; "li" ]

    let private tagPattern =
        System.Text.RegularExpressions.Regex(@"<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)\b[^>]*?(/?)\s*>")

    let sanitize (s: string) : string =
        tagPattern.Replace(
            s,
            System.Text.RegularExpressions.MatchEvaluator(fun m ->
                let closing = m.Groups.[1].Value = "/"
                let name = m.Groups.[2].Value.ToLowerInvariant()
                if Set.contains name allowedTags then
                    if name = "br" then "<br>"
                    elif closing then sprintf "</%s>" name
                    else sprintf "<%s>" name
                else ""))
        |> fun s -> s.Trim()

    /// games-r1tx4 (verifier iteration 2): projects an already-sanitized
    /// HTML-subset string (i.e. `sanitize`'s own output -- see `sanitize`
    /// above) down to plain text, for creation-path callers that must keep
    /// an event payload's `Description` field free of markup (ADR-0043: an
    /// event never carries HTML) even though the cache-tier identity card
    /// written on the same path keeps the sanitized HTML. `<br>` and the
    /// closing tags of block-level elements (`</p>`, `</li>`) become a
    /// single newline; every other allowed tag (`<p>`, `<strong>`, `<ul>`,
    /// etc.) is dropped without inserting whitespace. Runs of newlines
    /// collapse to one, and the result is trimmed.
    let private blockBreakPattern =
        System.Text.RegularExpressions.Regex(@"<br>|</p>|</li>")

    let private anyTagPattern =
        System.Text.RegularExpressions.Regex(@"<[^>]+>")

    let private repeatedNewlinesPattern =
        System.Text.RegularExpressions.Regex(@"\n{2,}")

    let toPlainText (s: string) : string =
        s
        |> fun s -> blockBreakPattern.Replace(s, "\n")
        |> fun s -> anyTagPattern.Replace(s, "")
        |> fun s -> repeatedNewlinesPattern.Replace(s, "\n")
        |> fun s -> s.Trim()
