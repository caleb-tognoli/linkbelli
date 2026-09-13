# Linkbelli Web

SvelteKit frontend for Linkbelli. It is a **pure client of the `/api/v1` API** with a
**BFF (backend-for-frontend) auth** layer: the SvelteKit server proxies the API and stores the
bearer + refresh tokens in **httpOnly cookies**, so tokens never reach browser JavaScript.

## Prerequisites

- **Node.js 20+** and npm.
- The Linkbelli API running (default `http://localhost:5180` — start it from `../api`), or the
  whole stack via `docker compose up` from the repository root.

## Setup

```bash
cd web
cp .env.example .env        # set API_BASE_URL if the API isn't on :5180
npm install
npm run dev                 # http://localhost:5173
```

| Command | What it does |
| --- | --- |
| `npm run dev` | Development server with hot reload |
| `npm run build` | Production build (`adapter-node`) |
| `npm run check` | `svelte-check` — types and template diagnostics |
| `npm run lint` | ESLint, including the Svelte rules |
| `npm test` | Vitest unit tests |
| `npm run test:coverage` | The same, with coverage |

## How it is put together

- `src/hooks.server.ts` — reads the access cookie, refreshes via `/api/v1/auth/refresh` when
  needed, exposes `locals.api()` (injects the bearer, retries once on 401), and guards routes
  (unauthenticated → `/login`; authenticated visiting `/login`|`/register` → `/`).
- `src/routes/api/v1/[...path]/+server.ts` — the proxy the browser actually calls. Headers pass
  through an allowlist in both directions, so conditional GETs, downloads and idempotency keys
  work while cookies and forged `Authorization` never reach the API.
- `src/lib/server/auth.ts` — httpOnly cookie helpers (`lb_access`, `lb_refresh`).
- `src/lib/server/config.ts` — `API_BASE` from `$env/dynamic/private`.
- `src/lib/api/` — typed wrappers around the endpoints, used by `+page.server.ts` loaders.
- `src/lib/components/` — the shared UI. `LinkTable` and `PlaylistView` carry most of it.
- `src/routes/+layout.*` — loads the current user (`/api/v1/me`) and renders the app shell
  (sidebar, folder tree, command palette) for signed-in users, or a bare centered view for the
  auth pages.
- `src/service-worker.ts` — caches the app shell so it opens with no connection. See
  [docs/offline.md](docs/offline.md), which also covers the save queue and what happens when the
  worker cannot install.

### Tech

SvelteKit (Svelte 5 runes) · TypeScript · Tailwind v4 (`@tailwindcss/vite`) · `adapter-node` ·
`bits-ui` for dialogs and menus · `svelte-dnd-action` for reordering · `@lucide/svelte` for icons.

## Conventions

- **Server-side loads fetch; components render.** A `+page.server.ts` gathers what a screen needs
  through `locals.api()` and degrades to an empty result rather than failing the page, so one
  rate-limited panel does not take down the screen around it.
- **No API data in the service worker cache.** A cached list of links is a lie with a timestamp
  on it; only the shell is cached.
- **Comments explain the decision, not the mechanism.** If a line looks odd, the comment says
  what went wrong without it.
