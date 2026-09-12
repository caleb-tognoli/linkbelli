# Email in

Save a link by emailing it.

A Cloudflare Email Worker receives the message, pulls the addresses out of it, and posts them to
Linkbelli's `/api/v1/inbox/{token}` — which hands them to the same webhook source a scripted push
would, so a mailed link gets the same filters, the same deduplication, the same quota and the same
run history as every other way in.

Both halves are free. Cloudflare Email Routing has no per-address charge, and this fits inside the
Workers free plan.

## How the addressing works

**The address is the credential.** A webhook source's token is the local part, so a source with
token `C8DP6Pd2…` receives mail at `C8DP6Pd2…@in.example.com`.

That is why there is no mapping table to keep in step: the worker needs nothing but an API URL,
and Linkbelli can show somebody their own inbox address — which it could not do if an operator
chose the local part separately.

Plus-addressing works, so `C8DP6Pd2…+recipes@in.example.com` reaches the same source and lets
your mail client file a copy.

## Who may send

An email address is **not** a secret the way a POST token is. It ends up in headers, in forwards,
and in other people's sent folders. So by default a source accepts mail only from **the account's
own email address**.

To accept more, set `allowedSenders` on the source's config — a comma-separated list:

```jsonc
{ "allowedSenders": "me@example.com, colleague@example.com" }
```

An explicit list **replaces** the owner rather than adding to them, because a list is a statement
about who may send and silently keeping somebody on it would make the setting mean something other
than what it says.

A sender who is not allowed gets the same answer as an address that does not exist — otherwise
trying would confirm the address is real.

## Setting it up

1. **Add the domain to Cloudflare** and enable Email Routing on it.
2. **Deploy the worker:**
   ```bash
   cd email-worker
   npm install
   wrangler secret put LINKBELLI_API_URL   # e.g. https://linkbelli.example
   wrangler deploy
   ```
3. **Route a catch-all to it.** In Email Routing, set the catch-all action to "Send to a Worker"
   and pick `linkbelli-email-in`. Catch-all, because the local part is a token and there is one per
   source.
4. **Tell Linkbelli the domain**, so it can show people their address:
   ```
   Email__InboxDomain=in.example.com
   ```
5. **Make a webhook source** in the app and note its token. The address is that token at your
   domain.

## What the worker does and does not do

- It **rejects** rather than silently dropping: a bounce tells the sender nothing arrived, where a
  discard loses the link with no trace anywhere.
- A message with no links in it is rejected with that reason, not retried — there is nothing to
  retry.
- Linkbelli being unreachable is rejected as temporary, so the sending server tries again rather
  than the message being accepted and lost.
- It **does not parse MIME properly.** It splits headers from body, decodes base64 and
  quoted-printable, and hands the result over as one string. The only question being asked is
  "where are the addresses in this", which survives a lot of imprecision — and a full MIME
  implementation is a large amount of code to maintain for a job the URL extractor finishes
  anyway. If that ever stops being true, `src/parse.js` is the file to grow.
- Quoted-printable **soft line breaks** are decoded, which matters more than it sounds: it is how
  a long URL survives a 76-column limit, and getting it wrong would split the one thing worth
  extracting.

## Developing

```bash
npm install
npm test     # the parser, without Cloudflare's runtime
```

The parser is tested on its own because every mail client formats a message differently and that
variety is what lands in `parse.js`.
