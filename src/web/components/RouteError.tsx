"use client";

import { useEffect } from "react";

export function RouteError({ error, retry }: { error: unknown; retry: () => void }) {
  useEffect(() => {
    console.error(error);
  }, [error]);
  return (
    <section role="alert" className="space-y-3 rounded-lg border border-red-200 bg-red-50 p-5 dark:border-red-900 dark:bg-red-950/40">
      <p className="font-medium">Something went wrong loading this page.</p>
      <button type="button" onClick={() => retry()}
        className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm font-medium hover:bg-white dark:border-zinc-700 dark:hover:bg-zinc-900">
        Try again
      </button>
    </section>
  );
}
