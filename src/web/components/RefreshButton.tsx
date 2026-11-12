"use client";

import { useRouter } from "next/navigation";
import { useState, useTransition } from "react";
import { apiSend } from "@/lib/api";

/** Drops the Api's cached Kite data, then re-renders the server components (doc 07: Refresh → router.refresh()). */
export function RefreshButton() {
  const router = useRouter();
  const [pending, startTransition] = useTransition();
  const [busy, setBusy] = useState(false);

  async function refresh() {
    setBusy(true);
    try {
      await apiSend("POST", "/api/refresh");
    } catch {
      // still refresh the page; it will show the Api's error state
    } finally {
      setBusy(false);
    }
    startTransition(() => router.refresh());
  }

  const working = busy || pending;
  return (
    <button
      type="button"
      onClick={refresh}
      disabled={working}
      className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm font-medium hover:bg-zinc-100 disabled:opacity-60 dark:border-zinc-700 dark:hover:bg-zinc-800"
    >
      {working ? "Refreshing…" : "Refresh"}
    </button>
  );
}
