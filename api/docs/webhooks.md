# Linkbelli API — Webhooks

A webhook is an address Linkbelli tells about things that happen in your library: a link saved, a
source that stopped, something tagged `to-read`. They are what you hang a Home Assistant
automation, a chat bot, an n8n flow or a site rebuild off.

Management lives under **`/api/v1/me/webhooks`** and needs a **signed-in session**. An API key
cannot create or change webhooks, whatever its scopes: a webhook forwards everything that happens
from then on to an address of the caller's choosing, and a key limited to reading one thing must
not be able to turn itself into a copy of everything.

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `GET`    | `/me/webhooks/events`                           | The events you can subscribe to, with a line each |
| `GET`    | `/me/webhooks`                                  | Your webhooks. Never includes the secret |
| `POST`   | `/me/webhooks`                                  | Make one. `201` with the signing secret — **shown once** |
| `PATCH`  | `/me/webhooks/{id}`                             | Change the address, events or description; `active` pauses or re-enables |
| `DELETE` | `/me/webhooks/{id}`                             | Remove it. Anything still queued for it is cancelled |
| `POST`   | `/me/webhooks/{id}/secret`                      | Replace the signing secret. Returns the new one, once |
| `POST`   | `/me/webhooks/{id}/test`                        | Send a `ping`. `202` with the delivery |
| `GET`    | `/me/webhooks/{id}/deliveries`                  | The last 50 deliveries, newest first |
| `POST`   | `/me/webhooks/deliveries/{deliveryId}/redeliver` | Send an earlier delivery's exact body again |

`test` and `redeliver` make this server send a request because somebody asked, so they share the
`sensitive` rate limit with the other endpoints that do.

```http
POST /api/v1/me/webhooks
{ "url": "https://example.com/hooks/linkbelli", "events": ["items.added"], "description": "Discord #reading" }
```

```json
{
  "webhook": {
    "id": "0199…", "url": "https://example.com/hooks/linkbelli", "description": "Discord #reading",
    "events": ["items.added"], "status": "Active", "consecutiveFailures": 0,
    "disabledReason": null, "lastDeliveredAt": null, "createdAt": "2026-09-18T09:00:00Z"
  },
  "secret": "whsec_…"
}
```

## Events

| Event | When |
|-------|------|
| `items.added`       | Links saved into a playlist — by hand, by a source, an import, a paste, or filed there by a rule |
| `items.finished`    | Links marked finished, including by reading to the end in the reader |
| `items.tagged`      | A tag put on links, by hand or by a rule. One event per tag, and only for tags that are new |
| `source.stopped`    | A source that kept failing and has been stopped |
| `highlight.created` | A passage marked in an article |
| `ping`              | Sent by the test endpoint only. Not something to subscribe to |

The item events are **plural on purpose**. One action is one event, however many items it
touched: a source run that finds forty links is a single `items.added` with forty items, not forty
requests. They are grouped **per playlist**, because the playlist is what a receiver routes on, and
they fire for the **playlist's owner** — an editor adding to your shared list is adding to your
library, and it is your automations that hear about it.

## What is sent

Every delivery is a `POST` with a JSON body and these headers:

| Header | Value |
|--------|-------|
| `Content-Type` | `application/json; charset=utf-8` |
| `Linkbelli-Event` | The event name |
| `Linkbelli-Delivery` | The delivery id — also `id` in the body |
| `Linkbelli-Signature` | `t=<unix seconds>,v1=<hex HMAC-SHA256>` — see below |
| `User-Agent` | `Linkbelli-Webhooks/1.0` |

The body is always an envelope around the event's data:

```json
{
  "id": "0199b8a2-…",
  "event": "items.added",
  "occurredAt": "2026-09-18T09:12:44.123Z",
  "data": {
    "via": "source",
    "playlist": { "id": "0198…", "name": "Reading", "slug": "reading" },
    "source": { "id": "0197…", "name": "Hacker News front page" },
    "items": [
      {
        "id": "0199…", "linkId": "0198…", "url": "https://example.org/a-good-piece",
        "title": null, "note": null, "status": "Added", "addedAt": "2026-09-18T09:12:44.001Z"
      }
    ]
  }
}
```

`via` is `manual`, `source`, `import`, `paste` or `rule`; `source` is `null` except when a source
found them. **`title` is often `null`**: a link is announced when it is saved, and its page is
fetched afterwards.

