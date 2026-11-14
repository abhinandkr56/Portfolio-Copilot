import type { Finding } from "@/lib/types";

/** The dashboard header's stale-price warning: the StaleNav finding's own message (count and oldest date), or nothing. */
export function StaleNavNotice({ findings }: { findings: Finding[] }) {
  const stale = findings.find((f) => f.rule === "StaleNav");
  if (!stale) return null;
  return (
    <span role="status" data-testid="stale-nav" className="ml-2 font-medium text-amber-700 dark:text-amber-400">
      · {stale.message}
    </span>
  );
}
