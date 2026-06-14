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
| `Scraper` | `url`, `itemSelector`, `linkAttribute?`, `titleSelector?` | Scrapes a page with CSS selectors. `itemSelector` selects link-bearing elements; `linkAttribute` (default `href`) holds the URL; `titleSelector` (within each element) or the element text supplies the title. Relative URLs resolve against `url`. |
| `JsonApi` | `url`, `itemsPath`, `urlPath`, `titlePath?`, `header.*` | Fetches JSON and extracts links via JSONPath. `itemsPath` selects item nodes; `urlPath`/`titlePath` are evaluated relative to each item. Any `header.<Name>` key is sent as a request header **and treated as a secret** (encrypted at rest, shown as `***` in responses). |

```jsonc
// Scraper config
{ "url": "https://news.example/section",
  "itemSelector": "a.headline", "titleSelector": null }

// JSON-API config (with an auth header secret)
{ "url": "https://api.example/v1/posts",
  "itemsPath": "$.data.posts[*]", "urlPath": "permalink", "titlePath": "title",
  "header.Authorization": "Bearer <token>" }
```

> **Secrets:** `header.*` values are encrypted with ASP.NET Core Data Protection before storage
> and returned redacted (`***`). On update, re-send `***` (or omit the key) to keep the existing
> secret; send a new value to replace it.

## Endpoints

All paths are under **`/api/v1`**. Reads require the `sources:read` scope and writes the
`sources:write` scope (only relevant for scoped API keys — see [auth.md](auth.md#scopes)).

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/api/v1/sources`          | — | List your sources |
| `POST`   | `/api/v1/sources`          | `name, type, config, schedule, playlistIds?, visibility?, nsfw?` | Create (enabled, scheduled immediately) |
| `GET`    | `/api/v1/sources/shared`   | — (`?q=`) | Browse **shared** sources (any owner) to subscribe |
| `POST`   | `/api/v1/sources/preview`  | `type, config` | Dry-run a config (live fetch, no save); returns up to 10 sample links. Rate-limited. |
| `GET`    | `/api/v1/sources/{id}`     | — | Get one |
| `PATCH`  | `/api/v1/sources/{id}`     | `name?, config?, schedule?, enabled?, playlistIds?` | Update (reschedules) |
| `DELETE` | `/api/v1/sources/{id}`     | — | Soft delete + unschedule |
| `POST`   | `/api/v1/sources/{id}/run` | — | Trigger a run now (202) |
| `GET`    | `/api/v1/sources/{id}/runs`| — | Recent run history |

- `schedule` is a standard **5-field cron** expression (e.g. `*/15 * * * *`), validated to run
  **no more than once every 5 minutes**.
- `playlistIds` must be playlists you own; discovered links are appended to each.

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
# -> [ { "status":"Succeeded", "itemsFound":37, "itemsAdded":35, "finishedAt":"...", ... } ]
```

A run records `itemsFound` (discovered) vs `itemsAdded` (new after dedup), plus `status`
(`Running`/`Succeeded`/`Failed`) and any `error`. Outbound fetches go through the same
SSRF-protected client as enrichment.

> Dev only: the Hangfire dashboard is at `/hangfire`.
