# Security checklist (OWASP ASVS 5.0, Level 1 baseline)

Status at the end of **Phase 1 (Foundation)**, 2026-10-29. Grouped by ASVS chapter. Each row points to the code or test that proves it — update this file in the same PR as any security-relevant change.

**Legend:** ✅ done · 🟡 partial · ⬜ planned (phase in brackets)

## V6 Authentication

| Control | Status | Evidence / note |
|---|---|---|
| Passwords stored with a slow, salted hash | ✅ | ASP.NET Core Identity PBKDF2 (HMAC-SHA512). Test: `SignUpTests.Password_is_stored_as_a_hash_never_as_plain_text` |
| Minimum length 12, no forced character mixes | ✅ | `DependencyInjection.cs` (NIST SP 800-63B style) |
| Check new passwords against known-breached lists | ⬜ | (Phase 4) e.g. Have I Been Pwned k-anonymity range API |
| Brute-force protection | ✅ | Lockout 5 → 15 min (`LoginTests.Five_wrong_passwords_lock…`); per-IP rate limit on `/auth/*` (`RateLimitTests`) |
| Generic errors that don't reveal whether an account exists | 🟡 | Login: same 401 for unknown email / wrong password; resend always 202. Sign-up still answers "already taken" (accepted for now, rate-limited) |
| Email ownership verified before password sign-in | ✅ | 403 until confirmed (`LoginTests.Unverified_email_cannot_log_in`); signed, expiring (24 h) tokens (`EmailVerificationTests`) |
| Password reset ("forgot password") | ⬜ | (Phase 4) — no reset flow yet; GitHub-created users have no password |
| External identity provider used safely | ✅ | GitHub OAuth: `state` checked, minimal scope `user:email`, link only on **verified** email, pre-hijacking defence (`GitHubLoginTests`) |
| Multi-factor authentication | ⬜ | Not planned before launch (consider for Team plan) |

## V7 Session Management

| Control | Status | Evidence / note |
|---|---|---|
| Session tokens are unguessable and stored hashed | ✅ | 256-bit refresh tokens, SHA-256 hash only (`RefreshTokenTests.Login_sets_a_locked_down_cookie_and_stores_only_a_hash`) |
| Cookies: HttpOnly, Secure, SameSite, narrow Path | ✅ | `RefreshTokenCookie.cs`; asserted in `RefreshTokenTests` and `WebProxyTests` (`path=/api/auth`) |
| Session rotation and theft detection | ✅ | Rotation + family revocation on reuse (ADR 0002; `RefreshTokenTests.Reusing_a_rotated_refresh_token_revokes_the_whole_family`) |
| Logout invalidates the session server-side | ✅ | `LogoutTests` (family revoked, cookie cleared) |
| Absolute/idle session limits | 🟡 | Refresh token expires after 14 days; no absolute maximum across rotations yet |
| "Sign out everywhere" / revoke all sessions | ⬜ | (Phase 4) together with password reset |

## V8 Authorization

| Control | Status | Evidence / note |
|---|---|---|
| Deny by default on protected endpoints | ✅ | `RequireAuthorization()` on `/me` and `/organizations/current/*` |
| Object-level access control (no IDOR) | ✅ | `OrganizationScopeFilter` membership check (ADR 0004; `TenancyTests.A_user_cannot_read_or_change_another_organization`) |
| Tenant data isolated even if a query forgets a filter | ✅ | Global query filter, fail closed (`TenancyTests.Query_filter_hides_tenant_data…`); write guard (`TenancyTests.Writing_another_organizations_data_is_refused`) |
| Function-level (role) checks | ✅ | Owner/Admin only for rename (`TenancyTests.A_member_can_read_but_not_rename`) |
| Database-level enforcement (Row-Level Security) | ⬜ | (Phase 4, optional) |

## V9 Self-contained Tokens (JWT)

| Control | Status | Evidence / note |
|---|---|---|
| Signature always verified; algorithm pinned | ✅ | `JwtAuthenticationExtensions.cs` (`ValidAlgorithms = [HS256]`); `LoginTests.Token_signed_with_another_key_is_rejected` |
| Issuer, audience, expiry validated; short lifetime | ✅ | 15 min, 30 s clock skew |
| Signing key strength and storage | ✅ | ≥ 32 random bytes, validated at start-up (`ValidateOnStart`); user-secrets / env var only |
| Key rotation (`kid`, multiple valid keys) | ⬜ | (Phase 7) |
| No sensitive data in token payload | ✅ | Only `sub`, `email`, `jti`, standard claims |

