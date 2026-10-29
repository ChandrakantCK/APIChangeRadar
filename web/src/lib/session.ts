import type { components } from "./api-types";

type LoginResponse = components["schemas"]["LoginResponse"];

// The access token lives only in memory - never in localStorage, where any injected script could read it.
// The refresh token is an HttpOnly cookie the browser sends to /api/auth/*; JavaScript never sees it.
let accessToken: string | null = null;
let expiresAtMs = 0;
let inFlight: Promise<boolean> | null = null;

export function getAccessToken(): string | null {
  return accessToken;
}

export function msUntilExpiry(): number {
  return accessToken ? expiresAtMs - Date.now() : 0;
}

export function setSession(session: LoginResponse): void {
  accessToken = session.accessToken;
  expiresAtMs = Date.parse(session.expiresAt);
}

export function clearSession(): void {
  accessToken = null;
  expiresAtMs = 0;
}

/**
 * Swaps the refresh cookie for a new access token (and a rotated cookie).
 * Refresh tokens work only ONCE: two refreshes racing with the same cookie look exactly like token
 * theft and the API ends the session. So we allow one refresh at a time - within this tab (shared
 * promise, which also covers React running effects twice in development) and across tabs (Web Locks).
 */
export function refreshSession(): Promise<boolean> {
  inFlight ??= (async () => {
    const run = async () => {
      const response = await fetch("/api/auth/refresh", { method: "POST" });
      if (!response.ok) {
        clearSession();
        return false;
      }

      setSession((await response.json()) as LoginResponse);
      return true;
    };

    try {
      return "locks" in navigator ? await navigator.locks.request("radar-refresh", run) : await run();
    } finally {
      inFlight = null;
    }
  })();

  return inFlight;
}
