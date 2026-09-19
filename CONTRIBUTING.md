# Contributing

The useful half of this document is not the pull-request process — it is the set of conventions
the code already follows. They are visible in the tree but not written down anywhere, so a second
person would have to reverse-engineer them from 150 commits.

## Getting it running

```sh
docker compose up --build
```

That is the whole stack: Postgres, the API on `:5180`, the web app on `:5173`, and Mailpit on
`:8025` to catch outbound mail. Running the pieces separately is in the [README](README.md).

## Running the tests

Five suites, and a change should leave all of them green:

```sh
cd api
dotnet test Linkbelli.slnx        # unit + integration (integration needs Docker)

cd web
npm run check && npm run lint && npm test

cd extension && npm test
cd mcp && npm test
cd email-worker && npm test
```

The API integration suite uses Testcontainers and takes about three minutes. It shares one
database across the whole run and does not parallelise, so a test that asserts on a global count
will be flaky — scope assertions to rows your test created, usually with a unique name or host.

The web tests run as two Vitest projects. Plain `*.test.ts` files are logic and server code.
`*.svelte.test.ts` files mount components with Testing Library: query the way somebody using the
page would (by role and accessible name — if a control cannot be found that way, that is usually
a bug in the component, not the test), and stand in for the API with `fakeApi` from
`$lib/testing/fakeApi`, which fails loudly on any route the test did not expect. A small Vite
plugin (`web/vite-plugins/deep-imports.ts`) rewrites icon and bits-ui imports to per-file ones in
that project only; without it every component test compiles several thousand Svelte files.

The end-to-end suite (`web/e2e`, `npm run test:e2e`) drives a real browser through a few whole
journeys against the `docker compose` stack, which has to be running. Locally it uses the Chrome
you have installed; set `E2E_CHANNEL` to use another, or run `npx playwright install chromium`
for Playwright's own. Only the step a journey is about goes through the browser — accounts,
playlists and links it merely needs are made through the API (`e2e/support/app.ts`), so a
journey about exporting does not break when the playlist screen changes. Some journeys reach the
public internet (example.com, wikipedia.org) because enrichment and thumbnails do.

## Conventions

### Every change comes with a test that would have failed before it

Not "a test", specifically one that discriminates. If a test passes against the old code, it is
documentation, not verification. Several tests in this repo say in their own comments what the
old behaviour was and why the assertion is shaped the way it is — that is the standard.

Where behaviour is hard to reach from a test (an outbound fetch, a browser API), verify it by
hand against the running stack and say so in the commit message, including what you ran.

### Comments explain the decision, not the mechanism

The code says what it does. A comment earns its place by saying what would go wrong without it,
what was tried first, or which of two plausible readings is the intended one. `// increment the
counter` is noise; `// Counted here too, because returning early meant no YouTube link ever
reached the counter` is the house style.

The same goes for XML doc comments on interfaces and entities: `<summary>` for what it is,
`<remarks>` for why it is that way.

### Names read like prose

Test names are sentences: `A_link_you_add_is_recorded_as_yours`, not `TestAddLink`. So are user
-facing strings — "That cursor is not one of ours. Ask for the first page without it." rather
than "Invalid cursor". Nothing in the UI or an error message should read like a stack trace.

### Layering

`api/src` is `Contracts → Core → Application → Infrastructure → Api`, and the arrows only point
one way. In particular **`Application` may not reference `Infrastructure`**: anything needing
Npgsql, EF internals or a provider-specific type goes behind an interface in `Application` and is
implemented in `Infrastructure`. `IFullTextSearch` and `IAppDbContext` are the examples to copy.

### Database conventions

- **Soft delete.** Entities implementing `ISoftDeletable` are hidden by a query filter and
  deleted by setting `DeletionTime` in `SaveChanges`. Use `Remove`/`RemoveRange` — `ExecuteDelete`
  is a genuine purge and belongs only in retention jobs.
- **Unique indexes must be partial.** `.IsUnique().ExcludeSoftDeleted()`, or a deleted row blocks
  recreating the same key forever.
- **Concurrency is `xmin`.** Do not add a version column.
- **Migrations are append-only.** There is a published instance with this history; it is never
  rewritten or squashed.

### Commits

Imperative, prose, no `feat:`/`fix:` prefixes. The subject says what changed from the reader's
point of view — "Say when the app cannot keep a copy of itself", not "Fix service worker". The
body says what was wrong, what was decided, and what was deliberately not done. If something was
verified by hand, the body says what was run and what came back.

## Reporting something

Bugs and ideas go in issues. Security problems do not — see [SECURITY.md](SECURITY.md).
