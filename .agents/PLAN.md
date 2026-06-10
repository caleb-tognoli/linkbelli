# Linkbelli — Backend Implementation Plan

A system to record links into "playlists", managed via a website, fed manually or by
user-configured automatic sources (RSS, scrapers, JSON APIs), and navigable like a
playlist while browsing the web.

**Stack:** ASP.NET Core (.NET 10) · PostgreSQL + EF Core · Hangfire · AngleSharp /
CodeHollow.FeedReader · declarative (Tier 1) source workers · versioned public JSON API.

---

## 1. Repository & solution structure

This is a **monorepo**: backend, frontend, browser extension, and any future side
projects live side by side as top-level areas.

```
linkbelli/                        # repo root
  .agents/                        # docs intended for LLM agents (this plan, conventions)
  docker-compose.yml              # postgres (+ api later); root-level because it spans areas
  api/
    Linkbelli.sln
    src/
      Linkbelli.Api/              # host: endpoints, auth, rate limiting, OpenAPI
      Linkbelli.Core/             # domain: entities, source interpreters, services (no EF/Hangfire refs)
      Linkbelli.Infrastructure/   # EF Core DbContext + migrations, Hangfire wiring, SafeHttpClient
    tests/
      Linkbelli.Tests/            # unit tests (canonicalization, interpreters, SSRF guard)
      Linkbelli.IntegrationTests/ # API + DB tests via Testcontainers
  web/                            # SvelteKit app (later)
  extension/                      # browser extension — the "player" (later)
  shared/                         # cross-area artifacts, e.g. OpenAPI-generated TS client (later)
```

Conventions:
- Each top-level area is self-contained (own build, own README); CI builds areas independently.
- The backend's OpenAPI spec is the contract between areas; `shared/` holds clients
  generated from it (NSwag or openapi-typescript).
- Three backend projects is deliberate: `Core` holds the source-interpreter logic so it is
  testable with fixture files and has no infrastructure dependencies.

## 2. Data model

| Entity | Key fields | Notes |
|---|---|---|
| `User` | ASP.NET Core Identity | |
| `ApiKey` | userId, name, prefix, **hash**, scopes, lastUsedAt | store hash only; prefix for lookup/display |
| `Playlist` | ownerId, name, slug, description, visibility (`private`/`unlisted`/`public`) | |
| `Host` | hostname (unique), favicon, displayName, blocked | one row per website; system-owned, created on demand; future home of scraper politeness state |
| `Link` | canonicalUrl, urlHash (unique), hostId, title, description, thumbnailUrl, siteName, metadata `jsonb` | global, shared across playlists; enriched async. URL stored whole for identity; hostId is derived from canonicalUrl. Host get-or-create happens in the link-persistence service (M2/M3) via upsert (`ON CONFLICT DO NOTHING` + select), the same race-safe pattern as Link dedup by urlHash |
| `PlaylistItem` | playlistId, linkId, **position**, note, origin (`manual`/sourceId), status (`active`/`pending`) | unique (playlistId, linkId) = dedup |
| `Source` | playlistId, type (`rss`/`scraper`/`jsonapi`), name, config `jsonb`, cron schedule, enabled, state `jsonb` (etag/lastModified/cursor) | a "user-created worker" is a row here |
| `SourceRun` | sourceId, startedAt, finishedAt, itemsFound, itemsAdded, status, error | run log surfaced in the UI |
| `PlaybackProgress` | userId, playlistId, currentItemId, updatedAt | powers the player/extension |

Design decisions:
- **Ordering:** integer `position` with gaps of 1024; renumber a playlist only when a
  gap is exhausted. Simple, and reorder = update one row almost always.
- **Dedup:** canonicalize URL (lowercase host, strip fragments + tracking params
  like `utm_*`, normalize trailing slash) → SHA-256 `urlHash`. Unique index on
  `Link.urlHash`; unique (playlistId, linkId) on items. Sources silently skip dupes.
- **Source config validation:** each source type owns a config schema; validate at
  create/update time and return field-level errors.

## 3. Ingestion pipeline

```
Hangfire recurring job (per enabled Source, user-defined cron, min interval 15 min)
  → resolve ISourceInterpreter by Source.type
  → fetch via SafeHttpClient            ← SSRF guard, caps (see §5)
  → parse → candidate links (url, title?, publishedAt?)
  → canonicalize + dedup
  → insert PlaylistItems (append at end) + record SourceRun
  → enqueue metadata-enrichment job per new Link (OpenGraph via AngleSharp)
```

