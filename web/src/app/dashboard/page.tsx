"use client";

import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState, type FormEvent } from "react";
import { Alert, Button, Field } from "@/components/ui";
import type { components } from "@/lib/api-types";
import { api, withSession } from "@/lib/api";
import { useAuth } from "@/lib/auth";

type Organization = components["schemas"]["OrganizationDetails"];
type AuditEntry = components["schemas"]["AuditEntryView"];
type DashboardData = { organization: Organization | null; auditLog: AuditEntry[] };

async function loadDashboard(): Promise<DashboardData> {
  const [organization, auditLog] = await Promise.all([
    withSession(() => api.GET("/organizations/current")),
    withSession(() => api.GET("/organizations/current/audit-log")),
  ]);
  return { organization: organization.data ?? null, auditLog: auditLog.data ?? [] };
}

export default function DashboardPage() {
  const router = useRouter();
  const { status, profile, signOut } = useAuth();
  const [organization, setOrganization] = useState<Organization | null>(null);
  const [auditLog, setAuditLog] = useState<AuditEntry[]>([]);
  const [message, setMessage] = useState<{ kind: "error" | "success"; text: string } | null>(null);

  useEffect(() => {
    if (status === "signedOut") router.replace("/login");
  }, [status, router]);

  const show = useCallback((data: DashboardData) => {
    setOrganization(data.organization);
    setAuditLog(data.auditLog);
  }, []);

  useEffect(() => {
    if (status !== "signedIn") return;
    let cancelled = false;
    void loadDashboard().then((data) => {
      if (!cancelled) show(data);
    });
    return () => {
      cancelled = true;
    };
  }, [status, show]);

  async function rename(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const name = String(new FormData(event.currentTarget).get("name"));
    const { response } = await withSession(() => api.PATCH("/organizations/current", { body: { name } }));
    setMessage(
      response.ok
        ? { kind: "success", text: "Organization renamed." }
        : { kind: "error", text: response.status === 403 ? "Only owners and admins can rename." : "Please enter a valid name (1-100 characters)." },
    );
    if (response.ok) show(await loadDashboard());
  }

  async function onSignOut() {
    await signOut();
    router.replace("/login");
  }

  if (status !== "signedIn" || !profile) {
    return <main className="p-8 text-sm text-gray-500">Loading...</main>;
  }

  return (
    <main className="mx-auto w-full max-w-3xl space-y-8 p-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Dashboard</h1>
          <p className="text-sm text-gray-500">Signed in as {profile.email}</p>
        </div>
        <button onClick={onSignOut} className="rounded-md border border-gray-300 px-3 py-1.5 text-sm hover:bg-gray-50">
          Sign out
        </button>
      </header>

      {organization && (
        <section className="space-y-4 rounded-xl border border-gray-200 p-6">
          <div>
            <h2 className="text-lg font-semibold">{organization.name}</h2>
            <p className="text-sm text-gray-500">
              Plan: {organization.plan} · Your role: {organization.role}
            </p>
          </div>
          {message && <Alert kind={message.kind}>{message.text}</Alert>}
          <form onSubmit={rename} className="flex items-end gap-3">
            <div className="flex-1">
              <Field label="Rename organization" name="name" defaultValue={organization.name} maxLength={100} required />
            </div>
            <div className="w-32">
              <Button type="submit">Save</Button>
            </div>
          </form>
        </section>
      )}

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Audit log</h2>
        <ul className="divide-y divide-gray-100 rounded-xl border border-gray-200">
          {auditLog.map((entry) => (
            <li key={`${entry.occurredAt}-${entry.action}`} className="flex justify-between gap-4 px-4 py-2 text-sm">
              <span>
                <span className="font-mono">{entry.action}</span>
                {entry.target && <span className="text-gray-500"> - {entry.target}</span>}
              </span>
              <time className="shrink-0 text-gray-500">{new Date(entry.occurredAt).toLocaleString()}</time>
            </li>
          ))}
          {auditLog.length === 0 && <li className="px-4 py-2 text-sm text-gray-500">No entries yet.</li>}
        </ul>
      </section>
    </main>
  );
}
