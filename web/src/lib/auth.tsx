"use client";

import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import type { components } from "./api-types";
import { api, withSession } from "./api";
import { clearSession, msUntilExpiry, refreshSession, setSession } from "./session";

export type UserProfile = components["schemas"]["UserProfile"];
export type SignInResult = "ok" | "invalid" | "unverified" | "tooMany";
type Status = "loading" | "signedIn" | "signedOut";

type AuthContextValue = {
  status: Status;
  profile: UserProfile | null;
  signIn: (email: string, password: string) => Promise<SignInResult>;
  signOut: () => Promise<void>;
};

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<Status>("loading");
  const [profile, setProfile] = useState<UserProfile | null>(null);

  const loadProfile = useCallback(async () => {
    const { data } = await withSession(() => api.GET("/me"));
    setProfile(data ?? null);
    setStatus(data ? "signedIn" : "signedOut");
  }, []);

  // On every page load: the access token was only in memory, so ask the refresh cookie for a new one.
  useEffect(() => {
    let cancelled = false;
    void refreshSession().then(async (ok) => {
      if (cancelled) return;
      if (ok) {
        await loadProfile();
      } else {
        setStatus("signedOut");
      }
    });
    return () => {
      cancelled = true;
    };
  }, [loadProfile]);

  // While signed in, renew the 15-minute access token shortly before it expires.
  useEffect(() => {
    if (status !== "signedIn") return;
    const timer = setInterval(() => {
      if (msUntilExpiry() < 2 * 60_000) {
        void refreshSession().then((ok) => {
          if (!ok) {
            setProfile(null);
            setStatus("signedOut");
          }
        });
      }
    }, 30_000);
    return () => clearInterval(timer);
  }, [status]);

  const signIn = useCallback(
    async (email: string, password: string): Promise<SignInResult> => {
      const { data, response } = await api.POST("/auth/login", { body: { email, password } });
      if (data) {
        setSession(data);
        await loadProfile();
        return "ok";
      }
      if (response.status === 403) return "unverified";
      if (response.status === 429) return "tooMany";
      return "invalid";
    },
    [loadProfile],
  );

  const signOut = useCallback(async () => {
    await api.POST("/auth/logout");
    clearSession();
    setProfile(null);
    setStatus("signedOut");
  }, []);

  const value = useMemo(() => ({ status, profile, signIn, signOut }), [status, profile, signIn, signOut]);
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used inside <AuthProvider>.");
  }
  return context;
}
