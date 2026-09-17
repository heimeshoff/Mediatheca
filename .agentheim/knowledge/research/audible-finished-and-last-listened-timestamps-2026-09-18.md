---
topic: Whether any unofficial Audible API surface returns a timestamp for "finished" or "last listened" (not just percent/boolean)
date: 2026-09-18
requested_by: user
related_tasks: []
---

# Research: Audible "finished" and "last listened" timestamps

## Question

Is there any way to determine via the (unofficial) Audible API *when* the user finished a book,
or *when* they last listened to a book? A timestamp/date is needed, not just a percent or a
boolean. This builds directly on
`.agentheim/knowledge/research/audible-api-surface-and-listening-progress-2026-09-16.md`, which
established that `/1.0/library` has `percent_complete`/`is_finished`/`listening_status` but not
`last_position_heard`, and left the exact shapes of `listening_status`, `last_position_heard`,
and the `/1.0/stats/*` group as open questions.

## Summary

- **Yes for "last listened," with a confirmed real-world example: `GET /1.0/content/{asin}/metadata`
  with `response_groups=last_position_heard` returns `last_position_heard.last_updated`, a genuine
  timestamp** (`"2023-09-23 21:03:18.228"` in the one real captured response found), alongside
  `position_ms` and a `status` of `"Exists"`/`"DoesNotExist"` [1][2]. This is the single most
  decision-relevant, best-evidenced finding in this report — it is a real API response pasted by a
  user in a GitHub discussion and independently cross-checked against `audible-cli`'s own source
  code, which builds exactly this call [1][2].
- **No for "finished," as a clean confirmed field.** `is_finished` on `/1.0/library` is a plain
  boolean with no accompanying date, and nothing in the `/1.0/library` response_groups list carries
  a finish date — this reconfirms the prior report [3]. The only endpoint whose *name* strongly
  implies a per-title finish date, `GET/POST /1.0/stats/status/finished`, has a **completely
  undocumented response shape** — its query/body parameters (`asin`, `start_date`,
  `continuation_token`) are confirmed to exist, but no sample response was found anywhere in this
  pass. This is the single highest-value unresolved lead [4][5].
- **`listening_status`'s shape remains genuinely unknown after this entire research pass.**
  `audible-cli`'s own source code requests it in `response_groups` but never parses or exports it
  as a column — no tool in the whole ecosystem surveyed (this pass or the prior one) is observed
  reading a sub-field of it. Treat it as functionally opaque, not just under-documented [6].
- **A second, batched candidate exists for "last listened": `GET /1.0/annotations/lastpositions?asins=a,b,c`**,
  confirmed real and used in production by a third-party project (AudioSilo) to reconcile listening
  positions at scale, chunked because a whole library in one request 400s/414s [7][8]. **Its
  response shape is not confirmed to include a timestamp** — the one practitioner doc found names
  only `position_ms` as the field they consume from it, and that same project computes "finished"
  itself via a runtime-proximity heuristic rather than trusting an Audible-supplied date [8]. This
  is the report's biggest unresolved gap: it is unknown whether the batched endpoint carries the
  same `last_updated` field the single-ASIN metadata endpoint does.
- **The Audible app's own "Listen Log" (per-title, session-level, exact dates/times) and "Listening
  Stats" (aggregate, monthly) are real, official, user-facing features — but both are confirmed
  app/website-only with no export or API, per Audible's own help pages** [9][10]. Not usable by
  Mediatheca.
