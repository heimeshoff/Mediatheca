/// books-nvnyk: renders an Audible/Audnexus-sourced (or legacy plain-text)
/// book description safely, as real paragraphs/emphasis/lists instead of
/// literal `<p>`/`<i>` tags or a trusted raw-markup blob.
///
/// This is the CLIENT half of the sanitizer belt-and-braces pair described
/// in `books-nvnyk`'s Notes: `Audible.sanitizeDescription` (server side,
/// `src/Server/Audible.fs`) already strips everything down to an allowlisted
/// HTML subset before the string ever reaches `book_metadata_cache`, but this
/// module allowlists AGAIN, independently, and never trusts the string --
/// deliberately NOT via the browser's own HTML parser or a raw-markup injection API, both because
/// the pure `parse` step below must run under the project's plain Vitest
/// `node` environment (no `jsdom` dependency available in this worktree, see
/// `vite.config.mts`'s `test.environment`) and because a hand-rolled
/// allowlisting tokenizer makes "never trust the string" mechanically
/// obvious: a disallowed tag is never even representable in `Node`.
///
/// Backward compatible with every row already in the cache (this task's own
/// acceptance criteria): a legacy plain-text row (no tags at all) renders as
/// paragraphs split on blank lines; a legacy raw, unsanitized Audnexus row
/// (real tags, still present because it predates the server-side fix) is
/// sanitized here too, so nothing needs a backfill or re-import.
module Mediatheca.Client.Components.RichText

open System
open System.Text.RegularExpressions
open Feliz

/// A parsed, already-allowlisted node. `tag` is always one of the allowed
/// tag names ("p" / "br" / "b" / "strong" / "i" / "em" / "ul" / "ol" /
/// "li") -- there is no representable "disallowed" tag, by construction.
type Node =
    | Text of string
    | Elem of tag: string * children: Node list

let private allowedTags = set [ "p"; "br"; "b"; "strong"; "i"; "em"; "ul"; "ol"; "li" ]
let private blockTags = set [ "p"; "ul"; "ol" ]

// ── Tokenizing (mirrors Audible.sanitizeDescription's tag-allowlist regex,
// but builds a token stream instead of a flat string, so nesting -- ul/li,
// b-inside-i -- survives). ─────────────────────────────────────────────────

type private Token =
    | TOpen of string
    | TClose of string
    | TText of string

let private tagPattern = Regex(@"<\s*(/?)\s*([a-zA-Z][a-zA-Z0-9]*)\b[^>]*?(/?)\s*>")
let private entityPattern = Regex(@"&(#x[0-9a-fA-F]+|#[0-9]+|[a-zA-Z][a-zA-Z0-9]*);")

/// Decodes the handful of entities a publisher/Audnexus summary realistically
/// carries, plus numeric refs. An unrecognized entity is left verbatim
/// (never crashes, never silently eats a real "&").
let private decodeEntities (s: string) : string =
    entityPattern.Replace(
        s,
        MatchEvaluator(fun m ->
            let body = m.Groups.[1].Value
            if body.StartsWith("#x") || body.StartsWith("#X") then
                match Int32.TryParse(body.Substring(2), Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture) with
                | true, code -> string (Convert.ToChar(code))
                | _ -> m.Value
            elif body.StartsWith("#") then
                match Int32.TryParse(body.Substring(1)) with
                | true, code -> string (Convert.ToChar(code))
                | _ -> m.Value
            else
                match body.ToLowerInvariant() with
                | "amp" -> "&"
                | "lt" -> "<"
                | "gt" -> ">"
                | "quot" -> "\""
                | "apos" -> "'"
                | "nbsp" -> " "
                | _ -> m.Value))

let private tokenize (html: string) : Token list =
    let tokens = ResizeArray<Token>()
    let mutable pos = 0
    for m in tagPattern.Matches(html) do
        if m.Index > pos then
            let raw = html.Substring(pos, m.Index - pos)
            if raw <> "" then tokens.Add(TText (decodeEntities raw))
        let closing = m.Groups.[1].Value = "/"
        let name = m.Groups.[2].Value.ToLowerInvariant()
        if Set.contains name allowedTags then
            if name = "br" then
                // Void element -- always a self-contained node, whichever of
                // `<br>` / `<br/>` / `<br />` / (malformed) `</br>` it was.
                tokens.Add(TOpen "br")
                tokens.Add(TClose "br")
            elif closing then
                tokens.Add(TClose name)
            else
                tokens.Add(TOpen name)
        // A disallowed tag (a/div/span/img/script/style/...) is dropped --
        // never a token -- so its own text content (already captured by the
        // surrounding TText runs) survives, unwrapped, exactly like the
        // server-side sanitizer's rule.
        pos <- m.Index + m.Length
    if pos < html.Length then
        let raw = html.Substring(pos)
        if raw <> "" then tokens.Add(TText (decodeEntities raw))
    tokens |> List.ofSeq

