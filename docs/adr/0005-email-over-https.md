# 0005. Send email through an HTTPS API, never block a request on it

- Status: Accepted
- Date: 2026-10-29 (Phase 1 Steps 5 and 11c)

## Context

Sign-up and "resend verification" send email. In production on Render's free tier, **outbound SMTP ports (25/465/587) are blocked**: SMTP to Resend hung for MailKit's 2-minute default timeout, so sign-up appeared frozen and resend returned 500.

## Decision

- `IEmailSender` with three implementations chosen by configuration:
  - **Resend HTTP API** (`Email:Resend:ApiKey`, HTTPS port 443, 15 s timeout) — production;
  - **SMTP** via MailKit (`Email:Smtp:Host`, 15 s timeout) — Mailpit locally;
  - **Logging only** (nothing configured) — never logs the body, which contains a secret link.
- Email is a **side effect**: account creation commits first; a failed email is logged and the request still succeeds (sign-up 201, resend always 202 — also avoids revealing which addresses have accounts). The user can request a new link.

## Alternatives considered

- **SMTP everywhere** — blocked on the current host.
- **Paid hosting with open SMTP** — cost without benefit; HTTPS APIs are the provider-recommended path anyway.
- **Fail the request when email fails** — a mail outage would block sign-ups and leak information.

## Consequences

- Works on any host that allows HTTPS.
- If email fails, the user may not get a link until they press "send again" — Phase 3's **transactional outbox** will deliver it later with retries instead.
- Resend's test sender (`onboarding@resend.dev`) only delivers to the Resend account owner; a verified domain is needed before real users (launch, Phase 6).
