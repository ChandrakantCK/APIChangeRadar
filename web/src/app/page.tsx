import Link from "next/link";
import { Suspense } from "react";
import { ApiHealth, ApiHealthSkeleton } from "@/components/api-health";

export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 p-8">
      <h1 className="text-4xl font-bold">API Change Radar</h1>
      <p className="text-lg text-gray-500">Know before your integration breaks.</p>
      <div className="flex gap-3">
        <Link href="/signup" className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700">
          Create account
        </Link>
        <Link href="/login" className="rounded-md border border-gray-300 px-4 py-2 text-sm font-semibold hover:bg-gray-50">
          Sign in
        </Link>
      </div>
      <Suspense fallback={<ApiHealthSkeleton />}>
        <ApiHealth />
      </Suspense>
    </main>
  );
}
