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
| `POST` | `/api/v1/auth/forgot-password` | `login` | Send a reset link. Always `202`, whether or not that account exists |
| `POST` | `/api/v1/auth/reset-password` | `email, token, newPassword` | Finish a reset. Ends every existing session |
| `POST` | `/api/v1/auth/confirm-email` | `email, token` | Prove the address belongs to you |
| `POST` | `/api/v1/auth/resend-confirmation` | `email` | Another confirmation link. Always `202` |

### Confirming an address

Registration sends a confirmation link, and **nothing else is ever mailed to an address nobody has
confirmed** — no digest, no notifications, not even a "send me one now" from the settings page.

Anyone could otherwise sign up with anyone's address. Two things went wrong at once: the instance
became a small spam cannon aimed at a stranger, with the instance's sending reputation paying for
it; and because addresses are unique, squatting one denied its real owner an account.

**Signing in is not gated on it.** An instance with no mail configured cannot confirm anything,
and locking those people out of their own accounts to close a mail problem would be the worse
trade. An unconfirmed account works normally and is simply never written to — which is exactly
what the address's real owner wanted.

`/me` reports `emailConfirmed`, so a screen that promises mail can say why none is arriving.

### Closing an account

`DELETE /api/v1/me` with `{ "password": "…" }` schedules it. Password-confirmed, because a
session left open on a shared machine should not be enough to end somebody's account.

It is a request with a **30-day grace period**, not an act. Immediately: every session ends, every
source is paused, and every playlist the account publishes is set private — which is the one
change that takes effect everywhere at once, since discovery, the profile page, the sitemap, the
feeds and every tag facet already filter on visibility.

**Signing in again calls it off**, and puts back exactly what was published before — not
everything, and not nothing. Somebody who comes back has changed their mind, and making them hunt
for a separate "actually, no" button after signing in successfully would be a worse version of the
same answer.

After the grace period a nightly job removes the account and everything it owns: playlists, items,
folders, sources, runs, keys, rules, saved searches, backups, quotas, memberships, likes, follows
and reports. Four things deliberately survive, and it is worth saying which:

| Survives | Why |
| --- | --- |
| `Link` and `Host` rows | Global and deduplicated — the row for a page this account saved is the same row everybody else saved it under. Deleting one would reach into other people's libraries |
| Forks of its playlists | A fork is an independent copy with its own item rows. Somebody who kept a copy keeps it; that was the point of taking one. All it loses is the pointer back |
| Audit entries | The record of what happened on this instance, administrators included. A record its subject can erase is not one |
| The username, until the purge | Freeing it sooner would let somebody take a name still attached to a live profile. It is released when the account actually goes |

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

### Accounts

| Method | Path | Purpose |
|--------|------|---------|
| `PUT` | `/api/v1/admin/users/{id}/suspended` | `{ "suspended": true }` blocks sign-in and takes their public content down, keeping everything. `false` lifts it and puts back exactly what was published |
| `PUT` | `/api/v1/admin/users/{id}/admin` | `{ "admin": true }` grants the role, `false` removes it |

Suspension and deletion are kept apart on purpose: they mean opposite things about whose decision
it was, and an admin needs to tell "I turned this off" from "they left" — the same reason a source
keeps `Paused` apart from `Failing`. Deleting an account is its owner's decision, made from their
own settings; an admin suspends.

**You cannot remove your own administrator access.** Locking yourself out of your own instance
takes a deploy to undo, and somebody meaning to demote a colleague and clicking their own row is
not a far-fetched afternoon. Ask another admin.

`Admin:Usernames` in configuration stays as the bootstrap path — a fresh instance with nobody in
the database still needs a way in — but adding a third admin no longer needs a deploy.

Every one of these is written to the audit trail with `asAdmin`.

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

## Metrics and tracing

Observability was one health check and Hangfire's dashboard: enough to say the process was alive,
and nothing about whether it was doing its job.

