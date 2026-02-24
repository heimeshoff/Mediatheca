module Mediatheca.Client.FuzzyMatch

open System.Text.RegularExpressions

/// Standard Levenshtein edit distance between two strings.
/// Counts insertions, deletions, and substitutions.
let levenshteinDistance (a: string) (b: string) : int =
    let m = a.Length
    let n = b.Length
    if m = 0 then n
    elif n = 0 then m
    else
        // Use two-row approach for efficiency
        let mutable prev = Array.init (n + 1) id
        let mutable curr = Array.zeroCreate (n + 1)
        for i in 1..m do
            curr.[0] <- i
            for j in 1..n do
                let cost = if a.[i - 1] = b.[j - 1] then 0 else 1
                curr.[j] <-
                    min (min (prev.[j] + 1) (curr.[j - 1] + 1)) (prev.[j - 1] + cost)
            let tmp = prev
            prev <- curr
            curr <- tmp
        prev.[n]

/// Compute a fuzzy match score (lower = better match).
/// Returns None if the match exceeds the threshold.
let fuzzyScore (query: string) (target: string) : float option =
    let q = query.ToLowerInvariant()
    let t = target.ToLowerInvariant()
    if q = "" then None
    else
        // Exact match
        if t = q then Some 0.0
        // Substring containment: strong bonus
        elif t.Contains(q) then
            let lengthRatio = float q.Length / float t.Length
            Some (0.1 * (1.0 - lengthRatio))
        else
            let dist = levenshteinDistance q t
            let maxThreshold = int (System.Math.Ceiling(float q.Length * 0.35))
            if dist > maxThreshold then None
            else
                let normalizedDist = float dist / float (max q.Length t.Length)
                Some (0.3 + normalizedDist * 0.7)

/// Match a query against a list of (key, item) pairs using fuzzy matching.
/// Returns items sorted by best score (lowest first), filtered by threshold, limited to top N.
let fuzzyMatch (maxResults: int) (query: string) (items: (string * 'T) list) : (float * 'T) list =
    if query.Trim() = "" then []
    else
        items
        |> List.choose (fun (key, item) ->
            fuzzyScore query key
            |> Option.map (fun score -> (score, item))
        )
        |> List.sortBy fst
        |> List.truncate maxResults

/// Filter items by fuzzy match, preserving original list order.
/// Unlike fuzzyMatch which sorts by score, this keeps items in their original position.
let fuzzyFilter (query: string) (items: (string * 'T) list) : 'T list =
    if query.Trim() = "" then items |> List.map snd
    else
        items
        |> List.choose (fun (key, item) ->
            fuzzyScore query key
            |> Option.map (fun _ -> item)
        )

/// Extract a trailing 4-digit year from a query string.
/// "inception 2010" -> ("inception", Some 2010)
/// "the matrix" -> ("the matrix", None)
/// "2001 a space odyssey" -> ("2001 a space odyssey", None)  -- year at start is likely part of title
let extractYear (query: string) : string * int option =
    let trimmed = query.Trim()
    let m = Regex.Match(trimmed, @"^(.+)\s+(\d{4})$")
    if m.Success then
        let yearStr = m.Groups.[2].Value
        match System.Int32.TryParse(yearStr) with
        | true, year when year >= 1888 && year <= 2100 ->
            let cleanQuery = m.Groups.[1].Value.Trim()
            if cleanQuery.Length > 0 then (cleanQuery, Some year)
            else (trimmed, None)
        | _ -> (trimmed, None)
    else
        (trimmed, None)
