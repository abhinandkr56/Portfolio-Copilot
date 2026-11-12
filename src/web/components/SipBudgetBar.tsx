import { formatInr, formatPct } from "@/lib/format";

/** "Active SIPs ₹16,500/month of a ₹40,000 budget", with an SVG bar. The percentage comes from the Api. */
export function SipBudgetBar({ active, budget, usedPercent }: { active: number; budget: number; usedPercent: number }) {
  const over = usedPercent > 100;
  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
      <p className="text-sm">
        Active SIPs <strong className="tabular-nums">{formatInr(active)}/month</strong> of a{" "}
        <strong className="tabular-nums">{formatInr(budget)}</strong> budget
        <span className={`ml-2 tabular-nums ${over ? "font-semibold text-red-700 dark:text-red-400" : "text-zinc-500"}`}>
          ({formatPct(usedPercent)}{over ? ", over budget" : ""})
        </span>
      </p>
      <svg role="img" aria-label={`${formatPct(usedPercent)} of the SIP budget used`} viewBox="0 0 100 6" preserveAspectRatio="none" className="mt-2 h-3 w-full">
        <rect width="100" height="6" rx="1" className="fill-zinc-200 dark:fill-zinc-800" />
        <rect width={Math.min(100, Math.max(0, usedPercent))} height="6" rx="1" className={over ? "fill-red-500" : "fill-green-600"} />
      </svg>
    </div>
  );
}