- **A prior over-broad claim is corrected here**: the docs state only `POST
  /1.0/content/{asin}/licenserequest` fails under bearer auth — not `/1.0/annotations/lastpositions`
  or `/1.0/content/{asin}/metadata`, contrary to one search-summary's claim that "library, license,
  and annotations" all require signed requests [11]. Mediatheca's existing bearer-only auth
  plumbing (`Audible.fs`'s `sendAuthenticated`) is plausibly sufficient for the metadata and
  annotations paths — **untested against a real account, so treat as "plausible, not confirmed."**

## Findings

### 1. `last_position_heard` — the one confirmed timestamp, and exactly where it lives

The prior report already established that `last_position_heard` is a `response_groups` value on
two endpoints: `POST /1.0/content/{asin}/licenserequest` and `GET /1.0/content/{asin}/metadata` —
never on `/1.0/library` [3]. This pass adds the actual shape and confirms which of the two paths is
the safer one to use.

**The shape, from a real captured API response.** A user in a `mkb79/audible-cli` GitHub discussion
pasted their tool's raw JSON output while debugging a missing-`chapter_info` bug; the response
included, verbatim:
```
"last_position_heard": {
    "last_updated": "2023-09-23 21:03:18.228",
    "position_ms": 896068,
    "status": "Exists"
}
```
and, in the corrected/working example the maintainer supplied for comparison, the "never listened"
case:
```
"last_position_heard": {
    "status": "DoesNotExist"
}
```
[1]. This is a genuine, single real-world sample — not a doc-page schema listing — so it should be
treated as **strong but single-source** evidence for the exact field names. `last_updated` is
unambiguously a "when was this listening position last saved" timestamp, i.e. as close to
"last listened" as this API surface gets.

**Which endpoint actually produces it, confirmed against source code.** `audible-cli`'s
`models.py` (the code that powers `audible download --chapter`, the exact command the discussion
above was about) shows `LibraryItem.get_content_metadata()` calling:
```
GET content/{asin}/metadata
params = {
    "response_groups": "last_position_heard, content_reference, chapter_info",
    "quality": <quality>,
    "drm_type": "Adrm",
    "chapter_titles_type": <chapter_type>,
}
```
[2] — this directly ties the confirmed JSON sample [1] to the `GET /1.0/content/{asin}/metadata`
path from the docs [3], not the `POST .../licenserequest` path. **This distinction matters for
risk**: `GET .../metadata` is a plain metadata read; `POST .../licenserequest` is Audible's actual
DRM-license-granting call (the same one `audible-cli download` uses to obtain a downloadable file)
[2][12]. Getting `last_position_heard` via the GET metadata call, rather than by issuing a license
request nobody intends to act on, avoids touching the download/license-throttling surface that
Libation's own docs warn can trigger "License not granted"/daily-download-limit style denials under
heavy use [13] — that throttling is documented as being about concurrent/volume *downloads*, not
proven to apply to a metadata read, but the license-request path is the one with a documented
throttle risk at all, and the metadata path isn't implicated in any throttling report found.

**Auth mode.** The docs state, as the *only* explicitly named bearer-incompatible call: "Some API
call, like the `POST /1.0/content/(string:asin)/licenserequest`, doesn't work" under bearer auth
[11]. `GET /1.0/content/{asin}/metadata` is not named as requiring the signed-request (RSA-SHA256)
method. This is an absence-of-restriction, not a positive "bearer works here" statement from the
docs — genuinely untested against a real account in this research pass — but it is consistent with
Mediatheca's own `Audible.fs`, which already calls `/1.0/library` (also not on the
bearer-incompatible list) successfully with bearer-only auth in production [14].

### 2. The batched alternative: `GET /1.0/annotations/lastpositions`

