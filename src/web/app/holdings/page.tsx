import { ConnectBanner } from "@/components/ConnectBanner";
import { ErrorPanel, KITE_DOWN_MESSAGE } from "@/components/ErrorPanel";
import { FundsTable, StocksTable } from "@/components/HoldingsTable";
import { Money } from "@/components/Money";
import { apiGet } from "@/lib/api";
import { formatIst } from "@/lib/format";
import { load } from "@/lib/load";
import type { HoldingsResponse } from "@/lib/types";

export default async function HoldingsPage() {
  const result = await load(() => apiGet<HoldingsResponse>("/api/holdings"));
  if (result.kind === "session") return <ConnectBanner />;
  if (result.kind === "unavailable") return <ErrorPanel message={KITE_DOWN_MESSAGE} />;

  const h = result.data;
  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h1 className="text-2xl font-semibold">Holdings</h1>
        <p className="text-sm text-zinc-500 dark:text-zinc-400">
          Total <strong className="text-zinc-900 dark:text-zinc-100"><Money value={h.totalValue} /></strong> · as of {formatIst(h.asOfUtc)}
        </p>
      </div>
      <StocksTable rows={h.stocks} total={h.stocksTotal} />
      <FundsTable rows={h.funds} total={h.fundsTotal} />
    </div>
  );
}
