# 0003. Browser talks only to the web app; `/api` proxy trusted by shared secret

- Status: Accepted
- Date: 2026-10-29 (Phase 1 Steps 8b, 11a–11c)

## Context

- The web app runs on Vercel (`api-change-radar.vercel.app`), the API on Render (`radar-api-lcou.onrender.com`) behind Cloudflare.
- `vercel.app` and `onrender.com` are on the Public Suffix List, so the two are **different sites**: a `SameSite=Strict` refresh cookie set by the API would never be sent from the web page. `SameSite=None` third-party cookies are blocked or partitioned by modern browsers.
- Rate limiting, auditing and lockout need the **real client IP**, but every hop (Vercel, Cloudflare, Render) is a proxy.

## Decision

1. The browser calls **only** `https://api-change-radar.vercel.app/api/*`. Next.js `proxy.ts` rewrites these to the API, so cookies are first-party.
2. The proxy removes any client-sent `x-radar-*` headers and `Host`, then adds `x-radar-proxy-secret` plus client IP, host, scheme and path base (`/api`).
3. The API's `TrustedWebProxyMiddleware` accepts those values **only** with the correct secret (constant-time comparison) and always strips the headers. It sets `RemoteIpAddress`, `Host`, `Scheme` and `PathBase`, so the cookie path (`/api/auth`) and the GitHub OAuth `redirect_uri` follow automatically.
4. For direct traffic, `X-Forwarded-For` is trusted only from **known networks** (Cloudflare's published ranges + private/loopback), walking right-to-left with no hop limit (`TrustedProxies`).
5. `GET /diagnostics/client` shows callers what the API sees about their own connection.

## Alternatives considered

- **CORS + `SameSite=None` cookie** — third-party cookie restrictions make this unreliable.
- **Custom domain for both** (`app.example.com`, `api.example.com`) — same-site cookies without a proxy; preferred once a domain is bought.
- **Trust Vercel by IP** — Vercel has no fixed egress IPs.
- **Trust one forwarding hop** — broke in production behind Cloudflare (rate limit never triggered).

## Consequences

- One origin for the browser; no CORS configuration needed.
- Every API call has an extra hop through Vercel (small latency; Vercel usage counts).
- The proxy secret must be identical in Vercel (`WEB_PROXY_SECRET`) and Render (`WebProxy__Secret`); rotating it needs both updated together.
- Cloudflare's IP list must be re-checked occasionally.
- The direct Render URL still works for health checks and tooling, without proxy trust.