/// Builds a `Node` tree from the allowlisted token stream. Malformed
/// nesting (an unmatched close, or a tag never closed) degrades gracefully:
/// an unmatched close is ignored; anything still open at the end is force-
/// closed and attached where it stands, rather than dropped.
let private buildTree (tokens: Token list) : Node list =
    let root = ResizeArray<Node>()
    let mutable stack : (string * ResizeArray<Node>) list = []
    for token in tokens do
        match token with
        | TText s ->
            let target = match stack with (_, children) :: _ -> children | [] -> root
            target.Add(Text s)
        | TOpen name -> stack <- (name, ResizeArray()) :: stack
        | TClose name ->
            match stack with
            | (openName, children) :: rest when openName = name ->
                stack <- rest
                let target = match rest with (_, parentChildren) :: _ -> parentChildren | [] -> root
                target.Add(Elem(name, children |> List.ofSeq))
            | _ -> () // unmatched close -- ignore, best-effort
    let mutable remaining = stack
    while not (List.isEmpty remaining) do
        match remaining with
        | (name, children) :: rest ->
            let node = Elem(name, children |> List.ofSeq)
            let target = match rest with (_, parentChildren) :: _ -> parentChildren | [] -> root
            target.Add node
            remaining <- rest
        | [] -> ()
    root |> List.ofSeq

// ── Block grouping (turns a flat top-level node list into real paragraphs
// -- including the legacy-plain-text "split on blank lines" rule). ────────

let private splitBlankLines (s: string) : string list =
    Regex.Split(s, @"\r?\n[ \t]*\r?\n")
    |> Array.map (fun chunk -> chunk.Trim())
    |> Array.filter (fun chunk -> chunk <> "")
    |> List.ofArray

/// Wraps a run of consecutive non-block nodes (stray text and/or inline
/// elements between/around top-level `<p>`/`<ul>`/`<ol>` blocks, or the
/// entirety of a legacy plain-text description with no tags at all) into
/// one or more `<p>` blocks.
let private wrapRun (run: Node list) : Node list =
    if List.isEmpty run then
        []
    else
        let isPureText = run |> List.forall (function Text _ -> true | _ -> false)
        if isPureText then
            let combined = run |> List.map (function Text t -> t | _ -> "") |> String.concat ""
            splitBlankLines combined |> List.map (fun p -> Elem("p", [ Text p ]))
        else
            [ Elem("p", run) ]

let private isBlockTag (tag: string) = Set.contains tag blockTags

let private toBlocks (nodes: Node list) : Node list =
    let blocks = ResizeArray<Node>()
    let mutable pending : Node list = []
    for node in nodes do
        match node with
        | Elem(tag, _) when isBlockTag tag ->
            blocks.AddRange(wrapRun (List.rev pending))
            pending <- []
            blocks.Add node
        | other -> pending <- other :: pending
    blocks.AddRange(wrapRun (List.rev pending))
    blocks |> List.ofSeq

/// The pure parse/map step (no DOM, no browser API) -- string in, an
/// allowlisted `Node` tree of block-level nodes out. Exercised directly by
/// `RichText.test.fs` under plain Vitest/Node (ADR-0064).
let parse (html: string) : Node list =
    html |> tokenize |> buildTree |> toBlocks

// ── Rendering (Feliz elements, never a raw-markup injection API). ─

let rec private renderInline (node: Node) : ReactElement =
    match node with
    | Text s -> Html.text s
    | Elem("br", _) -> Html.br []
    | Elem(("b" | "strong"), children) -> Html.strong [ prop.children (children |> List.map renderInline) ]
    | Elem(("i" | "em"), children) -> Html.em [ prop.children (children |> List.map renderInline) ]
    | Elem(_, children) -> React.fragment (children |> List.map renderInline)

let private renderListItem (node: Node) : ReactElement =
    match node with
    | Elem("li", children) -> Html.li [ prop.children (children |> List.map renderInline) ]
    | other -> Html.li [ prop.children [ renderInline other ] ]

let private renderBlock (node: Node) : ReactElement =
    match node with
    | Elem("p", children) ->
        Html.p [ prop.className "text-base-content/70 leading-relaxed"; prop.children (children |> List.map renderInline) ]
    | Elem("ul", items) ->
        Html.ul [ prop.className "list-disc list-inside text-base-content/70 leading-relaxed space-y-1"; prop.children (items |> List.map renderListItem) ]
    | Elem("ol", items) ->
        Html.ol [ prop.className "list-decimal list-inside text-base-content/70 leading-relaxed space-y-1"; prop.children (items |> List.map renderListItem) ]
    | other ->
        Html.p [ prop.className "text-base-content/70 leading-relaxed"; prop.children [ renderInline other ] ]

/// Renders a description string as safe, formatted rich text. Never
/// a raw-markup injection API -- every element is a Feliz
/// element built node-by-node from the allowlisted `parse` tree.
let render (html: string) : ReactElement =
    Html.div [
        prop.className "space-y-3"
        prop.children (parse html |> List.map renderBlock)
    ]
