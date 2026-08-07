# Movies

## Purpose
Owns the **Movie aggregate** — a film as a curated library entry with its metadata, posters, ratings, and the watch sessions tied to it. Source of truth for "did I watch this", "with whom", "did I like it".

## Classification
**core** — One of the three media-type BCs at the heart of the product.

## Actors
Single user (library owner).

## Ubiquitous language

- **Movie** — a film in the user's library. Identified by an internal slug; carries a TMDB id from Integration.
- **In Focus** — a toggle on the Movie meaning "I want to watch this soon". Auto-clears when the first watch session is recorded.
- **Watch session** — a single viewing event. Has a date, optional friend list (watched-with), and is the source of "did I see this".
- **Want to watch with (friend)** — non-decay intent flag tying a Movie to a friend slug.
- **Recommended by (friend)** — provenance: this movie entered the library because a friend suggested it.
- **Personal rating** — integer rating set by the user. Mutates over time.
- **Poster / Backdrop** — image refs in ImageStore, replaceable.

## Aggregates

- **Movie** — protects: a movie can only have watch sessions after `Movie_added_to_library`; watch-session edits target an existing session id; In Focus auto-clears on first watch session.

## Key events

`Movie_added_to_library`, `Movie_categorized`, `Movie_poster_replaced`, `Movie_backdrop_replaced`, `Movie_recommended_by`, `Recommendation_removed`, `Want_to_watch_with`, `Removed_want_to_watch_with`, `Watch_session_recorded`, `Watch_session_date_changed`, `Friend_added_to_watch_session`, `Friend_removed_from_watch_session`, `Watch_session_removed`, `Personal_rating_set`, plus the In Focus events (`Movie_in_focus_set` / `Movie_in_focus_cleared` — per vision §"In Focus").

## Key commands

`Add_movie_to_library`, `Categorize_movie`, `Replace_poster`, `Replace_backdrop`, `Recommend_by`, `Remove_recommendation`, `Add_want_to_watch_with`, `Remove_from_want_to_watch_with`, `Record_watch_session`, `Change_watch_session_date`, `Add_friend_to_watch_session`, `Remove_friend_from_watch_session`, `Remove_watch_session`, `Set_personal_rating`.

## Relationships with other contexts

- **Upstream of:** Journal (publishes `Watch_session_recorded` etc.), Intelligence (consumes everything).
- **Downstream of:** Friends (consumes `Friend_added` to validate friendSlug references).
- **Downstream of:** Integration via anticorruption (TMDB adapter translates to `Add_movie_to_library`).
- **Consumed by:** Curation (catalogs reference movies by id).

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- In Focus events (`Movie_in_focus_set` / `Movie_in_focus_cleared`) are vision-promised but not yet event-coded. They land here when M1 is implemented.
