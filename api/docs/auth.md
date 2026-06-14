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
