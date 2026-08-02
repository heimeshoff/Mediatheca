# Games

## Purpose
Owns the **Game aggregate** — video games with lifecycle status, play modes, family ownership, and Steam / HowLongToBeat metadata. Source of truth for "what am I playing", "how long did it take me", "who shares this title".

## Classification
**core** — One of the three media-type BCs.

## Actors
Single user.

## Ubiquitous language

- **Game** — a video game in the library. Carries a RAWG id (canonical metadata) and optionally a Steam appId.
- **Status** — lifecycle position, exactly five states (remodeled 2026-08-01,
  games-status-vocabulary-reconcile): `Backlog | InFocus | Retired | Abandoned | Dismissed`.
  `Playing` never exists as a status — `InFocus` covers "actively playing" alongside
  near-future intent and want-to-recommend. `OnHold` was removed as a distinction that
  never mattered (existing OnHold games upcast to InFocus). `Retired` (renamed from
  `Completed`) means "played enough for now" — a contented stop, not necessarily beaten,
  return possible; distinct from `Abandoned` (stopped because it's actively boring).
  `Dismissed` is a Backlog game never intended to be played, soft-hidden from default
  lists, rendered as a muted badge variant. Any recognized play session (Steam sync or
  manual) promotes a game from **any** non-InFocus status — including Retired, Abandoned,
  Dismissed — to InFocus (task 048, reaffirmed in the same remodel). The single source of
  `In Focus` for games (unlike Movies/Series where it's a separate flag).
- **Play time** — total minutes played: `PriorPlayTimeMinutes` (accumulated before tracking
  began, dateless) plus the sum of every **play session** (below). Games-p6vkz (2026-08-01):
  play sessions are first-class events, not a republished total.
- **Play session** — one gaming day's worth of playtime for one game. Natural key
  `(gameSlug, gamingDay)` — no synthetic id; two deltas landing on the same day merge
  (summed), never overwrite. Source is `SteamSync` or `Manual`.
- **Prior playtime** — playtime accumulated before session tracking began, recorded once per
  game (`Prior_play_time_recorded`, refused if already set). No date — never appears in the
  Journal diary, only in the total. A first Steam observation over 960 minutes (16h) is
  treated as prior playtime rather than a fabricated single-day session; at or under 960
  minutes it's dated correctly and recorded as a real session (`Games.PriorPlayTimeThresholdMinutes`).
- **Gaming day** — the calendar day a session is attributed to, offset from midnight by the
  sync hour plus a 30-minute grace window (`PlaytimeTracker.toGamingDay`), so a late-night
  session or an early-morning sync attribute to the previous day, not today.
- **Steam observed total** — `ActiveGame.SteamObservedMinutes`: prior playtime plus every
  Steam-sourced session delta *as originally recorded*, never reduced by a later correction,
  move, or removal. This is what makes the Steam sync's cursor derivable from the event log
  rather than external imperative state — see ADR-0050.
- **Family owner** — a friend who owns the game in their library / on shared accounts. Multiple allowed.
- **Played with (friend)** — friend has played this with the user.
- **HLTB hours** — three estimates from HowLongToBeat: main, main+extras, completionist.
- **Play mode** — labels like "Singleplayer", "Co-op", "Competitive".
- **Stores** — e.g. Steam, GOG. A game can be in multiple.
- **Steam library date** — when the game first appeared in the user's Steam library (sync metadata).

## Aggregates

- **Game** — protects: status transitions follow the lifecycle DU; HLTB hours / play time settable any time after `Game_added_to_library`; family owners and played-with are sets. Play time is a two-fold: `TotalPlayTimeMinutes` (`PriorPlayTimeMinutes` + Σ session minutes, what the user asserts happened) and `SteamObservedMinutes` (what Steam has told us, never reduced by correction/move/removal) — the second fold is what makes the Steam sync cursor derivable rather than externally-guarded state (ADR-0050). Only recording a *new* session promotes to InFocus; correcting, moving, removing a session, or recording prior playtime never does.

## Key events

`Game_added_to_library`, `Game_categorized`, `Game_cover_replaced`, `Game_backdrop_replaced`, `Game_personal_rating_set`, `Game_status_changed`, `Game_hltb_hours_set`, `Game_store_added`, `Game_store_removed`, `Game_family_owner_added`, `Game_family_owner_removed`, `Game_recommended_by`, `Game_recommendation_removed`, `Want_to_play_with`, `Removed_want_to_play_with`, `Game_played_with`, `Game_played_with_removed`, `Game_steam_app_id_set`, `Game_play_time_set` (legacy, evolve no-op since games-p6vkz), `Prior_play_time_recorded`, `Play_session_recorded`, `Play_session_minutes_corrected`, `Play_session_moved`, `Play_session_removed`, `Steam_observed_total_reconciled`, `Game_description_set`, `Game_short_description_set`, `Game_website_url_set`, `Game_play_mode_added`, `Game_play_mode_removed`, `Game_steam_library_date_set`, `Game_steam_last_played_set`.

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

None currently. (games-p6vkz, 2026-08-01, closed the standing question about
modeling play sessions as first-class events — see ADR-0050.)