The full endpoint enumeration on the primary docs page (re-fetched and cross-checked against the
raw `.rst` source on GitHub, both independently listing the same path) confirms two endpoints not
covered by the prior report's endpoint inventory:
```
GET /1.0/annotations/lastpositions
    :query asins: asin (comma-separated), e.g. ?asins=B01LWUJKQ7,B01LWUJKQ7,B01LWUJKQ7
PUT /1.0/lastpositions/(string:asin)
    :<json acr: obtained by POST /1.0/content/(string:asin)/licenserequest
    :<json asin:
    :<json position_ms:
```
[7][15] (the two independent fetches — the rendered HTML docs page and the raw GitHub `.rst`
source — agree verbatim on both endpoints' documented parameters).

**This is real and used in production**, not just documented-but-unused: a third-party project,
AudioSilo, documents using exactly `GET /1.0/annotations/lastpositions` (its own docs call it
`Client.LastPositions`) to reconcile Audible listening positions against its own server-side
progress, and specifically notes it must **chunk the ASIN list** because "a whole library in one
query takes [a long time]; the endpoint also returns a 400 error for an over-long list," converging
on an empirical per-request ASIN cap by splitting and retrying on 400 [8]. This is a second,
independent practical confirmation the endpoint exists and returns usable per-ASIN position data at
library scale, beyond the bare docs-page listing.

**Response shape — the key open gap.** Neither the official docs page nor AudioSilo's own
documentation gives a field-level response schema for this endpoint. AudioSilo's docs mention only
`position_ms` as the field consumed from it ("tolerant of number-or-string `position_ms`") [8]; no
`last_updated` or similar timestamp field is mentioned for this specific endpoint anywhere found.
Tellingly, **AudioSilo computes "finished" itself** via a heuristic — "a position at the end of the
book is written as finished... within 2% of, or 3 minutes from, the library's `runtime_length_min`"
[8] — rather than reading any Audible-supplied finished-date or last-listened-date from this
endpoint. That is circumstantial (a real project choosing not to rely on a field is not proof the
field doesn't exist), but it is the closest thing to negative evidence found: a project with every
incentive to use a timestamp, if one were reliably present in this response, apparently didn't.
**Whether `annotations/lastpositions` carries the same `last_updated` field the single-ASIN
metadata endpoint does is unconfirmed and unresolved by this research pass.**

### 3. `listening_status` — shape still unresolved

Re-confirmed present in `/1.0/library`'s (and `/1.0/catalog/products`' — for wishlisted/owned items)
`response_groups` list [3][6]. This pass specifically searched `audible-cli`'s own `models.py` for
every place it touches `listening_status` — the only occurrences are the two literal string
mentions inside the `response_groups` request strings themselves; **there is no code anywhere in
the file that reads a sub-field of the returned `listening_status` object**, and it is not one of
the columns `audible library export` exposes (`asin, title, subtitle,
extended_product_description, authors, narrators, series_title, series_sequence, genres,
runtime_length_min, is_finished, percent_complete, rating, num_ratings, date_added, release_date,
cover_url, purchase_date`) [6][16]. No GitHub issue/discussion in `mkb79/audible` or `audible-cli`
discussing its shape surfaced despite targeted searches. **Conclusion: `listening_status` should be
treated as functionally opaque** — present in the API, requestable, but nobody in the surveyed
open-source ecosystem is observed doing anything with its contents, so no claim about whether it
contains a date can be made either way.

(Side note, corroborating the prior report: `date_added` in the export-column list above is the
library-export tool's own local cache timestamp of when the CLI first saw the item, not an
Audible-supplied listening date — don't confuse the two if this field name is encountered again.)

### 4. The `/1.0/stats/*` group

Confirmed parameter lists (cross-checked between a rendered-docs fetch and the docs' own worked
example) [4][5][17]:
```
GET /1.0/stats/aggregates
    ?daily_listening_interval_duration (0-30)
    &daily_listening_interval_start_date (YYYY-MM-DD)
    &locale (e.g. en_US)
    &monthly_listening_interval_duration (0-12)
    &monthly_listening_interval_start_date (YYYY-MM)
    &response_groups=[total_listening_stats]
    &store ([AudibleForInstitutions, Audible, AmazonEnglish, Rodizio])
```
The docs' own worked example —
`client.get("1.0/stats/aggregates", monthly_listening_interval_duration="3",
monthly_listening_interval_start_date="2021-03", store="Audible")` [17] — takes **no `asin`
parameter at all**. This strongly confirms the endpoint is **account-level aggregate listening time
over a period (day/month), not per-title data** — consistent with the "aggregate" in its own path
segment. **Not a source of a per-book finished or last-listened date.**

```
GET  /1.0/stats/status/finished  ?asin  &start_date (RFC3339, e.g. 2000-01-01T00:00:00Z)
POST /1.0/stats/status/finished  {start_date, status, continuation_token}
PUT  /1.0/stats/events           {stats: [...]}
```
[4][5]. Unlike `stats/aggregates`, this endpoint **does** accept an `asin` parameter and is named
`status/finished` — both are suggestive of "list of titles marked finished, optionally filtered to
one ASIN, since some date, paginated via `continuation_token`." If it returns a finish
*timestamp* per item (not just a boolean membership list), this would be the cleanest source of a
"finished date" the whole research question is looking for. **No response schema, sample JSON, or
any tool/project observed actually calling and printing this endpoint's output surfaced anywhere in
this pass** — not in the docs, not in `audible-cli`, not in AudioSilo's docs, not in any blog post
or GitHub issue found. This is a documented-but-completely-unexercised endpoint as far as this
research could establish. **Flag as the single most promising unresolved lead** — worth an
empirical test call before ruling it out, but currently unconfirmed in every direction (both
"it has a date" and "it doesn't" are equally unevidenced).

`PUT /1.0/stats/events` is a write endpoint (the client posting its own listening telemetry back to
Audible) — not a read source for anything, mentioned only for completeness.

### 5. Official app/website features — real, but not API-reachable

Two distinct, confirmed-real, official Audible features exist and were checked for an API/export
path, per Audible's own help pages:

- **"Listen Log"** (per-title): "the date and time you started listening, when you paused, and how
  long your listening session lasted" [9]. Explicitly **app-only** and explicitly **not
  exportable**: "Listen Log data cannot be downloaded or exported" [9]. A secondary practitioner
  source corroborates the UI mechanics (opening a title's three-dot menu to view it) [18].
- **"Listening Stats"** (account-level): "insights on your listening behavior," monthly-refreshed
  "top genres," a "Download PDF" option for the aggregate stats view, a "Share my stats" social
  feature [10]. No mention of per-title dated data or any API/export beyond the PDF of aggregate
  stats.

Neither is reachable by `mkb79/audible`-style API calls — they are UI-rendered features with no
documented backing endpoint found in this pass (the Listen Log in particular reads as
client-side-only session logging, distinct from anything in the `1.0/*` API surface catalogued
above).

**A manual, non-scalable workaround exists**: manually adding a bookmark/note (e.g. "Finished
[title]") when finishing a book auto-timestamps that bookmark, and it can be read back later via the
UI [19]. This only works going forward, for books the user remembers to bookmark, and there is no
confirmed API to read bookmark timestamps back programmatically — one single-source mention
surfaced of a separate, differently-hosted endpoint (`cde-ta-g7g.amazon.com/FionaCDEServiceEngine/sidecar?type=AUDI&key={asin}`)
returning clips/notes/bookmarks for a book [20], but its response shape (whether bookmark
timestamps are present/parseable) was not independently confirmed in this pass — **flag as
unverified and out of scope of the main `api.audible.<tld>` surface** this project already
integrates with.

### 6. Sort order as a recency proxy — reconfirmed absent

Directly re-fetched and cross-checked (two independent fetches of the same docs page, both agreeing
verbatim): `/1.0/library`'s `sort_by` accepts exactly `-Author, -Length, -Narrator, -PurchaseDate,
-Title, Author, Length, Narrator, PurchaseDate, Title` [3][21]. **No `LastListened`/`LastHeard`
value exists.** Sorting cannot be used as an indirect recency signal; this fully reconfirms the
prior report with no new information.

### 7. What this means for Mediatheca

- **The one clean, confirmed timestamp path is `GET /1.0/content/{asin}/metadata?response_groups=last_position_heard`,
  authenticated (bearer likely sufficient, untested), returning `last_position_heard.last_updated`
  per ASIN** [1][2]. This is a per-ASIN call, so a full-library "last listened" sync would need one
  extra authenticated HTTP call per known book, on top of the single paginated `/1.0/library` call
  `AudibleSync.fs` already makes — a real N+1 cost worth weighing against how often "last listened"
  actually needs refreshing (e.g. only for books currently `percent_complete` between 1–99, not the
  whole library, would cut this down a lot).
- **`GET /1.0/annotations/lastpositions?asins=a,b,c,...` is the batched alternative** and is proven
  to work at library scale by a real third-party project [7][8] — but whether its response includes
  a timestamp at all is the single biggest open question in this report. If it turns out to carry
  the same `last_updated` field, it would be strictly better than the per-ASIN metadata calls
  (fewer requests, chunked rather than N+1). **This needs an empirical test against a real Audible
  account before it can be relied on** — nothing found in secondary sources settles it.
- **There is still no confirmed source of a "finished date."** `is_finished` remains boolean-only.
  `GET/POST /1.0/stats/status/finished` is the one lead whose name and parameter shape (`asin`,
  `start_date`, pagination) is suggestive of exactly the missing data, but it is completely
  unexercised in every source this research could find — the honest position is "we don't know,"
  not "it doesn't exist" or "it does."
- **Auth mechanics are probably not a blocker for the confirmed path.** The docs name only `POST
  /1.0/content/{asin}/licenserequest` as bearer-incompatible [11] — not the `GET .../metadata` path
  that actually carries `last_position_heard` in the confirmed sample. Mediatheca's `Audible.fs`
  already authenticates `/1.0/library` with bearer-only, no `client-id` header, in production [14],
  which is structurally consistent with (though not proof of) the metadata endpoint also working
  bearer-only. Mediatheca's imported `AudibleAuthFile` already carries `AdpToken` and
  `DevicePrivateKey` [22] — the ingredients for RSA-SHA256 signed requests are already on hand if a
  future need (e.g. actually calling `POST .../licenserequest`) required them, but nothing in this
  report's findings requires that heavier auth mode.
- **Avoid `POST /1.0/content/{asin}/licenserequest` for this purpose even though it's documented to
  carry `last_position_heard` too** — it is Audible's actual DRM-license-issuing call, the same one
  download tools use, and is the one surface in this whole research pass with any documented
  throttling/denial risk under repeated use [12][13]. The `GET .../metadata` path gets the identical
  field without that risk.

## Sources

1. [KeyError: 'chapter_info' · mkb79/audible-cli · Discussion #156](https://github.com/mkb79/audible-cli/discussions/156) — a real user-pasted raw API response containing `last_position_heard.last_updated`/`position_ms`/`status`, plus the maintainer's corrected `"status": "DoesNotExist"` example. Primary/direct evidence, single real sample (not a doc schema).
2. [audible_cli/models.py](https://raw.githubusercontent.com/mkb79/audible-cli/master/src/audible_cli/models.py) — `LibraryItem.get_content_metadata()`, quoted directly: `GET content/{asin}/metadata` with `response_groups="last_position_heard, content_reference, chapter_info"`; also the `Library.from_api()` default `response_groups` list (confirms `listening_status` is requested but not further parsed anywhere in the file).
3. [External Audible API — audible documentation](https://audible.readthedocs.io/en/latest/misc/external_api.html) — re-fetched this pass; `/1.0/library` `response_groups` list, `sort_by` values, confirms `last_position_heard` is not among them.
4. Same as [3] — `/1.0/stats/aggregates`, `/1.0/stats/status/finished` (GET/POST), `/1.0/stats/events` parameter lists, re-fetched and quoted directly.
5. [Audible/docs/source/misc/external_api.rst](https://github.com/mkb79/Audible/blob/master/docs/source/misc/external_api.rst) — raw `.rst` source, fetched a second time via `?plain=1` to cross-check the `annotations/lastpositions`/`lastpositions/{asin}` sections verbatim against the rendered docs page; independently agrees.
6. [audible_cli/cmds/cmd_library.py](https://raw.githubusercontent.com/mkb79/audible-cli/master/src/audible_cli/cmds/cmd_library.py) — the exact `library export` column list, quoted directly; confirms no `listening_status` column and no finish/last-listened date column exists in the export.
7. Full endpoint enumeration re-fetched directly from [External Audible API — audible documentation](https://audible.readthedocs.io/en/latest/misc/external_api.html) (all 51 documented endpoint paths listed in order) — confirms `GET /1.0/annotations/lastpositions` and `PUT /1.0/lastpositions/(string:asin)` exist as documented endpoints, distinct from `last_position_heard` on the content/metadata path.
8. [Audible backup pipeline | AudioSilo](https://docs.audiosilo.app/developers/manager/audible) — real third-party project's own developer docs; confirms `GET /1.0/annotations/lastpositions` used in production at library scale (chunked due to 400/URL-length limits), names `position_ms` as the field consumed, and describes computing "finished" via a runtime-proximity heuristic rather than an Audible-supplied date — the closest available evidence (circumstantial, not conclusive) on whether this endpoint carries a timestamp.
9. [View listening log — Audible Help](https://help.audible.com/s/article/view-listening-log?language=en_US) — official Audible source; confirms per-title session dates/times, app-only, explicitly not exportable.
10. [View Your Listening Stats — Audible Help](https://help.audible.com/s/article/view-your-listening-stats?language=en_US) — official Audible source; confirms account-level aggregate stats feature, PDF download of aggregates only, no per-title dated export.
11. [Authentication — audible documentation](https://audible.readthedocs.io/en/latest/auth/authentication.html) — re-fetched directly; confirms the *only* explicitly named bearer-incompatible call is `POST /1.0/content/(string:asin)/licenserequest` — corrects an earlier, broader "library/license/annotations all need signed requests" claim from a lower-quality search summary.
12. [audible_cli/cmds/cmd_download.py](https://raw.githubusercontent.com/mkb79/audible-cli/master/src/audible_cli/cmds/cmd_download.py) — confirms `get_content_metadata()` (the GET metadata call) is the download command's own chapter-fetch path, distinct from the license-granting download flow.
13. Libation/`audible-cli` issue threads on license denial under heavy concurrent download/license-request use (search-summary sourced, not independently fetched verbatim in this pass) — **single-source/secondary, treat with caution**; cited only to support "avoid the licenserequest path if a side-effect-free alternative exists," not for any specific numeric threshold.
14. `src/Server/Audible.fs` and `src/Server/AudibleSync.fs` (this project's own code, read directly) — confirms Mediatheca's existing `/1.0/library` calls succeed with bearer-only auth (no `client-id` header) in production, cited as circumstantial support that the metadata/annotations paths (also not on the bearer-incompatible list) are plausibly reachable the same way.
15. Same as [5].
16. Same as [6].
17. Docs' own worked example for `/1.0/stats/aggregates` (`client.get("1.0/stats/aggregates", monthly_listening_interval_duration="3", monthly_listening_interval_start_date="2021-03", store="Audible")`), from [Examples — audible documentation](https://audible.readthedocs.io/en/latest/misc/examples.html) — confirms the endpoint takes no `asin` parameter, supporting the "account-level aggregate, not per-title" conclusion.
18. [Learn How To Find When You Finished An Audible Audiobook!](https://bookwritten.com/how-to-find-when-finished-audible-audiobook/9131/) — practitioner blog corroborating the bookmark-timestamp manual workaround and confirming no API/export is involved; secondary source.
19. Same as [18].
20. Search-summary reference to `GET https://cde-ta-g7g.amazon.com/FionaCDEServiceEngine/sidecar?type=AUDI&key={asin}` for clips/notes/bookmarks — **single-source, ⚠️ UNVERIFIED (not independently fetched or shape-confirmed in this pass; different host than the `api.audible.<tld>` surface this project already uses)**.
21. Second independent fetch of [External Audible API — audible documentation](https://audible.readthedocs.io/en/latest/misc/external_api.html) confirming the `sort_by` value list verbatim, matching the prior report and source [3].
22. `src/Server/Audible.fs` `AudibleAuthFile` type (this project's own code) — confirms `AdpToken`/`DevicePrivateKey` are already imported fields, relevant to whether signed-request auth would be a heavier lift or not if ever needed.
23. `.agentheim/knowledge/research/audible-api-surface-and-listening-progress-2026-09-16.md` (internal, not a web source) — the prior report this one builds directly on; re-read in full before starting this research and not re-litigated where it already settled a question (e.g. `/1.0/library`'s full `response_groups` list, the account-risk profile, Audnexus, cover art).

## Unverified claims

- ⚠️ UNVERIFIED: `GET /1.0/annotations/lastpositions` returns (or does not return) a per-item
  timestamp alongside `position_ms`. No source found states this either way with a real sample;
  AudioSilo's practice of computing "finished" itself is circumstantial, not conclusive, evidence
  leaning toward "probably no reliable timestamp there," but this is an inference, not a finding.
- ⚠️ UNVERIFIED: whether `GET /1.0/content/{asin}/metadata` genuinely works under bearer-only auth
  (as opposed to requiring the signed-request/RSA mode). The docs only explicitly forbid bearer for
  `POST .../licenserequest`; everything else is absence-of-restriction, not a positive statement,
  and this was not empirically tested against a real Audible account in this research pass.
- ⚠️ UNVERIFIED: the `cde-ta-g7g.amazon.com` sidecar endpoint's response shape for bookmarks/clips
  and whether it carries usable timestamps — single search-summary mention, not independently
  fetched.
- ⚠️ UNVERIFIED: any response shape at all for `GET/POST /1.0/stats/status/finished` — this is
  flagged prominently above as the highest-value gap, not smoothed over.

## Open questions

- Does `GET /1.0/annotations/lastpositions` carry a timestamp per ASIN? This is the single most
  actionable open question — resolving it (via an empirical test call against a real, already-owned
  Audible account, not a new one) would determine whether a batched "last listened" sync is
  possible at all, versus needing N+1 per-ASIN metadata calls.
- What does `GET/POST /1.0/stats/status/finished` actually return? No sample exists anywhere found
  in two full research passes now. An empirical test (one real call, `asin` + a far-past
  `start_date`) would resolve this quickly and is the highest-value single next step if a
  "finished date" feature is worth building.
- Does `listening_status`'s object shape ever get documented or exercised anywhere? Two research
  passes (this one and the 2026-09-16 report) have both come up empty; it may simply be legacy/
  redundant with `is_finished`+`percent_complete`, or it may hold something nobody in this
  ecosystem has bothered to read — genuinely unknown.
- Rate-limit/throttle behavior specifically for `GET .../metadata` or `GET
  /1.0/annotations/lastpositions` called once per book on a daily cadence — no source found
  addresses this directly; the only throttling evidence found (Libation/`audible-cli` license-denial
  reports) is about the DRM license-request/download path, which this report's recommended approach
  deliberately avoids, but that inference hasn't been stress-tested against real volume.
