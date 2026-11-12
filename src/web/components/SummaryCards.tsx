import { formatInr } from "@/lib/format";
import type { FindingCounts } from "@/lib/types";

type Props = {
  totalValue: number;
  stockCount: number;
  fundCount: number;
  activeMonthlySip: number;
  counts: FindingCounts;
};

function Card({ title, children, small = false }: { title: string; children: React.ReactNode; small?: boolean }) {
  return (
    <div className="rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
      <p className="text-xs font-medium uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{title}</p>
      <div className={`mt-1 font-semibold tabular-nums ${small ? "text-base sm:text-lg" : "text-xl"}`}>{children}</div>
    </div>
  );
}

/** Every value here comes from the Api; the counts are lengths of lists the Api returned. */
export function SummaryCards({ totalValue, stockCount, fundCount, activeMonthlySip, counts }: Props) {
  return (
    <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
      <Card title="Total value">{formatInr(totalValue)}</Card>
      <Card title="Holdings">
        {stockCount + fundCount === 0 ? "0" : `${stockCount} stocks · ${fundCount} funds`}
      </Card>
      <Card title="Active SIPs / month">{formatInr(activeMonthlySip)}</Card>
      <Card title="Findings" small>
        <span className="text-red-700 dark:text-red-400">{counts.act} Act</span>
        <span className="mx-1 text-zinc-400">·</span>
        <span className="text-amber-700 dark:text-amber-400">{counts.watch} Watch</span>
        <span className="mx-1 text-zinc-400">·</span>
        <span className="text-blue-700 dark:text-blue-400">{counts.info} Info</span>
      </Card>
    </div>
  );
}
