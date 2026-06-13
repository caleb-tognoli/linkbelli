# Linkbelli API — Playlists & Items

All endpoints below require authentication (bearer token or API key — see [auth.md](auth.md))
and operate only on the caller's own playlists. Responses are JSON; lists are paginated with
an opaque `nextCursor` (pass it back as `?cursor=` for the next page; `null` means no more).
Enum values such as `visibility` are strings: `Private`, `Unlisted`, `Public`.

## Playlists

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/playlists`       | — (`?limit=`, `?cursor=`) | List your playlists (newest first) |
| `POST`   | `/playlists`       | `name`, `description?`, `visibility?` | Create (slug auto-generated, unique per user) |
| `GET`    | `/playlists/{id}`  | — | Get one |
| `PATCH`  | `/playlists/{id}`  | `name?`, `description?`, `visibility?` | Update provided fields (slug is stable) |
| `DELETE` | `/playlists/{id}`  | — | Soft delete |

```bash
curl -X POST http://localhost:5180/playlists \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"name":"My Reading List","visibility":"Public"}'
# -> { "id":"...", "name":"My Reading List", "slug":"my-reading-list",
#      "visibility":"Public", "itemCount":0, "creationTime":"..." }
```

## Links (create-only)

`POST /links` ensures a link exists in the system and returns it. Because links are global
and deduplicated, this is a get-or-create: the URL is canonicalized (lowercased host,
tracking params and fragment stripped, query sorted) and an existing match is returned
rather than duplicated. New links come back `enriched: false`.

```bash
curl -X POST http://localhost:5180/links \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/article"}'
# -> { "id":"...", "url":"https://example.com/article", "host":"example.com",
#      "title":null, "thumbnailUrl":null, "enriched":false }
```

> Path case is significant: `/Article` and `/article` are different links.

## Items

A link is added by URL; the URL is canonicalized (lowercased host, tracking params and
fragment stripped, query sorted) and deduplicated globally. Adding a URL already present in
the playlist returns **409**. New links come back with `enriched: false` — title/thumbnail
metadata is filled in asynchronously (milestone 3).

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/playlists/{id}/items` | — (`?limit=`, `?cursor=`) | List items in order |
| `POST`   | `/playlists/{id}/items` | `url`, `note?` | Add a link to the end |
| `PATCH`  | `/items/{id}`           | `note?` | Update an item's note |
| `DELETE` | `/items/{id}`           | — | Soft delete (remove from playlist) |
| `POST`   | `/items/{id}/move`      | `afterItemId?` | Reorder: place after the given item; `null` = move to front |

```bash
# add
curl -X POST http://localhost:5180/playlists/<id>/items \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"url":"https://example.com/article","note":"read later"}'

# reorder to the front
curl -X POST http://localhost:5180/items/<itemId>/move \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"afterItemId":null}'
```

Item ordering uses gapped integer positions, so a move is normally a single-row update; the
server transparently renumbers a playlist if a gap runs out.

> Public/unlisted playlists are currently still owner-only over the API; anonymous read
> access for shared playlists arrives in a later milestone.
