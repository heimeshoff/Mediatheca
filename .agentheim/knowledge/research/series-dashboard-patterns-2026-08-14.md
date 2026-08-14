---
topic: Series-tracking app dashboards — loved features, complaints, and patterns for a single-user event-sourced series library
date: 2026-08-14
requested_by: architect
related_tasks: []
---

# Research: What the best-loved TV-series-tracking apps show on their dashboard/home screen

## Question
What do the best-loved TV-series-tracking applications (Trakt, TV Time, Plex/Jellyfin/Emby, Simkl,
Serializd, MyAnimeList-style trackers, Sonarr) show on their series dashboard/home screen, and which
of those elements do users actually praise or heavily use — to inform the redesign of Mediatheca's
Series dashboard (event-sourced per-episode history, computed "next up," ratings, friend
recommendations, "in focus" shortlist, Jellyfin playback links)?

## Summary (decision-relevant first)

- **A "next up" row/queue is the single most load-bearing element across every app studied.** Trakt's
  dashboard, TV Time's "Watch Next," Plex's "On Deck," Jellyfin's "Next Up," and Ryot's episode
  tracking all put "here is the one episode you should watch right now, per show" front and center.
  When this logic is wrong, it is the single most-complained-about failure mode in every ecosystem
  (Jellyfin GitHub issues, Plex forums) — correctness here matters more than any visual polish [3][4][7][8].
- **Per-show progress (percent-complete / episodes-remaining / time-remaining) is a praised,
  low-controversy feature.** Trakt's dashboard shows a progress bar per in-progress show plus
  "episodes and time remaining," explicitly to help decide what to watch next; this is described as
  one of its core, reliably-loved capabilities, especially as a Plex/Jellyfin/Kodi scrobble target
  users trust because "it has run reliably since 2010" [1][2][12].
- **Airing calendars are valued but are secondary to next-up, not primary.** Trakt, Simkl, and Sonarr
  all ship an upcoming-episodes calendar; it's consistently listed as a wanted feature ("know when
  shows return") but user complaints target *missing detail* (no air-times) rather than the concept
  itself — the calendar disappoints by being too shallow, not by existing [6][11][14].
  Sonarr's calendar is the app's headline UI surface for a *download-automation* tool, not primarily a
  "what did I watch" dashboard — it answers "what's coming" for the *library*, not "what should I
  watch next" for the *viewer*; the two are complementary, not substitutes [6].
- **Status-bucket models (Watching / Plan to Watch / On Hold / Dropped, MyAnimeList-style) are
  functional but not obviously *loved* as a dashboard surface** — evidence found them useful as
  filters/organization (Simkl, MAL, Serializd) but no source praised the buckets themselves as a
  delightful UX; one review flagged Simkl's bucketed dashboard as "cluttered and hard to navigate"
  even by a user who liked having the lists at all [9][13].
- **Gamification (streaks, badges, "time spent" stats) is the most polarizing element.** TV Time built
  its identity on badges/streaks/diaries and had 26M+ users, but post-mortem coverage of its July 2026
  shutdown and migration commentary repeatedly frame competitors (Trakt, Simkl) as the "grown-up,"
  clutter-free alternative, and one synthesis explicitly ranks "one-tap episode marking" as ~95% of
  daily value with badges/social feed treated as the part users "wear thin" on over time [2][10][15].
  No source found praised badges/streaks as a *reason* to prefer an app; several treated them as bloat.
- **Nobody in this research set has solved the "abandoned/stalled show" problem elegantly.** No app
  surfaces "you haven't opened this in 90 days, is it dropped?" proactively — status buckets
  (Dropped/On Hold) exist but rely entirely on the user remembering to reclassify manually [9][13]. This
  is an open gap, not a solved pattern — see Open Questions.
- **Single-user self-hosted context changes what's worth building:** self-hosted trackers (Jellyfin +
  Next Up, Sonarr calendar, Ryot, MediaTracker) drop the social/gamification layer entirely and
  concentrate on next-episode correctness + upcoming-air awareness + notifications — validating that a
  single-user app should prioritize next-up correctness and progress clarity over
  streaks/badges/feeds [6][16][17].

## Findings