`ISourceInterpreter` implementations for v1:
1. **RssInterpreter** — CodeHollow.FeedReader; honor ETag/Last-Modified stored in `Source.state`.
2. **ScraperInterpreter** — AngleSharp; config: `url`, `itemSelector`, `linkAttribute`, optional `titleSelector`.
3. **JsonApiInterpreter** — fetch JSON; config: `url`, JSONPath for items/url/title, optional headers (encrypted at rest).

Manual link adds reuse the same canonicalize → dedup → enrich path.

**Dry-run endpoint** (`POST /sources/preview`): executes a source config once,
returns the first ~10 candidates without saving. Critical UX for letting users
build scraper configs by trial and error.

## 4. Public API (v1 surface)

All under `/api/v1`, OpenAPI + Scalar docs, `ProblemDetails` errors, cursor pagination.
The frontend is just another consumer.

- `POST /auth/...` (Identity endpoints), `GET|POST|DELETE /me/apikeys`
- `GET|POST /playlists`, `GET|PATCH|DELETE /playlists/{id}`
- `GET|POST /playlists/{id}/items`, `PATCH|DELETE /items/{id}`, `POST /items/{id}/move`
- `GET|POST /playlists/{id}/sources`, `GET|PATCH|DELETE /sources/{id}`,
  `POST /sources/{id}/run` (manual trigger), `GET /sources/{id}/runs`, `POST /sources/preview`
- `GET /playlists/{id}/play` + `PUT /playlists/{id}/progress` — resolves
  current/next/prev for the player & extension
- Anonymous read access for `public`/`unlisted` playlists.

Auth: cookie (own frontend) **and** `X-Api-Key` (third parties) via a small
authentication handler. Rate limiting with built-in `AddRateLimiter`: per-API-key
token bucket; stricter anonymous policy; very strict policy on `/sources/preview`
(it triggers outbound fetches).

## 5. Security (non-negotiable from day one)

- **SSRF guard** in one `DelegatingHandler` that *every* outbound fetch goes through:
  resolve DNS, reject private/link-local/loopback ranges (v4 + v6), re-validate on
  every redirect, http/https only, 10 s timeout, 5 MB response cap, max 5 redirects.
- Per-user quotas: max sources, min schedule interval, max items per run.
- API keys hashed; secrets in source configs (API headers) encrypted via Data Protection.
- Hangfire dashboard behind admin auth only.

## 6. Milestones

| # | Milestone | Contents | Done when |
|---|---|---|---|
| 0 | Skeleton | solution, docker-compose (postgres), EF Core + first migration, CI build+test, health check | `docker compose up` + `dotnet test` green |
| 1 | Auth | Identity, API keys, auth handler, rate limiter skeleton | curl with API key works |
| 2 | Playlists & links | CRUD, ordering/move, manual add with async OpenGraph enrichment, pagination, OpenAPI docs | full playlist lifecycle via API |
| 3 | Ingestion core | Source/SourceRun, Hangfire, SafeHttpClient + SSRF tests, **RSS interpreter**, dedup | RSS feed auto-populates a playlist on schedule |
| 4 | More sources | Scraper + JSON API interpreters, dry-run preview, run logs endpoint, quotas | build a working scraper source via API only |
| 5 | Public hardening | per-key rate policies, scopes, anonymous public-playlist reads, CORS, integration test pass | safe to expose to the internet |
| 6 | Playback | `/play` + progress endpoints (contract designed with the extension in mind) | extension prototype can drive next/prev |
| 7 | Ops | API Dockerfile in compose, Serilog, Hangfire dashboard auth, backup notes | one-command self-host deploy |

Milestones 0–2 are a generic API; **3 is the heart of the product** — front-load the
SSRF guard and canonicalization there, with tests, because everything later reuses them.

## 7. Testing strategy

- Unit: URL canonicalization table tests; each interpreter against fixture
  HTML/XML/JSON files; SSRF guard against a list of hostile URLs/redirect chains.
- Integration: Testcontainers (Postgres) + `WebApplicationFactory`; cover auth,
  dedup-on-concurrent-runs, rate limiting headers.
- A `samples/` folder of real-world source configs used as living documentation.
