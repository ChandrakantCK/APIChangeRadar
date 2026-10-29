"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { Alert, AuthCard } from "@/components/ui";
import { useAuth } from "@/lib/auth";

// GitHub sign-in ends here. The API has already set the refresh cookie; AuthProvider exchanges it
// for an access token on page load, and we just wait for the outcome.
export default function AuthCallbackPage() {
  const router = useRouter();
  const { status } = useAuth();

  useEffect(() => {
    if (status === "signedIn") router.replace("/dashboard");
    if (status === "signedOut") router.replace("/login?error=github");
  }, [status, router]);

  return (
    <AuthCard title="Signing you in">
      <Alert kind="info">Finishing sign-in...</Alert>
    </AuthCard>
  );
}
