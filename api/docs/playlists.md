# Linkbelli API — Playlists & Items

All endpoints below require authentication (bearer token or API key — see [auth.md](auth.md))
and operate only on the caller's own playlists. Responses are JSON; lists are paginated with
an opaque `nextCursor` (pass it back as `?cursor=` for the next page; `null` means no more).
Enum values such as `visibility` are strings: `Private`, `Unlisted`, `Public`.

All paths are under **`/api/v1`**. Reads require the `playlists:read` scope and writes the
`playlists:write` scope (only relevant for scoped API keys — see [auth.md](auth.md#scopes)).

## Playlists

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/api/v1/playlists`       | — (`?limit=`, `?cursor=`, `?tag=`) | List your playlists (recently updated first) |
| `POST`   | `/api/v1/playlists`       | `name`, `description?`, `visibility?`, `tags?` | Create (slug auto-generated, unique per user) |
| `GET`    | `/api/v1/playlists/{id}`  | — | Get one |
| `PATCH`  | `/api/v1/playlists/{id}`  | `name?`, `description?`, `visibility?`, `tags?` | Update provided fields (slug is stable) |
| `DELETE` | `/api/v1/playlists/{id}`  | — | Soft delete |

- **Ordering:** the list is sorted by *recent activity* — the later of the playlist's creation
  and its newest item — so playlists that just received links float to the top.
- A playlist reports `averageScore` and `scoredCount`, averaged over the items that were
  actually rated — counting unrated ones as zero would say something false about the playlist.
- **`tags`** are free text; they're normalized (trimmed, lowercased, de-duplicated, max 25). On
  update, sending `tags` **replaces** the whole set. Filter your list with `?tag=` — repeat it
  (`?tag=a&tag=b`) to require **all** of them (AND).

```bash
curl -X POST http://localhost:5180/api/v1/playlists \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"name":"My Reading List","visibility":"Public","tags":["tech","ai"]}'
# -> { "id":"...", "name":"My Reading List", "slug":"my-reading-list",
#      "visibility":"Public", "itemCount":0, "creationTime":"...", "tags":["tech","ai"] }
```

### How you look at a playlist

Sort, filters and what the rows show are saved **per account**, not per browser — so they follow
you to another device. A playlist read carries the caller's saved `view`, or null when they have
none, which means opening a playlist needs no second round trip.

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `PUT` | `/api/v1/playlists/{id}/view` | `sort?`, `source?`, `status?`, `showUrls`, `showThumbnails`, `viewMode?` | Replace the saved view |

`viewMode` is `table` or `grid` — a reading queue reads best as a list, a playlist of videos does
not. Null means the client applies its own default rather than the server guessing one.

The body replaces the whole view rather than patching it, so a client sends the state it wants.
Anonymous readers have no account to save against and fall back to a browser cookie.

### Tags

Playlist tags describe a list. **Item tags** describe the link itself, which is what makes
something findable across the lists it happens to sit in — `PATCH /items/{id}` takes a `tags`
array that replaces the whole set, and search takes `?itemTag=` (repeatable, AND). Both kinds
share the same globally deduplicated tag rows.

Tags are stored normalized and shared across the system (deduplicated by name), so they can be
listed and counted globally.

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `GET` | `/api/v1/tags`        | yours       | Tags across **your** playlists, with counts (`?q=` prefix filter) |
| `GET` | `/api/v1/public/tags` | anonymous   | Tags across **public** playlists, with counts (`?q=` prefix filter) |

```bash
curl "http://localhost:5180/api/v1/tags?q=te" -H "Authorization: Bearer <token>"
# -> [ { "name":"tech", "playlistCount":3 }, ... ]
```

## Links (create-only)

`POST /api/v1/links` ensures a link exists in the system and returns it. Because links are
global and deduplicated, this is a get-or-create: the URL is canonicalized (lowercased host,
tracking params and fragment stripped, query sorted) and an existing match is returned
rather than duplicated. New links come back `enriched: false` and are enriched
asynchronously (title/thumbnail/etc.) shortly after. Requires the `links:write` scope.

```bash
curl -X POST http://localhost:5180/api/v1/links \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/article"}'
# -> { "id":"...", "url":"https://example.com/article", "host":"example.com",
#      "title":null, "thumbnailUrl":null, "enriched":false }
```

> Path case is significant: `/Article` and `/article` are different links.

### Preview (before saving)

`POST /api/v1/links/preview` fetches a URL's metadata **without saving anything** — for a
paste → preview → confirm flow. Best-effort: if the page can't be fetched (blocked, offline,
non-HTML) you still get the canonical URL back with `null` metadata. Rate-limited; `links:write`.

```bash
curl -X POST http://localhost:5180/api/v1/links/preview \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/article"}'
# -> { "canonicalUrl":"https://example.com/article", "host":"example.com",
#      "title":"…", "description":"…", "imageUrl":"…", "siteName":"…" }
```

## Items

A link is added by URL; the URL is canonicalized (lowercased host, tracking params and
fragment stripped, query sorted) and deduplicated globally. Adding a URL already present in
the playlist returns **409**. New links come back with `enriched: false`; a background
worker then fetches the page (through an SSRF-protected client) and fills in
title/description/thumbnail/site, flipping `enriched` to `true` — usually within seconds.

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/api/v1/playlists/{id}/items` | — (`?limit=`, `?cursor=`) | List items in order |
| `POST`   | `/api/v1/playlists/{id}/items` | `url`, `note?` | Add a link to the end |
| `PATCH`  | `/api/v1/items/{id}`           | `note?`, `status?`, `tags?` | Update an item's note, status or tags |
| `DELETE` | `/api/v1/items/{id}`           | — | Soft delete (remove from playlist) |
| `POST`   | `/api/v1/items/{id}/move`      | `afterItemId?` | Reorder: place after the given item; `null` = move to front |

```bash
# add
curl -X POST http://localhost:5180/api/v1/playlists/<id>/items \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/article","note":"read later"}'

# reorder to the front
curl -X POST http://localhost:5180/api/v1/items/<itemId>/move \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"afterItemId":null}'
```

Item ordering uses gapped integer positions, so a move is normally a single-row update; the
server transparently renumbers a playlist if a gap runs out.

### Acting on a selection

`POST /api/v1/items/bulk` applies one action to up to **500** items at once.

```jsonc
{ "itemIds": ["…"], "action": "SetStatus", "status": "Watched" }
```

| `action` | Extra fields | Effect |
|----------|--------------|--------|
| `Delete`     | — | Soft delete, recoverable from the trash like any other |
| `SetStatus`  | `status` | Mark watched / unwatched |
| `SetScore`   | `score` (null clears) | Set or clear the score |
| `Move`       | `targetPlaylistId` | Move into another playlist you own |
| `Copy`       | `targetPlaylistId` | Copy, carrying the note and score across |

Returns `{ "affected": n, "skipped": n }`.

- Items you don't own — and ids that no longer exist — are **skipped**, not fatal. A stale id in
  a selection is a normal thing to happen, not a reason to throw the other 39 away.
- On a move or copy, links already in the target are skipped: that's dedup working.
- `SetStatus` doesn't count items already in that state; stamping a fresh timestamp would make
  re-marking look like progress.
- A target playlist you don't own returns **404**.

### Thumbnails

`GET /api/v1/thumbnails/{linkId}` serves a link's image from this server, fetched once and cached
on disk. Anonymous, because thumbnails appear on public playlist pages, and rate-limited, because
a miss means an outbound fetch.

Rendering the origin URL directly told every site in a playlist the viewer's IP address and what
they were looking at, and broke outright whenever a host refused hotlinking.

- `404` when the link has no image, or the fetch failed — the page falls back to the site's
  favicon, exactly as it does for a link that never had one.
- Only image content types are served: the output is rendered in an `<img>`, and passing through
  whatever a third-party host returned would be a way to smuggle something else.
- The fetch goes through the same SSRF-protected client as enrichment; a thumbnail URL is
  attacker-supplied in exactly the way a page URL is.
- `thumbnailUrl` in the API keeps reporting the **original** address. Proxying is a rendering
  decision, and a feed reader or an export wants the real one.

> Set `Thumbnails:CachePath` to a directory that survives restarts. Without it the cache lands in
> the system temp directory and is refetched whenever that is cleared.

### Enrichment outcomes

Every link records how its last fetch went, so a page that could not be read is distinguishable
from one that was:

| `enrichmentStatus` | Meaning |
|--------------------|---------|
| `Pending`   | Not fetched yet |
| `Succeeded` | Fetched and read |
| `Failed`    | The fetch went wrong — blocked, not a web page, a 4xx |
| `Broken`    | The page is gone (404/410). The link is fine; the thing it pointed at isn't |

`enrichmentError` carries the reason in words worth showing someone. Previously a permanent
failure was stamped exactly like a success with the reason hidden inside the OpenGraph metadata
bag, so a dead link simply rendered as a bare URL forever.

Links are re-checked automatically: a success is trusted for 30 days, then looked at again; a
failure backs off exponentially from 6 hours and is given up on after 6 attempts.

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/v1/links/{id}/recheck` | Try a link again now. Clears its backoff first, so an explicit retry isn't swallowed by the wait it was already serving. Rate-limited |
| `GET` | `/api/v1/links/{id}/content` | The article text kept when the page was first read — see [Saved articles](#saved-articles) |

### Saved articles

Enrichment keeps the readable part of a page, not just its metadata. The copy on the web is the
part that rots, so a saved article that can only be re-fetched is a saved address.

```bash
curl http://localhost:5180/api/v1/links/<id>/content -H "Authorization: Bearer <token>"
# -> { "title":"...", "host":"...", "paragraphs":["...","..."], "wordCount":1840, "truncated":false }
```

- **Most pages have no article in them.** Anything under 60 words of prose is stored as nothing at
  all, and this returns **404** — a reader view of one sentence is worse than none. `wordCount` on
  a link is what says whether there is anything to read.
- **Text is capped at 60,000 characters**, cut at a word boundary, with `truncated` saying so.
  `wordCount` still describes the whole article, not the part that was kept.
- **Only people who saved the page can read it.** Links are global rows shared by everyone who
  saved the same address; the text behind one is not.
- `q` on [search](#search) looks inside this text, so a word from the middle of an article finds
  it. Hits that matched **only** there carry a `snippet` — without it the row looks like a mistake,
  since nothing on it contains the word that was typed.

### What a link is

Every link carries a `kind`: `Article`, `Video`, `Repository`, `Paper`, `Document`, `Audio`,
`Image`, `Social`, or `Unknown`. A collection is one undifferentiated list of addresses without
it, and "what can I watch now" has no way to be asked.

It is worked out from what is already known — what was served, the path, the host, the page's own
`og:type`, and how much prose was found — in that order. The host is checked **before** the prose,
because a video page with a long description is still a video. Nothing conclusive leaves it
`Unknown`, which is a real answer: a landing page is not an article, and calling it one would put
it in front of someone looking for something to read.

Links saved before any of this existed are classified in the background (`links:classify`, every
twenty minutes, in batches) using the same rules and nothing but the row — no page is re-fetched.



> **Only enriched items are listed.** Manual adds enrich **immediately** (so they appear at once);
> source-ingested links appear once their metadata has been fetched asynchronously. `itemCount`
> reflects enriched items, and `pendingCount` reports how many are still being fetched — so a
> playlist filling up after a source run says so, rather than its count creeping upward on its
> own. Public reads omit `pendingCount`: a visitor can't act on it.

## NSFW

Links are flagged adult **automatically** (via the page's `rating`/RTA meta tag during
enrichment, or by being ingested from an [NSFW source](sources.md#nsfw-sources)). By default a
**playlist is NSFW** if it contains any NSFW item.

### Overriding the automatic reading

Detection reads a signal a site declares about itself, so it gets false positives — and one of
those used to hide a playlist from everyone who hadn't opted in, permanently, with no appeal.

`PATCH /playlists/{id}` accepts `nsfw`:

| Value | Meaning |
|-------|---------|
| `Auto` | Work it out from the items (the default) |
| `Yes`  | Adult, whatever the items say |
| `No`   | Not adult, whatever the items say |

The playlist read reports the current choice as `nsfwSetting`. Listings omit it rather than
defaulting to `Auto` and misreporting an override.

Admins can correct a link at its source — `POST /admin/links/{id}/clear-nsfw` clears the flag
globally, for everyone who has that link.

Each user has a **Show NSFW** preference (default **off**): `GET /api/v1/me` returns `showNsfw`,
`PUT /api/v1/me/preferences { "showNsfw": true|false }` updates it. While off, NSFW playlists and
items are hidden everywhere — your own lists, item lists, discovery, and public views (a NSFW public
playlist returns 404). Anonymous viewers are always treated as off.

## Search (across everything you own)

The per-playlist item list answers "where in this list is it". This answers "where did I put it".

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/v1/search`       | Links across **all** your playlists |
| `GET` | `/api/v1/search/hosts` | Sites you save from, with counts (`?q=` prefix filter) — the facets for a host filter |

| Parameter | Meaning |
|-----------|---------|
| `q`        | Free text over title, description, site name, **your note**, the [saved article text](#saved-articles), URL and hostname. A pasted URL is canonicalized and matched on the dedup hash instead |
| `host`     | Restrict to one hostname |
| `tag`      | Repeatable; the **playlist** must carry all of them |
| `itemTag`  | Repeatable; the **link** must carry all of them — which is what finds the same subject across different lists |
| `status`   | `watched` or `unwatched` |
| `minScore` | Only items you scored at least this highly |
| `finishedSince` | Only items you marked watched at or after this instant — "what did I get through this week" |
| `broken` | `true` to list only links whose page is gone or can no longer be read — the link rot in your collection |
| `kind` | One of `article`, `video`, `repository`, `paper`, `document`, `audio`, `image`, `social`. An unrecognised name matches **nothing** rather than everything, so a typo returns an empty list instead of quietly ignoring the filter |
| `maxMinutes` | Only what can be read in this many minutes — how people actually pick what to open. Links with no article behind them have no length to compare and are excluded |
| `sort` | `score` for best-rated first, across every playlist. Unrated items sort last rather than as zero. `queue` for "what now": rated things first, then whatever has been carried longest — a queue that leads with the newest arrival is how a backlog becomes permanent |
| `limit`, `cursor` | Paging; `limit` maxes out at 100 |

- Results are ordered by relevance when `q` is given — a title hit, then a site-name hit, then
  anything else — and newest-first when it isn't, which is what a bare browse wants.
- Each hit carries `playlistId` and `playlistName`, because "which list did I put it in" is most
  of the question being asked.
- A hit that matched only inside the article text carries a `snippet` showing where.
- NSFW items are excluded unless you have opted in.

```bash
curl "http://localhost:5180/api/v1/search?q=postgres&status=unwatched"   -H "Authorization: Bearer <token>"
```

### Saved searches

A search worth coming back to — "unread, from this site, rated above 70". What it matches is
whatever matches **now**, so it keeps up with the collection instead of freezing a list of ids.

| Method | Path | Purpose |
|--------|------|---------|
| `GET`    | `/api/v1/search/saved`      | Your saved searches, newest first |
| `POST`   | `/api/v1/search/saved`      | Save one (`name` plus any of the search filters) |
| `GET`    | `/api/v1/search/saved/{id}` | Run it and return what matches now (paginated) |
| `DELETE` | `/api/v1/search/saved/{id}` | Forget it. The links it found are untouched — it was only a question |

> A saved search is deliberately not a playlist. A playlist can be reordered, added to,
> syndicated and exported; a query-defined list can do none of those, and making one polymorphic
> would put that branch into every read of every playlist.

> Search matches with `lower(col) LIKE '%term%'`, covered by trigram GIN indexes. Terms shorter
> than three characters carry too little trigram content for the index and fall back to a scan.

## Export (data portability)

Import has existed since the CSV importer; this is the way out.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/v1/export?format=`                    | Everything you own |
| `GET` | `/api/v1/export/playlists/{id}?format=`     | One playlist you own |

`format` is one of:

| Format | What it is |
|--------|-----------|
| `json` (default) | Full structured dump: playlists, items, folders and sources |
| `csv`  | One row per link. The first two columns are `url` and `note`, which is exactly what the CSV importer reads, so an export feeds straight back in |
| `html` | Netscape bookmark file — importable by every browser; playlists become folders |
| `opml` | Subscription list of your **RSS** sources. Scraper and JSON-API sources are left out: OPML describes feeds, and they have no feed URL a reader could subscribe to |

- Responses are served as an attachment with a dated filename.
- **Source secrets are redacted** (`***`). An export is a file that gets emailed around, and a
  scraper's auth header has no business travelling in one.
- Unenriched links are included — they are your data whether or not we managed to fetch a title.

```bash
curl -OJ "http://localhost:5180/api/v1/export?format=csv" -H "Authorization: Bearer <token>"
```

## Embedding a playlist

A public playlist can sit on someone else's page.

| Address | What it is |
|---------|-----------|
| `/embed/{username}/{slug}` | A self-contained card, the ten most recent links. `?theme=dark` for a dark host page |
| `/oembed?url=…` | oEmbed discovery, so anything that speaks oEmbed turns a pasted playlist link into that card |

- The public playlist page advertises its oEmbed endpoint, so a paste is enough.
- `/oembed` only describes addresses **on this server**. An oEmbed endpoint that described anyone's
  URL would be a way to make this server fetch and vouch for anything.
- Only `/embed` is frameable; every other page now sends `frame-ancestors 'none'`, which nothing
  did before — any page could previously be put inside someone's iframe.
- An embed is `noindex`: it is a fragment of someone else's page, not one of ours.

## Sync (what changed since I last looked)

`GET /api/v1/sync?since=<instant>` reports everything of yours that changed after that moment.
Without it a caching client — the browser extension, an offline queue, a mobile app — has only
two options: re-read everything, or trust a stale copy.

```bash
curl "http://localhost:5180/api/v1/sync" -H "Authorization: Bearer <token>"
# -> { "until": "...", "more": false, "playlists": [...], "items": [...] }
```

- Omit `since` for a first sync, which returns everything.
- **Deleted rows come back as tombstones** (`"deleted": true`, every other field null) rather
  than simply being absent. A client that only ever hears about what exists can never learn that
  something went away.
- `until` is read from the **server's** clock and is what to pass as `since` next time — a client
  with a skewed clock would otherwise ask for a window that skips changes it never saw.
- At most 500 rows of each kind per call; `more` says the page was capped, and `until` then
  resumes at the last row actually returned rather than skipping what was left behind.
- Changing an item's **tags** counts as changing the item, even though tags live in their own
  rows — otherwise a syncing client would never hear about it.

## Duplicates

`GET /api/v1/duplicates` finds the same thing saved more than once across playlists you own.

Dedup already stops the identical link landing twice in one playlist, and canonicalization
strips the tracking parameters it knows about. Neither helps with:

| `kind` | What it catches |
|--------|-----------------|
| `SameLink` | The identical link, saved into several playlists |
| `SamePage` | One page reached by different addresses — same host and path, different query |

Each group carries every copy with the playlist it lives in, so a client can offer "keep this
one" and delete the rest with `POST /items/bulk`. A trailing slash is not treated as a different
page, and a link saved by two different **people** is nobody's duplicate.

## Trash (undo a delete)

Deleting a playlist or an item is a **soft delete**: the row is kept and can be restored for
**30 days**, after which a nightly job removes it for good.

| Method | Path | Purpose |
|--------|------|---------|
| `GET`    | `/api/v1/trash`                          | Everything you deleted that is still restorable |
| `POST`   | `/api/v1/trash/playlists/{id}/restore`   | Put a deleted playlist back (with its items) |
| `POST`   | `/api/v1/trash/items/{id}/restore`       | Put a deleted item back at the end of its playlist |
| `DELETE` | `/api/v1/trash`                          | Purge your trash now, permanently |

- Items whose **playlist** was also deleted aren't listed on their own — they come back with the
  playlist. Restoring the playlist restores them.
- If another playlist took the slug while this one sat in the trash, the restored playlist gets a
  suffixed slug (`weekend-reading-2`) rather than failing.
- Restoring an item whose link was re-added to the playlist in the meantime returns **409** —
  the link that came back on its own wins.
- Each entry reports `deletedAt` and `purgeAfter` so a client can show how long is left.

```bash
curl http://localhost:5180/api/v1/trash -H "Authorization: Bearer <token>"
# -> { "playlists": [ { "id":"P...", "name":"Recipes", "itemCount":12,
#                       "deletedAt":"...", "purgeAfter":"..." } ],
#      "items": [], "retentionDays": 30 }

curl -X POST http://localhost:5180/api/v1/trash/playlists/P.../restore   -H "Authorization: Bearer <token>"   # -> 204 No Content
```

## Public (anonymous) reads

`Public` and `Unlisted` playlists can be read **without authentication**, addressed by the
owner's **username + slug**. `Private` playlists (and unknown ones) return **404** — a
private playlist is indistinguishable from one that doesn't exist. `Unlisted` works the same
way; it simply isn't surfaced in any listing, so it acts as a share-by-link.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/v1/public/playlists`                         | **Discover**: search/browse public playlists (`?q=` name, `?tag=` repeatable=AND, paginated) |
| `GET` | `/api/v1/public/playlists/{username}/{slug}`       | Read a non-private playlist |
| `GET` | `/api/v1/public/playlists/{username}/{slug}/items` | Read its items (paginated) |

Discovery returns only **Public** playlists (Unlisted is share-by-link, never listed). Each
result carries `ownerUsername` + `slug` so you can deep-link to the read endpoint.

### Profiles

Every result names its owner, so there is somewhere to click through to.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/v1/public/users/{username}`           | A user as seen from the outside |
| `GET` | `/api/v1/public/users/{username}/playlists` | Their public playlists (paginated) |

- Counts and listings cover **Public** playlists only — `Unlisted` never appears, not even on
  the owner's own profile, and `Private` is invisible.
- A profile carries username, join date and counts. Email and everything else private is never
  included.
- Usernames match case-insensitively; an unknown one returns **404**.

```bash
curl "http://localhost:5180/api/v1/public/playlists?q=cooking&tag=recipes"
curl http://localhost:5180/api/v1/public/playlists/alice/my-reading-list
curl http://localhost:5180/api/v1/public/playlists/alice/my-reading-list/items
```

## Feeds (syndication)

A non-private playlist is readable as **RSS 2.0**, **Atom 1.0** and **JSON Feed 1.1** — so a
playlist can be followed from any reader, including by another Linkbelli source.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/v1/public/playlists/{username}/{slug}/feed.rss`  | RSS 2.0 |
| `GET` | `/api/v1/public/playlists/{username}/{slug}/feed.atom` | Atom 1.0 |
| `GET` | `/api/v1/public/playlists/{username}/{slug}/feed.json` | JSON Feed 1.1 |

- Anonymous, and governed by the same visibility rules as the HTML read: `Public` and
  `Unlisted` are served, `Private` returns **404**.
- **NSFW playlists are never syndicated.** A feed reader carries no session, so there is no
  viewer to have opted in.
- Entries are **newest first** (a feed is a stream), capped at **50**, and each entry links to
  the target URL — not back to a Linkbelli page.
- The entry summary is the owner's note when there is one, otherwise the link's description.
- The web app serves the same feeds at the friendlier
  `/public/{username}/{slug}/feed.rss`, and the public playlist page advertises all three via
  `<link rel="alternate">` so readers find them on their own.

> Set `PublicWebBaseUrl` to the web app's origin so feeds link readers to the real page. Without
> it the API falls back to its own address, which behind a proxy is an internal hostname.

```bash
curl http://localhost:5180/api/v1/public/playlists/alice/my-reading-list/feed.rss
```

## Source subscriptions

A playlist can be fed by **sources** (see [sources.md](sources.md)). You attach a source to a
playlist you own; when that source runs, its links flow into the playlist.

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/api/v1/playlists/{id}/sources`            | — | List sources feeding this playlist (incl. shared ones you subscribed; `ownedByMe` flag) |
| `POST`   | `/api/v1/playlists/{id}/sources`            | `sourceId` | Subscribe a source to your playlist |
| `DELETE` | `/api/v1/playlists/{id}/sources/{sourceId}` | — | Unsubscribe |

You can attach **your own** sources (any visibility) and **anyone's `Shared`** sources; a
private source you don't own returns **404**. Browse subscribable sources via
`GET /api/v1/sources/shared` (see [sources.md](sources.md)).

```bash
curl -X POST http://localhost:5180/api/v1/playlists/<id>/sources \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"sourceId":"<sourceId>"}'
```