The other events carry:

- `items.finished` — `playlist` and `items`.
- `items.tagged` — `tag`, `playlist` and `items`.
- `source.stopped` — `source` (`id`, `name`, `type`), `consecutiveFailures` and `lastError`.
- `highlight.created` — `link` (`id`, `url`, `title`) and `highlight` (`id`, `text`, `note`,
  `paragraphIndex`).

The body is fixed when the event happens and stored. A retry an hour later sends it **byte for
byte**, describing things as they were — and `id` stays the same across retries and redeliveries,
so a receiver can drop one it has already handled.

## Verifying a delivery

Compute an HMAC-SHA256 over `<t>.<raw body>` with the secret, compare it to `v1` in constant time,
and reject anything whose `t` is more than a few minutes from now. The timestamp is inside the
signature, so a captured delivery cannot be replayed later with a fresh date. There may be more
than one `v1`; any one matching is enough.

Use the **raw** body — parsing and re-serialising the JSON first will not reproduce the same bytes.

```js
// Node
import { createHmac, timingSafeEqual } from 'node:crypto';

function verify(secret, rawBody, header, toleranceSeconds = 300) {
	const parts = Object.groupBy(header.split(','), (p) => p.split('=')[0]);
	const t = parts.t?.[0]?.slice(2);
	if (!t || Math.abs(Date.now() / 1000 - Number(t)) > toleranceSeconds) return false;

	const expected = Buffer.from(createHmac('sha256', secret).update(`${t}.${rawBody}`).digest('hex'));
	return (parts.v1 ?? []).some((p) => {
		const given = Buffer.from(p.slice(3));
		return given.length === expected.length && timingSafeEqual(given, expected);
	});
}
```

```python
# Python
import hashlib, hmac, time

def verify(secret: str, raw_body: bytes, header: str, tolerance: int = 300) -> bool:
    pairs = [p.split("=", 1) for p in header.split(",") if "=" in p]
    t = next((v for k, v in pairs if k == "t"), None)
    if t is None or not t.isdigit() or abs(time.time() - int(t)) > tolerance:
        return False
    expected = hmac.new(secret.encode(), f"{t}.".encode() + raw_body, hashlib.sha256).hexdigest()
    return any(hmac.compare_digest(v, expected) for k, v in pairs if k == "v1")
```

## When it does not arrive

Anything but a `2xx` is a failure. Redirects are **not followed** — a `3xx` is reported as a
failure telling you to use the final address — and a receiver has **10 seconds** to answer.

A failed delivery is retried **1 minute, 5 minutes, 30 minutes and 2 hours** later: five attempts
over a little under three hours, spread out because the usual reason a receiver is down is that it
is being restarted. After the fifth it is marked `Failed`.

**Five failed deliveries in a row turn the webhook off** (`status: "Disabled"`, with a
`disabledReason`), and any success resets the count. A receiver that answers **`410 Gone`** is
turned off at once — it is the one unambiguous thing a receiver can say. Turning it back on with
`PATCH { "active": true }` clears the count. A `ping` goes even to a paused or disabled webhook, so
you can check a receiver is fixed before re-enabling it, and never counts either way.

Deliveries are kept for **30 days**, then forgotten.

## Which addresses are allowed

The address is yours, and this server fetches it — which makes a webhook a request-forgery
primitive if nothing checks where it points. Every delivery goes through the same guard enrichment
uses, at the moment the connection is made, so a name that resolves somewhere private is refused
however it got there.

By default only public addresses are allowed. **Whoever runs the server** can also allow private
network addresses — `10/8`, `172.16/12`, `192.168/16` and IPv6 `fc00::/7` — which is where most
home receivers live:

```
Webhooks__AllowPrivateNetworks=true
```

It is an operator setting rather than a per-user one because on a server other people use it would
let any of them send requests into the network the server sits on. Loopback and link-local —
including a cloud instance's metadata service at `169.254.169.254` — are refused either way.

## Limits

| Limit | Value |
|-------|-------|
| Webhooks per account | 10 |
| Address length | 2,048 characters |
| Deliveries in flight at once, per server | 4 |
| Attempts per delivery | 5 |
| Failed deliveries in a row before it is turned off | 5 |
| Delivery history | 30 days, last 50 shown |
