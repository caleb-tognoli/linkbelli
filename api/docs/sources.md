# Linkbelli API — Sources (automatic link ingestion)

A **source** is a user-configured worker that periodically discovers links and appends them
to one or more of your playlists. All endpoints require auth (bearer or API key) and operate
on your own sources. Discovered links are canonicalized + deduplicated like manual adds, and
new ones are enqueued for metadata enrichment automatically.

Runs are executed by a background job server (Hangfire) on the source's cron schedule, or
on demand via the run endpoint. Each execution is logged as a *run*.

## Source types

All types discover up to 100 links per run (then capped again by your `maxItemsPerRun` quota).

| Type | Config keys | Notes |
|------|-------------|-------|
| `Rss` | `feedUrl` | RSS or Atom feed. Uses conditional GET (ETag/Last-Modified) to skip unchanged feeds. |
| `Scraper` | `url`, `itemSelector`, `linkSelector?`, `linkAttribute?`, `meta.*`, `header.*` | Scrapes a page with CSS selectors. `itemSelector` selects each item container; `linkSelector` (within the item, optional) picks the URL-bearing element; `linkAttribute` (default `href`, empty = text content) holds the URL. Relative URLs resolve against `url`. Metadata comes from `meta.<name>` (CSS selector within the item) plus optional `meta.<name>.attr` (attribute to read; absent = text content), `meta.<name>.regex` and `meta.<name>.replacement` — see below. Any `header.<Name>` key is sent as a request header **and treated as a secret**. |
| `JsonApi` | `url`, `itemsPath`, `urlPath?`, `urlTemplate?`, `titlePath?`, `header.*` | Fetches JSON and extracts links via JSONPath. `itemsPath` selects item nodes; `urlPath`/`titlePath` are evaluated relative to each item. Either `urlPath` **or** `urlTemplate` must be set. `urlTemplate` builds a URL from item fields via `{jsonpath}` placeholders — e.g. `https://site.tld/movie/{id}` or `https://site.tld/r/{subreddit}/{id}/{meta.slug}`; items where any placeholder resolves empty are skipped. Any `header.<Name>` key is sent as a request header **and treated as a secret** (encrypted at rest, shown as `***` in responses). |

```jsonc
// Scraper config
{ "url": "https://news.example/section",
  "itemSelector": "li.story", "linkSelector": "a.headline",
  "meta.title": "a.headline",
  "meta.title.regex": "\\s*\\|\\s*News Example$",   // strips a trailing " | News Example"
  "meta.author": ".byline",
  "meta.author.regex": "^Posted by (.+)$", "meta.author.replacement": "$1" }

// JSON-API config (with an auth header secret)
{ "url": "https://api.example/v1/posts",
  "itemsPath": "$.data.posts[*]", "urlPath": "permalink", "titlePath": "title",
  "header.Authorization": "Bearer <token>" }
```

> **Metadata regex (Scraper):** `meta.<name>.regex` post-processes the value a `meta.<name>`
> selector extracted, as a find/replace: every match of the pattern is replaced with
> `meta.<name>.replacement` (absent = the match is deleted), which may reference capture groups
> as `$1`. A value the pattern never matches passes through unchanged; one the pattern empties
> is dropped from the item's metadata. Patterns are .NET regex, validated on create/update, and
> run with a 250 ms per-value timeout.

> **Secrets:** `header.*` values are encrypted with ASP.NET Core Data Protection before storage
> and returned redacted (`***`). On update, re-send `***` (or omit the key) to keep the existing
> secret; send a new value to replace it.

## Templates

Working out a service's feed path, or its CSS selectors, is the steepest part of setting a source
up — and it is identical work for everyone pointing at the same service. Templates do it once.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/v1/sources/templates` | Browse the ready-made configs and the fields each still needs |

Create from one by sending `templateId` and `variables` instead of a `config`:

```bash
curl -X POST http://localhost:5180/api/v1/sources   -H "Authorization: Bearer <token>" -H "Content-Type: application/json"   -d '{ "name": "dotnet/runtime releases",
        "templateId": "<templateId>",
        "variables": { "owner": "dotnet", "repo": "runtime" } }'
