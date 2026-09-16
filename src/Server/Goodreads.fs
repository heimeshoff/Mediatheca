namespace Mediatheca.Server

open System.Net.Http
open System.Xml.Linq

/// Goodreads — read through its public, key-free RSS feeds keyed by the
/// user's Goodreads user id (ADR-0075). No developer API exists any more
/// (retired 2020-12-08) and no session cookie is ever accepted here; the
/// shelf RSS feed (`review/list_rss/{id}?shelf={shelf}`) is public for a
/// public profile and needs neither key nor auth. This module is compiled
/// BEFORE `BookProjection.fs`/`Api.fs` in `Server.fsproj` (the adapter
/// block), mirroring `OpenLibrary.fs`'s own doc comment about that ordering.
module Goodreads =

    /// integration-wmqn3 (ADR-0075): the user's Goodreads setting -- their
    /// public user id, never a key, never a cookie -- plus which shelves
    /// (beyond the always-on `currently-reading`) get NEW, unmatched items
    /// imported into the library.
    type GoodreadsConfig = {
        UserId: string option
        ImportShelves: string list
    }

    /// One item off a shelf RSS feed (`review/list_rss/{id}?shelf=...`) --
    /// field names per the research report's live element list (goodreads-
    /// reading-progress-and-book-metadata-sources-2026-09-16, §2a).
    type GoodreadsShelfItem = {
        BookId: string
        Title: string
        Author: string
        Isbn: string option
        Isbn13: string option
        ImageUrl: string option
        LargeImageUrl: string option
        NumPages: int option
        AverageRating: float option
        /// `user_rating` of `0` means "not rated" on Goodreads -- decoded to
        /// `None`, never `Some 0` (this task's own acceptance criterion).
        UserRating: int option
        Published: int option
        DateAdded: string option
        ReadAt: string option
        Shelves: string list
    }

    /// A 403/404, an empty channel with no title, or a redirect to a sign-in
    /// page all collapse to `ProfilePrivateOrUnknown` -- the feature requires
    /// a public profile, and Goodreads gives no sharper signal than that.
    type GoodreadsError =
        | ProfilePrivateOrUnknown
        | FeedUnavailable of string
        | ParseFailed of string

    /// One item off the user-status feed (`user_status/list/{id}?format=rss`,
    /// integration-y2ak4, ADR-0075 §4) -- the item carries no book id, only
    /// free text (`Text`, the item's `<title>`) that `parseProgress` reads.
    /// `StatusId` is pulled from the numeric suffix of `user_status/show/{id}`
    /// in `<link>` (falling back to `<guid>`) -- the idempotency marker's key.
    type GoodreadsStatusItem = {
        StatusId: string
        Text: string
        PublishedAt: System.DateTimeOffset
        Link: string option
    }

    /// The result of parsing one status item's text (`parseProgress`) --
    /// this task's own vocabulary for the four recognized shapes. `Started`
    /// never reaches `Observe_reading_progress` (there is no percent to
    /// report); it is purely informational in the sync's counts.
    type ProgressUpdate =
        | PageProgress of page: int * total: int * title: string
        | PercentProgress of percent: int * title: string
        | Finished of title: string
        | Started of title: string

    type private GoodreadsFeed = {
        ChannelTitle: string
        Items: GoodreadsShelfItem list
    }

    // ── parseUserId ──────────────────────────────────────────────────────

    /// Accepts a bare numeric id, or a `goodreads.com/user/show/{id}[-slug]`
    /// / `goodreads.com/review/list/{id}...` URL (with or without a scheme).
    /// Rejects anything without a recognizable numeric id.
    let parseUserId (input: string) : Result<string, string> =
        let trimmed = if isNull input then "" else input.Trim()
        if trimmed = "" then
            Error "Goodreads user id cannot be empty"
        elif System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d+$") then
            Ok trimmed
        else
            let m =
                System.Text.RegularExpressions.Regex.Match(
                    trimmed,
                    @"goodreads\.com/(?:user/show|review/list)/(\d+)")
            if m.Success then Ok m.Groups.[1].Value
            else Error "Could not find a Goodreads user id in that input -- paste your profile URL or numeric id"

    // ── Throttle (ADR-0066's shape, copied from OpenLibrary.fs/Steam.fs) ──
    // One request per 2 seconds across all Goodreads calls (this task's own
    // instructions) -- Goodreads has an active anti-automation posture and
    // this adapter is a plain unauthenticated GET, never a login.

    let mutable throttleInterval = System.TimeSpan.FromSeconds(2.0)

    let private gate = new System.Threading.SemaphoreSlim(1, 1)
    let mutable private lastCallStartedAt: System.DateTime option = None

    /// Exposed so tests can exercise the gate directly, the same
    /// `OpenLibrary.throttleApiCall` precedent.
    let throttleCall (fetch: unit -> Async<'a>) : Async<'a> =
        async {
            do! gate.WaitAsync() |> Async.AwaitTask
            try
                let now = System.DateTime.UtcNow
                match lastCallStartedAt with
                | Some last ->
                    let remaining = throttleInterval - (now - last)
                    if remaining > System.TimeSpan.Zero then
                        do! Async.Sleep remaining
                | None -> ()
                lastCallStartedAt <- Some System.DateTime.UtcNow
                return! fetch ()
            finally
                gate.Release() |> ignore
        }

    // ── RSS parsing ──────────────────────────────────────────────────────

    /// A browser-like User-Agent -- Goodreads' anti-bot posture treats a
    /// bare/empty or clearly-automated UA more suspiciously than a normal
    /// browser string; this is still a plain unauthenticated GET, never a
    /// login (ADR-0070's reasoning).
    let private userAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

    let private elementText (item: XElement) (name: string) : string option =
        match item.Element(XName.Get name) with
        | null -> None
        | el ->
            let v = el.Value.Trim()
            if v = "" then None else Some v

    let private parseIntElement (item: XElement) (name: string) : int option =
        elementText item name
        |> Option.bind (fun s -> match System.Int32.TryParse(s) with true, v -> Some v | _ -> None)

    let private parseItem (item: XElement) : GoodreadsShelfItem =
        let userRating =
            parseIntElement item "user_rating"
            |> Option.bind (fun v -> if v = 0 then None else Some v)
        let shelves =
            elementText item "user_shelves"
            |> Option.map (fun s ->
                s.Split(',')
                |> Array.map (fun x -> x.Trim())
                |> Array.filter (fun x -> x <> "")
                |> Array.toList)
            |> Option.defaultValue []
        { BookId = elementText item "book_id" |> Option.defaultValue ""
          Title = elementText item "title" |> Option.defaultValue ""
          Author = elementText item "author_name" |> Option.defaultValue ""
          Isbn = elementText item "isbn"
          Isbn13 = elementText item "isbn13"
          ImageUrl = elementText item "book_image_url"
          LargeImageUrl = elementText item "book_large_image_url"
          NumPages = parseIntElement item "num_pages"
          AverageRating =
            elementText item "average_rating"
            |> Option.bind (fun s -> match System.Double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture) with true, v -> Some v | _ -> None)
          UserRating = userRating
          Published = parseIntElement item "book_published"
          DateAdded = elementText item "user_date_added"
          ReadAt = elementText item "user_read_at"
          Shelves = shelves }

    let private parseFeed (xml: string) : Result<GoodreadsFeed, GoodreadsError> =
        try
            let doc = XDocument.Parse(xml)
            match doc.Root with
            | null -> Error (ParseFailed "empty document")
            | root ->
                match root.Element(XName.Get "channel") with
                | null -> Error (ParseFailed "no <channel> element")
                | channel ->
                    match elementText channel "title" with
                    | None -> Error ProfilePrivateOrUnknown
                    | Some channelTitle ->
                        let items =
                            channel.Elements(XName.Get "item")
                            |> Seq.map parseItem
                            |> Seq.toList
                        Ok { ChannelTitle = channelTitle; Items = items }
        with ex ->
            Error (ParseFailed ex.Message)

    let private fetchFeed (httpClient: HttpClient) (userId: string) (shelf: string) : Async<Result<GoodreadsFeed, GoodreadsError>> =
        throttleCall (fun () ->
            async {
                try
                    let url =
                        sprintf "https://www.goodreads.com/review/list_rss/%s?shelf=%s"
                            userId (System.Uri.EscapeDataString shelf)
                    use request = new HttpRequestMessage(HttpMethod.Get, url)
                    request.Headers.Add("User-Agent", userAgent)
                    request.Headers.Add("Accept", "application/rss+xml, application/xml")
                    let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                    if response.StatusCode = System.Net.HttpStatusCode.Forbidden
                       || response.StatusCode = System.Net.HttpStatusCode.NotFound then
                        return Error ProfilePrivateOrUnknown
                    elif not response.IsSuccessStatusCode then
                        return Error (FeedUnavailable (sprintf "HTTP %d" (int response.StatusCode)))
                    else
                        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return parseFeed body
                with ex ->
                    return Error (FeedUnavailable ex.Message)
            })

    /// `GET https://www.goodreads.com/review/list_rss/{userId}?shelf={shelf}`
    /// -- shelf membership, ratings, read dates.
    let getShelf (httpClient: HttpClient) (userId: string) (shelf: string) : Async<Result<GoodreadsShelfItem list, GoodreadsError>> =
        async {
            let! result = fetchFeed httpClient userId shelf
            return result |> Result.map (fun f -> f.Items)
        }

    /// The channel `<title>` of the feed (e.g. "Marco's bookshelf:
    /// currently-reading") -- `testGoodreadsConnection`'s profile name.
    let getProfileName (httpClient: HttpClient) (userId: string) (shelf: string) : Async<Result<string, GoodreadsError>> =
        async {
            let! result = fetchFeed httpClient userId shelf
            return result |> Result.map (fun f -> f.ChannelTitle)
        }

    // ── Progress feed (user_status) -- integration-y2ak4, ADR-0075 §4 ──────

    /// RFC-822-shaped dates ("Tue, 02 Sep 2026 12:00:00 -0800") -- same
    /// day-of-week-is-untrustworthy problem `GoodreadsSync.parseReadAtDate`
    /// works around for `user_read_at`; duplicated here (rather than shared)
    /// because this module has no dependency on `GoodreadsSync.fs`, which is
    /// compiled after it.
    let private parseRfc822 (s: string) : System.DateTimeOffset option =
        let withoutDayName =
            match s.IndexOf(',') with
            | -1 -> s
            | idx -> s.Substring(idx + 1).Trim()
        match System.DateTimeOffset.TryParse(withoutDayName, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None) with
        | true, dto -> Some dto
        | false, _ ->
            match System.DateTimeOffset.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None) with
            | true, dto -> Some dto
            | false, _ -> None

    let private statusIdPattern = System.Text.RegularExpressions.Regex(@"user_status/show/(\d+)")

    let private extractStatusId (link: string option) (guid: string option) : string =
        let tryExtract (s: string option) =
            s
            |> Option.bind (fun v ->
                let m = statusIdPattern.Match(v)
                if m.Success then Some m.Groups.[1].Value else None)
        match tryExtract link with
        | Some id -> id
        | None ->
            match tryExtract guid with
            | Some id -> id
            | None -> link |> Option.orElse guid |> Option.defaultValue ""

    let private parseStatusItem (item: XElement) : GoodreadsStatusItem option =
        let text = elementText item "title"
        let link = elementText item "link"
        let guid = elementText item "guid"
        let pubDate = elementText item "pubDate" |> Option.bind parseRfc822
        match text, pubDate with
        | Some t, Some dto -> Some { StatusId = extractStatusId link guid; Text = t; PublishedAt = dto; Link = link }
        | _ -> None

    let private parseStatusFeed (xml: string) : Result<GoodreadsStatusItem list, GoodreadsError> =
        try
            let doc = XDocument.Parse(xml)
            match doc.Root with
            | null -> Error (ParseFailed "empty document")
            | root ->
                match root.Element(XName.Get "channel") with
                | null -> Error (ParseFailed "no <channel> element")
                | channel ->
                    match elementText channel "title" with
                    | None -> Error ProfilePrivateOrUnknown
                    | Some _ ->
                        let items =
                            channel.Elements(XName.Get "item")
                            |> Seq.choose parseStatusItem
                            |> Seq.toList
                        Ok items
        with ex ->
            Error (ParseFailed ex.Message)

    /// `GET https://www.goodreads.com/user_status/list/{userId}?format=rss&page={page}`
    /// -- the user's status updates, paginated. Shares the same throttle gate
    /// and User-Agent as the shelf feed (both are plain unauthenticated GETs
    /// against goodreads.com, ADR-0070's reasoning).
    let getStatusUpdates (httpClient: HttpClient) (userId: string) (page: int) : Async<Result<GoodreadsStatusItem list, GoodreadsError>> =
        throttleCall (fun () ->
            async {
                try
                    let url =
                        sprintf "https://www.goodreads.com/user_status/list/%s?format=rss&page=%d"
                            userId page
                    use request = new HttpRequestMessage(HttpMethod.Get, url)
                    request.Headers.Add("User-Agent", userAgent)
                    request.Headers.Add("Accept", "application/rss+xml, application/xml")
                    let! response = httpClient.SendAsync(request) |> Async.AwaitTask
                    if response.StatusCode = System.Net.HttpStatusCode.Forbidden
                       || response.StatusCode = System.Net.HttpStatusCode.NotFound then
                        return Error ProfilePrivateOrUnknown
                    elif not response.IsSuccessStatusCode then
                        return Error (FeedUnavailable (sprintf "HTTP %d" (int response.StatusCode)))
                    else
                        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return parseStatusFeed body
                with ex ->
                    return Error (FeedUnavailable ex.Message)
            })

    let private whitespacePattern = System.Text.RegularExpressions.Regex(@"\s+")
    let private normalizeWhitespace (s: string) = whitespacePattern.Replace(s, " ").Trim()

    let private regexOpts = System.Text.RegularExpressions.RegexOptions.IgnoreCase
    let private pagePattern = System.Text.RegularExpressions.Regex(@"\bis on page\s+(\d+)\s+of\s+(\d+)\s+of\s+(.+)$", regexOpts)
    let private percentPattern = System.Text.RegularExpressions.Regex(@"\bis\s+(\d+)\s*%\s+done\s+with\s+(.+)$", regexOpts)
    let private finishedPattern = System.Text.RegularExpressions.Regex(@"\b(?:finished reading|is finished with)\s+(.+)$", regexOpts)
    let private startedPattern = System.Text.RegularExpressions.Regex(@"\b(?:is starting|started reading)\s+(.+)$", regexOpts)

    /// A pure function over one status item's text (`GoodreadsStatusItem.Text`)
    /// -- case-insensitive, whitespace-normalized, HTML-entity-decoded
    /// (`&amp;`, `&#39;`, ...). Anything not matching one of the four known
    /// shapes -- quotes, shelvings, reviews -- is `None`, never an error
    /// (this task's own instructions).
    let parseProgress (text: string) : ProgressUpdate option =
        if isNull text then None
        else
            let decoded = System.Net.WebUtility.HtmlDecode(text)
            let t = normalizeWhitespace decoded
            let pageMatch = pagePattern.Match(t)
            if pageMatch.Success then
                Some (PageProgress (int pageMatch.Groups.[1].Value, int pageMatch.Groups.[2].Value, pageMatch.Groups.[3].Value.Trim()))
            else
                let percentMatch = percentPattern.Match(t)
                if percentMatch.Success then
                    Some (PercentProgress (int percentMatch.Groups.[1].Value, percentMatch.Groups.[2].Value.Trim()))
                else
                    let finishedMatch = finishedPattern.Match(t)
                    if finishedMatch.Success then
                        Some (Finished (finishedMatch.Groups.[1].Value.Trim()))
                    else
                        let startedMatch = startedPattern.Match(t)
                        if startedMatch.Success then
                            Some (Started (startedMatch.Groups.[1].Value.Trim()))
                        else
                            None

    let private trailingParentheticalPattern = System.Text.RegularExpressions.Regex(@"\s*\([^)]*\)\s*$")

    /// Title normalization for the status-feed join (this task's own
    /// instructions): strip surrounding quotes/asterisks, a trailing author
    /// parenthetical (e.g. "(Dune #1)"), and a subtitle after a colon; then
    /// lower-case and collapse whitespace. Used to compare a status item's
    /// free-text title against shelf-item and library-book titles.
    let normalizeTitle (title: string) : string =
        if isNull title then ""
        else
            let trimQuotes (s: string) =
                let s = s.Trim()
                let quoteChars = [| '"'; '\''; '*' |]
                if s.Length >= 2 && Array.contains s.[0] quoteChars && s.[s.Length - 1] = s.[0] then
                    s.Substring(1, s.Length - 2)
                else s
            let withoutQuotes = trimQuotes title
            let withoutParenthetical = trailingParentheticalPattern.Replace(withoutQuotes, "")
            let withoutSubtitle =
                match withoutParenthetical.IndexOf(':') with
                | -1 -> withoutParenthetical
                | idx -> withoutParenthetical.Substring(0, idx)
            normalizeWhitespace (withoutSubtitle.ToLowerInvariant())

    /// Fixed, user-facing text for each error case -- `goodreads_last_error`
    /// stores exactly this for `ProfilePrivateOrUnknown` (this task's own
    /// acceptance criterion).
    let describeError (err: GoodreadsError) : string =
        match err with
        | ProfilePrivateOrUnknown -> "profile private or user id unknown"
        | FeedUnavailable msg -> sprintf "Goodreads feed unavailable: %s" msg
        | ParseFailed msg -> sprintf "Goodreads feed could not be parsed: %s" msg
