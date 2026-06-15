---
name: start-project
description: Build and run the full Linkbelli stack (Postgres + API + web) locally with Docker Compose. Use when asked to start, run, boot, or spin up the project / app / full stack, or to verify a change in the running app.
---

# Start the Linkbelli stack

Brings up the whole app — PostgreSQL, the .NET API, and the SvelteKit web app — via Docker Compose.

## Prerequisites

- **Docker Desktop must be running** (the Linux engine). Check with `docker info`; if the daemon
  pipe is unavailable, start Docker Desktop and wait until `docker info` succeeds (can take a few minutes).
- Run all commands from the repo root (`docker-compose.yml` lives there).

## Start

```bash
docker compose up --build
```

`--build` rebuilds the API and web images so local code changes are included. Drop it for a faster
boot when nothing changed. Add `-d` to run detached (background).

The API applies EF migrations at startup (`Database__MigrateAtStartup=true`), so the schema is
created/updated automatically — no manual migration step needed.

## Services & URLs

| Service  | URL / port | Notes |
|----------|------------|-------|
| Web app  | http://localhost:5173 | The SvelteKit BFF; the only thing the browser talks to |
| API      | http://localhost:5180 | Runs in `Development`, so Scalar docs are at http://localhost:5180/scalar |
| Hangfire | http://localhost:5180/hangfire | Background-job dashboard (open in Development) |
| Postgres | host port **5433** | Mapped to container 5432 (5432 is assumed taken by a native install) |

The API persists its Data Protection key ring to the `dpkeys` volume, so bearer tokens and
encrypted source secrets survive restarts.

## Verify it's up

```bash
curl -s http://localhost:5180/health      # API health check
curl -s http://localhost:5173 -o /dev/null -w "%{http_code}\n"   # web responds
```

Then open http://localhost:5173 and register a user.

## Stop / reset

```bash
docker compose down            # stop containers, keep data
docker compose down -v         # also drop volumes (pgdata + dpkeys) for a clean slate
```

## Troubleshooting

- **`docker info` fails / named-pipe error** → Docker Desktop isn't running. Start it and retry.
- **Port already in use (5173/5180/5433)** → stop the conflicting process or edit the `ports`
  mappings in `docker-compose.yml`.
- **Watch logs** → `docker compose logs -f api` (or `web` / `postgres`).
- **Rebuild only one service** → `docker compose up --build api`.
