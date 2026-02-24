# Task: Unify List Page Search with Fuzzy Matching

**ID:** 029
**Milestone:** --
**Size:** Small
**Created:** 2026-02-24
**Dependencies:** None (builds on task 021 FuzzyMatch.fs)

## Objective

Replace the naive substring search (`.Contains()`) on the Movies, Series, and Games list pages with the same fuzzy matching algorithm used in the Ctrl+K search modal (`FuzzyMatch.fs`). This gives users typo tolerance and year filtering on all pages.

## Current Behavior

All three list pages use identical logic:
```fsharp
model.SearchQuery = "" ||
m.Name.ToLowerInvariant().Contains(model.SearchQuery.ToLowerInvariant())
```

- No typo tolerance — "Incpetion" finds nothing
- No year support — "2010" doesn't filter by year
- No relevance scoring — all matches treated equally

## Desired Behavior

- Typo tolerance via Levenshtein distance (same as modal)
- Year extraction — "inception 2010" filters to year 2010
- Fuzzy match decides show/hide, but results keep their current sort order (user-selected sort is preserved, not overridden by relevance)

## Changes

### 1. Movies List Page (`src/Client/Pages/Movies/Views.fs`, ~line 84-89)

**Before:**
```fsharp
let filtered =
    model.Movies
    |> List.filter (fun m ->
        model.SearchQuery = "" ||
        m.Name.ToLowerInvariant().Contains(model.SearchQuery.ToLowerInvariant())
    )
```

**After:**
```fsharp
let filtered =
    if model.SearchQuery = "" then model.Movies
    else
        let query, yearFilter = FuzzyMatch.extractYear model.SearchQuery
        let items = model.Movies |> List.map (fun m -> (m.Name, m))
        let matched = FuzzyMatch.fuzzyMatch 0 query items |> List.map snd
        match yearFilter with
        | Some year -> matched |> List.filter (fun m -> m.Year = year)
        | None -> matched
```

Note: `FuzzyMatch.fuzzyMatch 0` with limit 0 means no limit — return all matches. (Verify this behavior; if `0` doesn't mean unlimited, use a high number like `9999` or the list length.)

### 2. Series List Page (`src/Client/Pages/Series/Views.fs`, ~line 160-165)

Same pattern as Movies, using `s.Name` and `s.Year`.

### 3. Games List Page (`src/Client/Pages/Games/Views.fs`, ~line 198-207)

Same pattern, using `g.Name` and `g.Year`. Preserve the existing status filter — apply fuzzy match first, then status filter (or vice versa).

### 4. Verify `FuzzyMatch.fuzzyMatch` Limit Behavior

Check `FuzzyMatch.fs` to confirm how `maxResults = 0` behaves. If it returns empty, change the approach to pass `List.length items` as the limit, or add an `fuzzyFilter` helper that returns all matches without truncation.

If needed, add a convenience function to `FuzzyMatch.fs`:
```fsharp
let fuzzyFilter (query: string) (items: (string * 'a) list) : 'a list =
    fuzzyMatch (List.length items) query items |> List.map snd
```

## Files Changed

1. **`src/Client/Pages/Movies/Views.fs`** — Replace `.Contains()` filter with fuzzy match
2. **`src/Client/Pages/Series/Views.fs`** — Replace `.Contains()` filter with fuzzy match
3. **`src/Client/Pages/Games/Views.fs`** — Replace `.Contains()` filter with fuzzy match
4. **`src/Client/FuzzyMatch.fs`** — Possibly add `fuzzyFilter` convenience function

## Acceptance Criteria

- [x] Searching "Incpetion" on the Movies page finds "Inception" (typo tolerance)
- [x] Searching "2010" on the Movies page filters to movies from 2010
- [x] Searching "witcher 2019" on the Series page finds the correct series
- [x] Searching "zeld" on the Games page finds "Zelda" titles
- [x] Results maintain their current sort order (not reordered by relevance)
- [x] Empty search query still shows all items (no regression)
- [x] Games page status filter still works in combination with fuzzy search
- [x] All existing tests pass

---

### 2026-02-24 17:00 -- Work Completed

**What was done:**
- Added `fuzzyFilter` convenience function to `FuzzyMatch.fs` that filters items by fuzzy match while preserving original list order (unlike `fuzzyMatch` which sorts by score)
- Replaced `.Contains()` substring search with `FuzzyMatch.fuzzyFilter` + `FuzzyMatch.extractYear` on all three list pages (Movies, Series, Games)
- Games page preserves the existing status filter as a second filtering step after fuzzy search

**Acceptance criteria status:**
- [x] Searching "Incpetion" on the Movies page finds "Inception" -- fuzzyScore uses Levenshtein distance with 35% threshold allowing typos
- [x] Searching "2010" on the Movies page filters to movies from 2010 -- extractYear parses trailing 4-digit years
- [x] Searching "witcher 2019" on the Series page finds the correct series -- extractYear extracts "witcher" + year 2019, fuzzyFilter matches name, year filter narrows results
- [x] Searching "zeld" on the Games page finds "Zelda" titles -- fuzzyScore matches "zeld" to "Zelda" via Levenshtein
- [x] Results maintain their current sort order -- fuzzyFilter uses List.choose which preserves input order (no sorting by score)
- [x] Empty search query still shows all items -- explicit `if model.SearchQuery = "" then model.Items` returns full list
- [x] Games page status filter still works in combination with fuzzy search -- applied as separate List.filter after fuzzy filtering
- [x] All existing tests pass -- `npm test` passes (233 tests); `npm run build` compiles successfully

**Files changed:**
- `src/Client/FuzzyMatch.fs` -- Added `fuzzyFilter` function for order-preserving fuzzy filtering
- `src/Client/Pages/Movies/Views.fs` -- Replaced `.Contains()` search with fuzzyFilter + extractYear
- `src/Client/Pages/Series/Views.fs` -- Replaced `.Contains()` search with fuzzyFilter + extractYear
- `src/Client/Pages/Games/Views.fs` -- Replaced `.Contains()` search with fuzzyFilter + extractYear, preserving status filter