### 1. Trakt — dashboard / "Up Next" / progress
Trakt's dashboard is explicitly built around "what do I watch next": a row of in-progress shows each
with a progress bar (episodes watched vs. total aired), and detail views showing episodes and time
remaining per show [1][2]. This "Up Next" surface is described by Trakt's own team as "a core feature"
— when an internal API change caused watchlisted (not-yet-started) shows to leak into Up Next by
default, the resulting user backlash was significant enough that Trakt published a public post-mortem
acknowledging "how important Up Next is and how frustrating it felt" and committing to staged rollouts
of future Up Next changes in future [3]. That episode is decent evidence the *scope discipline* of the
row matters as much as its existence — users want Up Next to mean "shows I am actively watching," not
"everything vaguely on my radar."

Trakt's calendar (multiple variants: all shows, premieres, new-show premieres, movies, DVD/Blu-ray)
is a secondary but valued surface, gated partly behind VIP for iCal/email delivery [14]. A specific,
repeated complaint: the redesigned calendar removed air-time-of-day information, so "airing today" is
"little use without knowing when it airs, as it could be airing at 11pm" [14] — a concrete lesson that
calendar/airing surfaces need to answer "when," not just "which day."

Independent secondary coverage (2026, post-TV-Time-shutdown migration guides) consistently frames
Trakt as a reliable "sync/data hub" — valued for automatic scrobbling from Plex/Kodi/Infuse/Jellyfin
and for having "run reliably since 2010" — while noting its mobile UI is "functional... not polished"
and that many users treat it as a background layer rather than a daily-open app [12]. This is a
single-source-family claim (Achriom/Moviebase, both apparently commercial content aggregators with an
interest in positioning their own apps favorably) — treat the "reliability" framing as directionally
credible (matches Trakt's own longevity and the TV-Time-shutdown migration wave) but the specific
UI-polish critique as lower-confidence marketing-adjacent commentary [2][12].

### 2. TV Time — Watch Next, streaks, badges (now defunct as of July 2026)
Important context: **TV Time shut down permanently on July 15, 2026**, deleting watch history for its
~26 million users; parent company Whip Media pivoted to enterprise AI rather than sell or maintain the
app, citing that a free ad-supported model wasn't sustainable and insufficient user interest in paying
[10]. This is corroborated by TechCrunch, TechTimes, ScreenRant, and AlternativeTo coverage from
July 2026 [10]. It remains highly relevant as the *reference UX* the market is now migrating away from
(and toward Trakt/Simkl/Serializd), so its patterns and complaints are still instructive.

While live, TV Time's core loop was: a "Watch Next" queue, per-show episode countdowns, watch-time
stats, a monthly "diary," a shareable year-in-review, and badges for watching/voting/interacting with
other fans [2][10]. Complaints that surface repeatedly in review aggregation: ads, frequent redesigns,
and "a feed full of strangers," cited as reasons users went looking for alternatives even before the
shutdown [2]. One synthesis of tracker requirements (written during the TV-Time-to-Trakt/Simkl
migration wave) explicitly states "one-tap episode marking... is 95% of what you do" in a tracker, and
frames badges/streaks/social feed as the part of the value prop that erodes goodwill over time rather
than building it [15]. No source in this research praised streaks/badges as a *reason to choose or stay
with* an app; they show up only in feature lists (descriptive) or complaints (critical), never in
"what users love" framing.

### 3. Plex / Jellyfin / Emby — On Deck / Continue Watching / Next Up
**Semantics.** "On Deck" (Plex) and "Next Up" (Jellyfin) are meant to answer "which single episode,
per show, should I resume/start" — distinct from a raw "Continue Watching" row, which is for
in-progress (partially watched) items generally, including movies. The distinction matters: on some
Plex clients, "On Deck" has been observed collapsing into behaving identically to "Continue Watching"
(only showing partially-watched items, not surfacing the *next unwatched* episode once the current one
finishes) — a regression users explicitly reported and asked Plex to fix [4].

**Known failure modes (all sourced from official bug trackers/forums, not speculation):**
- Plex: after finishing an episode, On Deck / the Home screen sometimes doesn't update to the next
  unwatched episode until the user navigates away and back; more recently, focus after an episode ends
  lands on unrelated items underneath rather than the obvious "play next episode" affordance, requiring
  manual navigation [4].
- Plex: a server-version regression (~1.40.x) broke the on-deck reappearance of a show after a user
  manually marked an episode watched [4].
- Jellyfin: "Next Up" has documented bugs where it shows a random/wrong future episode instead of the
  true next one — root-caused in one GitHub issue to a fallback sort by an internal
  `PresentationUniqueKey` string rather than true episode order when metadata (air dates, episode
  numbers) is irregular (e.g., daily/non-numbered shows, non-standard filenames) [3].

**Takeaway for a from-scratch design:** because these are the three most-used home-server platforms and
still have *unsolved, actively-reported* next-episode bugs years into their existence, "compute the
correct next episode" is evidently a harder problem than it looks (edge cases: specials, multi-part
episodes, non-standard numbering, partially-aired seasons) and is worth deliberately testing against
those edge cases — which aligns with Mediatheca already having dedicated regression coverage for its
`NextUp.compute` frontier rule (per repo history) rather than treating it as a trivial "max watched + 1"
computation.

