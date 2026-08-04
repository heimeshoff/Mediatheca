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
- **Play mode** — the retired free-text predecessor to **play facets** (below),
  labels like "Singleplayer", "Co-op", "Competitive". Fully cut over as of
  games-v4nqe (ADR-0053): `Add_play_mode`/`Remove_play_mode`/`Categorize_game`
  (and the description/short-description/website-url/HLTB/Steam-last-played
  setters carried alongside the same emission cutover) are deleted from
  `GameCommand`; `Game_play_mode_added`/`Game_play_mode_removed`/
  `Game_categorized`/`Game_description_set`/`Game_short_description_set`/
  `Game_website_url_set`/`Game_hltb_hours_set`/`Game_steam_last_played_set`
  stay in the codec (old streams still deserialize) but their `evolve` and
  `GameProjection.handleEvent` arms are explicit no-ops, matching the
  `Game_store_added` precedent. `game_list.hltb_hours` and
  `game_detail.description`/`short_description`/`website_url`/
  `hltb_hours`/`hltb_main_plus_hours`/`hltb_completionist_hours`/`play_modes`/
  `steam_last_played` are dropped from the projection schema entirely.
  `genres` is deliberately NOT among them (ADR-0055, amending ADR-0043) — see
  **Identity card**, below.
- **Play facets** (ADR-0053) — seven typed facets (`Solo`, `CoopCouch`,
  `CoopOnline`, `VersusCouch`, `VersusOnline`, `RemotePlayTogether`, `Vr`)
  superseding the 302-distinct-string `play_modes` free text. Two-fold:
  `PlayFacets` is the **cache-derived default**, mechanically derived from
  Steam Store category ids (`FacetDerivation.deriveFacets`,
  `src/Server/FacetDerivation.fs`) and written to `game_metadata_cache`'s
  facet columns by a resumable throttled backfill job (`GameFacetBackfill.fs`)
  or imperatively at Steam-fetch command time (`Api.fs`/`PlaytimeTracker.fs`,
  games-v4nqe) — never carried by an event. `PlayFacetsOverride` is the
  **manual correction**, event-sourced (`Game_play_facets_overridden` /
  `Override_play_facets`) for non-Steam games and for correcting Steam's own
  mis-categorization; all seven fields are `option`, `None` meaning "defer to the cache".
  `FacetDerivation.merge` composes the two at query time (override wins where set),
  wired into both `GameListItem.PlayFacets` and `GameDetail.PlayFacets`/
  `PlayFacetsOverride` (games-v4nqe) — the client must send the raw
  `PlayFacetsOverride` back on the next `overrideGamePlayFacets` call, never the
  merged `PlayFacets` value (that would freeze every cache-derived field as a
  permanent manual override). See ADR-0053 and ADR-0054 (the live-verified
  Steam category-id table, including the one judgment call the source
  decision log left open: a bare "Multi-player" tag with no other
  multiplayer signal resolves to `CoopOnline`).
  **UI (games-j6wkr):** `Components/PlayFacetsDisplay.fs` is the shared
  badge/control vocabulary consumed by both `Pages/Games` (list cards +
  client-side facet filters) and `Pages/GameDetail` (hero badges + a "Play
  Facets" panel of seven Auto/On/Off segmented controls, VR getting the
  4-option Auto/No VR/Supported/VR only variant). Badges cap at 4 — Solo,
  Co-op (couch/online sub-label), Versus (couch/online sub-label), and a
  standalone Couch summary badge that fires on any couch-playable mode.
  Segmented controls always render the merged `PlayFacets` for display but
  POST a single-field-changed `PlayFacetsOverride`, built via
  `Shared.PlayFacetsOverride`'s `withX` functions (`GameDetail/State.fs`'s
  `Override_*` message arms) — the ADR-0053 trap guard, covered by
  `PlayFacetsOverrideTests.fs`.
- **Deck compatibility** (`DeckCompatibility`, games-b8xnw, ADR-0043/ADR-0045/ADR-0059) —
  Steam's own Deck-readiness verdict (`Verified`/`Playable`/`Unsupported`/`Unknown`),
  cache-tier only — no event, no override, no aggregate involvement at all (unlike
  play facets, this isn't something Marco is likely to know better than Valve's own
  testing). Written to `game_metadata_cache.deck_compat` by a resumable throttled
  backfill job (`GameDeckCompatBackfill.fs`, reusing `GameFacetBackfill.fs`'s shape)
  walking its OWN cursor column, `deck_compat_fetched_at` — deliberately separate from
  the play-facets backfill's `fetched_at`, since the two jobs run on independent
  schedules against different sources and a shared cursor would let one job's stamp
  silently exempt the other's work for the same game. The unofficial
  `ajaxgetdeckappcompatibilityreport` endpoint this feature was originally framed
  around is DEAD (verified live, ADR-0059) — Valve retired it; the verdict is scraped
  instead from the `data-hardwarecompatibility="{...}"` attribute embedded in each
  store app page's own HTML (`Steam.fs`'s `getDeckCompatibility`/
  `decodeDeckCompatFromHtml`/`mapDeckCompatCategory`), which needs Steam's age-gate
  cookies to render for Mature-rated titles. Read straight through (no merge) into
  `GameListItem.DeckCompat`/`GameDetail.DeckCompat`, rendered as a colored badge
  alongside the play-facet badges (`Components/PlayFacetsDisplay.fs`'s
  `deckCompatBadge`) — `Unknown` renders nothing.
