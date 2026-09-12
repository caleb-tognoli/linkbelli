# Offline

The app is a view onto data that lives on a server, so most of it needs a connection and says so.
One part does not: **saving a link never loses the link**, whether or not the request can get out.

## Why this exists

The system share sheet is how links arrive from a phone, and a phone is exactly where there is no
signal. Before this, sharing a link on a train did nothing recoverable — the request failed, the
sheet closed behind the person, and nothing anywhere recorded that a link had been meant to be
saved. The link was gone, and so was any trace that it had ever existed.

## The queue

A save that cannot reach the server is kept on the device and sent later.

- Queued when the request never completes (no connection) **or** when the server answers 5xx — a
  server having a bad minute is as good a reason to wait as no signal.
- Not queued on a 4xx. The server read the request and refused it; waiting changes nothing.
- The screen says *"Waiting for a connection"*, not *"Saved"*. The link is kept, and which of
  those it is matters.
- Queueing the same address into the same playlist twice collapses to one entry. Pressing save
  again because nothing appeared to happen is the expected reaction, not a request for two copies.

### Sending it

The queue drains when a connection comes back — on the `online` event, on the next page load, and
from a **Send now** button on the banner. Sends are sequential: a connection returning is the
worst moment to open forty requests at once, and the links were queued in an order somebody chose.

A send stops at the first failure that looks like a missing connection rather than marching
through the rest, because every one of them is about to fail the same way.

### Giving up

A queued save that the server **refuses** is retried up to three times and then left alone. The
banner names each one with the playlist it was headed for, and offers to forget it. Retrying a
malformed address forever would park a permanent error at the front of the queue and block
everything behind it.

### Limits

- At most 200 queued saves. Nobody shares that many links by hand without noticing, so this is a
  guard against a loop filling a phone's storage rather than a limit anyone should meet.
- Kept in `localStorage`, which is per-device and per-browser: a link queued on a phone is sent by
  that phone. Storage that is blocked or full means the save cannot be kept — the screen says so
  rather than pretending otherwise.

## The service worker

`src/service-worker.ts` caches the built bundle, everything in `static/`, and the shells of
`/save`, `/playlists` and `/queue`, so opening the app with no connection gets the app rather than
the browser's error page. Shell pages are cached under their path alone, without the query string:
the share sheet appends a different URL every time, so caching per full address would never hit.

**Nothing from `/api/` is ever cached.** A cached list of links is a lie with a timestamp on it.
API requests go to the network and fail honestly when there is none; the queue is what handles
writes that cannot get through.

Navigations are network-first — the page is rendered from live data, and the live one is right.
The cache is only what stands in when there is no network at all, and a request that finds neither
gets a plain offline page written into the worker itself, since the one thing certainly
unavailable in that branch is the server.

Registration is explicit (`src/lib/serviceWorker.ts`), called once from the root layout. It is not
left to the framework: nothing in the built output registered it, and a worker that is compiled
but never installed looks like offline support while providing none.

> **Not verified in an automated test.** Service worker registration is blocked in the browser
> this was developed against, so the caching behaviour above has only been reasoned about and
> built, not exercised. The queue, which is the part that prevents losing a link, is verified
> end to end. Check the worker in a real browser's Application tab before relying on it.
