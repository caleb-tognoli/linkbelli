# Changelog

Notable changes, newest first. Kept by hand, in the same register as the commit messages: what
changed from the point of view of somebody using or running this, not which files moved.

This file starts partway through the project's life. Everything before the first entry below is
in the git history, which is verbose and reads well; nothing has been reconstructed here that
could be got wrong.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Nothing is released yet — everything
below is under `Unreleased`, and `0.1.0` will be cut from it.

## [Unreleased]

### Added

- Tags can be renamed, merged and removed, with a management screen that counts how many
  playlists and links a change will touch before it runs.
- An owner can see who added each link on a shared playlist, and who liked a playlist they
  published.
- An automation rule can be run over the links already in a playlist, rather than only over what
  arrives next.
- A link saved by email: mail a URL to the address on your profile and it lands in a playlist.
- A weekly digest, to whoever asks for one.
- Backups: a scheduled export of the whole library, kept for a retention window, and a
  restore that puts one back. It merges rather than replaces — anything saved since the
  snapshot survives it — and says how many playlists and links it will add before it runs.
  A file downloaded earlier restores the same way, so a library outlives the server it was
  kept on.
- A password reset that works, and an onboarding checklist for a new account.
- An MCP server, so an assistant can read the library.
- Full-text search over stored article text, backed by a Postgres index rather than a scan.
- The app is installable, saves links with no connection, and queues them until there is one.

### Changed

- **Listings page by position rather than by offset.** A cursor now names the row a page stopped
  at, so nothing is repeated or skipped when something is written while you read. Search
  relevance, discovery's `liked`/`largest` orderings, a playlist's score and shuffle sorts, and
  the moderation queue still page by counting, because their ordering has no column to resume
  after; the API documentation says which are which.
- **A `limit` outside 1–100 is a `400`, not a quietly corrected page size**, as is a cursor that
  cannot be read.
- The sitemap is built from one query instead of fifty serial round trips, cached for an hour,
  and stops at the sitemaps protocol's 50,000-URL limit rather than silently truncating at 5,000.
  Its `lastmod` is now the newest link in a playlist rather than the day the playlist was made.
- "Similar playlists" is asked from the join tables instead of scanned across every public
  playlist. Measured on 2,000 public playlists and 105,000 items: 8.4 seconds to 1.8 ms.
- ETags are computed only for JSON that is not a download, and only up to 512 KB — exports and
  backups are no longer buffered in memory to produce a header nothing reads.
- The BFF proxy forwards headers through an allowlist in both directions, so conditional GETs,
  download filenames, cache directives and idempotency keys all work through it.
- Duplicate detection happens in the database, and now includes addresses that redirect to the
  same page — offered as a suggestion, since a paywall stub or a consent screen also collects
  several addresses.
- The web app says when a browser will not keep an offline copy of it, rather than failing
  silently.
- Usernames are validated, and an instance can close registration.

### Fixed

- A playlist page no longer loses every thumbnail after the tenth.
- Saving a link no longer fails silently in the link table.
- Two instances can no longer run migrations at once.
- The vulnerable-dependency CI gate actually fails when it finds something.
- The API answers `400` and `409` where it was answering `500`.
- The browser extension no longer asks for access to every website.
- An export carries the tags on each link. They were being written out with the playlist's
  tags and none of their own, so a round trip through a backup lost them.

### Security

- Licensed AGPL-3.0.
- The API refuses to start in a non-Development environment without the settings whose absence
  loses data — the connection string, the data-protection key ring, and the mail settings when
  mail is configured.
- Sign-in and sign-up have their own rate-limit bucket, separate from everything else.
