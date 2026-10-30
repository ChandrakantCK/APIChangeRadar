# 0002. Short-lived JWTs + rotating refresh tokens with reuse detection

- Status: Accepted
- Date: 2026-10-29 (implemented in Phase 1 Steps 6–8)

## Context

The API needs to know who is calling on every request, quickly, across multiple instances. Users expect to stay signed in for days. A stolen credential must be containable, and we must be able to end a session (logout, theft).

## Decision

- **Access token:** a JWT, HS256, **15 minutes**, claims `sub`, `email`, `jti`, `iss`, `aud`. Validated statelessly; algorithm pinned (`ValidAlgorithms = [HS256]`), issuer/audience/lifetime checked, 30 s clock skew. Signing key ≥ 32 random bytes, validated at start-up, kept in user-secrets / environment variables only.
- **Refresh token:** 32 random bytes (base64url), **14 days**, stored **only as a SHA-256 hash** (unique index), grouped into a **family** per sign-in.
  - **Rotation:** every refresh revokes the presented token (`replaced_by` → new one) and issues a new one in the same family.
  - **Reuse detection:** presenting an already-revoked token revokes the **whole family** (`reuse-detected`).
  - **Race safety:** revocation is a compare-and-set `UPDATE … WHERE revoked_at IS NULL`; the loser of a race is treated as reuse.
  - **Logout** revokes the family (`logout`).
- **Transport:** refresh token only in an `HttpOnly; Secure; SameSite=Strict` cookie with `Path = <PathBase>/auth`. The access token is kept **in memory** in the browser (never `localStorage`); refresh is single-flight (shared promise + Web Locks).
- **Brute force:** Identity lockout (5 failures → 15 min) plus per-IP rate limiting on `/auth/*` (10 per 60 s).
- Proven by `Reusing_a_rotated_refresh_token_revokes_the_whole_family` (Phase 1 "done" test).

## Alternatives considered

- **Long-lived JWT only** — simple, but a stolen token can't be revoked.
- **Server-side session cookie only** (no JWT) — easy revocation, but a DB/cache lookup on every request and harder for non-browser clients; still a good option for a pure web app.
- **Refresh token in `localStorage` / response body** — readable by any injected script (XSS).
- **Opaque refresh token without hashing** — a database leak would hand out working sessions.
- **Hosted auth (Auth0, Clerk…)** — less code, but the goal of this project is to learn the mechanics.

## Consequences

- Fast authorization (no lookup per request) and revocable sessions.
- An access token stays valid up to 15 minutes after logout or reuse detection — accepted trade-off.
- Two tabs refreshing at the same instant could look like reuse; mitigated in the web app by single-flight refresh. A short reuse grace period is a possible future refinement.
- Expired/revoked `refresh_tokens` rows accumulate → a cleanup job is needed (Phase 3).
- Rotating the JWT signing key logs everyone out of their *access* tokens (≤ 15 min impact); key rotation with `kid` is a future improvement.
