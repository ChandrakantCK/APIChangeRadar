"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { Alert } from "@/components/ui";
import type { components } from "@/lib/api-types";
import { api, withSession } from "@/lib/api";
import { useAuth } from "@/lib/auth";

type WatchList = components["schemas"]["WatchList"];
type CatalogProvider = components["schemas"]["CatalogProviderView"];
type WatchesData = { watches: WatchList | null; catalog: CatalogProvider[] };

async function loadWatches(): Promise<WatchesData> {
  const [watches, catalog] = await Promise.all([
    withSession(() => api.GET("/organizations/current/watches")),
    api.GET("/catalog"),
  ]);
  return { watches: watches.data ?? null, catalog: catalog.data ?? [] };
}

export default function WatchesPage() {
  const router = useRouter();
  const { status } = useAuth();
  const [data, setData] = useState<WatchesData>({ watches: null, catalog: [] });
  const [message, setMessage] = useState<{ kind: "error" | "success"; text: string } | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (status === "signedOut") router.replace("/login");
  }, [status, router]);

  useEffect(() => {
    if (status !== "signedIn") return;
    let cancelled = false;
    void loadWatches().then((loaded) => {
      if (!cancelled) setData(loaded);
    });
    return () => {
      cancelled = true;
    };
  }, [status]);

  const refresh = useCallback(async () => setData(await loadWatches()), []);

  async function watch(sourceId: string, name: string) {
    setBusy(true);
    const { response, error } = await withSession(() =>
      api.POST("/organizations/current/watches", { body: { sourceId } }),
    );
    setBusy(false);
    // The server enforces the plan limit; its message says what to do.
    setMessage(
      response.ok
        ? { kind: "success", text: `Now watching ${name}.` }
        : { kind: "error", text: (error && "detail" in error && error.detail) || "Could not add the watch." },
    );
    await refresh();
  }

  async function unwatch(id: string, name: string) {
    setBusy(true);
    const { response } = await withSession(() =>
      api.DELETE("/organizations/current/watches/{id}", { params: { path: { id } } }),
    );
    setBusy(false);
    setMessage(response.ok ? { kind: "success", text: `Stopped watching ${name}.` } : { kind: "error", text: "Could not remove the watch." });
    await refresh();
  }

  if (status !== "signedIn" || !data.watches) {
    return <main className="p-8 text-sm text-gray-500">Loading...</main>;
  }

  const { watches, catalog } = data;
  const watchedSources = new Set(watches.items.map((w) => w.sourceId));
  const full = watches.used >= watches.limit;

  return (
    <main className="mx-auto w-full max-w-3xl space-y-8 p-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">Watches</h1>
          <p className="text-sm text-gray-500">
            {watches.used} of {watches.limit} APIs watched on your plan
          </p>
        </div>
        <Link href="/dashboard" className="text-sm text-blue-600 hover:underline">
          Dashboard
        </Link>
      </header>

      {message && <Alert kind={message.kind}>{message.text}</Alert>}

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Your watches</h2>
        <ul className="divide-y divide-gray-100 rounded-xl border border-gray-200">
          {watches.items.map((w) => (
            <li key={w.id} className="flex items-center justify-between gap-4 px-4 py-3 text-sm">
              <span>
                <span className="font-medium">{w.providerName}</span>
                <span className="text-gray-500"> · {w.sourceName}</span>
              </span>
              <button
                onClick={() => unwatch(w.id, w.sourceName)}
                disabled={busy}
                className="rounded-md border border-gray-300 px-3 py-1 hover:bg-gray-50 disabled:opacity-50"
              >
                Remove
              </button>
            </li>
          ))}
          {watches.items.length === 0 && (
            <li className="px-4 py-3 text-sm text-gray-500">Nothing watched yet. Pick an API from the catalog below.</li>
          )}
        </ul>
      </section>

      <section className="space-y-3">
        <h2 className="text-lg font-semibold">Catalog</h2>
        <ul className="divide-y divide-gray-100 rounded-xl border border-gray-200">
          {catalog.flatMap((provider) =>
            provider.sources.map((source) => {
              const watched = watchedSources.has(source.id);
              return (
                <li key={source.id} className="flex items-center justify-between gap-4 px-4 py-3 text-sm">
                  <span>
                    <span className="font-medium">{provider.name}</span>
                    <span className="text-gray-500"> · {source.name}</span>
                  </span>
                  {watched ? (
                    <span className="text-green-700">Watching</span>
                  ) : (
                    <button
                      onClick={() => watch(source.id, source.name)}
                      disabled={busy || full}
                      title={full ? "Plan limit reached" : undefined}
                      className="rounded-md bg-blue-600 px-3 py-1 font-semibold text-white hover:bg-blue-700 disabled:opacity-50"
                    >
                      Watch
                    </button>
                  )}
                </li>
              );
            }),
          )}
        </ul>
      </section>
    </main>
  );
}
