import { formatDate } from "@/lib/format";
import type { SipDto } from "@/lib/types";
import { Money } from "./Money";

function StatusChip({ status }: { status: string }) {
  const active = status === "ACTIVE";
  return (
    <span className={`rounded px-1.5 py-0.5 text-xs font-medium ${active ? "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200" : "bg-amber-100 text-amber-900 dark:bg-amber-950 dark:text-amber-200"}`}>
      {status}
    </span>
  );
}

export function SipTable({ sips }: { sips: SipDto[] }) {
  return (
    <div className="overflow-x-auto rounded-lg border border-zinc-200 dark:border-zinc-800">
      <p className="px-3 pt-2 text-xs text-zinc-500 md:hidden">Scroll sideways for amounts and dates →</p>
      <table className="w-full min-w-[720px] text-sm">
        <thead>
          <tr className="border-b border-zinc-200 text-left text-xs uppercase text-zinc-500 dark:border-zinc-800 dark:text-zinc-400">
            <th scope="col" className="px-3 py-2">Fund</th>
            <th scope="col" className="px-3 py-2">Status</th>
            <th scope="col" className="px-3 py-2">Frequency</th>
            <th scope="col" className="px-3 py-2 text-right">Amount</th>
            <th scope="col" className="px-3 py-2 text-right">Monthly equivalent</th>
            <th scope="col" className="px-3 py-2">Next instalment</th>
            <th scope="col" className="px-3 py-2 text-right">Completed</th>
          </tr>
        </thead>
        <tbody>
          {sips.length === 0 ? (
            <tr><td colSpan={7} className="px-3 py-4 text-zinc-500">No SIPs</td></tr>
          ) : (
            sips.map((s) => (
              <tr key={s.sipId} className={`border-b border-zinc-100 last:border-0 dark:border-zinc-800/60 ${s.status === "ACTIVE" ? "" : "opacity-60"}`}>
                <td className="px-3 py-2 font-medium">{s.fund}</td>
                <td className="px-3 py-2"><StatusChip status={s.status} /></td>
                <td className="px-3 py-2 capitalize">{s.frequency}</td>
                <td className="px-3 py-2 text-right"><Money value={s.instalmentAmount} /></td>
                <td className="px-3 py-2 text-right">{s.status === "ACTIVE" ? <Money value={s.monthlyEquivalent} /> : "–"}</td>
                <td className="px-3 py-2">{formatDate(s.nextInstalment)}</td>
                <td className="px-3 py-2 text-right tabular-nums">{s.completedInstalments}</td>
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}
