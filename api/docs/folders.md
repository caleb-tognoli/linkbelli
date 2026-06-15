# Linkbelli API — Folders

Folders are a **strictly private**, per-user way to organize playlists into a tree and to save
public playlists (your own or anyone's) for easy access. They never appear on the public/discovery
side, and sources are not organized into folders. All endpoints require authentication (bearer
token or API key — see [auth.md](auth.md)) and operate only on the caller's own folders.

All paths are under **`/api/v1`**. Reads require the `playlists:read` scope and writes the
`playlists:write` scope (only relevant for scoped API keys).

## Folders

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `GET`    | `/api/v1/folders`            | — | Flat list of all your folders (build the tree from `parentId`) |
| `POST`   | `/api/v1/folders`            | `name`, `parentId?` | Create a folder (root if `parentId` omitted) |
| `GET`    | `/api/v1/folders/{id}`       | — | Folder contents: breadcrumbs, subfolders, filed playlists |
| `PATCH`  | `/api/v1/folders/{id}`       | `name` | Rename |
| `POST`   | `/api/v1/folders/{id}/move`  | `parentId` (`null` = root) | Reparent (rejects cycles / excessive depth) |
| `DELETE` | `/api/v1/folders/{id}`       | — | Delete the folder, its subfolders, and all filed-playlist entries (the playlists themselves are untouched) |

- Folders nest up to **10** levels deep.
- A folder list/detail node reports `subfolderCount` and `playlistCount` for its direct contents.

## Filing playlists into a folder

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `POST`   | `/api/v1/folders/{id}/playlists`              | `playlistId` | File a playlist into the folder (moves it if already filed elsewhere) |
| `DELETE` | `/api/v1/folders/{id}/playlists/{playlistId}` | — | Remove (unfile / unsave) the playlist |

- You may file **your own** playlist (any visibility) or **any `Public`** playlist owned by
  someone else (saving it for easy access). Private playlists you don't own return `404`.
- A playlist is filed in **at most one folder per user** — filing it again moves it.
- In folder detail, each entry carries `ownedByMe` and `ownerUsername`; saved public playlists
  deep-link to their public page. A saved public playlist that later turns private is hidden.

```bash
# Create a folder, then save a public playlist into it
curl -X POST http://localhost:5180/api/v1/folders \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"name":"Reading"}'
# -> { "id":"F...", "name":"Reading", "parentId":null, "subfolderCount":0, "playlistCount":0, ... }

curl -X POST http://localhost:5180/api/v1/folders/F.../playlists \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{"playlistId":"P..."}'   # -> 204 No Content
```
