import { Suspense } from "react";
import { ApiHealth, ApiHealthSkeleton } from "@/components/api-health";

export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 p-8">
      <h1 className="text-4xl font-bold">API Change Radar</h1>
      <p className="text-lg text-gray-500">Coming soon.</p>
      <Suspense fallback={<ApiHealthSkeleton />}>
        <ApiHealth />
      </Suspense>
    </main>
  );
}