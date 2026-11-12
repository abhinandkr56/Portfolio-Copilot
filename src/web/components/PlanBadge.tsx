import type { PlanType } from "@/lib/types";

const styles: Record<PlanType, string> = {
  Direct: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
  Regular: "bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-200",
  Unknown: "bg-zinc-200 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300",
};

export function PlanBadge({ plan }: { plan: PlanType }) {
  return <span className={`inline-flex rounded px-1.5 py-0.5 text-xs font-medium ${styles[plan]}`}>{plan}</span>;
}
