# Architecture

The feature documents in [`api/docs/`](../api/docs/) explain how each part behaves. This one is
the level above them: the rules a change has to obey, and the conventions that are currently
transmitted only by comments at the point where they bite.

Short on purpose. Where detail matters, it links.

## The pieces

| Package | What it is |
| --- | --- |
| `api/` | ASP.NET Core 10 minimal APIs over Postgres 17, with Hangfire for background work |
| `web/` | SvelteKit 2 / Svelte 5, `adapter-node`. A pure client of the API, plus a BFF auth layer |
| `extension/` | An MV3 browser extension that captures the current tab |
| `mcp/` | A stdio MCP server, so an assistant can read a library |
| `email-worker/` | A Cloudflare Email Worker that turns a mailed link into a saved one |

The web app never talks to the database, and the API never renders HTML. Everything the browser
does goes through `/api/v1/*` on the SvelteKit origin, which proxies to the API and injects a
bearer token from an httpOnly cookie. Tokens never reach browser JavaScript.

## The layers, and the one rule

```
Contracts  →  Core  →  Application  →  Infrastructure  →  Api
```

Arrows point one way and only one way.

- **`Contracts`** — the request and response records. No behaviour. Referenced by everything,
  including the tests, so the shape on the wire is one definition rather than several.
- **`Core`** — entities and pure domain logic: `UrlCanonicalizer`, `TagNormalizer`,
  `PlaylistOrdering`, `UsernamePolicy`. No EF, no HTTP, no clock. Everything here is testable
  without a database, and the unit suite does exactly that.
- **`Application`** — the services that do the work, plus the interfaces they need from the
  outside world (`IAppDbContext`, `IEmailSender`, `IFullTextSearch`, `IHostThrottle`).
- **`Infrastructure`** — EF Core, Npgsql, the mail sender, the search implementation, migrations.
- **`Api`** — endpoint mapping, authentication, filters, dependency registration.

**`Application` may not reference `Infrastructure`.** This is the rule that shapes the codebase.
Anything needing a provider-specific type goes behind an interface in `Application` and is
implemented in `Infrastructure`:

- `IAppDbContext` exposes `DbSet<T>` and `SaveChangesAsync` without exposing `LinkbelliDbContext`.
- `IFullTextSearch` hides `NpgsqlTsVector`, `websearch_to_tsquery` and `ts_headline`.
- Shadow properties (`EF.Property<T>(...)`) hold columns no entity should carry — the search
  vector, the host+path duplicate key — so Core stays free of them.

## The request pipeline

In order, for a request arriving at the API:

1. **`UseForwardedHeaders`** — first, so the rate limiter, the logs and anything reading a client
   address see the caller rather than the proxy. Which proxies are believed is configured
   (`ForwardedHeaders:TrustedNetworks`); without it, every request looks like it came from the
   web container and the anonymous rate limit becomes one bucket for the whole internet.
2. **`X-Request-Id`** — the trace id, on every response, so a bug report can name one request.
3. **`UseExceptionHandler`** → `AppExceptionHandler`, which maps the application's exception types
   to problem details: `NotFoundException` → 404, `ValidationException` → 400 with field errors,
   `ConflictException` and `UniqueConstraintException` → 409, `QuotaExceededException` → 429,
   `BlockedHostException` → 403.
4. **Idempotency buffering** — ahead of routing, because parameter binding consumes the body
   before any endpoint filter runs, and a body that will be hashed has to be re-readable.
5. **Auth, then the rate limiter, then authorization** — in that order, so the limiter can
   partition on *who* is calling rather than on which credential they presented.
6. **Two endpoint filters on `/api/v1`**: `IdempotencyFilter` (opt-in by header, so a POST can be
   retried safely) and `ETagFilter` (conditional GETs; buffers JSON up to 512 KB and streams
   anything larger or non-JSON straight through).

### In front of it: the web proxy

Every browser call to the API goes through `web/src/routes/api/v1/[...path]/+server.ts`, which
holds the tokens in httpOnly cookies so they never reach page script. It is a deliberate proxy
rather than a pass-through:

- **Header allowlists both ways.** Conditional-GET headers, `Idempotency-Key` and `Content-Type`
  go up; `ETag`, `Content-Disposition`, `Cache-Control`, `Retry-After` and `X-Request-Id` come
  down. Cookies and a page-set `Authorization` never go up; `Set-Cookie` never comes down.
- **One trace per browser request.** The hook starts a W3C `traceparent` for each request and
  sends it on every API call that request makes; the API adopts it, so the `X-Request-Id` the
  browser is shown is the id in both servers' logs. Pages carry the header too.
- **A deadline to the first byte, not the last.** An API that has not started answering in 60
  seconds is abandoned; a slow download that has started is not.
- **Its own failures in the API's shape.** An API that is down or hung is answered `502`/`504` as
  Problem Details with the request id, not as SvelteKit's generic 500.
- **Bodies buffered, deliberately.** A request body is read as bytes once, so it can be replayed
  after a token refresh — a stream cannot be sent twice.
- `X-Forwarded-For` is appended, and unsafe methods must come from the same origin.

Two authentication schemes coexist: ASP.NET Identity bearer tokens and a custom `X-Api-Key`.
Endpoints that accept both say so explicitly (`AuthSchemes.BearerOrApiKey`); API keys additionally
carry scopes, checked by policy. See [auth.md](../api/docs/auth.md).

