# Linkbelli API — Authentication

Everything the website does goes through this same public API. There are two ways to
authenticate: **bearer tokens** for interactive/app sessions and **API keys** for scripts
and integrations.

- **Base URL (local dev):** `http://localhost:5180`
- **API version:** all business endpoints live under **`/api/v1`** (e.g. `/api/v1/playlists`).
  Infrastructure routes — `/`, `/health`, `/openapi/v1.json`, `/scalar/v1`, `/hangfire` — are
  unversioned.
- **Interactive reference (dev only):** `http://localhost:5180/scalar/v1` — includes an
  **Authorize** button so you can paste a bearer token or API key and call secured endpoints.
- **Errors** use the standard `application/problem+json` shape.

## 1. Bearer token (interactive)

### Register

Accounts have both a **username** and an **email**; both are required and must be unique.

```bash
curl -X POST http://localhost:5180/api/v1/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username":"alice","email":"alice@example.com","password":"Passw0rd!23"}'
```

### Log in

The `login` field accepts **either the username or the email**:

```bash
# by username
curl -X POST http://localhost:5180/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"login":"alice","password":"Passw0rd!23"}'

# or by email — same endpoint, same field
curl -X POST http://localhost:5180/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"login":"alice@example.com","password":"Passw0rd!23"}'
# -> { "tokenType": "Bearer", "accessToken": "...", "expiresIn": 3600, "refreshToken": "..." }
```

Send the token on subsequent requests, and refresh it before it expires:

```bash
curl http://localhost:5180/api/v1/me -H "Authorization: Bearer <accessToken>"

curl -X POST http://localhost:5180/api/v1/auth/refresh \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<refreshToken>"}'
```

| Method | Path | Body | Purpose |
|--------|------|------|---------|
| `POST` | `/api/v1/auth/register` | `username, email, password` | Create an account |
| `POST` | `/api/v1/auth/login`    | `login` (username or email), `password` | Get access + refresh tokens |
| `POST` | `/api/v1/auth/refresh`  | `refreshToken` | Exchange a refresh token for new tokens |

## 2. API key (programmatic)

API keys are created while logged in with a bearer token, and are then used on their own.
The full key is shown **once** at creation — store it securely; only its hash is kept.
An API key **cannot** be used to create more keys (that requires a bearer token).

```bash
# Create a key (requires a bearer token)
curl -X POST http://localhost:5180/api/v1/me/apikeys \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"name":"my script","scopes":["playlists:read"]}'
# -> { "id": "...", "name": "my script", "prefix": "ab12...", "token": "lbk_ab12..._xxxxx", ... }

# Use the returned token via the X-Api-Key header
curl http://localhost:5180/api/v1/me -H "X-Api-Key: lbk_ab12..._xxxxx"
```

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `GET`    | `/api/v1/me/apikeys`      | bearer only | List your keys (never returns the secret) |
| `POST`   | `/api/v1/me/apikeys`      | bearer only | Create a key (returns the full token once) |
| `DELETE` | `/api/v1/me/apikeys/{id}` | bearer only | Revoke a key immediately |

### Scopes

A key may be granted **scopes** to limit what it can do. A key with **no scopes is
unrestricted** (full access for its owner); once a key lists any scopes, it is limited to
exactly those. Bearer (interactive) sessions are never scope-limited. A call missing the
required scope returns **HTTP 403**.

| Scope | Grants |
|-------|--------|
| `playlists:read`  | List/read playlists and their items |
| `playlists:write` | Create/update/delete playlists, items, and moves |
| `sources:read`    | List/read sources and run history |
| `sources:write`   | Create/update/delete/run sources; preview configs |
| `links:write`     | `POST /api/v1/links` |

## Who am I?

`GET /api/v1/me` accepts **either** a bearer token or an API key and returns the caller's id,
auth method, and scopes — handy for verifying a token or key works.

```bash
curl http://localhost:5180/api/v1/me -H "X-Api-Key: lbk_ab12..._xxxxx"
# -> { "userId": "...", "authMethod": "apikey", "scopes": ["playlists:read"], "showNsfw": false }
```

Update preferences (currently just the NSFW content filter, default off):

```bash
curl -X PUT http://localhost:5180/api/v1/me/preferences \
  -H "Authorization: Bearer <token>" -H "Content-Type: application/json" \
  -d '{ "showNsfw": true }'
```

## Rate limiting

Requests are rate limited with a token bucket, partitioned by API key (or client IP for
anonymous/bearer requests). Exceeding the limit returns **HTTP 429** with a **`Retry-After`**
header (seconds) — back off and retry after that delay.

## Instance overview

`GET /api/v1/admin/overview` — Admin role, interactive bearer scheme, as with every `/admin`
route.

Failing sources, unreadable links and the enrichment backlog were all being recorded and none of
it had anywhere to be seen. The overview reports totals, the things worth acting on (enrichment
backlog, broken links, failing sources, failed runs in the last 7 days), background-job queue
depth, the ten worst-off sources with their owners and last errors, the ten busiest hosts, and
the ten most recent fetch errors.

`jobs` is **null** when the job runner could not be reached — which is itself a finding, and
better than a console that 500s exactly when it is needed.

`GET /api/v1/me` reports `roles`, so a client can offer this to the people it will work for
rather than showing everyone a link that 403s.

## Audit log

`GET /api/v1/admin/audit` — Admin only, like the rest of `/admin`. Filters: `action` (prefix, so
`admin.` finds every admin action), `actorId`, `targetId`, plus `limit` and `cursor`.

Admin actions reach into other people's data and a few user actions destroy rows outright.
Neither left any trace — the only record that a host had been blocked, or a trash emptied, was
the absence of what used to be there.

Recorded today: `admin.host.block` / `admin.host.unblock`, `admin.quota.set` (with the whole
before and after), `admin.links.re-enrich`, `admin.link.clear-nsfw`, `playlist.delete` (with what
went with it), and `trash.empty` — the one action in the app that really deletes rows rather than
hiding them.

- **The actor's name is stored, not just their id.** An audit trail that stops naming people once
  their account goes is not an audit trail.
- **`asAdmin` marks the entries where someone was acting on data that was not theirs.**
- **Writing an entry never fails the action.** The thing being recorded has already happened;
  undoing it because the note about it failed would be much the worse outcome.
- **The table carries no soft-delete filter.** A trail the application can delete from is not one.

## Content reports

Moderation was a host blocklist and nothing else: a visitor who found something wrong had no way
to say so, and whoever runs the instance had no way to hear it.

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/v1/public/playlists/{username}/{slug}/report` | Report it (`{ "reason": "Spam", "note": "…" }`). Signed in, rate-limited |
| `GET`  | `/api/v1/admin/reports` | The queue (`?status=Open`), open first |
| `POST` | `/api/v1/admin/reports/{id}/resolve` | Close it (`{ "dismiss": true }` or `{ "takeDown": true }`) |

Reasons: `Spam`, `Nsfw`, `Malware`, `Illegal`, `Copyright`, `Other` — a short list, because a long
one gets picked from at random.

- **Signing in is required.** A queue anyone can fill anonymously is a queue nobody reads.
- **Reporting the same playlist twice gives back the same open report**, rather than a second row
  saying the same thing.
- **You cannot report your own playlist** — you can change it directly.
- **A private playlist cannot be reported.** Nobody was shown it.
- **A takedown makes the playlist private; it never deletes it.** It stops being published and its
  owner keeps their work, because deleting somebody's collection over a report is not recoverable.
  Takedowns are written to the [audit log](#audit-log).
- **The queue lists open reports first.** Sorted purely by date, what still needs doing gets
  buried under what has already been handled.
