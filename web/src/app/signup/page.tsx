"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Alert, AuthCard, Button, Field } from "@/components/ui";
import { api } from "@/lib/api";

export default function SignUpPage() {
  const [submitting, setSubmitting] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [sentTo, setSentTo] = useState<string | null>(null);
  const [resent, setResent] = useState(false);

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const email = String(form.get("email"));
    setSubmitting(true);
    setErrors([]);

    const { response, error } = await api.POST("/auth/signup", {
      body: {
        email,
        password: String(form.get("password")),
        organizationName: String(form.get("organizationName")) || null,
      },
    });

    setSubmitting(false);
    if (response.ok) {
      setSentTo(email);
    } else if (response.status === 429) {
      setErrors(["Too many attempts. Please wait a minute and try again."]);
    } else {
      setErrors(Object.values(error?.errors ?? {}).flat().filter((e): e is string => !!e));
    }
  }

  async function resend() {
    if (!sentTo) return;
    await api.POST("/auth/resend-verification", { body: { email: sentTo } });
    setResent(true);
  }

  if (sentTo) {
    return (
      <AuthCard title="Check your email">
        <Alert kind="success">
          We sent a confirmation link to <strong>{sentTo}</strong>. Open it to activate your account.
        </Alert>
        <button onClick={resend} className="text-sm text-blue-600 hover:underline" disabled={resent}>
          {resent ? "Sent again - check your inbox" : "Didn't get it? Send it again"}
        </button>
      </AuthCard>
    );
  }

  return (
    <AuthCard
      title="Create your account"
      footer={
        <>
          Already have an account? <Link href="/login" className="text-blue-600 hover:underline">Sign in</Link>
        </>
      }
    >
      <form onSubmit={onSubmit} className="space-y-4">
        {errors.length > 0 && (
          <Alert kind="error">
            {errors.map((e) => (
              <div key={e}>{e}</div>
            ))}
          </Alert>
        )}
        <Field label="Email" name="email" type="email" autoComplete="email" required />
        <Field label="Password (at least 12 characters)" name="password" type="password" autoComplete="new-password" minLength={12} required />
        <Field label="Organization name (optional)" name="organizationName" placeholder="My workspace" />
        <Button type="submit" disabled={submitting}>
          {submitting ? "Creating account..." : "Create account"}
        </Button>
      </form>
    </AuthCard>
  );
}
