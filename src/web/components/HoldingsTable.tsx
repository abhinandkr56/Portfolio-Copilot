"use client";

import { useState } from "react";
import { formatDate, formatQty } from "@/lib/format";
import type { FundRow, HoldingsTotal, StockRow } from "@/lib/types";
import { Money } from "./Money";
import { Percent } from "./Percent";
import { PlanBadge } from "./PlanBadge";

type Column<T> = { key: string; label: string; numeric?: boolean; sortValue: (r: T) => string | number; render: (r: T) => React.ReactNode };

const stockColumns: Column<StockRow>[] = [
  { key: "name", label: "Name", sortValue: (r) => r.symbol, render: (r) => <span className="font-medium">{r.symbol}</span> },
  { key: "qty", label: "Qty", numeric: true, sortValue: (r) => r.quantity, render: (r) => formatQty(r.quantity) },
  { key: "avg", label: "Avg", numeric: true, sortValue: (r) => r.averagePrice, render: (r) => <Money value={r.averagePrice} price /> },
  { key: "ltp", label: "LTP", numeric: true, sortValue: (r) => r.lastPrice, render: (r) => <Money value={r.lastPrice} price /> },
  { key: "value", label: "Value", numeric: true, sortValue: (r) => r.value, render: (r) => <Money value={r.value} /> },
  { key: "pnl", label: "P&L", numeric: true, sortValue: (r) => r.pnl, render: (r) => <Money value={r.pnl} tone /> },
  { key: "pct", label: "% of total", numeric: true, sortValue: (r) => r.percentOfTotal, render: (r) => <Percent value={r.percentOfTotal} /> },
  { key: "class", label: "Asset class", sortValue: (r) => r.assetClassLabel, render: (r) => r.assetClassLabel },
];

const fundColumns: Column<FundRow>[] = [
  {
    key: "name",
    label: "Name",
    sortValue: (r) => r.fund,
    render: (r) => (
      <span>
        <span className="font-medium">{r.fund}</span>
        <span className="block text-xs text-zinc-500 dark:text-zinc-400">
          Folio {r.folioMasked} · NAV {formatDate(r.lastPriceDate)}
        </span>
      </span>
    ),
  },
  { key: "qty", label: "Qty", numeric: true, sortValue: (r) => r.quantity, render: (r) => formatQty(r.quantity) },
  { key: "avg", label: "Avg", numeric: true, sortValue: (r) => r.averagePrice, render: (r) => <Money value={r.averagePrice} price /> },
  { key: "ltp", label: "LTP", numeric: true, sortValue: (r) => r.lastPrice, render: (r) => <Money value={r.lastPrice} price /> },
  { key: "value", label: "Value", numeric: true, sortValue: (r) => r.value, render: (r) => <Money value={r.value} /> },
  { key: "pnl", label: "P&L", numeric: true, sortValue: (r) => r.pnl, render: (r) => <Money value={r.pnl} tone /> },
  { key: "pct", label: "% of total", numeric: true, sortValue: (r) => r.percentOfTotal, render: (r) => <Percent value={r.percentOfTotal} /> },
  { key: "class", label: "Asset class", sortValue: (r) => r.assetClassLabel, render: (r) => r.assetClassLabel },
  { key: "plan", label: "Plan", sortValue: (r) => r.plan, render: (r) => <PlanBadge plan={r.plan} /> },
];

function SortableTable<T>({ caption, rows, columns, total, rowKey }: {
  caption: string;
  rows: T[];
  columns: Column<T>[];
  total: HoldingsTotal;
  rowKey: (r: T, i: number) => string;
}) {
  const [sort, setSort] = useState<{ key: string; dir: 1 | -1 } | null>(null);

  const sorted = sort
    ? [...rows].sort((a, b) => {
        const col = columns.find((c) => c.key === sort.key)!;
        const x = col.sortValue(a);
        const y = col.sortValue(b);
        return (typeof x === "number" && typeof y === "number" ? x - y : String(x).localeCompare(String(y))) * sort.dir;
      })
    : rows;

  const toggle = (key: string) =>
    setSort((s) => (s?.key === key ? { key, dir: s.dir === 1 ? -1 : 1 } : { key, dir: 1 }));

  const totalCells: Record<string, React.ReactNode> = {
    value: <Money value={total.value} />,
    pnl: <Money value={total.pnl} tone />,
    pct: <Percent value={total.percentOfTotal} />,
  };

  return (
    <div className="overflow-x-auto rounded-lg border border-zinc-200 dark:border-zinc-800">
      <p className="px-3 pt-2 text-xs text-zinc-500 md:hidden">Scroll sideways for value, P&amp;L and class →</p>
      <table className="w-full min-w-[720px] text-sm">
        <caption className="bg-zinc-50 px-3 py-2 text-left font-semibold dark:bg-zinc-900">{caption}</caption>
        <thead>
          <tr className="border-b border-zinc-200 text-xs uppercase text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
            {columns.map((c) => (
              <th
                key={c.key}
                scope="col"
                aria-sort={sort?.key === c.key ? (sort.dir === 1 ? "ascending" : "descending") : "none"}
                className={`px-3 py-2 ${c.numeric ? "text-right" : "text-left"}`}
              >
                <button type="button" onClick={() => toggle(c.key)} className="uppercase hover:underline">
                  {c.label}
                  {sort?.key === c.key ? (sort.dir === 1 ? " ▲" : " ▼") : ""}
                </button>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sorted.length === 0 ? (
            <tr>
              <td colSpan={columns.length} className="px-3 py-4 text-zinc-500">None</td>
            </tr>
          ) : (
            sorted.map((r, i) => (
              <tr key={rowKey(r, i)} className="border-b border-zinc-100 last:border-0 dark:border-zinc-800/60">
                {columns.map((c) => (
                  <td key={c.key} className={`px-3 py-2 align-top ${c.numeric ? "text-right tabular-nums" : ""}`}>
                    {c.render(r)}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
        <tfoot>
          <tr className="border-t border-zinc-300 bg-zinc-50 font-semibold dark:border-zinc-700 dark:bg-zinc-900">
            {columns.map((c, i) => (
              <td key={c.key} className={`px-3 py-2 ${c.numeric ? "text-right tabular-nums" : ""}`}>
                {i === 0 ? "Total" : (totalCells[c.key] ?? "")}
              </td>
            ))}
          </tr>
        </tfoot>
      </table>
    </div>
  );
}

export function StocksTable({ rows, total }: { rows: StockRow[]; total: HoldingsTotal }) {
  return <SortableTable caption="Stocks" rows={rows} columns={stockColumns} total={total} rowKey={(r) => `${r.symbol}-${r.exchange}`} />;
}

export function FundsTable({ rows, total }: { rows: FundRow[]; total: HoldingsTotal }) {
  return <SortableTable caption="Mutual funds" rows={rows} columns={fundColumns} total={total} rowKey={(r, i) => `${r.isin}-${r.folioMasked}-${i}`} />;
}
