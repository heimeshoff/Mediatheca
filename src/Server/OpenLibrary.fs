namespace Mediatheca.Server

open System.Net.Http
open Thoth.Json.Net

/// Open Library — the anticorruption layer for book search, ISBN/work
/// lookup, and cover download (ADR-0075). Mirrors `Tmdb.fs`'s structure
/// (config record, `fetchJson`, `Decoder<_>`s, `SearchCache`) and
/// `Steam.fs`'s ~lines 240-288 adapter-owned throttle shape (ADR-0066) —
/// copied, not shared, per this task's own instructions. This module is
/// compiled BEFORE `ImageStore.fs` in `Server.fsproj` (the adapter block),
/// so `downloadCover` below writes bytes directly with `System.IO`, the
/// same choice every earlier adapter in that block already makes
/// (`Tmdb.downloadImage`, `Steam.downloadSteamCover`) rather than depending
/// on a module that compiles later.
module OpenLibrary =

    type OpenLibraryConfig = {
        UserAgent: string
    }

    // ── Throttle (ADR-0066's shape, copied from Steam.fs, not shared) ──
    //
    // Two independent gates: `openlibrary.org` (search/work/isbn — default
    // 1000ms, Open Library's 1 req/s anonymous / 3 req/s identified rate
    // policy) and `covers.openlibrary.org` (default 3000ms — the covers
    // host's separate 100 requests / 5 minutes by ISBN/OLID key ceiling).
    // Both intervals are `mutable`, not `private`, so tests can drive them
    // fast — the same public-for-testability precedent
    // `Steam.throttleStorefrontInterval` sets. Production callers never
    // touch them.
    let mutable throttleApiInterval = System.TimeSpan.FromMilliseconds(1000.0)
    let mutable throttleCoversInterval = System.TimeSpan.FromMilliseconds(3000.0)

    let private apiGate = new System.Threading.SemaphoreSlim(1, 1)
    let mutable private lastApiCallStartedAt: System.DateTime option = None

    /// Runs `fetch` under the `openlibrary.org` throttle — serializes with
    /// every other gated API call (a single `SemaphoreSlim` held for the
    /// gate's full duration) and, if fewer than `throttleApiInterval` has
    /// elapsed since the previous gated call *started*, waits out the
    /// remainder first. Exposed so tests can exercise the gate directly.
    let throttleApiCall (fetch: unit -> Async<'a>) : Async<'a> =
        async {
            do! apiGate.WaitAsync() |> Async.AwaitTask
            try
                let now = System.DateTime.UtcNow
                match lastApiCallStartedAt with
                | Some last ->
                    let remaining = throttleApiInterval - (now - last)
                    if remaining > System.TimeSpan.Zero then
                        do! Async.Sleep remaining
                | None -> ()
                lastApiCallStartedAt <- Some System.DateTime.UtcNow
                return! fetch ()
            finally
                apiGate.Release() |> ignore
        }

    let private coversGate = new System.Threading.SemaphoreSlim(1, 1)
    let mutable private lastCoversCallStartedAt: System.DateTime option = None

    /// Runs `fetch` under the independent `covers.openlibrary.org` throttle
    /// — never waits on, or is waited on by, `throttleApiCall`.
    let throttleCoverCall (fetch: unit -> Async<'a>) : Async<'a> =
        async {
            do! coversGate.WaitAsync() |> Async.AwaitTask
            try
                let now = System.DateTime.UtcNow
                match lastCoversCallStartedAt with
                | Some last ->
                    let remaining = throttleCoversInterval - (now - last)
                    if remaining > System.TimeSpan.Zero then
                        do! Async.Sleep remaining
                | None -> ()
                lastCoversCallStartedAt <- Some System.DateTime.UtcNow
                return! fetch ()
            finally
                coversGate.Release() |> ignore
        }

    // ── HTTP helpers ─────────────────────────────────────────────────────

    /// Every outbound request — search, work, isbn, author, cover — carries
    /// the configured `User-Agent` (Open Library's identified-client
    /// policy, ADR-0075).
    let private fetchJson (httpClient: HttpClient) (config: OpenLibraryConfig) (url: string) : Async<string> =
        async {
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            request.Headers.Add("User-Agent", config.UserAgent)
            let! response = httpClient.SendAsync(request) |> Async.AwaitTask
            response.EnsureSuccessStatusCode() |> ignore
            let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
            return body
        }

    /// Same as `fetchJson` but returns `None` on a 404 instead of throwing —
    /// `getEditionByIsbn`'s "no edition for this ISBN" case, never an
    /// exception (this task's own acceptance criterion).
    let private fetchJsonOptional (httpClient: HttpClient) (config: OpenLibraryConfig) (url: string) : Async<string option> =
        async {
            use request = new HttpRequestMessage(HttpMethod.Get, url)
            request.Headers.Add("User-Agent", config.UserAgent)
            let! response = httpClient.SendAsync(request) |> Async.AwaitTask
            if response.StatusCode = System.Net.HttpStatusCode.NotFound then
                return None
            else
                response.EnsureSuccessStatusCode() |> ignore
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                return Some body
        }

    // ── Search ───────────────────────────────────────────────────────────

    /// books-xntts: `search.json`'s `edition_key` lists every edition of the
    /// work in no meaningful order (e.g. 253 entries for a well-known work,
    /// the first being a decades-old foreign-language printing) — picking
    /// `List.tryHead` blind produced a Spanish title/cover on an English
    /// search. `cover_edition_key` is the edition Open Library itself
    /// already picked as canonical (it carries the same `cover_i` the
    /// search tile renders), so it is always preferred when present.
    ///
    /// When absent, the fallback is still `List.tryHead editionKeys` in
    /// every case — `language` is inspected only to document why: when the
    /// doc's `language` list is absent or contains `eng`, the blind pick is
    /// no worse than any other guess; when `language` is present and lacks
    /// `eng`, the work simply has no English edition at all, so there is no
    /// better edition to vouch for either — today's blind pick is the
    /// documented ceiling, not a regression this task is closing. A future
    /// preferred-language setting (Administration) is the one place to make
    /// this pickier.
    let private preferredEditionKey (coverEditionKey: string option) (editionKeys: string list) (language: string list option) : string option =
        match coverEditionKey with
        | Some key -> Some key
        | None ->
            match language with
            | Some langs when not (List.contains "eng" langs) -> editionKeys |> List.tryHead
            | _ -> editionKeys |> List.tryHead

    let private decodeSearchDoc : Decoder<Mediatheca.Shared.OpenLibrarySearchResult> =
        Decode.object (fun get ->
            let coverId = get.Optional.Field "cover_i" Decode.int
            let coverEditionKey = get.Optional.Field "cover_edition_key" Decode.string
            let language = get.Optional.Field "language" (Decode.list Decode.string)
            let editionKeys = get.Optional.Field "edition_key" (Decode.list Decode.string) |> Option.defaultValue []
            { Mediatheca.Shared.OpenLibrarySearchResult.WorkKey = get.Required.Field "key" Decode.string
              Title = get.Required.Field "title" Decode.string
              Authors = get.Optional.Field "author_name" (Decode.list Decode.string) |> Option.defaultValue []
              Year = get.Optional.Field "first_publish_year" Decode.int
              CoverId = coverId
              Isbn13 = get.Optional.Field "isbn" (Decode.list Decode.string) |> Option.defaultValue [] |> List.tryFind (fun s -> s.Length = 13 && s |> Seq.forall System.Char.IsDigit)
              EditionKey = preferredEditionKey coverEditionKey editionKeys language
              Subjects = get.Optional.Field "subject" (Decode.list Decode.string) |> Option.defaultValue [] |> List.truncate 8
              PageCount = get.Optional.Field "number_of_pages_median" Decode.int
              CoverUrl = coverId |> Option.map (fun id -> sprintf "https://covers.openlibrary.org/b/id/%d-M.jpg" id) })

    let private decodeSearchResponse : Decoder<Mediatheca.Shared.OpenLibrarySearchResult list> =
        Decode.object (fun get -> get.Required.Field "docs" (Decode.list decodeSearchDoc))

    // 1h in-process cache keyed by query (the `Tmdb.SearchCache` shape).
    module private SearchCache =
        open System
        open System.Collections.Concurrent

        type CacheEntry = {
            Results: Mediatheca.Shared.OpenLibrarySearchResult list
            ExpiresAt: DateTime
        }

        let private cache = ConcurrentDictionary<string, CacheEntry>()

        let tryGet (query: string) : Mediatheca.Shared.OpenLibrarySearchResult list option =
            let key = query.ToLowerInvariant().Trim()
            match cache.TryGetValue(key) with
            | true, entry ->
                if entry.ExpiresAt > DateTime.UtcNow then Some entry.Results
                else
                    cache.TryRemove(key) |> ignore
                    None
            | _ -> None

        let set (query: string) (results: Mediatheca.Shared.OpenLibrarySearchResult list) =
            let key = query.ToLowerInvariant().Trim()
            cache.[key] <- { Results = results; ExpiresAt = DateTime.UtcNow.AddHours(1.0) }

    let searchBooks (httpClient: HttpClient) (config: OpenLibraryConfig) (query: string) : Async<Mediatheca.Shared.OpenLibrarySearchResult list> =
        async {
            match SearchCache.tryGet query with
            | Some cached -> return cached
            | None ->
                let url =
                    sprintf
                        "https://openlibrary.org/search.json?q=%s&fields=key,title,author_name,first_publish_year,cover_i,isbn,edition_key,cover_edition_key,language,subject,number_of_pages_median&limit=20"
                        (System.Uri.EscapeDataString query)
                let! json = throttleApiCall (fun () -> fetchJson httpClient config url)
                match Decode.fromString decodeSearchResponse json with
                | Ok results ->
                    SearchCache.set query results
                    return results
                | Error _ -> return []
        }

    // ── Work lookup ──────────────────────────────────────────────────────

    type OpenLibraryWork = {
        Description: string option
        Subjects: string list
    }

    /// `description` is a string OR a `{type,value}` object depending on the
    /// edit history of the work — both decode.
    let private decodeWorkDescription : Decoder<string option> =
        Decode.oneOf [
            Decode.field "description" Decode.string |> Decode.map Some
            Decode.field "description" (Decode.field "value" Decode.string) |> Decode.map Some
            Decode.succeed None
        ]

    let private decodeWorkSubjects : Decoder<string list> =
        Decode.oneOf [
            Decode.field "subjects" (Decode.list Decode.string)
            Decode.succeed []
        ]

    let private decodeWork : Decoder<OpenLibraryWork> =
        Decode.map2
            (fun description subjects -> { Description = description; Subjects = subjects })
            decodeWorkDescription
            decodeWorkSubjects

    // ── Markdown work descriptions → sanitized HTML subset (books-xntts) ──
    //
    // Open Library work descriptions are Markdown, not HTML — the raw
    // string used to be stored verbatim in `book_metadata_cache.description`
    // and rendered through the client's HTML-subset `RichText.render`
    // (books-nvnyk), which knows nothing about Markdown, so a `- [text](url)`
    // list collapsed into one paragraph of literal bracket/paren text. This
    // converts the Markdown subset Open Library actually uses into
    // `DescriptionSanitizer`'s allowlisted HTML subset (p/br/b/strong/i/em/
    // ul/ol/li), then runs the result through `DescriptionSanitizer.sanitize`
    // itself — one allowlist, no second one (games-r1tx4's rule) — so any
    // stray real HTML already sitting in the Markdown source (Open Library
    // allows it) is handled exactly like Audible's/Steam's descriptions.
    let private horizontalRulePattern =
        System.Text.RegularExpressions.Regex(@"^[ \t]*(?:-{3,}|\*{3,}|_{3,})[ \t]*$", System.Text.RegularExpressions.RegexOptions.Multiline)

    /// Open Library uses the horizontal rule exclusively to separate the
    /// blurb from editorial appendices ("Contains", "See also", source
    /// attributions) — everything from the first such rule line to the end
    /// is discarded, never rendered.
    let private cutTrailer (markdown: string) : string =
        let m = horizontalRulePattern.Match(markdown)
        if m.Success then markdown.Substring(0, m.Index).TrimEnd() else markdown

    let private trailingReferenceDefPattern =
        System.Text.RegularExpressions.Regex(@"(\r?\n[ \t]*)*(\[\d+\]:[ \t]*\S+.*(\r?\n)*)+$")

    let private sourceCitationPattern =
        System.Text.RegularExpressions.Regex(@"[ \t]*\(\[source\]\[\d+\]\)")

    /// Strips a trailing `[n]: url` reference-definition block and any
    /// inline `([source][n])` citation even when no horizontal rule
    /// separated it from the blurb (the rule is the common case; this is
    /// the safety net for the appendix shapes that skip it).
    let private stripReferenceAppendix (markdown: string) : string =
        markdown
        |> fun s -> trailingReferenceDefPattern.Replace(s, "")
        |> fun s -> sourceCitationPattern.Replace(s, "")
        |> fun s -> s.TrimEnd()

    // Markdown escapes protected as private-use placeholders before any
    // link/emphasis pattern runs (so `\[`/`\]`/`\*`/`\_` are never mistaken
    // for real link/emphasis syntax), restored to their literal character
    // right before the final `DescriptionSanitizer.sanitize` pass.
    let private protectEscapes (s: string) : string =
        s.Replace(@"\[", "").Replace(@"\]", "").Replace(@"\*", "").Replace(@"\_", "")

    let private restoreEscapes (s: string) : string =
        s.Replace("", "[").Replace("", "]").Replace("", "*").Replace("", "_")

    /// A bare `<https://…>` autolink — dropped entirely, never converted to
    /// visible text or a link (the renderer has no anchor support).
    let private autolinkPattern =
        System.Text.RegularExpressions.Regex(@"<https?://[^>\s]*>")

    let private referenceLinkPattern = System.Text.RegularExpressions.Regex(@"\[([^\]]*)\]\[[^\]]*\]")
    let private inlineLinkPattern = System.Text.RegularExpressions.Regex(@"\[([^\]]*)\]\(([^)]*)\)")
    let private boldStarPattern = System.Text.RegularExpressions.Regex(@"\*\*(.+?)\*\*")
    let private boldUnderscorePattern = System.Text.RegularExpressions.Regex(@"__(.+?)__")
    let private italicStarPattern = System.Text.RegularExpressions.Regex(@"\*(.+?)\*")
    let private italicUnderscorePattern = System.Text.RegularExpressions.Regex(@"_(.+?)_")

    /// Inline formatting for one line/segment of already escape-protected
    /// Markdown: reference and inline links become plain text (this
    /// renderer has no anchor support and a link to an Open Library work is
    /// noise on a library card), then bold before italic for both the
    /// `**`/`*` and `__`/`_` families (bold first, so a `**bold**` run is
    /// never half-consumed by the single-character italic pattern).
    let private inlineFormat (text: string) : string =
        text
        |> fun s -> referenceLinkPattern.Replace(s, "$1")
        |> fun s -> inlineLinkPattern.Replace(s, "$1")
        |> fun s -> boldStarPattern.Replace(s, "<strong>$1</strong>")
        |> fun s -> boldUnderscorePattern.Replace(s, "<strong>$1</strong>")
        |> fun s -> italicStarPattern.Replace(s, "<em>$1</em>")
        |> fun s -> italicUnderscorePattern.Replace(s, "<em>$1</em>")

    let private headingPattern = System.Text.RegularExpressions.Regex(@"^#{1,6}[ \t]+(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline)
    let private unorderedItemPattern = System.Text.RegularExpressions.Regex(@"^[-*][ \t]+(.*)$")
    let private orderedItemPattern = System.Text.RegularExpressions.Regex(@"^\d+\.[ \t]+(.*)$")

    /// Splits a Markdown document into blank-line-separated blocks — the
    /// same "one or more blank lines" rule `RichText.splitBlankLines`
    /// (client side) uses for legacy plain-text rows.
    let private splitBlankLineBlocks (s: string) : string list =
        System.Text.RegularExpressions.Regex.Split(s, @"\r?\n[ \t]*\r?\n")
        |> Array.map (fun chunk -> chunk.Trim())
        |> Array.filter (fun chunk -> chunk <> "")
        |> List.ofArray

    /// One blank-line-separated block → its HTML: a single `#`-heading line
    /// becomes `<p><strong>…</strong></p>`; a block whose every line is a
    /// `-`/`*` or `1.` list item becomes `<ul><li>…</li></ul>` /
    /// `<ol><li>…</li></ol>`; anything else is a paragraph, its internal
    /// single newlines becoming `<br>`.
    let private blockToHtml (block: string) : string =
        let headingMatch = headingPattern.Match(block)
        if headingMatch.Success && not (block.Contains("\n")) then
            sprintf "<p><strong>%s</strong></p>" (inlineFormat (headingMatch.Groups.[1].Value.Trim()))
        else
            let lines = block.Split('\n') |> Array.map (fun l -> l.Trim()) |> Array.filter (fun l -> l <> "")
            let isUnorderedList = lines.Length > 0 && lines |> Array.forall unorderedItemPattern.IsMatch
            let isOrderedList = lines.Length > 0 && lines |> Array.forall orderedItemPattern.IsMatch
            if isUnorderedList then
                lines
                |> Array.map (fun l -> sprintf "<li>%s</li>" (inlineFormat (unorderedItemPattern.Match(l).Groups.[1].Value)))
                |> String.concat ""
                |> sprintf "<ul>%s</ul>"
            elif isOrderedList then
                lines
                |> Array.map (fun l -> sprintf "<li>%s</li>" (inlineFormat (orderedItemPattern.Match(l).Groups.[1].Value)))
                |> String.concat ""
                |> sprintf "<ol>%s</ol>"
            else
                lines
                |> Array.map inlineFormat
                |> String.concat "<br>"
                |> sprintf "<p>%s</p>"

    /// Converts an Open Library work description (Markdown) into
    /// `DescriptionSanitizer`'s allowlisted HTML subset. Pure — no I/O.
    let descriptionToHtml (markdown: string) : string =
        markdown.Replace("\r\n", "\n")
        |> cutTrailer
        |> stripReferenceAppendix
        |> fun s -> autolinkPattern.Replace(s, "")
        |> protectEscapes
        |> splitBlankLineBlocks
        |> List.map blockToHtml
        |> String.concat ""
        |> restoreEscapes
        |> DescriptionSanitizer.sanitize

    let getWork (httpClient: HttpClient) (config: OpenLibraryConfig) (workKey: string) : Async<OpenLibraryWork> =
        async {
            let url = sprintf "https://openlibrary.org%s.json" workKey
            let! json = throttleApiCall (fun () -> fetchJson httpClient config url)
            match Decode.fromString decodeWork json with
            | Ok work -> return { work with Description = work.Description |> Option.map descriptionToHtml }
            | Error _ -> return { Description = None; Subjects = [] }
        }

    // ── Edition lookup by ISBN ───────────────────────────────────────────

    type OpenLibraryEdition = {
        EditionKey: string
        WorkKey: string option
        Title: string
        Authors: string list
        PublishDate: string option
        Publishers: string list
        PageCount: int option
        CoverId: int option
    }

    let private decodeAuthorKeys : Decoder<string list> =
        Decode.oneOf [
            Decode.field "authors" (Decode.list (Decode.field "key" Decode.string))
            Decode.succeed []
        ]

    let private decodeEditionRaw : Decoder<{| Key: string; WorkKey: string option; Title: string; AuthorKeys: string list; PublishDate: string option; Publishers: string list; PageCount: int option; CoverId: int option |}> =
        Decode.object (fun get ->
            {| Key = get.Required.Field "key" Decode.string
               WorkKey =
                get.Optional.Field "works" (Decode.list (Decode.field "key" Decode.string))
                |> Option.defaultValue []
                |> List.tryHead
               Title = get.Required.Field "title" Decode.string
               AuthorKeys = get.Optional.Raw decodeAuthorKeys |> Option.defaultValue []
               PublishDate = get.Optional.Field "publish_date" Decode.string
               Publishers = get.Optional.Field "publishers" (Decode.list Decode.string) |> Option.defaultValue []
               PageCount = get.Optional.Field "number_of_pages" Decode.int
               CoverId = get.Optional.Field "covers" (Decode.list Decode.int) |> Option.defaultValue [] |> List.tryHead |})

    let private decodeAuthorName : Decoder<string> =
        Decode.field "name" Decode.string

    /// Resolves at most 3 `/authors/{key}.json` references to display names,
    /// best-effort — a lookup that fails or times out is skipped, never
    /// surfaced as an error (this task's own instructions).
    let private resolveAuthorNames (httpClient: HttpClient) (config: OpenLibraryConfig) (authorKeys: string list) : Async<string list> =
        async {
            let mutable names = []
            for key in authorKeys |> List.truncate 3 do
                try
                    let url = sprintf "https://openlibrary.org%s.json" key
                    let! json = throttleApiCall (fun () -> fetchJson httpClient config url)
                    match Decode.fromString decodeAuthorName json with
                    | Ok name -> names <- names @ [ name ]
                    | Error _ -> ()
                with _ -> ()
            return names
        }

    /// Shared by `getEditionByIsbn` and `getEditionByOlid` (verifier
    /// iteration 2) — both endpoints return the same edition JSON shape,
    /// only the URL (and therefore the addressing key) differs. `None` on a
    /// 404 (this task's own acceptance criterion — never an exception).
    let private fetchEdition (httpClient: HttpClient) (config: OpenLibraryConfig) (url: string) : Async<OpenLibraryEdition option> =
        async {
            let! jsonOpt = throttleApiCall (fun () -> fetchJsonOptional httpClient config url)
            match jsonOpt with
            | None -> return None
            | Some json ->
                match Decode.fromString decodeEditionRaw json with
                | Error _ -> return None
                | Ok raw ->
                    let! authorNames = resolveAuthorNames httpClient config raw.AuthorKeys
                    return Some {
                        EditionKey = raw.Key
                        WorkKey = raw.WorkKey
                        Title = raw.Title
                        Authors = authorNames
                        PublishDate = raw.PublishDate
                        Publishers = raw.Publishers
                        PageCount = raw.PageCount
                        CoverId = raw.CoverId
                    }
        }

    /// `None` on a 404 (this task's own acceptance criterion — never an
    /// exception).
    let getEditionByIsbn (httpClient: HttpClient) (config: OpenLibraryConfig) (isbn: string) : Async<OpenLibraryEdition option> =
        fetchEdition httpClient config (sprintf "https://openlibrary.org/isbn/%s.json" isbn)

    /// `search.json`'s `edition_key` is an OLID (`OL33246498M`), never an
    /// ISBN (verifier iteration 1) — this resolves it via `/books/{OLID}.json`,
    /// the OLID-addressed sibling of `getEditionByIsbn`'s ISBN-addressed
    /// lookup. `None` on a 404, same as `getEditionByIsbn`.
    let getEditionByOlid (httpClient: HttpClient) (config: OpenLibraryConfig) (olid: string) : Async<OpenLibraryEdition option> =
        fetchEdition httpClient config (sprintf "https://openlibrary.org/books/%s.json" olid)

    // ── Covers ───────────────────────────────────────────────────────────

    type CoverSize = Small | Medium | Large

    let private coverSizeCode (size: CoverSize) =
        match size with
        | Small -> "S"
        | Medium -> "M"
        | Large -> "L"

    let coverUrl (coverId: int) (size: CoverSize) : string =
        sprintf "https://covers.openlibrary.org/b/id/%d-%s.jpg" coverId (coverSizeCode size)

    /// A tiny placeholder response (Open Library's 1x1 "no cover" GIF served
    /// with a 200) is indistinguishable from a real cover by status code
    /// alone — a response under 1 KB is treated the same as a 404.
    let private placeholderThresholdBytes = 1024

    /// Saves `posters/book-{slug}.jpg`. Writes bytes directly with
    /// `System.IO` (this module compiles before `ImageStore.fs` — see the
    /// module doc comment above) rather than depending on `ImageStore`.
    /// `None` on a 404 or a sub-1KB placeholder response.
    let downloadCover (httpClient: HttpClient) (config: OpenLibraryConfig) (coverId: int) (slug: string) (imageBasePath: string) : Async<string option> =
        throttleCoverCall (fun () ->
            async {
                try
                    let url = coverUrl coverId Large
                    use request = new HttpRequestMessage(HttpMethod.Get, url)
                    request.Headers.Add("User-Agent", config.UserAgent)
                    let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                    if not response.IsSuccessStatusCode then
                        return None
                    else
                        let! bytes = response.Content.ReadAsByteArrayAsync() |> Async.AwaitTask
                        if bytes.Length < placeholderThresholdBytes then
                            return None
                        else
                            let relativePath = sprintf "posters/book-%s.jpg" slug
                            let destPath = System.IO.Path.Combine(imageBasePath, relativePath)
                            let dir = System.IO.Path.GetDirectoryName(destPath)
                            if not (System.IO.Directory.Exists(dir)) then
                                System.IO.Directory.CreateDirectory(dir) |> ignore
                            System.IO.File.WriteAllBytes(destPath, bytes)
                            return Some relativePath
                with _ -> return None
            })