## V10 OAuth

| Control | Status | Evidence / note |
|---|---|---|
| Authorization code flow, code exchanged server-side with client secret | ✅ | `GitHubLogin.cs` (aspnet-contrib handler) |
| CSRF protection with `state` + correlation cookie | ✅ | Handler default; `GitHubLoginTests.Start_redirects_to_github…` asserts `state` |
| Exact registered redirect URIs per environment | ✅ | Separate dev and production OAuth apps (ADR 0003) |

## V11 Cryptography

| Control | Status | Evidence / note |
|---|---|---|
| Cryptographically secure randomness for secrets/tokens | ✅ | `RandomNumberGenerator` (refresh tokens, keys, proxy secret) |
| Constant-time comparison of secrets | ✅ | `TrustedWebProxyMiddleware` (`CryptographicOperations.FixedTimeEquals`) |
| Data Protection keys persisted and shared | ✅ | `data_protection_keys` table |
| Data Protection keys encrypted at rest | ⬜ | (Phase 4) |

## V12 Secure Communication

| Control | Status | Evidence / note |
|---|---|---|
| HTTPS for all external traffic | ✅ | Vercel and Render terminate TLS; database `SSL Mode=Require`; email over HTTPS (ADR 0005) |
| HSTS / security response headers | ⬜ | (Phase 4) HSTS, `X-Content-Type-Options`, `Referrer-Policy`, frame-ancestors, CSP for the web app |

## V13 Configuration

| Control | Status | Evidence / note |
|---|---|---|
| No secrets in the repository | ✅ | user-secrets, `.env*` ignored, GitHub environment secret, Render/Vercel env vars |
| Fail fast on missing/weak configuration | ✅ | Connection string and JWT options validated at start-up |
| Detailed errors only in Development | ✅ | Developer exception page only in Development; production returns plain 500 |
| Dependency vulnerability scanning | ⬜ | (Phase 4) Dependabot / `dotnet list package --vulnerable` / `npm audit` in CI |
| Trusted proxies explicitly configured | ✅ | Cloudflare + private ranges; web proxy by secret (ADR 0003; `ForwardedHeadersTests`, `WebProxyTests`) |

## V14 Data Protection

| Control | Status | Evidence / note |
|---|---|---|
| Minimal personal data collected | ✅ | Email, organization name; GitHub email only (scope `user:email`) |
| Tokens/links never logged | ✅ | `LoggingEmailSender` logs the subject only |
| Account deletion / data export | ⬜ | (before public launch, Phase 6) |

## V16 Security Logging and Error Handling

| Control | Status | Evidence / note |
|---|---|---|
| Audit trail of security-relevant actions per organization | 🟡 | `audit_entries`: organization created/renamed (actor, IP, time). Sign-in, sign-out, failed logins, lockouts, refresh reuse not yet audited — (Phase 4) |
| Security events logged | 🟡 | Refresh reuse logged as warning; email failures logged. Central log search/alerts (Phase 7) |
| Errors don't leak internals | ✅ | ProblemDetails with generic messages; no stack traces in production |

## V2 / V4 Validation, Business Logic and API

| Control | Status | Evidence / note |
|---|---|---|
| Input validated at the domain boundary | ✅ | Domain constructors/`Rename` validate; Identity validates email/password |
| Parameterized database access | ✅ | EF Core only (no string-built SQL) |
| Explicit request contracts (no mass assignment) | ✅ | Request records per endpoint |
| Rate limiting on abuse-prone endpoints | ✅ | `/auth/*` 10/min per real client IP; distributed (Redis) in Phase 3 |
| Outbound requests safe from SSRF | ⬜ | (Phase 4) `SafeHttpClient` before user-supplied URLs (webhooks, custom sources) |

## Open items, in priority order

1. Security response headers (web + API) — Phase 4
2. Password reset + "sign out everywhere" — Phase 4
3. Audit sign-in / failed sign-in / lockout / reuse events — Phase 4
4. Dependency scanning in CI — Phase 4
5. `SafeHttpClient` (SSRF) before any user-supplied URL — Phase 4
6. Breached-password check — Phase 4
7. Encrypt Data Protection keys at rest; JWT key rotation — Phase 4 / 7
8. Verified sending domain, account deletion — before launch (Phase 6)
