"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useRef, useState } from "react";
import { Alert, AuthCard } from "@/components/ui";
import { api } from "@/lib/api";

type State = "verifying" | "verified" | "invalid";

function VerifyEmail() {
  const params = useSearchParams();
  const userId = params.get("userId");
  const token = params.get("token");
  const [result, setResult] = useState<State>("verifying");
  const state: State = userId && token ? result : "invalid";

  // Send the confirmation exactly once. React runs effects twice in development (StrictMode),
  // and two simultaneous confirmations race each other in the database.
  const sent = useRef(false);

  useEffect(() => {
    if (!userId || !token || sent.current) return;
    sent.current = true;

    // POST, not GET: email scanners open links automatically, and a GET must never change state.
    void api.POST("/auth/verify-email", { body: { userId, token } }).then(({ response }) => {
      setResult(response.ok ? "verified" : "invalid");
    });
  }, [userId, token]);

  if (state === "verifying") {
    return <Alert kind="info">Confirming your email address...</Alert>;
  }

  return state === "verified" ? (
    <>
      <Alert kind="success">Your email is confirmed.</Alert>
      <Link href="/login" className="block text-center text-sm font-semibold text-blue-600 hover:underline">
        Continue to sign in
      </Link>
    </>
  ) : (
    <>
      <Alert kind="error">This link is invalid or has expired.</Alert>
      <Link href="/signup" className="block text-center text-sm text-blue-600 hover:underline">
        Sign up again or request a new link
      </Link>
    </>
  );
}

export default function VerifyEmailPage() {
  return (
    <AuthCard title="Confirm your email">
      <Suspense fallback={<Alert kind="info">Loading...</Alert>}>
        <VerifyEmail />
      </Suspense>
    </AuthCard>
  );
}
