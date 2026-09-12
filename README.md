# Linkbelli

Self-hosted link aggregation: define **sources** (RSS, JSON APIs, HTML scrapers) that are
polled on a schedule, enrich the discovered links, and organize them into **playlists** you
can keep private or share. A C# / .NET 10 API backs a SvelteKit web app via a
backend-for-frontend (BFF) auth layer.

## Architecture

```
api/   ASP.NET Core Minimal APIs  ─ Layered: Contracts → Core → Application → Infrastructure → Api
       EF Core 10 + Npgsql (PostgreSQL 17), Hangfire (Postgres storage) for scheduled source runs
web/   SvelteKit 2 / Svelte 5 (adapter-node). BFF proxies /api/v1; tokens live in httpOnly cookies
extension/  Manifest V3 browser extension — saves the current tab into a playlist (no build step)
mcp/   Model Context Protocol server — lets an AI assistant search the library and read the
       article text saved with each link (see mcp/README.md)
email-worker/  Cloudflare Email Worker — save a link by emailing it (see email-worker/README.md)
```

Four ways to save a link without opening the app: the browser extension, a bookmarklet (on the
profile page — drag it to the bookmarks bar), the system share sheet once the web app is installed
to a phone's home screen, and emailing it. The first three land on `/save`, which pre-fills the
address; email goes to a webhook source via the Cloudflare worker in `email-worker/`. A save made
with no connection is queued and sent when one returns (see `web/docs/offline.md`).

- Auth is dual-scheme: Identity bearer tokens (for the web BFF) and `X-Api-Key` keys.
- The web app never exposes tokens to browser JS — they're held in httpOnly cookies and
  proxied through `/api/v1`, with an Origin check on unsafe methods as CSRF defense.

## Run the full stack locally

Requires Docker. From the repo root:

```sh
docker compose up --build
```

- Web app: http://localhost:5173
- API + Scalar docs (Development): http://localhost:5180/scalar
- Hangfire dashboard: http://localhost:5180/hangfire (open in Development)
- **Mailpit** catches every message the API sends: http://localhost:8025. No provider account, no
  domain, and nothing a dev box sends can reach a real person.
- Postgres is published on host port **5433** (5432 is assumed taken by a native install).

Nothing needs configuring to run locally. To point mail at a real provider, `cp .env.example .env`
and edit — it is gitignored, and every key falls back to the local default when absent.

## Develop without Docker

```sh
# Database only
docker compose up postgres

# API (set the connection string to the host-published port)
cd api
dotnet run --project src/Linkbelli.Api

# Web
cd web
npm install
npm run dev
```

## Tests

```sh
cd api
dotnet test tests/Linkbelli.Tests/Linkbelli.Tests.csproj                 # unit, no infra
dotnet test tests/Linkbelli.IntegrationTests/Linkbelli.IntegrationTests.csproj  # Testcontainers (needs Docker)

cd web
npm run check   # svelte-check / type-check
npm test        # Vitest unit suite

cd extension
npm test        # Vitest unit suite (chrome.* is stubbed; no browser needed)

cd mcp
npm test        # Vitest unit suite (the protocol surface, over an in-memory transport)

cd email-worker
npm test        # Vitest unit suite (the MIME parsing, without Cloudflare's runtime)
```

CI (`.github/workflows/ci.yml`) runs the unit suite, a vulnerable-dependency scan, the
integration suite (with Docker), the web type-check, unit tests and build, and the extension's
unit tests.

## Required production configuration

The API reads these via standard .NET configuration (env vars use `__` for nesting, e.g.
`ConnectionStrings__Default`):

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:Default` | PostgreSQL connection string. |
| `DataProtection:KeyRingPath` | **Required in production.** Directory for the persisted Data Protection key ring. Without it, keys regenerate on restart — logging out all users and making encrypted source-config secrets undecryptable. Must be a stable, shared path across instances. |
| `DataProtection:ApplicationName` | Optional; defaults to `Linkbelli`. Keep stable across deploys. |
| `Hangfire:Dashboard:Username` / `Hangfire:Dashboard:Password` | Basic-auth credentials for the Hangfire dashboard outside Development. If unset, the dashboard is closed. |
| `Admin:Usernames` | String array of usernames granted the admin role at startup. |
| `Cors:AllowedOrigins` | String array of allowed browser origins. |
| `Thumbnails:CachePath` | Directory for cached link thumbnails, which are served from this host rather than hotlinked from the sites they came from. Without it the cache lands in the system temp directory and is refetched whenever that is cleared. |
| `PublicWebBaseUrl` | The web app's public origin. Used to build the links inside syndicated playlist feeds; without it the API falls back to its own address, which behind a proxy is an internal hostname. |
| `Database:MigrateAtStartup` | `true` to apply EF migrations on boot. |
| `Email:Host` / `Email:Port` / `Email:Username` / `Email:Password` | SMTP, for password resets, notifications and the weekly digest. **Empty means mail is off**, and the features that need it say so rather than promising a message nobody will send. Plain SMTP on purpose: the provider is a config change, not a code change. Brevo is `smtp-relay.brevo.com:587`. |
| `Email:FromAddress` / `Email:FromName` | Who mail comes from. The address must be one the provider has verified for the domain, or it will be filed as spam. |
| `Email:UseTls` | On everywhere real. Off only for a local catch-all mailbox that speaks plaintext on purpose. |
| `Email:PublicUrl` | Where links in mail point. **Never derived from the request** — a reset link built from an inbound Host header is a way to mail somebody a link to an attacker's site. |
| `Email:InboxDomain` | Optional. The domain that receives mailed-in links, so the app can show somebody their inbox address. See `email-worker/README.md`. |

The web app reads `API_BASE_URL` (where the BFF reaches the API), `ORIGIN` (the app's public
origin, used for form-action CSRF checks), and `COOKIE_SECURE` (`false` only for local HTTP).

> Horizontal scaling additionally requires the Data Protection key ring to be shared *and*
> encrypted at rest (`ProtectKeysWith*`), so every replica validates tokens minted by the others.
