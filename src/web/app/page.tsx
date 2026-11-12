import { AllocationBars } from "@/components/AllocationBars";
import { ConnectBanner } from "@/components/ConnectBanner";
import { ConnectToast } from "@/components/ConnectToast";
import { ErrorPanel, KITE_DOWN_MESSAGE } from "@/components/ErrorPanel";
import { ExplainButton } from "@/components/ExplainButton";
import { FindingsList } from "@/components/FindingsList";
import { SummaryCards } from "@/components/SummaryCards";
import { apiGet, apiSend } from "@/lib/api";
import { formatDate, formatIst } from "@/lib/format";
import { load } from "@/lib/load";
import type { HoldingsResponse, Plan, PortfolioReview, SipsResponse } from "@/lib/types";

const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v);

export default async function Dashboard({ searchParams }: PageProps<"/">) {
  const sp = await searchParams;
  const toast = <ConnectToast connected={first(sp.connected)} reason={first(sp.reason)} />;

  const result = await load(() =>
    Promise.all([
      apiSend<PortfolioReview>("POST", "/api/review", { explain: false }),
      apiGet<HoldingsResponse>("/api/holdings"),
      apiGet<SipsResponse>("/api/sips"),
      apiGet<Plan>("/api/plan"),
    ]),
  );

  if (result.kind === "session") return <div className="space-y-4">{toast}<ConnectBanner /></div>;
  if (result.kind === "unavailable") return <div className="space-y-4">{toast}<ErrorPanel message={KITE_DOWN_MESSAGE} /></div>;

  const [review, holdings, sips, plan] = result.data;
  const stale = review.findings.find((f) => f.rule === "StaleNav");

  return (
    <div className="space-y-6">
      {toast}
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h1 className="text-2xl font-semibold">Dashboard</h1>
        <p className="text-sm text-zinc-500 dark:text-zinc-400">
          As of {formatIst(review.asOfUtc)} · NAV date {formatDate(review.navDate)}
          {stale && <span className="ml-2 font-medium text-amber-700 dark:text-amber-400">· some fund prices are stale</span>}
        </p>
      </div>

      <SummaryCards
        totalValue={holdings.totalValue}
        stockCount={holdings.stocks.length}
        fundCount={holdings.funds.length}
        activeMonthlySip={sips.activeMonthlyEquivalent}
        counts={review.counts}
      />

      <section className="rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900">
        <h2 className="mb-3 font-semibold">Allocation vs target</h2>
        <AllocationBars classes={review.allocation.classes} bandPoints={plan.driftBandPoints} />
      </section>

      <section>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 className="font-semibold">Findings</h2>
          <ExplainButton />
        </div>
        <FindingsList findings={review.findings} initialSeverity={first(sp.severity)} />
      </section>
    </div>
  );
}
