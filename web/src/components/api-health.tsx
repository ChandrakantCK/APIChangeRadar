const apiUrl = process.env.API_URL ?? "http://localhost:5214";

async function getApiHealth(): Promise<string> {
  try {
    const res = await fetch(`${apiUrl}/health`, { cache: "no-store" });
    return res.ok ? await res.text() : `Unhealthy (HTTP ${res.status})`;
  } catch {
    return "Unreachable";
  }
}

export async function ApiHealth() {
  const health = await getApiHealth();
  const healthy = health === "Healthy";

  return (
    <div className="flex items-center gap-2 rounded-lg border px-4 py-2">
      <span className={`h-3 w-3 rounded-full ${healthy ? "bg-green-500" : "bg-red-500"}`} />
      <span>
        API: <strong>{health}</strong>
      </span>
    </div>
  );
}

export function ApiHealthSkeleton() {
  return (
    <div className="flex items-center gap-2 rounded-lg border px-4 py-2">
      <span className="h-3 w-3 animate-pulse rounded-full bg-gray-400" />
      <span>API: checking…</span>
    </div>
  );
}