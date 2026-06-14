# Linkbelli Web (M8)

SvelteKit frontend for Linkbelli. It is a **pure client of the `/api/v1` API** with a
**BFF (backend-for-frontend) auth** layer: the SvelteKit server proxies the API and stores the
bearer + refresh tokens in **httpOnly cookies**, so tokens never reach browser JavaScript.

See [`../.agents/WEB-PLAN.md`](../.agents/WEB-PLAN.md) for the full design and milestone plan.

## Prerequisites

- **Node.js 20+** and npm (not currently installed on this machine — install before running).
- The Linkbelli API running (default `http://localhost:5180` — start it from `../api`).

## Setup

```bash
cd web
cp .env.example .env        # set API_BASE_URL if the API isn't on :5180
npm install
npm run dev                 # http://localhost:5173
```

`npm run check` runs `svelte-check` (type-checking).

## Architecture (B1 — done)

- `src/hooks.server.ts` — reads the access cookie, refreshes via `/api/v1/auth/refresh` when
  needed, exposes `locals.api()` (injects the bearer, retries once on 401), and guards routes
  (unauthenticated → `/login`; authenticated visiting `/login`|`/register` → `/`).
- `src/lib/server/auth.ts` — httpOnly cookie helpers (`lb_access`, `lb_refresh`).
- `src/lib/server/config.ts` — `API_BASE` from `$env/dynamic/private`.
- `src/routes/login`, `src/routes/register` — form actions that authenticate and set cookies;
  register auto-signs-in. `src/routes/logout` — clears cookies.
- `src/routes/+layout.*` — loads the current user (`/api/v1/me`) and renders the app shell
  (sidebar) for signed-in users, or a bare centered view for the auth pages.

### Tech

SvelteKit (Svelte 5 runes) · TypeScript · Tailwind v4 (`@tailwindcss/vite`) · `adapter-node`.
`bits-ui` and `svelte-dnd-action` are installed for upcoming screens (B3+).

## Next (per WEB-PLAN)

B2 Home (playlist list) · B3 Playlist page (table, add+preview, reorder, tags, subscribe) ·
B4 Sources · B5 Discover + public view · B6 Settings + theme toggle.
