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
```

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
- Postgres is published on host port **5433** (5432 is assumed taken by a native install).

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
```

CI (`.github/workflows/ci.yml`) runs the unit suite, a vulnerable-dependency scan, the
integration suite (with Docker), and the web type-check + build.

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
| `Database:MigrateAtStartup` | `true` to apply EF migrations on boot. |

The web app reads `API_BASE_URL` (where the BFF reaches the API), `ORIGIN` (the app's public
origin, used for form-action CSRF checks), and `COOKIE_SECURE` (`false` only for local HTTP).

> Horizontal scaling additionally requires the Data Protection key ring to be shared *and*
> encrypted at rest (`ProtectKeysWith*`), so every replica validates tokens minted by the others.