- **Metadata cache slice** (games-v4nqe) — description/short_description/website_url,
  cache-only (`game_metadata_cache`, `MetadataCache.upsertGameIdentityCard`/
  `tryGetGameIdentityCard` — the type's own name predates this rename and is kept for
  now). Written by the creation code path immediately after `Add_game` succeeds
  (never `GameProjection.handleEvent` — ADR-0045's hard constraint) and by every
  Steam-fetch call site thereafter, scoped to an `INSERT ... ON CONFLICT DO UPDATE`
  slice that never touches the facet/category-id/`fetched_at` columns on the same
  row. `genres` is deliberately NOT in this slice — see **Identity card**, below.
- **Identity card** (ADR-0043's term) — an externally-sourced field that stays a
  `game_list`/`game_detail` projection column, read directly, never cache-joined,
  because it is written exclusively by an event that carries it and never by a
  refresh path: `Name`, `Year`, `CoverRef`/`BackdropRef`, `Genres`. `games-v4nqe`
  iteration 1 briefly moved `Genres` to the cache slice above; ADR-0055 reverted
  that — no refresh path in this codebase ever re-derives Game genres (RAWG genre
  search only ever runs at creation time), so it fails the cache tier's
  re-derivability test and stays here, matching Series' identity-card treatment of
  its own `Genres` (ADR-0048).
- **Stores** — e.g. Steam, GOG. A game can be in multiple.
- **Steam library date** — when the game first appeared in the user's Steam library (sync metadata).

## Aggregates

- **Game** — protects: status transitions follow the lifecycle DU; play time settable any time after `Game_added_to_library`; family owners and played-with are sets. HLTB hours are no longer aggregate-settable (games-v4nqe demoted `Set_hltb_hours`/`Game_hltb_hours_set` — HLTB hours are cache-derived, `MetadataCache.upsertGameHltbHours`). Play time is a two-fold: `TotalPlayTimeMinutes` (`PriorPlayTimeMinutes` + Σ session minutes, what the user asserts happened) and `SteamObservedMinutes` (what Steam has told us, never reduced by correction/move/removal) — the second fold is what makes the Steam sync cursor derivable rather than externally-guarded state (ADR-0050). `PlayFacetsOverride` (ADR-0053) is cache-blind by construction: no invariant here ever reads `game_metadata_cache`, so a redundant-but-harmless override is accepted as normal, self-correcting state, not refused. Only recording a *new* session promotes to InFocus; correcting, moving, removing a session, or recording prior playtime never does.

## Key events

`Game_added_to_library`, `Game_categorized` (legacy, evolve/projection no-op since games-v4nqe), `Game_cover_replaced`, `Game_backdrop_replaced`, `Game_personal_rating_set`, `Game_status_changed`, `Game_hltb_hours_set` (legacy, evolve/projection no-op since games-v4nqe), `Game_store_added`/`Game_store_removed` (legacy, evolve/projection no-op since the pre-existing four-part-rule precedent), `Game_family_owner_added`, `Game_family_owner_removed`, `Game_recommended_by`, `Game_recommendation_removed`, `Want_to_play_with`, `Removed_want_to_play_with`, `Game_played_with`, `Game_played_with_removed`, `Game_steam_app_id_set`, `Game_play_time_set` (legacy, evolve no-op since games-p6vkz), `Prior_play_time_recorded`, `Play_session_recorded`, `Play_session_minutes_corrected`, `Play_session_moved`, `Play_session_removed`, `Steam_observed_total_reconciled`, `Game_description_set` (legacy, evolve/projection no-op since games-v4nqe), `Game_short_description_set` (legacy, same), `Game_website_url_set` (legacy, same), `Game_play_mode_added`/`Game_play_mode_removed` (legacy, evolve/projection no-op since games-v4nqe — superseded by `Game_play_facets_overridden`), `Game_steam_library_date_set`, `Game_steam_last_played_set` (legacy, evolve/projection no-op since games-v4nqe — redundant with `game_play_session`, derived at query time), `Game_play_facets_overridden` (ADR-0053 — the play-facets manual correction).

All "legacy, evolve/projection no-op" events stay in the codec (`Games.Serialization`
still deserializes them; `evolve`/`GameProjection.handleEvent` are explicit no-ops,
the `Game_store_added` precedent) so pre-cutover event streams keep replaying without
error or corrupted state — only their commands are gone.

## Key commands

Direct counterparts to the still-live events above (see `Games.fs` for the full
list). `Categorize_game`, `Set_hltb_hours`, `Set_description`,
`Set_short_description`, `Set_website_url`, `Add_play_mode`, `Remove_play_mode`,
`Set_steam_last_played` are deleted from `GameCommand` (games-v4nqe) — their
event counterparts above are legacy-only now.

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
