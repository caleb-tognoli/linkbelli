# Linkbelli API — Usage Guide

The Linkbelli API is a standard JSON HTTP API. Everything the website does is done
through this same public API, so anything below works equally from your own scripts.

- **Base URL (local dev):** `http://localhost:5180`
- **Interactive reference (dev only):** `http://localhost:5180/scalar/v1` — browse every
  endpoint and try requests in the browser. The raw OpenAPI document is at
  `http://localhost:5180/openapi/v1.json`.
- **Errors** use the standard `application/problem+json` shape.

## Authentication

There are two ways to authenticate. Use **bearer tokens** for interactive/app sessions
and **API keys** for scripts and integrations.

### 1. Bearer token (interactive)

Register, then log in to receive an `accessToken`:

```bash
# Register
curl -X POST http://localhost:5180/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"Passw0rd!23"}'

# Log in -> returns { "accessToken": "...", "refreshToken": "...", "expiresIn": 3600 }
curl -X POST http://localhost:5180/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"you@example.com","password":"Passw0rd!23"}'
```

Send the token on subsequent requests:

```bash
curl http://localhost:5180/me -H "Authorization: Bearer <accessToken>"
```

Tokens expire; use `POST /auth/refresh` with the `refreshToken` to get a new one.

### 2. API key (programmatic)

API keys are created while logged in with a bearer token, and are then used on their own.
The full key is shown **once** at creation — store it securely; only its hash is kept.

```bash
# Create a key (requires a bearer token; an API key cannot create more keys)
curl -X POST http://localhost:5180/me/apikeys \
  -H "Authorization: Bearer <accessToken>" \
  -H "Content-Type: application/json" \
  -d '{"name":"my script","scopes":["playlists:read"]}'
# -> { "id": "...", "name": "my script", "prefix": "ab12...", "token": "lbk_ab12..._xxxxx", ... }
```

Use the returned `token` via the `X-Api-Key` header:

```bash
curl http://localhost:5180/me -H "X-Api-Key: lbk_ab12..._xxxxx"
```

Manage keys:

| Method | Path | Purpose |
|--------|------|---------|
| `GET`    | `/me/apikeys`        | List your keys (never returns the secret) |
| `POST`   | `/me/apikeys`        | Create a key (returns the full token once) |
| `DELETE` | `/me/apikeys/{id}`   | Revoke a key immediately |

## Current endpoints

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `GET` | `/`        | none | Service info |
| `GET` | `/health`  | none | Health check (incl. database) |
| `GET` | `/me`      | bearer or API key | The authenticated caller's id, auth method, and scopes |
| `*`   | `/auth/*`  | varies | Identity endpoints (register, login, refresh, …) |
| `*`   | `/me/apikeys` | bearer only | API key management (see above) |

> More endpoints (playlists, links, sources) arrive in later milestones; this file is
> updated as they land.

## Rate limiting

Requests are rate limited with a token bucket, partitioned by API key (or client IP for
anonymous/bearer requests). Exceeding the limit returns **HTTP 429**. Limits are generous
for normal use; back off and retry when you receive a 429.