### 4. Simkl / Serializd / MyAnimeList-style status buckets
**Simkl** organizes its dashboard around: recently watched, "aired but not yet watched," a calendar of
upcoming episodes, and status-based lists (Watching / Plan to Watch / Dropped, etc.) plus an anime
section that mirrors MyAnimeList conventions [9]. Reception is mixed: one review liked the separated
lists but found the dashboard "a little cluttered and hard to navigate" [9]; other, more marketing-
adjacent sources describe it as "clean" — this is a genuine disagreement between sources, not resolved
by this research, and possibly reflects web vs. mobile-app differences (one complaint specifically
targeted mobile navigation) [9][13].

**Serializd** is explicitly positioned by its own reviewers as "a review diary first and a tracker
second" — strong on ratings/reviews/community discussion per season or episode, but a 2026 comparison
piece notes it "lacks calendars and reminders," i.e., it deliberately does not compete on the
next-up/airing-awareness axis that Trakt/Simkl/TV Time treat as core [10][15]. This is useful negative
evidence: a beloved-for-reviews app can skip airing/next-up features entirely and still have a devoted
niche following, because its value proposition is different (curation/critique, not tracking
mechanics).

**MyAnimeList's five-bucket model** (Watching / Completed / On Hold / Dropped / Plan to Watch) is the
de facto standard vocabulary that Simkl explicitly copies for anime and that shows up across the anime-
tracker ecosystem [9][13]. No source in this research offered qualitative praise or criticism of the
bucket model itself as a *dashboard UX* choice (as opposed to a *list-organization* choice) — it
appears to function well as a filing system but wasn't found described anywhere as a delightful
dashboard surface. This is a genuine evidence gap, not a confirmed finding either way.

### 5. Sonarr — calendar as the flagship surface
Sonarr's calendar (upcoming air dates across the monitored library) is widely referenced across
self-hosted tooling as a headline feature, including third-party syncs (sonarr-calendar-sync) and
integrations that pipe *arr calendars into Jellyfin's own home screen as sections [6][16]. However, this
research found no strong, sourced "this is *the* loved feature" testimonial with direct user quotes —
that claim should be treated as **plausible but not independently confirmed** by this pass; what *is*
confirmed is that the calendar is important enough to be re-exported into other tools' dashboards
(Jellyfin home sections plugin explicitly lists "upcoming sections from the *arrs" as an integration
target) [16]. Sonarr itself is a download-automation tool, not a personal-viewing tracker, so its
calendar answers "what's coming to my library," not "what have I watched" — a meaningfully different
question from Trakt/TV Time's progress-centric dashboards.

### 6. Airing-schedule awareness generally
Across Trakt, Simkl, Sonarr, and TV Time, some form of "what's airing / coming up" surface is present
and is treated as expected baseline functionality, not a differentiator — the differentiation is in
*execution* (air-time precision, notification delivery, VIP-gating) rather than existence [9][14]. The
strongest single piece of evidence that airing-awareness is centrally valued: multiple TV-Time-shutdown
migration guides frame "notifications so you know when shows return" as one of a short list (four to
six items) of things any credible tracker must do well, alongside one-tap marking and next-episode
surfacing [15].