```

- The template's `suggestedSchedule` is used when you don't send one; an explicit `schedule`
  still wins.
- A value you haven't supplied is **named in the error**, by its label — rather than rendering a
  URL with `{{repo}}` still in the middle of it and failing later as a mysterious fetch error.
- The rendered config still goes through the interpreter's own validation, so a template cannot
  talk the app into accepting a config it otherwise wouldn't.
- Built-ins are **re-seeded on startup**, so a corrected feed path reaches everyone who used the
  template rather than only people who create a source after the fix.

Shipped today: YouTube channels and playlists, subreddits, Hacker News above a score, GitHub
releases and commits, podcasts, Mastodon hashtags, and a plain feed for when you already have
the address.

## Endpoints

All paths are under **`/api/v1`**. Reads require the `sources:read` scope and writes the
`sources:write` scope (only relevant for scoped API keys — see [auth.md](auth.md#scopes)).

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/api/v1/sources`          | — | List your sources |
| `POST`   | `/api/v1/sources`          | `name, type, config, schedule, timeZone?, playlistIds?, visibility?, nsfw?` | Create (active, scheduled immediately) |
| `GET`    | `/api/v1/sources/shared`   | — (`?q=`) | Browse **shared** sources (any owner) to subscribe |
| `POST`   | `/api/v1/sources/preview`  | `type, config` | Dry-run a config (live fetch, no save); returns up to 10 sample links. Rate-limited. |
| `GET`    | `/api/v1/sources/{id}`     | — | Get one |
| `PATCH`  | `/api/v1/sources/{id}`     | `name?, type?, config?, schedule?, timeZone?, playlistIds?, visibility?, status?` | Update (reschedules) |
| `DELETE` | `/api/v1/sources/{id}`     | — | Soft delete + unschedule |
| `POST`   | `/api/v1/sources/{id}/run` | — | Trigger a run now (202) |
| `GET`    | `/api/v1/sources/{id}/runs`| — | Recent run history |
| `GET`    | `/api/v1/sources/{id}/health`| — | The last 30 days summarised |

- `schedule` is a standard **5-field cron** expression (e.g. `*/15 * * * *`), validated to run
  **no more than once every 5 minutes**.
- `timeZone` is the IANA zone the schedule is read in (e.g. `Europe/Rome`). Omit it for **UTC**.
  A zone the server doesn't recognise is **rejected**, not quietly ignored — silently falling
  back to UTC would run the schedule at the wrong hour with nothing to show for it. Daylight
  saving is observed, so "every day at 8" stays at 8 all year.
