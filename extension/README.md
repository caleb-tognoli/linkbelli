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

`host_permissions` in the manifest is what lets the extension call your server directly; it does
not need an entry in the API's `Cors:AllowedOrigins`. The manifest ships with `http://localhost:5180`
for local development plus `https://*/*` — narrow that second one to your own host before you
hand the extension to anyone else.
