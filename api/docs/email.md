# Linkbelli API — Email

Mail is **off unless configured**, and the features that need it say so rather than promising a
message nobody will send. `POST /auth/forgot-password` answers `503` on an instance with no mail;
notifications and the digest simply do not send.

## Choosing a provider

Plain SMTP, deliberately. Every provider worth using speaks it, so switching between Brevo,
Resend, SES, Postmark or a company relay is five settings and no code:

```
Email__Host=smtp-relay.brevo.com
Email__Port=587
Email__Username=…
Email__Password=…
Email__FromAddress=no-reply@yourdomain
Email__PublicUrl=https://linkbelli.example
```

Port 587 upgrades with STARTTLS; 465 is TLS from the first byte. Getting that the wrong way round
is the most common way an SMTP config fails to connect at all, so it is chosen from the port.

**Verify your domain with the provider** and set SPF and DKIM. Without them, mail from a free tier
lands in spam — which for a password reset means the feature does not work, however correct the
code is.

Locally, `docker compose` runs **Mailpit**: every message is caught and readable at
http://localhost:8025, with no account and no way to reach a real person.

## What gets sent

| Message | When | Can be turned off |
|---------|------|-------------------|
| Password reset | Somebody asks for one | No — it is the way back into an account |
| Password changed | A password actually changes | No — this is how somebody learns their account was taken |
| Playlist shared with you | Somebody shares one | Yes, on by default |
| A source stopped | It gives up after repeated failures | Yes, on by default |
| Somebody followed your playlist | A follow | Yes, **off** by default |
| Weekly summary | Weekly, if there was anything | Yes, **off** by default |

The defaults follow one rule: **rare and caused by something done to your account** is on;
**recurring, or somebody else's activity** is chosen rather than discovered. A recurring email
nobody asked for is the surest way to make somebody resent an app.

## Turning things off

```http
GET  /api/v1/notifications          → { onShare, onFollow, onSourceStopped, weeklyDigest }
PUT  /api/v1/notifications          { "weeklyDigest": true }
POST /api/v1/notifications/unsubscribe  { "token": "…" }
```

Every field of the `PUT` is optional and an omitted one is left alone, so a screen that owns one
switch can save it without deciding about the rest.

**The unsubscribe endpoint is anonymous**, and deliberately. Somebody who does not want mail from
us is the least likely person to go and sign in — they may not remember the account — and asking
them to is how a product earns a spam complaint instead of an unsubscribe. Every notification
carries a link to it. The token is protected with the app's key ring, so it cannot be edited to
unsubscribe somebody else, and clicking it twice is not an error, because mail clients prefetch
links.

The password reset and password-changed notices carry **no** unsubscribe link. They are not
marketing, and a way to stop hearing that your account was taken is not a feature.

## The weekly digest

`POST /api/v1/notifications/digest/preview` sends one immediately — even when the digest is off
and even in a week when nothing happened. Deciding whether to want a weekly email is much easier
having seen one, and a preview does not count as that week's real digest.

The scheduled sweep runs hourly, fifty accounts at a time, each account weekly. That rotates
rather than mailing a whole instance in one minute, which is also what a free provider's daily cap
requires. A week in which nothing arrived sends nothing at all.

## Password reset

```http
POST /api/v1/auth/forgot-password   { "login": "username or email" }   → 202, always
POST /api/v1/auth/reset-password    { "email", "token", "newPassword" } → 204
```

The first always answers `202` whether or not the account exists, and the second gives an unknown
address the same answer as a bad token. Anything else turns these into a way to find out who has
an account.

A link works **once** and lasts **two hours**. It is built from `Email:PublicUrl`, never from the
request's Host header. Resetting invalidates every existing session, which is the point — a reset
is what somebody does when they think another person has the old password.

## Receiving mail

See [email-worker/README.md](../../email-worker/README.md) for saving a link by emailing it.
