# Games

## Purpose
Owns the **Game aggregate** — video games with lifecycle status, play modes, family ownership, and Steam / HowLongToBeat metadata. Source of truth for "what am I playing", "how long did it take me", "who shares this title".

## Classification
**core** — One of the three media-type BCs.

## Actors
Single user.

## Ubiquitous language

- **Game** — a video game in the library. Carries a RAWG id (canonical metadata) and optionally a Steam appId.
- **Status** — lifecycle position. Per vision: `Backlog → InFocus → Playing → Completed | Abandoned | OnHold | Dismissed`. The single source of `In Focus` for games (unlike Movies/Series where it's a separate flag).
- **Play time** — total minutes played (often sourced from Steam).
- **Family owner** — a friend who owns the game in their library / on shared accounts. Multiple allowed.
- **Played with (friend)** — friend has played this with the user.
- **HLTB hours** — three estimates from HowLongToBeat: main, main+extras, completionist.
- **Play mode** — labels like "Singleplayer", "Co-op", "Competitive".
- **Stores** — e.g. Steam, GOG. A game can be in multiple.
- **Steam library date** — when the game first appeared in the user's Steam library (sync metadata).

## Aggregates

- **Game** — protects: status transitions follow the lifecycle DU; HLTB hours / play time settable any time after `Game_added_to_library`; family owners and played-with are sets.

## Key events

`Game_added_to_library`, `Game_categorized`, `Game_cover_replaced`, `Game_backdrop_replaced`, `Game_personal_rating_set`, `Game_status_changed`, `Game_hltb_hours_set`, `Game_store_added`, `Game_store_removed`, `Game_family_owner_added`, `Game_family_owner_removed`, `Game_recommended_by`, `Game_recommendation_removed`, `Want_to_play_with`, `Removed_want_to_play_with`, `Game_played_with`, `Game_played_with_removed`, `Game_steam_app_id_set`, `Game_play_time_set`, `Game_description_set`, `Game_short_description_set`, `Game_website_url_set`, `Game_play_mode_added`, `Game_play_mode_removed`, `Game_steam_library_date_set`, `Game_steam_last_played_set`.

## Key commands

Direct counterparts to the events above (see `Games.fs` for the full list).

## Relationships with other contexts

- **Upstream of:** Journal (publishes play-time changes / status transitions used to derive play sessions), Intelligence.
- **Downstream of:** Friends.
- **Downstream of:** Integration (RAWG metadata adapter, Steam adapter, HLTB adapter — see also `PlaytimeTracker.fs`).
- **Consumed by:** Curation.

## Frontend gate

Frontend tasks in this BC **must** `depends_on` the design-system styleguide task. See [[design-system]].

## Open questions

- The `Game_play_time_set` / `Game_steam_last_played_set` events double as a poor man's play-session feed. Whether to model real play sessions as first-class events (vs. derived from totals) is open.
- `Dismissed` was added late (task 020) — verify it's covered in every Status pattern match.
