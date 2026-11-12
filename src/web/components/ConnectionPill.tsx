import { apiGet } from "@/lib/api";
import type { SessionResponse } from "@/lib/types";

export async function ConnectionPill() {
  let session: SessionResponse | null = null;
  try {
    session = await apiGet<SessionResponse>("/api/session");
  } catch {
    session = null;
  }

  if (!session) {
    return <span className="rounded-full bg-zinc-200 px-2.5 py-1 text-xs dark:bg-zinc-800">Api unreachable</span>;
  }
  if (!session.connected) {
    return (
      <a href="/connect" className="rounded-full bg-zinc-200 px-2.5 py-1 text-xs font-medium hover:bg-zinc-300 dark:bg-zinc-800 dark:hover:bg-zinc-700">
        Not connected · Connect
      </a>
    );
  }
  return (
    <span className="rounded-full bg-green-100 px-2.5 py-1 text-xs font-medium text-green-800 dark:bg-green-950 dark:text-green-200">
      Connected · {session.userIdMasked} · {session.mode}
    </span>
  );
}

export function ConnectionPillSkeleton() {
  return <span className="inline-block h-6 w-32 animate-pulse rounded-full bg-zinc-200 dark:bg-zinc-800" />;
}
