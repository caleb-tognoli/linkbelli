# Linkbelli MCP server

A [Model Context Protocol](https://modelcontextprotocol.io) server that gives an AI assistant
access to a Linkbelli library: search it, read what is in it, and add to it.

## Why this is worth having

Anything can fetch a URL. What Linkbelli has that a fetch does not is **the text as it was when
you saved it** — the readable body of every page, kept at the moment it was added. So an assistant
can answer from what you actually read, including for pages that have since changed, gone behind a
paywall, or disappeared.

`search_links` searches that stored text, not just titles. "What was that article about borrow
checking I saved last year" is a question this can answer and a web search cannot.

## Setting it up

You need an API key. Make one in the web app under **Profile → API keys**, or:

```bash
curl -X POST http://localhost:5180/api/v1/me/apikeys \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"MCP"}'
```

The response's `token` is shown once. Give the key the `playlists:read` scope for reading, and
`playlists:write` too if you want the assistant to be able to save things.

### Claude Desktop / Claude Code

```json
{
  "mcpServers": {
    "linkbelli": {
      "command": "node",
      "args": ["/absolute/path/to/linkbelli/mcp/src/server.js"],
      "env": {
        "LINKBELLI_API_URL": "http://localhost:5180",
        "LINKBELLI_API_KEY": "lbk_…"
      }
    }
  }
}
```

| Variable | Default | Meaning |
|----------|---------|---------|
| `LINKBELLI_API_URL` | `http://localhost:5180` | Where the API is |
| `LINKBELLI_API_KEY` | — | Required. The key from above |
| `LINKBELLI_MCP_READONLY` | unset | `1` offers only the reading tools |

## Tools

| Tool | What it does |
|------|--------------|
| `search_links` | Search titles, notes and **saved article text** across every playlist. Filters by site, tag, status, kind, score, broken-ness |
| `read_article` | The stored text of one saved page, by `linkId` |
| `list_highlights` | Passages marked while reading, with their notes — everywhere, or in one article by `linkId` |
| `list_playlists` | The playlists, with counts and visibility |
| `list_playlist_items` | What is in one playlist, in its own order |
| `save_link` | Add a URL to a playlist |
| `create_playlist` | Make a playlist — **private unless told otherwise** |
| `mark_items` | Mark items watched or unwatched |

`search_links` and `list_playlist_items` return both a `linkId` and an `itemId`, because the next
thing an assistant wants is either to read the page or to change the item.

## What it deliberately will not do

- **Nothing deletes.** There is no tool to remove a link, a playlist, or anything else. An
  assistant holding your API key cannot throw your library away.
- **Nothing publishes by accident.** `create_playlist` defaults to private; making something
  public takes an explicit `visibility`.
- **Read-only is real.** With `LINKBELLI_MCP_READONLY=1` the writing tools are never registered,
  so they are not merely refused — they are not there to call.
- Scope it further with the API key itself. A key with only `playlists:read` cannot write however
  the server is configured.

## Behaviour worth knowing

- **Articles are trimmed to about 12 000 characters**, cut at a paragraph boundary, and the reply
  says when it has been cut and how long the whole thing is. A 60 000-character article would
  crowd out the conversation it was fetched for.
- **Listings are capped at 50.**
- **Saving a link that is already there is not an error.** It says so and moves on, because
  reported as a failure an assistant would simply try again.
- **A refusal comes back as words, not an exception** — the API's own message where there is one,
  so "A valid http(s) URL is required" reaches the assistant rather than "400".
- **A page is fetched shortly after it is saved**, so `read_article` will not find text for
  something saved a moment ago.

## Developing

```bash
npm install
npm test
```

`live-check.mjs` drives the real server over stdio against a running Linkbelli — it is not part of
the suite because it needs both:

```bash
LINKBELLI_API_KEY=lbk_… node live-check.mjs
```
