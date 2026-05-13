# Task: Fuzzy Search

**ID:** 021
**Milestone:** --
**Size:** Medium
**Created:** 2026-02-24
**Dependencies:** None

## Objective

Make the Ctrl+K search modal tolerant of typos and support year extraction. Local library search should use Levenshtein distance for fuzzy matching. External APIs should benefit from year parameter extraction and, where available, native fuzzy support.

## Details

### 1. Levenshtein Distance Module (`src/Client/FuzzyMatch.fs`)

Create a small, self-contained fuzzy matching module:

- **`levenshteinDistance`**: Standard edit-distance algorithm (insertions, deletions, substitutions) between two strings.
- **`fuzzyScore`**: Scoring function that combines:
  - Normalized Levenshtein distance (lower is better)
  - Substring containment bonus (if the query is a substring, boost the score)
  - Threshold: reject matches where edit distance exceeds ~30% of query length
- **`fuzzyMatch`**: Takes a query and a list of `(key: string, item: 'T)` pairs, returns items sorted by best match score, filtered by threshold.

All functions operate on lowercased strings.

### 2. Local Library Search (`src/Client/Components/SearchModal.fs`)

Replace the current `filterLibrary` function (which uses `.Contains()` substring matching) with fuzzy matching:

- **Current:** `movies |> List.filter (fun m -> m.Name.ToLowerInvariant().Contains(q))`
- **New:** Use `fuzzyMatch` against `Name` field. Score and rank results, return top 10.
- Keep it client-side (data is already in memory from modal init).

### 3. Year Extraction (Shared Utility)

Add a query pre-processing function that detects a 4-digit year at the end of the query:

- `"inception 2010"` → `("inception", Some 2010)`
- `"the matrix"` → `("the matrix", None)`
- `"2001 a space odyssey"` → `("2001 a space odyssey", None)` (year at start = likely part of title)

Use this in both local and external search paths.

### 4. TMDB Search Enhancement (`src/Server/Tmdb.fs`)

- Accept an optional `year` parameter in `searchMovies` and `searchTvSeries`.
- When year is provided, append `&year={year}` (movies) or `&first_air_date_year={year}` (series) to the API URL.
- Update the shared API contract to pass the year through.

**Note:** TMDB has no native fuzzy search. The year parameter is the main improvement here. Typo tolerance for TMDB would require a client-side re-ranking layer on returned results, which is out of scope for this task (the API simply won't return results for badly misspelled queries).

### 5. RAWG Search Enhancement (`src/Server/Rawg.fs`)

- RAWG already performs fuzzy search by default — no changes needed for typo tolerance.
- Accept an optional `year` parameter. When provided, append `&dates={year}-01-01,{year}-12-31` to narrow results.
- Update the shared API contract to pass the year through.

### 6. API Contract Changes (`src/Shared/Shared.fs`, `src/Server/Api.fs`)

Update the `IMediathecaApi` search methods to accept `query * year option` tuples (or a small search request record) instead of plain strings:

- `searchTmdb: string -> Async<TmdbSearchResult list>` → `searchTmdb: string * int option -> Async<TmdbSearchResult list>`
- `searchTvSeries: string -> Async<TmdbSearchResult list>` → `searchTvSeries: string * int option -> Async<TmdbSearchResult list>`
- `searchRawgGames: string -> Async<RawgSearchResult list>` → `searchRawgGames: string * int option -> Async<RawgSearchResult list>`

The `searchLibrary` server endpoint is currently unused (library search happens client-side). If it stays unused, skip it. If it's wired up somewhere, update its SQL to use a fuzzy approach (SQLite `LIKE` with stripped characters, or return all and re-rank).

### 7. Client State Integration (`src/Client/State.fs`)

Update the debounce handler that fires API calls:

- Before dispatching external searches, run year extraction on the query.
- Pass `(cleanedQuery, yearOption)` to the API methods.
- For the Library tab, apply fuzzy matching with the extracted year as an additional filter (if year is present, boost items matching that year).

## Acceptance Criteria

- [x] Typing "incption" (missing 'e') in the Library tab finds "Inception" in local results
- [x] Typing "the wtcher" finds "The Witcher" locally
- [x] Typing "inception 2010" on the Movies tab passes year=2010 to TMDB, improving result ranking
- [x] Typing "zelda 2023" on the Games tab passes dates filter to RAWG
- [x] RAWG search continues to handle typos natively (no regression)
- [x] Results are scored and sorted by relevance (best matches first)
- [x] Existing exact-match searches continue to work at least as well as before
- [x] No performance regression on keystroke (fuzzy matching on ~hundreds of items should be <5ms)
- [x] All existing tests pass

## Work Log

### 2026-02-24 - Implementation Complete

**Changes made:**

1. **Created `src/Client/FuzzyMatch.fs`** - New fuzzy matching module with:
   - `levenshteinDistance`: Standard edit-distance algorithm using two-row optimization
   - `fuzzyScore`: Scoring combining normalized Levenshtein distance, substring containment bonus, and 35% threshold rejection
   - `fuzzyMatch`: Takes query + keyed items, returns scored/sorted/filtered results
   - `extractYear`: Extracts trailing 4-digit year from query ("inception 2010" -> ("inception", Some 2010))

2. **Updated `src/Shared/Shared.fs`** - Changed API contract signatures:
   - `searchTmdb: string * int option -> ...`
   - `searchTvSeries: string * int option -> ...`
   - `searchRawgGames: string * int option -> ...`

3. **Updated `src/Server/Tmdb.fs`** - `searchMovies` and `searchTvSeries` now accept optional year parameter, appending `&year=` or `&first_air_date_year=` to TMDB API URLs. Cache keys incorporate year.

4. **Updated `src/Server/Rawg.fs`** - `searchGames` now accepts optional year parameter, appending `&dates={year}-01-01,{year}-12-31` to RAWG API URL. Cache keys incorporate year.

5. **Updated `src/Server/Api.fs`** - All API endpoint implementations destructure the `(query, year)` tuple. Also fixed 3 internal call sites (testTmdbApiKey, testRawgApiKey, Steam sync) to pass `None` for year.

6. **Updated `src/Client/Components/SearchModal.fs`** - Replaced substring `.Contains()` filtering in `filterLibrary` with fuzzy matching via `FuzzyMatch.fuzzyMatch`. Year extraction boosts year-matching items.

7. **Updated `src/Client/State.fs`** - Added `FuzzyMatch.extractYear` before all external search API calls. Passes `(cleanQuery, yearOpt)` tuples to API methods.

8. **Updated `src/Client/Client.fsproj`** - Added `FuzzyMatch.fs` before `SearchModal.fs` in compilation order.

**Verification:** `npm run build` succeeds, `npm test` passes all 233 tests.
