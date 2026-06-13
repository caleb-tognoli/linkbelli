# Linkbelli API — Sources (automatic link ingestion)

A **source** is a user-configured worker that periodically discovers links and appends them
to one or more of your playlists. All endpoints require auth (bearer or API key) and operate
on your own sources. Discovered links are canonicalized + deduplicated like manual adds, and
new ones are enqueued for metadata enrichment automatically.

Runs are executed by a background job server (Hangfire) on the source's cron schedule, or
on demand via the run endpoint. Each execution is logged as a *run*.

## Source types

| Type | Config keys | Notes |
|------|-------------|-------|
| `Rss` | `feedUrl` | RSS or Atom feed. Up to 100 newest entries per run. |

(More types — scraper, JSON API — arrive later; the model is the same.)

## Endpoints

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/sources`          | — | List your sources |
| `POST`   | `/sources`          | `name, type, config, schedule, playlistIds?` | Create (enabled, scheduled immediately) |
| `GET`    | `/sources/{id}`     | — | Get one |
| `PATCH`  | `/sources/{id}`     | `name?, config?, schedule?, enabled?, playlistIds?` | Update (reschedules) |
| `DELETE` | `/sources/{id}`     | — | Soft delete + unschedule |
| `POST`   | `/sources/{id}/run` | — | Trigger a run now (202) |
| `GET`    | `/sources/{id}/runs`| — | Recent run history |

- `schedule` is a standard **5-field cron** expression (validated; e.g. `*/15 * * * *`).
- `playlistIds` must be playlists you own; discovered links are appended to each.

```bash
# Create an RSS source feeding a playlist every 15 minutes
curl -X POST http://localhost:5180/sources \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{
        "name": "BBC News",
        "type": "Rss",
        "config": { "feedUrl": "https://feeds.bbci.co.uk/news/rss.xml" },
        "schedule": "*/15 * * * *",
        "playlistIds": ["<playlistId>"]
      }'

# Run it immediately instead of waiting for the schedule
curl -X POST http://localhost:5180/sources/<id>/run -H "Authorization: Bearer <token>"

# Inspect what happened
curl http://localhost:5180/sources/<id>/runs -H "Authorization: Bearer <token>"
# -> [ { "status":"Succeeded", "itemsFound":37, "itemsAdded":35, "finishedAt":"...", ... } ]
```

A run records `itemsFound` (discovered) vs `itemsAdded` (new after dedup), plus `status`
(`Running`/`Succeeded`/`Failed`) and any `error`. Outbound fetches go through the same
SSRF-protected client as enrichment.

> Dev only: the Hangfire dashboard is at `/hangfire`.
