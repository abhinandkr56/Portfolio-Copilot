"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";
import { ApiError, apiSend } from "@/lib/api";
import type { AssetClass, Plan } from "@/lib/types";

const targetClasses = ["Equity", "Debt", "Gold", "Other"] as const;
const overrideClasses: AssetClass[] = ["Equity", "Debt", "Gold", "Other"];

type OverrideRow = { id: number; key: string; cls: AssetClass };

function FieldError({ messages }: { messages?: string[] }) {
  if (!messages?.length) return null;
  return (
    <p role="alert" className="mt-1 text-xs text-red-700 dark:text-red-400">
      {messages.join(" ")}
    </p>
  );
}

const inputClass =
  "w-full rounded-md border border-zinc-300 bg-white px-2 py-1.5 text-sm tabular-nums dark:border-zinc-700 dark:bg-zinc-900";

/**
 * Edits the plan. The live "Total" only adds up what's typed into the form (input feedback);
 * the Api validates for real and its 400 errors appear next to their fields.
 */
export function PlanForm({ plan }: { plan: Plan }) {
  const router = useRouter();
  const [targets, setTargets] = useState<Record<string, string>>(
    Object.fromEntries(targetClasses.map((c) => [c, String(plan.targetAllocation[c] ?? 0)])),
  );
  const [band, setBand] = useState(String(plan.driftBandPoints));
  const [stockCap, setStockCap] = useState(String(plan.maxSingleStockPercent));
  const [fundCap, setFundCap] = useState(String(plan.maxSingleFundPercent));
  const [budget, setBudget] = useState(String(plan.monthlySipBudget));
  const [hybrid, setHybrid] = useState(String(plan.hybridEquityPercent));
  const [preferDirect, setPreferDirect] = useState(plan.preferDirectPlans);
  const [notes, setNotes] = useState(plan.notes ?? "");
  const [overrides, setOverrides] = useState<OverrideRow[]>(
    Object.entries(plan.assetClassOverrides).map(([key, cls], i) => ({ id: i, key, cls })),
  );
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [status, setStatus] = useState<"idle" | "saving" | "saved" | "failed">("idle");
  const [failure, setFailure] = useState("");

  const total = Math.round(targetClasses.reduce((sum, c) => sum + (Number(targets[c]) || 0), 0) * 100) / 100;
  const totalOk = Math.abs(total - 100) <= 0.01;

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setStatus("saving");
    setErrors({});
    const body: Plan = {
      targetAllocation: Object.fromEntries(targetClasses.filter((c) => Number(targets[c]) > 0 || c !== "Other").map((c) => [c, Number(targets[c]) || 0])),
      driftBandPoints: Number(band),
      maxSingleStockPercent: Number(stockCap),
      maxSingleFundPercent: Number(fundCap),
      preferDirectPlans: preferDirect,
      monthlySipBudget: Number(budget),
      hybridEquityPercent: Number(hybrid),
      assetClassOverrides: Object.fromEntries(overrides.filter((o) => o.key.trim()).map((o) => [o.key.trim(), o.cls])),
      notes: notes.trim() ? notes : null,
    };
    try {
      await apiSend<Plan>("PUT", "/api/plan", body);
      setStatus("saved");
      router.refresh();
    } catch (err) {
      setStatus("failed");
      if (err instanceof ApiError && err.status === 400) {
        setErrors(err.errors);
        setFailure("Please fix the highlighted fields.");
      } else {
        setFailure(err instanceof Error ? err.message : "Saving failed.");
      }
    }
  }

  const numberField = (id: string, label: string, value: string, set: (v: string) => void, errorKey: string, hint?: string) => (
    <div>
      <label htmlFor={id} className="block text-sm font-medium">{label}</label>
      <input id={id} type="number" step="any" inputMode="decimal" value={value} onChange={(e) => set(e.target.value)}
        aria-invalid={!!errors[errorKey]} className={inputClass} />
      {hint && <p className="mt-0.5 text-xs text-zinc-500">{hint}</p>}
      <FieldError messages={errors[errorKey]} />
    </div>
  );

  return (
    <form onSubmit={save} className="space-y-6" noValidate>
      <fieldset className="rounded-lg border border-zinc-200 p-4 dark:border-zinc-800">
        <legend className="px-1 font-semibold">Target allocation (%)</legend>
        <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
          {targetClasses.map((c) => (
            <div key={c}>
              <label htmlFor={`target-${c}`} className="block text-sm font-medium">{c}</label>
              <input id={`target-${c}`} type="number" step="any" inputMode="decimal" value={targets[c]}
                onChange={(e) => setTargets((t) => ({ ...t, [c]: e.target.value }))} className={inputClass} />
            </div>
          ))}
        </div>
        <p data-testid="target-total" className={`mt-3 text-sm font-semibold ${totalOk ? "text-green-700 dark:text-green-400" : "text-red-700 dark:text-red-400"}`}>
          Total: {total}%{totalOk ? "" : " (must be 100)"}
        </p>
        <FieldError messages={errors["targetAllocation"]} />
      </fieldset>

      <fieldset className="grid gap-4 rounded-lg border border-zinc-200 p-4 sm:grid-cols-2 dark:border-zinc-800">
        <legend className="px-1 font-semibold">Limits</legend>
        {numberField("band", "Drift band (points)", band, setBand, "driftBandPoints", "1 to 20")}
        {numberField("stockCap", "Max single stock (%)", stockCap, setStockCap, "maxSingleStockPercent", "1 to 100")}
        {numberField("fundCap", "Max single fund (%)", fundCap, setFundCap, "maxSingleFundPercent", "1 to 100")}
        {numberField("budget", "Monthly SIP budget (₹)", budget, setBudget, "monthlySipBudget")}
        {numberField("hybrid", "Hybrid funds: equity part (%)", hybrid, setHybrid, "hybridEquityPercent", "Used to split hybrid funds into equity and debt")}
        <div className="flex items-center gap-2 self-end pb-1">
          <input id="preferDirect" type="checkbox" checked={preferDirect} onChange={(e) => setPreferDirect(e.target.checked)} className="h-4 w-4" />
          <label htmlFor="preferDirect" className="text-sm font-medium">Prefer Direct plans</label>
        </div>
      </fieldset>

      <fieldset className="rounded-lg border border-zinc-200 p-4 dark:border-zinc-800">
        <legend className="px-1 font-semibold">Asset class overrides</legend>
        <p className="mb-2 text-xs text-zinc-500">Key = stock symbol or fund ISIN, e.g. GOLDBEES → Gold.</p>
        <table className="w-full text-sm">
          <thead>
            <tr className="text-left text-xs uppercase text-zinc-500">
              <th scope="col" className="py-1 pr-2">Symbol or ISIN</th>
              <th scope="col" className="py-1 pr-2">Asset class</th>
              <th scope="col" className="py-1"><span className="sr-only">Remove</span></th>
            </tr>
          </thead>
          <tbody>
            {overrides.map((o) => (
              <tr key={o.id}>
                <td className="py-1 pr-2">
                  <input aria-label="Symbol or ISIN" value={o.key} className={inputClass}
                    onChange={(e) => setOverrides((rows) => rows.map((r) => (r.id === o.id ? { ...r, key: e.target.value } : r)))} />
                </td>
                <td className="py-1 pr-2">
                  <select aria-label="Asset class" value={o.cls} className={inputClass}
                    onChange={(e) => setOverrides((rows) => rows.map((r) => (r.id === o.id ? { ...r, cls: e.target.value as AssetClass } : r)))}>
                    {overrideClasses.map((c) => <option key={c} value={c}>{c}</option>)}
                  </select>
                </td>
                <td className="py-1">
                  <button type="button" onClick={() => setOverrides((rows) => rows.filter((r) => r.id !== o.id))}
                    className="rounded px-2 py-1 text-sm text-red-700 hover:bg-red-50 dark:text-red-400 dark:hover:bg-red-950">
                    Remove
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <button type="button" onClick={() => setOverrides((rows) => [...rows, { id: Date.now(), key: "", cls: "Equity" }])}
          className="mt-2 rounded-md border border-zinc-300 px-3 py-1 text-sm hover:bg-zinc-100 dark:border-zinc-700 dark:hover:bg-zinc-800">
          Add override
        </button>
        <FieldError messages={errors["assetClassOverrides"]} />
      </fieldset>

      <div>
        <label htmlFor="notes" className="block text-sm font-medium">Notes</label>
        <textarea id="notes" rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} className={inputClass} />
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <button type="submit" disabled={status === "saving"}
          className="rounded-md bg-blue-700 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-800 disabled:opacity-60">
          {status === "saving" ? "Saving…" : "Save"}
        </button>
        {status === "saved" && <p role="status" className="text-sm font-medium text-green-700 dark:text-green-400">Plan saved · review updated</p>}
        {status === "failed" && <p role="alert" className="text-sm font-medium text-red-700 dark:text-red-400">{failure}</p>}
      </div>
    </form>
  );
}