### 7. Self-hosted / single-user patterns
- **Jellyfin** ships "Next Up" and "Continue Watching" as built-in home sections; going beyond that
  requires the community **Home Screen Sections plugin** (not in the official catalog, requires two
  companion plugins), which lets a self-hoster build Netflix-style rows ("Because You Watched," "Watch
  Again") and pull in Jellyseerr/*arr calendar sections — evidence that self-hosted single-user setups
  *do* want more dashboard richness than stock Jellyfin offers, but the community had to build it
  outside core [16].
- **Ryot** ("roll your own tracker") is a Rust-based, self-hosted, single-user-oriented tracker
  covering movies/TV/anime/manga/books/games; it explicitly ships a calendar view for release dates and
  new-episode notifications, and per-episode watched/unwatched tracking with reviews, but published
  materials don't detail its dashboard layout precisely enough to say which cards are shown — evidence
  is directional (feature list) rather than a UX walkthrough [17].
- **MediaTracker** (bonukai) is another self-hosted, single-user tracker spanning
  movies/TV/games/books/audiobooks, explicitly inspired by "flox"; available detail was limited to
  install/deploy documentation, not dashboard UX, so this is a named-but-unverified data point only [18].
- **Mediatheca's own domain context** (from CLAUDE.md / repo history) already treats "next up" as a
  first-class computed concern worth dedicated regression tests (the "frontier rule": gaps behind the
  furthest-watched episode are history, not a queue) — this is consistent with the Plex/Jellyfin
  evidence that naive "next unwatched episode" logic breaks on real-world edge cases, and suggests the
  frontier-rule design already anticipates a class of bug that mainstream self-hosted tools still ship
  with (per the Jellyfin GitHub issues) [3][7].

Overall, no self-hosted single-user tool in this research bundles gamification (streaks/badges/social
feed) at all — that layer appears specific to consumer, ad- or attention-funded apps (TV Time,
Serializd's community layer) and is absent from Trakt-as-sync-backend, Sonarr, Jellyfin, Ryot, and
MediaTracker. This is a reasonably strong, multi-source pattern: **single-user self-hosted tools
converge on next-up correctness + progress + calendar/notifications, and drop social/gamification
entirely** [6][15][16][17][18].

## Synthesis

**(a) Catalog of series-dashboard card/row types observed across apps:**
1. In-progress / "continue watching" row — partially-watched episodes, cross-show (Plex, Jellyfin,
   Trakt "recently watched") [1][4][7]
2. "Next up" / "Up next" / "On Deck" / "Watch Next" — one computed next-episode per actively-watched
   show (Trakt, TV Time, Plex, Jellyfin) [1][2][3][4][7]
3. Per-show progress indicator — percent complete, episodes remaining, time remaining (Trakt) [1][2]
4. Upcoming/airing calendar — episodes airing soon across the library, sometimes with air time (Trakt,
   Simkl, Sonarr) [6][9][14]
5. "Aired but not yet watched" backlog — episodes that have aired and are unwatched but not necessarily
   "next" (Simkl) [9]
6. Status-bucketed lists — Watching / Plan to Watch / On Hold / Dropped / Completed, as
   filters/sections rather than dashboard cards per se (Simkl, MyAnimeList, Serializd) [9][13]
7. Stats/summary — watch time, "last 30 days" activity, genre breakdown, year-in-review (Trakt,
   TV Time, Serializd) [1][2][10]
8. Gamification — streaks, badges, diary, social feed of friends'/strangers' activity (TV Time,
   Serializd community layer) [2][10]
9. Recommendations / discovery row — "because you watched," dashboard recommendations (Trakt added
   this to its dashboard in 2024; Jellyfin community plugin adds it) [16]

**(b) Consistently loved, with evidence:**
- Next-up/on-deck as a concept (universal, load-bearing; validated negatively by how loudly its
  breakage is complained about) [1][3][4][7]
- Per-show progress bars / episodes-and-time-remaining (Trakt, explicitly framed as core value and a
  reason to switch trackers) [1][2][12]
- Reliable background scrobbling/sync feeding the dashboard automatically, rather than manual entry
  (Trakt's Plex/Kodi/Jellyfin integration explicitly praised for "just working") [12]
- Airing calendars as a baseline expectation (present in every actively-praised tracker; absence is
  noted as a gap even for a beloved app like Serializd) [10][15]

**(c) Common complaints:**
- Wrong next-episode logic on non-standard content (daily shows, irregular numbering, specials) —
  Jellyfin, documented in GitHub issues [3]
- Stale/non-updating on-deck state after finishing an episode, or on-deck degrading into a duplicate of
  continue-watching — Plex, documented across multiple forum threads and years [4]
- Calendar surfaces that are "aware of the day but not the time" — Trakt [14]
- Cluttered/hard-to-navigate dashboards when many list types are shown at once without hierarchy —
  Simkl (contested by other reviewers, so treat as a real but not universal complaint) [9]
- Ads, redesign churn, and a social feed of strangers eroding goodwill over time — TV Time, cited
  repeatedly as reasons to leave even before the shutdown [2]
- Gamification perceived as filler/bloat rather than a value driver once the novelty wears off — TV
  Time retrospectives [15]

**(d) Single-user self-hosted vs. social/ad-funded apps — what's different:**
- No commercial pressure to maximize daily-active-use via streaks/badges/social feed — self-hosted
  tools studied (Ryot, MediaTracker, Jellyfin, Sonarr) carry none of that layer [6][16][17][18]
- Self-hosted tools lean harder on *correctness* and *automation* (scrobbling, calendar sync from
  *arr apps) because there's no community/social layer to paper over friction with engagement loops
  [6][16]
- A single user with full event-sourced history (as in Mediatheca) can afford *more* precise/complex
  next-up logic (e.g., the frontier rule) than commercial apps bother with, because there's no need to
  keep the algorithm simple enough to explain to millions of casual users — this is an opportunity
  the research doesn't contradict, only a design implication drawn from the pattern of documented bugs
  in mass-market tools [3][7]
- Discovery/recommendation rows are the one dashboard element that plausibly matters *less* in a
  single-user, already-curated library (with friend "recommended by" already modeled explicitly in
  Mediatheca) than in a general-audience app trying to surface a catalog the user hasn't chosen yet —
  this is inference, not sourced directly

**(e) The "abandoned / stalled show" problem — how it's handled (or not):**
- Every bucket-model app (Simkl, MyAnimeList) has a manual "Dropped" and/or "On Hold" status the user
  must actively set — there is no evidence in this research of any app *proactively* detecting and
  surfacing "you haven't touched this in N weeks, want to mark it dropped/on hold?" [9][13]
- This is a **genuine open gap** across the entire market segment researched, not a solved-and-copyable
  pattern. If Mediatheca wants to differentiate, this is white space: using the event-sourced watch
  history to compute staleness (e.g., "last episode watched > 60 days ago, and not marked in-focus")
  and surface it distinctly from the active "next up" queue (so an abandoned show doesn't visually
  compete with genuinely active watching) would be a novel-for-this-market feature, not a re-
  implementation of something users are already known to want (no evidence either way — see Open
  Questions).

## Sources
1. [Trakt.tv - Track What You're Watching – The Nerdy Student](https://www.thenerdystudent.com/2019/03/trakt/) — describes dashboard layout, progress bar per show, "up next" concept. 2019, dated but core mechanics haven't changed per newer sources.
2. [Trakt helps you keep track of your streaming shows | PCWorld](https://www.pcworld.com/article/2607062/trakt-helps-you-keep-track-of-your-streaming-shows.html) — independent description of Trakt's Up Next / progress features.
3. [Update on the Recent "Up Next" Change - Trakt Forums](https://forums.trakt.tv/t/update-on-the-recent-up-next-change/81139) — Trakt's own team post-mortem on Up Next scope change and user frustration; primary source.
4. [Random episodes in Next Up · Issue #13592 · jellyfin/jellyfin](https://github.com/jellyfin/jellyfin/issues/13592) and [Issue #9568](https://github.com/jellyfin/jellyfin/issues/9568) — primary bug reports on Jellyfin's Next Up sort logic.
5. (merged into 4 — same source family, Jellyfin GitHub issues)
6. [Sonarr Review | shareconnector.net](https://shareconnector.net/sonarr-review/) and [Jellyfin.Plugin.HomeScreenSections](https://www.nuget.org/packages/Jellyfin.Plugin.HomeScreenSections/) — Sonarr calendar positioning and its re-export into Jellyfin home sections.
7. [Workaround: Plex broke "Continue Watching"/"On Deck" · Issue #2136](https://github.com/croneter/PlexKodiConnect/issues/2136); [Plex Forums — On Deck functioning the same as Continue Watching](https://forums.plex.tv/t/on-deck-functioning-the-same-as-continue-watching/613128); [Plex Forums — Home screen On Deck not updating](https://forums.plex.tv/t/home-screen-on-deck-not-updating-after-playing-an-episode/885183) — primary bug reports on Plex On Deck semantics and regressions.
8. (merged into 7 — Plex forum thread family)
9. [Simkl Review - Track Your Shows – The Nerdy Student](https://www.thenerdystudent.com/2019/07/simkl-review/); [SIMKL vs Trakt — docs.simkl.org](https://docs.simkl.org/how-to-use-simkl/faq/frequently-asked-questions/simkl-alternatives/simkl-vs-trakt) — Simkl dashboard structure, status buckets, "cluttered" complaint vs. "clean" counter-claims.
10. [TV Time Shuts Down: Whip Media's AI Pivot Ends a 26-Million-User Community — TechTimes](https://www.techtimes.com/articles/320754/20260716/tv-time-shuts-down-whip-medias-ai-pivot-ends-26-million-user-community.htm); [TechCrunch — Popular TV-tracking app TV Time is shutting down](https://techcrunch.com/2026/07/02/popular-tv-tracking-app-tv-time-is-shutting-down-as-company-focuses-on-ai/) — shutdown facts, scale, reasons, user reaction. July 2026.
11. [TV Time is shutting down its service on July 15, 2026 — AlternativeTo](https://alternativeto.net/news/2026/7/tv-time-is-shutting-down-its-service-on-july-15-2026-here-are-some-great-replacements/) — corroborating shutdown coverage and migration options.
12. [Trakt vs TV Time — Achriom](https://www.achriom.com/blog/trakt-vs-tv-time/) and [TV Time vs Trakt — Moviebase](https://moviebase.app/resources/tv-time-vs-trakt) — commercial/aggregator sources; framed as marketing-adjacent, used only for directionally-corroborated claims (reliability, mobile polish).
13. MyAnimeList status categories — findings drawn from search-result aggregation of live MyAnimeList list pages (myanimelist.net/animelist/*) rather than a single authoritative doc; treat as descriptive/observed, not an official MAL statement.
14. [The new calendar is dreadful as there's no air times — Trakt Forums](https://forums.trakt.tv/t/the-new-calendar-is-dreadful-as-theres-no-air-times-like-with-the-old-calendar-can-you-fix-that/103644); [Trakt Calendars — Trakt Forums](https://forums.trakt.tv/t/trakt-calendars/19099) — user complaints about calendar redesign losing air-time detail.
15. [7 best TV show tracker apps in 2026, ranked honestly — Hobi Blog](https://hobiapp.com/blog/best-tv-show-tracker-apps) — synthesis piece naming "one-tap marking," calendars/reminders, and next-episode-on-open as the essential functions; explicitly notes Serializd lacks calendars/reminders. Note: Hobi is itself a competing tracker app, so treat feature-priority framing as informed-but-interested.
16. [New Home Screen Section Type - Pinned Collections Proposal · jellyfin-meta Discussion #93](https://github.com/jellyfin-meta/discussions/93); [GitHub - IAmParadox27/jellyfin-plugin-home-sections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) — community plugin evidence that stock Jellyfin home sections (Next Up, Continue Watching) are felt to be insufficient by power users.
17. [Ryot — Features](https://ryot.io/features/) and [GitHub - IgnisDa/ryot](https://github.com/ignisda/ryot) — self-hosted single-user tracker feature list (calendar, notifications, per-episode tracking); dashboard layout not documented in detail.
18. [GitHub - bonukai/MediaTracker](https://github.com/bonukai/MediaTracker) — self-hosted single-user tracker, install docs only; named for completeness, feature/UX detail not independently verified.

## Open questions
- **No dashboard-layout screenshots/walkthroughs were fetched** for Ryot or MediaTracker — only
  feature-list marketing copy. If precise card layout matters for the redesign, a follow-up pass
  should pull GitHub README screenshots or a demo instance for both.
- **The "abandoned/stalled show" problem has no known solved precedent** in this research set — this
  should be treated as an open design problem for Mediatheca, not something to copy from a competitor.
  Worth a dedicated design spike rather than more research (the research bottleneck here is that no one
  seems to have shipped this well, not that we haven't found the article about it).
- **Status-bucket dashboards (MyAnimeList-style) were not found to be independently praised or
  panned as a dashboard UX** — only as an organizational/filing convenience. If the redesign considers
  adopting Watching/On Hold/Dropped buckets as dashboard sections (vs. filters), that's a design bet
  without strong external validation either way from this pass.
- **Trakt's 2024 "dashboard recommendations" feature** (mentioned once, via AlternativeTo news) wasn't
  explored in depth — unclear how it's received or how prominent it is on the dashboard vs. a secondary
  page.
- **TrustPilot's TV Time review page returned HTTP 403** and could not be fetched directly; complaint
  characterization for TV Time rests on secondary aggregation (JustUseApp summary, TechCrunch/TechTimes
  shutdown coverage) rather than a first-hand read of raw reviews. Treat TV Time complaint specifics as
  reasonably but not maximally confident.
