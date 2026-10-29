"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState, type FormEvent } from "react";
import { Alert, AuthCard, Button, Field } from "@/components/ui";
import { api } from "@/lib/api";
import { useAuth, type SignInResult } from "@/lib/auth";

const redirectErrors: Record<string, string> = {
  github: "GitHub sign-in didn't complete. Please try again.",
  "github-no-verified-email": "Your GitHub account has no verified email address. Verify one on GitHub, then try again.",
  locked: "This account is temporarily locked. Try again in 15 minutes.",
};

const signInErrors: Record<Exclude<SignInResult, "ok">, string> = {
  invalid: "Invalid email or password.",
  unverified: "Please confirm your email address first.",
  tooMany: "Too many attempts. Please wait and try again.",
};

function LoginForm() {
  const router = useRouter();
  const params = useSearchParams();
  const { status, signIn } = useAuth();
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<SignInResult | null>(null);
  const [email, setEmail] = useState("");
  const [resent, setResent] = useState(false);

  // Already signed in (for example coming back with a valid session): go straight to the dashboard.
  useEffect(() => {
    if (status === "signedIn") router.replace("/dashboard");
  }, [status, router]);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setSubmitting(true);
    const outcome = await signIn(String(form.get("email")), String(form.get("password")));
    setSubmitting(false);
    setResult(outcome);
    if (outcome === "ok") router.replace("/dashboard");
  }

  async function resend() {
    await api.POST("/auth/resend-verification", { body: { email } });
    setResent(true);
  }

  const redirectError = redirectErrors[params.get("error") ?? ""];

  return (
    <div className="space-y-4">
      {redirectError && <Alert kind="error">{redirectError}</Alert>}
      {result && result !== "ok" && (
        <Alert kind="error">
          {signInErrors[result]}
          {result === "unverified" && (
            <button type="button" onClick={resend} disabled={resent} className="ml-1 underline">
              {resent ? "Link sent." : "Send the link again"}
            </button>
          )}
        </Alert>
      )}

      {/* A plain link, not fetch: the browser itself must travel to GitHub and back. */}
      <a
        href="/api/auth/github/start"
        className="flex w-full items-center justify-center gap-2 rounded-md border border-gray-300 px-3 py-2 text-sm font-semibold hover:bg-gray-50"
      >
        Continue with GitHub
      </a>

      <div className="flex items-center gap-3 text-xs text-gray-400">
        <span className="h-px flex-1 bg-gray-200" /> or <span className="h-px flex-1 bg-gray-200" />
      </div>

      <form onSubmit={onSubmit} className="space-y-4">
        <Field label="Email" name="email" type="email" autoComplete="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
        <Field label="Password" name="password" type="password" autoComplete="current-password" required />
        <Button type="submit" disabled={submitting}>
          {submitting ? "Signing in..." : "Sign in"}
        </Button>
      </form>
    </div>
  );
}

export default function LoginPage() {
  return (
    <AuthCard
      title="Sign in"
      footer={
        <>
          New here? <Link href="/signup" className="text-blue-600 hover:underline">Create an account</Link>
        </>
      }
    >
      <Suspense fallback={<Alert kind="info">Loading...</Alert>}>
        <LoginForm />
      </Suspense>
    </AuthCard>
  );
}
