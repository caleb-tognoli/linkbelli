# Linkbelli API — Backups

A backup is a snapshot of everything one user owns, taken on a schedule and kept so it can be
handed back later. It is the same content as `GET /api/v1/export?format=json`; the difference is
who remembers to run it. Export helps the people who thought to ask. Backups are for the ones who
did not, which is most people on the day it matters.

Nothing leaves the server: snapshots are stored in the database beside the data they copy, and
only the account they belong to can list, download or delete them.

All paths are under **`/api/v1`** and require authentication (bearer token or API key — see
[auth.md](auth.md)). Reads require the `playlists:read` scope and writes `playlists:write`.

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET`    | `/api/v1/backups`        | Your snapshots, newest first. Metadata only |
| `POST`   | `/api/v1/backups`        | Take one now. `201` with the new snapshot, or `204` if nothing has changed |
| `GET`    | `/api/v1/backups/{id}`   | Download one, as a JSON file attachment |
| `DELETE` | `/api/v1/backups/{id}`   | Delete one, permanently |
| `GET`    | `/api/v1/backups/{id}/restore` | What restoring it would do. Changes nothing |
| `POST`   | `/api/v1/backups/{id}/restore` | Do it, and report what was done |
| `POST`   | `/api/v1/backups/restore`      | The same, from a file rather than a stored snapshot |

A listing entry looks like:

```json
{
  "id": "6f1b...",
  "takenAt": "2026-09-12T08:36:04Z",
  "sizeBytes": 48213,
  "playlistCount": 12,
  "itemCount": 1043,
  "automatic": true
}
```

`automatic` distinguishes the schedule's work from a snapshot somebody took by hand.

## The schedule

The sweep runs hourly and considers up to **50** accounts per run, so a large instance rotates
through its users rather than exporting everyone at once. An account is due when its last sweep
was more than **7 days** ago, and accounts that have never been backed up are reached first.

Two things keep the table bounded:

- **An unchanged library is not stored twice.** The content is hashed with the export timestamp
  excluded, so a dormant account keeps exactly one snapshot however long it sits there. `POST`
  answers `204 No Content` in that case rather than writing a duplicate.
- **Only the newest 5 snapshots per account are kept.** Older ones are deleted as new ones land.

A snapshot larger than **32 MB compressed** is refused with a validation error rather than stored;
export by hand in that case.

## Turning it off

Backups are **on by default** — unlike archiving, a snapshot involves no third party, so there is
nobody to consent to. Storage is the only cost, and the deduplication above bounds it.

```http
PUT /api/v1/me/preferences
{ "backupsEnabled": false }
```

Every field of that request is optional and an omitted field is left alone, so a screen that owns
one setting can save it without stating a position on the others. `GET /api/v1/me` reports the
current value as `backupsEnabled`.

With it off, no snapshots are taken and the account is skipped entirely — but snapshots already
taken are kept until deleted.

## Restoring

A restore **merges rather than replaces**. It adds what is absent and leaves alone what is there,
because wiping and rewriting is a far more destructive thing than most people asking for a restore
have in mind — and it would throw away everything saved since the snapshot was taken.

What counts as already here:

| Restored | Matched against what you have by |
|----------|----------------------------------|
| Playlist | Its slug |
| Folder   | Its name and its parent |
| Source   | Its name |
| Item     | The canonical URL of its link, within the playlist it belongs to |

A matched row is left exactly as it is. An item that is already there keeps its own note, score
and read status — restoring a snapshot does not undo the reading you have done since.

Two things arrive deliberately quieter than they left:

- **Restored playlists are private**, whatever they were when the snapshot was taken. Republishing
  is one click and is yours to make; a restore that re-publishes a list you had since unpublished
  is not recoverable by a click.
- **Restored sources are paused.** An export redacts credentials out of a source's config, so a
  restored source may be missing the key or password it needs — and a source that resumed on its
  own would start failing, or start fetching, without being asked.

### The dry run

`GET /api/v1/backups/{id}/restore` costs the restore without performing it, and
`POST /api/v1/backups/restore` with `"dryRun": true` does the same for a file. Both answer the same
shape the real thing answers:

```json
{
  "dryRun": true,
  "formatVersion": 2,
  "takenAt": "2026-09-12T08:36:04Z",
  "foldersAdded": 3,
  "playlistsAdded": 1,
  "playlistsMatched": 11,
  "itemsAdded": 412,
  "itemsAlreadyThere": 631,
  "sourcesAdded": 2,
  "truncated": false,
  "sourcesNeedCredentials": true
}
```

`sourcesNeedCredentials` is true whenever any source was restored, since an export redacts config
secrets and this one cannot know which of them mattered. It exists so a caller can say plainly that
some typing may be needed rather than leaving it to be discovered on the next scheduled run. `truncated` is true when the restore hit
its ceiling of **5,000 items** in one request and stopped there; running it again carries on, since
everything already put back is now "already there".

### From a file

```http
POST /api/v1/backups/restore
{ "json": "<the whole downloaded file, as a string>", "dryRun": false }
```

This is the case a backup system is actually for: the server the snapshot came from is gone, and
what somebody has is the file they downloaded before it went. Nothing in the format is keyed to the
instance that wrote it, so a file from one Linkbelli restores into another.

### Format versions

An export carries a `version`. Version 1 was the original; version 2 added each link's own tags,
which earlier exports dropped. A restore reads any version up to the one it knows and **refuses
anything newer** rather than half-reading it — a restore that silently ignores fields it does not
understand is worse than one that declines and says so.
