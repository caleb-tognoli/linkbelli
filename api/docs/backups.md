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

**There is no restore endpoint yet, and this is the honest limit of the feature.** A snapshot is
the JSON export format — the complete record of a library, in a documented shape — but
`POST /api/v1/import` reads rows of `{ url, note }`, not that shape. Putting a snapshot back today
means pulling the URLs out of it yourself and feeding them to the importer, which recovers the
links and loses the structure around them: playlist membership, tags, notes, scores, folders.

So what a backup protects against right now is losing the *data*, not losing the *arrangement*.
That is worth having — it is the difference between a bad week and starting over — but a restore
that reads this format back in is the piece that finishes the job.