- `playlistIds` must be playlists you own; discovered links are appended to each.
- `filter` decides what the source may bring in — see [Filters](#filters). Omit it entirely to
  accept everything, which is what every source did before filters existed.

### Pausing

`status` is `Active` (default), `Paused`, or `Failing`.

- Pausing **unschedules** the source: it stops running on its cron.
- The `schedule` is left exactly as you set it, so resuming picks the same cadence back up —
  you don't have to re-enter it.
- A paused source can still be run explicitly with `POST /sources/{id}/run`; pausing stops the
  timer, not the source.

```bash
curl -X PATCH http://localhost:5180/api/v1/sources/<id>   -H "Authorization: Bearer <token>" -H "Content-Type: application/json"   -d '{"status":"Paused"}'
```

### Failing sources stop themselves

A broken config — a selector that no longer matches, a feed that moved — fails identically on
every run. Rather than burning the daily quota on the same error indefinitely:

- `consecutiveFailures` counts failures since the last success, and any success resets it.
- At **5** consecutive failures an `Active` source becomes `Failing` and is **unscheduled**.
- `Failing` is deliberately distinct from `Paused`: one means "this broke", the other means
  "I turned this off", and they call for different actions. A source the owner already paused is
  never relabelled.
- Setting `status` back to `Active` **clears the failure count** — whatever the owner just
  changed is their attempt at a fix, and it deserves a fresh count rather than tripping again on
  the next run.

### Visibility & subscriptions

- `visibility` is `Private` (default) or `Shared`, and **can be changed** (PATCH `visibility`).
  Going `Private → Shared` is free; going `Shared → Private` **unsubscribes every other user's
  playlist** that was following it (the owner's own attachments are kept) — warn before doing it.
- A `Shared` source can be subscribed by **other users** to their own playlists; a `Private`
  source can only be attached by its owner. Either way, the source still runs on its owner's
  schedule and quota — subscribers just receive its links.
- Discover shareable sources with `GET /api/v1/sources/shared` (returns id, name, type, owner
  username — never config, which may hold secrets). Subscribe/unsubscribe from the **playlist**
  side: `POST|DELETE /api/v1/playlists/{id}/sources` (see [playlists.md](playlists.md#source-subscriptions)).

### NSFW sources

- `nsfw` (manual, editable) marks a source as adult. Every link it ingests is flagged NSFW, so
  playlists fed by it become NSFW and are hidden from users who haven't opted in (see
  [playlists.md](playlists.md#nsfw)). Links can otherwise only become NSFW **automatically** during
  enrichment (via the page's `rating`/RTA meta tag) — there is no manual per-link NSFW flag.

## Quotas

Each user has limits (stored per-user, with defaults; see `GET /me/quota`):

| Quota | Default | Enforced |
|-------|---------|----------|
| Max sources | 5 | At `POST /sources` → **429** when reached |
| Max runs / day | 10 | Every run — manual (`POST .../run` → **429**) and scheduled (silently skipped). Counts executions in the trailing 24h |
| Max items / run | 100 | A run processes at most this many discovered links |

```bash
curl http://localhost:5180/api/v1/me/quota -H "Authorization: Bearer <token>"
# -> { "maxSources":5, "sourcesUsed":3, "maxRunsPerDay":10, "runsUsedToday":2, "maxItemsPerRun":100 }
```

`GET /me/usage` reports the collection itself — playlists, links, distinct sites, how many are
watched, how many are broken, and how much is sitting in the trash. Quotas were only ever visible
as a 429, and nothing at all reported size, so a hundred rotting links could go unnoticed.

```bash
curl http://localhost:5180/api/v1/me/usage -H "Authorization: Bearer <token>"
# -> { "playlists":12, "items":875, "sites":94, "watched":310, "broken":21, "inTrash":3, ... }
```

### Admin & moderation

Admins (users in the `Admin` role) manage other users and the host blocklist. Admin endpoints
require a **bearer token** whose user holds the Admin role (API keys are rejected).

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET` | `/api/v1/admin/users` | — (`?q=`, `?limit=`) | Search users by username/email (id + counts) |
| `GET` | `/api/v1/admin/users/{userId}/quota` | — | View a user's limits + usage |
| `PUT` | `/api/v1/admin/users/{userId}/quota` | `maxSources, maxRunsPerDay, maxItemsPerRun` | Override a user's limits |
| `GET` | `/api/v1/admin/hosts` | — (`?q=`, `?blocked=`) | List hosts + blocked flag + link counts |
| `PUT` | `/api/v1/admin/hosts` | `hostname, blocked` | Block/unblock a host (created if not yet seen) |

- **Host blocklist:** a blocked host refuses new links — manual adds (`POST /links`, `POST /items`)
  return **403**, and source ingestion silently skips them. You can block a hostname before it's
  ever seen; future links from it are then refused.
- Granting admin: list usernames under config `Admin:Usernames`; on startup the app ensures the
  `Admin` role exists and grants it to those (already-registered) users.

```bash
# Find a user, then block a site
curl "http://localhost:5180/api/v1/admin/users?q=alice" -H "Authorization: Bearer <admin-token>"
curl -X PUT http://localhost:5180/api/v1/admin/hosts \
  -H "Authorization: Bearer <admin-token>" -H "Content-Type: application/json" \
  -d '{ "hostname": "spam.example", "blocked": true }'
```

> **Hangfire dashboard** (`/hangfire`): open in Development; in other environments it requires HTTP
> Basic credentials from config `Hangfire:Dashboard:Username`/`Password` (and is closed if unset).

```bash
curl -X PUT http://localhost:5180/api/v1/admin/users/<userId>/quota \
  -H "Authorization: Bearer <admin-token>" -H "Content-Type: application/json" \
  -d '{ "maxSources": 25, "maxRunsPerDay": 500, "maxItemsPerRun": 200 }'
```

```bash
# Create an RSS source feeding a playlist every 15 minutes
curl -X POST http://localhost:5180/api/v1/sources \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
        "name": "BBC News",
        "type": "Rss",
        "config": { "feedUrl": "https://feeds.bbci.co.uk/news/rss.xml" },
        "schedule": "*/15 * * * *",
        "playlistIds": ["<playlistId>"]
      }'

# Preview a config before saving (no source created)
curl -X POST http://localhost:5180/api/v1/sources/preview \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{ "type": "Scraper",
        "config": { "url": "https://news.example", "itemSelector": "a.headline" } }'
# -> { "count": 10, "links": [ { "url": "...", "title": "..." }, ... ] }

# Run it immediately instead of waiting for the schedule
curl -X POST http://localhost:5180/api/v1/sources/<id>/run -H "Authorization: Bearer <token>"

# Inspect what happened
curl http://localhost:5180/api/v1/sources/<id>/runs -H "Authorization: Bearer <token>"
# -> [ { "status":"Succeeded", "foundCount":37, "addedCount":35, "skippedCount":3, ... } ]
```

A run records `foundCount` (discovered) vs `addedCount` (new after dedup), plus `status`
(`Running`/`Succeeded`/`Failed`) and any `error`. Outbound fetches go through the same
SSRF-protected client as enrichment.

`itemsFound` and `itemsAdded` carry **up to 20** of the URLs for inspection — a sample, not a
record. Runs are the fastest-growing table in the schema, and the URLs are duplicated verbatim
from the links themselves, which are still there.

### Webhook sources

Every other source type asks a schedule to go and look, so a link cannot arrive until the next
poll — and anything without a feed cannot be a source at all.

A webhook source waits instead. Create one with `"type": "Webhook"` and an empty config; the
server mints its token and returns it as `webhookToken`, and the URL is:

```bash
curl -X POST http://localhost:5180/api/v1/hooks/<token>   -H "Content-Type: application/json"   -d '{ "links": [ { "url": "https://example.com/thing", "title": "Optional" } ] }'
# -> { "received":1, "found":1, "added":1, "skipped":0, "status":"Succeeded", "error":null }
```

- **The URL is the whole credential** — no key, no token header. That is what makes it pasteable
  into n8n, a Zap, a GitHub Action or a shell script. Treat it like a password.
- **A push goes down the same road as a poll**: the same [filter](#filters), the same dedup, the
  same run row, the same quota. A second ingestion path is how two kinds of source quietly start
  behaving differently.
- **Webhook sources are never scheduled.** A scheduled run of one would find nothing and spend a
  slot of the owner's daily quota doing it.
- Up to **100 links** per push; the per-run item quota still applies on top.
- An unknown token answers exactly like a disabled one, because a webhook URL is a secret.

### Filters

Everything a source finds lands unless a filter turns it away, which makes a broad feed an
all-or-nothing decision: take the firehose or don't subscribe.

```jsonc
{
  "titleInclude": "rust|zig",       // regex, case-insensitive; null accepts any title
  "titleExclude": "sponsored",
  "urlInclude": "^https://blog\.",
  "urlExclude": "/tag/|/author/",
  "minAgeHours": 24,                // needs a date from the source; 0-720
  "maxItems": 20,                   // per run, applied after the patterns
  "dedupeWindowDays": 7             // 0-30
}
```

Every field is optional. A filter that would change nothing is stored as **null**, not as an
object full of nulls — so `GET` gives back `"filter": null` for a source that accepts everything.
On `PATCH`, an **omitted** `filter` leaves the stored one alone (as every other field does) and an
**empty object** clears it: null already means "don't touch".

Details worth knowing:

- **Patterns are checked when you save them**, not when a run meets them: an unparseable one comes
  back as a `400` naming the field, rather than failing every run at 3am. They run on the linear
  matching engine where the syntax allows it, and behind a 250ms timeout where it doesn't, so a
  pattern can't hang the worker.
- **The patterns run before `maxItems`**, so the cap keeps the items you asked for instead of the
  first N the feed happened to list.
- **`minAgeHours` only applies when the source reports a publish date.** A link with no date is
  accepted: unknown is not new, and a scraper reports no dates at all.
- **`dedupeWindowDays` is what makes a deletion stick.** Removing something a source found is
  otherwise temporary — the next run puts it straight back. The window is answered from the
  removed items themselves, which is why it can't outlast the **30 days** they sit in the trash.

A run reports `skippedCount` alongside `foundCount` and `addedCount`: everything the filter turned
away, by pattern, by age or by the cap. Without it, a strict filter and a broken selector look
identical from the outside — both succeed and add nothing.

### Health

```bash
curl http://localhost:5180/api/v1/sources/<id>/health -H "Authorization: Bearer <token>"
# -> { "runs":28, "windowDays":30, "succeeded":26, "failed":2, "successRate":93,
#      "averageFound":12.4, "averageAdded":1.8, "emptyRuns":9, "consecutiveFailures":0,
#      "lastRunAt":"...", "lastRunStatus":"Succeeded", "lastError":null }
```

Finished runs only — a run still in flight has no outcome to count. Two details are deliberate:

- **The averages cover successful runs only.** A failed run found nothing because it failed, not
  because there was nothing to find, and averaging it in drags every figure down for a reason
  that has nothing to do with the feed.
- **`emptyRuns` counts runs that succeeded and found nothing.** This is the failure nothing else
  can show: a scraper whose selector stopped matching succeeds every single time, reports 100%,
  and returns an empty list forever. `successRate` is green throughout.

`successRate` and both averages are `null` rather than `0` when there is nothing to compute from
— a source nobody has run yet is not failing, and a red `0%` would say it is.

**Retention:** successful runs are kept **30 days**, failures **90** (they are the ones you come
back to diagnose), and the **20 most recent runs per source always survive** however old they
are — a source that runs monthly should never be left with no history at all. A nightly job
applies this.

> Dev only: the Hangfire dashboard is at `/hangfire`.
