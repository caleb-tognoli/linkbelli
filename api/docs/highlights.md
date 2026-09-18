# Linkbelli API — Highlights

A highlight is a passage somebody marked in an article they saved, with an optional note beside
it. Linkbelli keeps the readable text of every article it can extract, on the reasoning that the
copy on the web is the part that rots; highlights are what make that stored copy worth more than
the link.

All paths are under **`/api/v1`** and require authentication (bearer token or API key — see
[auth.md](auth.md)). Reads require the `playlists:read` scope and writes `playlists:write`.

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET`    | `/links/{linkId}/highlights` | Everything you marked in one article, in reading order |
| `POST`   | `/links/{linkId}/highlights` | Mark a passage |
| `GET`    | `/highlights`                | Everything you have marked, newest first, keyset-paged |
| `PATCH`  | `/highlights/{id}`           | Change the note |
| `DELETE` | `/highlights/{id}`           | Remove it |

### Marking a passage

```http
POST /api/v1/links/{linkId}/highlights
{ "paragraphIndex": 1, "start": 25, "end": 48, "note": "The line I came back for" }
```

`paragraphIndex` counts the paragraphs of `GET /links/{linkId}/content` from zero; `start` and
`end` are character offsets within that paragraph, `end` exclusive. A selection must sit inside
one paragraph — anything else is a `400`.

The quoted text is **taken from the stored article, not from the request**. A `text` field in the
body is accepted and ignored, so a client cannot file words of its own choosing under a passage it
pointed at.

Marking the same passage again returns the existing highlight rather than a `409`; if the second
request carries a note, the note is updated.

## Against the link, not the playlist item

Highlights belong to **you and the article**, not to a row in a playlist. A link in two playlists
is one article: reading progress is already shared across copies for the same reason, and a mark
that existed in one list and not the other would be the same passage marked twice with no way to
tell which was meant.

You can mark only an article you have saved. Links are shared rows across every account, so the
link alone says nothing about who may read its text — and marking it is reading it. Somebody else
who saved the same article sees none of your marks.

## When the article changes: `orphaned`

An article can be enriched again — a paywall lifts, the extractor improves — and its paragraphs
shift under a highlight. Every highlight stores its quote alongside its offsets for exactly this
case. When the offsets no longer find the quote, the highlight comes back with
`"orphaned": true`: the words are still there to read, and a client knows not to draw the mark
over whatever now sits at those offsets.

```json
{
  "id": "0199…",
  "linkId": "0198…",
  "paragraphIndex": 1,
  "start": 25,
  "end": 48,
  "text": "something worth keeping",
  "note": null,
  "createdAt": "2026-09-18T08:36:04Z",
  "orphaned": false
}
```

## Limits

| Limit | Value | Why |
|-------|-------|-----|
| Passage length | 2,000 characters | A selection is a sentence or a paragraph, not the article |
| Note length | 1,000 characters | The same budget as an item note |
| Highlights per article | 500 | The reader draws every one on open |

## Where else they turn up

- **Search** matches your highlight notes (and the passages themselves), and `is:highlighted`
  narrows any search to articles you marked something in. See the operators table in
  [playlists.md](playlists.md).
- **The weekly digest** quotes up to three passages you marked that week, and a week spent marking
  passages is no longer counted as a quiet one.
- **Exports and backups** carry them, from export format version 3; a restore puts them back onto
  articles that are saved once it has run. See [backups.md](backups.md).
- **The MCP server** has a read-only `list_highlights` tool.