## The data model

About thirty entities. The groups, and the edges that matter:

- **Identity** — `ApplicationUser` (ASP.NET Identity), `ApiKey`, `UserQuota`, `AuditEntry`.
- **Content** — `Link` is globally deduplicated by the SHA-256 of its canonical URL and shared by
  everyone; `Host` is likewise get-or-created per hostname and carries the moderation blocklist.
  A `Link` holds the fetched metadata and the extracted article text.
- **Collections** — `Playlist` → `PlaylistItem` → `Link`. A playlist has `PlaylistMember` for
  sharing, `PlaylistLike`, `PlaylistPreference` (per person, per playlist), and lives optionally
  in a `Folder` via `FolderPlaylist`.
- **Tags** — `Tag` rows are global and unique on name. `PlaylistTag` and `PlaylistItemTag` join
  them. This is why renaming a tag means repointing one person's joins rather than editing the
  row: the row belongs to everybody.
- **Ingestion** — `Source` (RSS, scraper, JSON API) → `SourceRun`, attached to playlists through
  `PlaylistSource`, optionally built from a `SourceTemplate`. `AutomationRule` acts on what
  arrives.
- **Everything else** — `Follow`, `SavedSearch`, `Backup`, `ContentReport`, `IdempotencyRecord`.

### Conventions that apply to all of it

- **Soft delete.** `ISoftDeletable` entities get a query filter hiding rows with a `DeletionTime`,
  and `SaveChanges` turns `EntityState.Deleted` into a timestamp. Use `Remove`/`RemoveRange`.
  `ExecuteDelete` is a real purge and belongs only in retention jobs (`TrashService`,
  `SourceRunRetention`, `IdempotencyRetention`).
- **Unique indexes must be partial.** `.IsUnique().ExcludeSoftDeleted()` adds
  `WHERE "DeletionTime" IS NULL`. Without it a soft-deleted row blocks recreating the same key
  forever — delete a playlist called "Reading" and you can never have another.
- **Concurrency is `xmin`.** Postgres's system column, configured as the concurrency token. There
  is no version column and there should not be one.
- **Stamping is central.** `CreationTime` and `LastModified` are set in `SaveChanges`, not by
  callers. The consequence worth knowing: a change that only touches join rows leaves the parent
  untouched, so anything ordering or syncing on `LastModified` never hears about it — which is
  why editing tags explicitly touches the playlist or item they hang off.
- **Generated columns where a value must not drift.** The search vector and the duplicate-
  detection host+path key are both `HasComputedColumnSql(..., stored: true)`, maintained by the
  database rather than by remembering to update them.
- **Migrations are append-only.** There is a published instance with this history. It is never
  rewritten, never squashed.

## Paging

Listings page by position, not by offset: a cursor names the row the page stopped at. See
[Paging](../api/docs/playlists.md#paging) for which listings do this, which still count rows and
why, and what a malformed cursor or an out-of-range `limit` gets back.

## Enrichment

A saved link starts with nothing but an address. `LinkEnricher` fetches it — immediately for a
manual save, through Hangfire for anything a source brought in — and fills in the title,
description, thumbnail, site name, content kind and the readable article text. The fetch is
throttled per host, guarded against SSRF, and its outcome is recorded as one of four states:

| `EnrichmentStatus` | Meaning |
| --- | --- |
| `Pending` | Never successfully fetched |
| `Succeeded` | Fetched and read |
| `Failed` | Failed for a reason that may not last — blocked, non-HTML, a 4xx |
| `Broken` | The page is gone. The link still exists; the thing it pointed at does not |

Reads only surface links with an `EnrichedAt`, including failed ones: a link that cannot be
fetched should appear, labelled, rather than sit invisible in a playlist whose owner can see the
count. A failure stamps `EnrichmentError` and increments `FailureCount`, which drives the recheck
backoff; a transient failure (5xx, 429) throws instead, so Hangfire retries rather than recording
a dead end.

The enricher also records where the fetch actually landed, when a redirect took it somewhere
else, which is what lets the duplicates page notice two addresses for one page.

## Background work

Hangfire, with Postgres storage, in the API process. One recurring job per source on its own
schedule, registered by `HangfireSourceScheduler`, plus the maintenance set in
`MaintenanceScheduler`: the trash purge, link rechecks, the content-kind classification sweep, the
automation runner, the archive sweep, the backup sweep, the weekly digest, and retention for
source runs and idempotency records. The dashboard is mounted at `/hangfire` in development only.

Everything here assumes **one instance**. Migrations take a Postgres advisory lock so two
starting processes cannot race, but the schedulers do not coordinate; running two would run every
recurring job twice. Scaling out means moving the workers out of the API process first.

## Where to read next

- [auth.md](../api/docs/auth.md) — the two schemes, scopes, and what an API key can do
- [playlists.md](../api/docs/playlists.md) — the main API surface, paging, search
- [sources.md](../api/docs/sources.md) — the three source types and how a run works
- [folders.md](../api/docs/folders.md), [backups.md](../api/docs/backups.md),
  [highlights.md](../api/docs/highlights.md), [webhooks.md](../api/docs/webhooks.md),
  [email.md](../api/docs/email.md)
- [web/docs/offline.md](../web/docs/offline.md) — the save queue and the service worker
- [CONTRIBUTING.md](../CONTRIBUTING.md) — the conventions a change is expected to follow
