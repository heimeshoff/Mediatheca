# Codebase Snapshot (as of 2026-02-08)

## Domain Model

### Movies Aggregate (`src/Server/Movies.fs`)
- **Stream ID:** `Movie-{slug}`
- **State:** `Not_created | Active of ActiveMovie | Removed`
- **ActiveMovie fields:** Name, Year, Runtime?, Overview, Genres[], PosterRef?, BackdropRef?, TmdbId, TmdbRating?, RecommendedBy (Set<string>), Want_to_watch_with (Set<string>)
- **9 Events:** Movie_added_to_library (fat), Movie_removed_from_library, Movie_categorized, Movie_poster_replaced, Movie_backdrop_replaced, Movie_recommended_by, Recommendation_removed, Want_to_watch_with, Removed_want_to_watch_with
- **9 Commands:** Add_movie_to_library, Remove_movie_from_library, Categorize_movie, Replace_poster, Replace_backdrop, Recommend_by, Remove_recommendation, Add_want_to_watch_with, Remove_from_want_to_watch_with
- **Invariants:** Idempotent set operations (recommend/watch-with), state guards (Removed is terminal)

### Friends Aggregate (`src/Server/Friends.fs`)
- **Stream ID:** `Friend-{slug}`
- **State:** `Not_created | Active of ActiveFriend | Removed`
- **ActiveFriend fields:** Name, ImageRef?
- **3 Events:** Friend_added, Friend_updated, Friend_removed
- **3 Commands:** Add_friend, Update_friend, Remove_friend
- **Cascade:** Friend removal scrubs slugs from all movie_detail recommended_by/want_to_watch_with via FriendProjection

### Cross-Aggregate: Movies reference friends by slug in RecommendedBy and Want_to_watch_with sets

## API Surface

### IMediathecaApi (21 methods)
- **Movies:** addMovie(tmdbId), removeMovie(slug), getMovie(slug), getMovies(), categorizeMovie, replacePoster, replaceBackdrop, recommendMovie, removeRecommendation, wantToWatchWith, removeWantToWatchWith
- **Friends:** addFriend(name), updateFriend, removeFriend, getFriend, getFriends
- **TMDB:** searchTmdb(query)
- **Settings:** getTmdbApiKey, setTmdbApiKey, testTmdbApiKey
- **Health:** healthCheck

### DTOs
- MovieListItem (slug, name, year, posterRef?, genres[], tmdbRating?)
- MovieDetail (all above + runtime?, overview, backdropRef?, tmdbId, cast[], recommendedBy[], wantToWatchWith[])
- FriendListItem / FriendDetail (slug, name, imageRef?)
- FriendRef (slug, name), CastMemberDto (name, role, imageRef?, tmdbId), TmdbSearchResult

### Slug Module: slugify(), movieSlug(name, year), friendSlug(name)

## Storage & Projections

### Event Store (`EventStore.fs`)
- **Table:** `events` (global_position PK, stream_id, stream_position, event_type, data JSON, metadata JSON, timestamp)
- **Checkpoints:** `projection_checkpoints` (projection_name PK, last_position, updated_at)
- **Concurrency:** Optimistic locking via UNIQUE(stream_id, stream_position)
- **Pragmas:** WAL, NORMAL sync, FK enabled, 5s busy timeout

### Projections
- **MovieProjection:** `movie_list` (slug PK, name, year, poster_ref, genres JSON, tmdb_rating), `movie_detail` (+ runtime, overview, backdrop_ref, tmdb_id, recommended_by JSON, want_to_watch_with JSON)
- **FriendProjection:** `friend_list` (slug PK, name, image_ref) + cascade cleanup on removal

### Non-Event-Sourced
- **CastStore:** `cast_members` (id, name, tmdb_id UNIQUE, image_ref), `movie_cast` (movie_stream_id, cast_member_id, role, billing_order, is_top_billed)
- **ImageStore:** File-based (saveImage, deleteImage, imageExists) at `{BaseDir}/images`
- **SettingsStore:** `settings` KV table (key PK, value, updated_at)

### Wiring: `projectionHandlers = [MovieProjection.handler; FriendProjection.handler]` in Program.fs; 100-event batch processing with checkpoint resume

## Client Architecture

### Pages (7)
| Page | Route | Pattern |
|------|-------|---------|
| Dashboard | `/` | Stats + recent movies |
| Movie_list | `/movies` | Search/filter grid + TMDB modal |
| Movie_detail | `/movies/:slug` | Backdrop hero + cast + friend pickers |
| Friend_list | `/friends` | Grid + inline add form |
| Friend_detail | `/friends/:slug` | Profile + inline edit |
| Settings | `/settings` | TMDB key config |
| Not_found | `/not-found` | 404 |

### Root MVU: All child models initialized upfront, message routing via `Cmd.map`, page switch reinitializes target page

### Components: Layout (Sidebar + BottomNav responsive shell), Icons (6 SVGs), PageContainer, TmdbSearchModal (nested MVU in Movies page)

### Styling: TailwindCSS 4 + DaisyUI 5, custom "dim" theme, Oswald/Inter fonts, hero gradients, card hover animations, stagger grid animations

## Cross-Cutting Observations

- Watch sessions (Phase 3) will extend Movie aggregate — needs new events, state fields, projection columns
- Content blocks are a new concept — may need new projection tables or extend movie_detail
- No WatchSession or ContentBlock types exist yet in Shared.fs
- Movie detail page will need a new "Watch History" section and content block editor
- Image upload for content blocks needs new ImageStore paths and API endpoints
