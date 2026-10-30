# Architecture Decision Records

An ADR records **one** significant decision: the context that forced it, what we chose, what we rejected, and what it costs us. They are short, numbered, and never rewritten — if a decision changes, add a new ADR that supersedes the old one.

| # | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | Accepted |
| [0002](0002-refresh-token-rotation.md) | Short-lived JWTs + rotating refresh tokens with reuse detection | Accepted |
| [0003](0003-same-origin-web-proxy.md) | Browser talks only to the web app; `/api` proxy trusted by shared secret | Accepted |
| [0004](0004-tenant-isolation.md) | Tenant isolation in the application: scope filter + EF query filter + write guard | Accepted |
| [0005](0005-email-over-https.md) | Send email through an HTTPS API, never block a request on it | Accepted |

Planned (from the build plan): Postgres job queue, transactional outbox, SSRF-safe HTTP client, billing provider.

## Template

```markdown
# NNNN. Title

- Status: Proposed | Accepted | Superseded by NNNN
- Date: YYYY-MM-DD

## Context
What problem or force makes a decision necessary?

## Decision
What we do.

## Alternatives considered
What else, and why not.

## Consequences
Good, bad, and follow-ups.
```
