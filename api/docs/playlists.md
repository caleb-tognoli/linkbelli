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
| `GET`    | `/api/v1/playlists`       | — (`?limit=`, `?cursor=`) | List your playlists (newest first) |
| `POST`   | `/api/v1/playlists`       | `name`, `description?`, `visibility?` | Create (slug auto-generated, unique per user) |
| `GET`    | `/api/v1/playlists/{id}`  | — | Get one |
| `PATCH`  | `/api/v1/playlists/{id}`  | `name?`, `description?`, `visibility?` | Update provided fields (slug is stable) |
| `DELETE` | `/api/v1/playlists/{id}`  | — | Soft delete |

```bash
curl -X POST http://localhost:5180/api/v1/playlists \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"name":"My Reading List","visibility":"Public"}'
# -> { "id":"...", "name":"My Reading List", "slug":"my-reading-list",
#      "visibility":"Public", "itemCount":0, "creationTime":"..." }
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
| `PATCH`  | `/api/v1/items/{id}`           | `note?` | Update an item's note |
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

## Public (anonymous) reads

`Public` and `Unlisted` playlists can be read **without authentication**, addressed by the
owner's **username + slug**. `Private` playlists (and unknown ones) return **404** — a
private playlist is indistinguishable from one that doesn't exist. `Unlisted` works the same
way; it simply isn't surfaced in any listing, so it acts as a share-by-link.

| Method | Path | Purpose |
|--------|------|---------|
| `GET` | `/api/v1/public/playlists/{username}/{slug}`       | Read a non-private playlist |
| `GET` | `/api/v1/public/playlists/{username}/{slug}/items` | Read its items (paginated) |

```bash
curl http://localhost:5180/api/v1/public/playlists/alice/my-reading-list
curl http://localhost:5180/api/v1/public/playlists/alice/my-reading-list/items
```
