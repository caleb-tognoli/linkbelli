# Linkbelli browser extension

Saves the page you are on into a Linkbelli playlist. Manifest V3, no build step — the files here
are what the browser loads.

## Install it

1. Open `chrome://extensions` (or `edge://extensions`) and turn on **Developer mode**.
2. **Load unpacked**, and pick this `extension/` directory.
3. Open its options and fill in your server address and an API key.

## The API key

Make one under **Profile → API keys** in Linkbelli. The extension needs exactly three scopes:

- `playlists:read` — to list your playlists and see where a page is already saved
- `playlists:write` — to add the link
- `links:write` — the link itself

A key is used rather than your session on purpose: it can be revoked on its own, without signing
you out of the web app, and it never leaves the extension's own storage.

## What it does

| | |
|---|---|
| **Toolbar button** | The current page, a playlist picker, an optional note. Remembers the playlist you used last, because people save to the same list repeatedly. |
| **Already saved** | Before you save, it says which playlists already hold this page. Search canonicalizes a pasted URL and matches it on the dedup hash, so this is an exact lookup rather than a text scan. |
| **Badge** | A ✓ on the icon when the page you are looking at is already saved — the answer before you click. |
| **Right-click** | "Save to Linkbelli" on any page or link, straight into the playlist you used last. |

## Tests

```sh
cd extension
npm install
npm test
```

The tests cover `api.js`, which is where every request, error message and status-code decision
lives. `chrome.*` is stubbed, so they run without a browser.

## Cross-origin requests

Access to your server is what lets the extension call it directly; it does not need an entry in
the API's `Cors:AllowedOrigins`.

The manifest asks for **no** sites up front. It used to ship `https://*/*`, which is
read-and-modify on every website you visit — a hard thing to justify at the install prompt for a
bookmarking tool, and far more than it needs. Instead the address you enter on the options page is
requested at that moment, through `optional_host_permissions`, so the extension ends up with
access to exactly one host: yours.

Saving the current page needs nothing extra: `activeTab` covers the tab you are looking at, for
as long as you are acting on it.

## The "already saved" tick

Off by default, and worth understanding before turning it on. It asks your server about every page
you open, so your server's request log becomes a record of your browsing. When it is on, answers
are cached per address for the session and incognito tabs are skipped entirely.