`GET /metrics` serves Prometheus exposition. **Behind the Admin role by default** — metrics name
every host this instance fetches and how much of everything there is, which is not a public fact
about somebody's private collection. Set `Telemetry:Metrics:AllowAnonymous` to `true` when the
port is only reachable from a scraper, or `Telemetry:Metrics:Enabled` to `false` to turn it off.

Beyond the ASP.NET, HttpClient and runtime instrumentation, the app's own meter (`Linkbelli`)
reports:

| Metric | What it says |
|--------|--------------|
| `linkbelli.enrichment.attempts` | Pages fetched, tagged by outcome and host |
| `linkbelli.enrichment.duration` | How long a fetch took, including the per-host wait — that wait is part of how long a link really takes to appear |
| `linkbelli.source.runs` | Source runs, tagged by outcome and source type |
| `linkbelli.source.links` | Links a run found, added, or skipped by its filter |
| `linkbelli.archive.attempts` | Snapshots asked for: archived, refused, or deferred |

The host is a tag on the attempt counter but **not** on the duration histogram: a per-host latency
series for a collection spanning thousands of sites is a cardinality problem, not a measurement.

Tracing is exported only when `Telemetry:Otlp:Endpoint` names a collector — collecting spans and
dropping them on the floor costs the same as collecting spans somebody reads. The scrape endpoint
is excluded from traces: it is hit constantly and says nothing.

## Idempotency keys

A scripted client whose request times out cannot tell "the server never got it" from "the server
did it and the reply was lost". Retrying was a coin flip between a duplicate and a missing row.

Send `Idempotency-Key: <your own unique string>` on any `POST` under `/api/v1`. The first request
runs; a retry with the same key returns the **same status and the same body**, with
`Idempotent-Replay: true` on the response. Requests without the header behave exactly as they
always did.

- **Keys are scoped to the caller**, so two clients picking the same UUID never collide.
- **The same key with a different body or a different path is a `409`.** Replaying the first
  answer there would hide a client bug and silently drop the second request.
- **A retry that arrives while the first is still running gets a `409`** telling it to try again
  shortly, rather than racing it.
- **A request that never completed gives its key back.** If the endpoint threw, nothing happened,
  so a retry — including a corrected one under the same key — is free to run.
- Keys are remembered for **24 hours**, then forgotten by an hourly job.

## Admin scopes

Scopes existed, but there was no admin scope and the admin endpoints refused API keys outright —
so instance maintenance could only be run by a person with a session open in a browser.

Two new scopes: `admin:read` (the overview, the audit trail, the moderation queue) and
`admin:write` (blocking a host, setting a quota, taking something down).

- **A scope is not a promotion.** It opens the door; the Admin **role** is still checked, and
  minting a key cannot grant one. A key with `admin:write` held by an ordinary user gets `403`.
- **An unrestricted key is unrestricted over its owner's own data, never over the instance.** A
  key with no scopes reaches everything of its owner's and nothing of the instance's — otherwise
  every general-purpose key an admin ever minted would quietly be an instance-wide credential.
  Admin access by key is opt-in, per key, by name.
- Interactive bearer principals were never scope-limited and still are not.

## Conditional GETs

Everything that polls this API — the extension, the sync client, a feed reader — re-downloaded an
identical payload every time it looked.

Every successful `GET` under `/api/v1` carries a weak `ETag`. Send it back as `If-None-Match` and
an unchanged response comes back as `304 Not Modified` with no body.

- **The tag is derived from the response itself**, so it is right by construction rather than by
  someone remembering to bump a version when a field changes.
- **Weak, deliberately**: it compares one serialization byte for byte, which is not a claim about
  the resource.
- **Failures are never tagged.** Caching a `404` under an entity tag is how a transient failure
  becomes a sticky one.
- `*` is honoured, and a tag that lost its `W/` marker in transit is still recognised — refusing
  it would just resend the body.
