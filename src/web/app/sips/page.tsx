import { ConnectBanner } from "@/components/ConnectBanner";
import { ErrorPanel, KITE_DOWN_MESSAGE } from "@/components/ErrorPanel";
import { SipBudgetBar } from "@/components/SipBudgetBar";
import { SipTable } from "@/components/SipTable";
import { apiGet } from "@/lib/api";
import { formatDate } from "@/lib/format";
import { load } from "@/lib/load";
import type { SipsResponse } from "@/lib/types";

export default async function SipsPage() {
  const result = await load(() => apiGet<SipsResponse>("/api/sips"));
  if (result.kind === "session") return <ConnectBanner />;
  if (result.kind === "unavailable") return <ErrorPanel message={KITE_DOWN_MESSAGE} />;

  const s = result.data;
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">SIPs</h1>
      <SipBudgetBar active={s.activeMonthlyEquivalent} budget={s.monthlyBudget} usedPercent={s.budgetUsedPercent} />
      <SipTable sips={s.sips} />
      <section>
        <h2 className="mb-2 font-semibold">Recent failed SIP orders (last 7 days)</h2>
        {s.recentFailedOrders.length === 0 ? (
          <p className="text-sm text-zinc-500">None</p>
        ) : (
          <ul className="space-y-2">
            {s.recentFailedOrders.map((o) => (
              <li key={o.orderId} className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm dark:border-red-900 dark:bg-red-950/40">
                <span className="font-medium">{o.fund}</span> · {formatDate(o.date)} · {o.message}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
