# Security

Linkbelli is self-hosted, and it is a fairly large attack surface for its size: it authenticates
people, publishes pages anonymously, accepts mail, and fetches arbitrary URLs on the internet on
its users' behalf. If you find something, please tell me before you tell anyone else.

## Reporting

Email **tognolicaleb@gmail.com**. Please include enough to reproduce it — a request, a URL, a
sequence of steps — and say whether you have told anyone else.

You will get an acknowledgement within a week. I am one person, so I cannot promise a fix
timeline, but I will tell you honestly what I intend to do and when, and I will credit you in the
release notes unless you would rather I did not.

Please do not open a public issue for anything in the list below.

## What I am particularly interested in

- **Anything that crosses an account boundary.** Reading, writing or deleting another user's
  playlists, items, sources, API keys or backups.
- **Anything that makes the fetcher do your bidding.** The enricher, the source runner and the
  thumbnail cache all make outbound requests. SSRF protections are in
  `api/src/Linkbelli.Application/Http`, and a way past them is a real finding.
- **Anything a `Private` or `Unlisted` playlist leaks.** Unlisted is share-by-link and must never
  appear in a listing, a sitemap, a feed, discovery or a search result.
- **Token handling.** Bearer and refresh tokens live in httpOnly cookies and are injected by the
  SvelteKit BFF; anything that gets one into browser-reachable JavaScript, or past the proxy's
  header allowlist, matters.
- **API key scopes.** A key granted `playlists:read` performing a write, or reaching an admin
  surface.
- **Stored content rendering.** Titles, descriptions, notes and scraped metadata all come from
  the open web and are rendered in the app and in public pages.
- **The email ingest path.** Mail arriving at the Cloudflare worker is parsed and turned into
  saved links; it is untrusted input that reaches the database.

## What is already known, and not a finding

- **Anonymous rate limits are per IP.** Everyone behind one NAT shares a bucket. Configurable
  (`RateLimits:GlobalBurst`), documented, and deliberate.
- **A single instance has no CSRF token beyond `SameSite=Lax` plus an `Origin` check** on unsafe
  methods through the BFF. If you can defeat that pair, that *is* a finding.
- **Registration is open by default.** `Registration:Mode` closes it; an operator who leaves it
  open has chosen that.
- **`Content` and scraped metadata are stored verbatim.** They are escaped at render, not at
  write, so a stored payload existing in the database is expected — one that executes is not.

## Running it safely

The README lists the configuration a production deployment must set. Two are worth repeating
here: `DataProtection:KeyRingPath` must be persistent (without it every restart invalidates every
token), and `ForwardedHeaders:TrustedNetworks` must match your proxy (without it the rate limiter
sees one client for the whole internet). The API refuses to start in a non-Development
environment when the required keys are missing.
