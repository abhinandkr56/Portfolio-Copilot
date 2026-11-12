import { formatEvidence, humanizeKey } from "@/lib/format";
import type { Finding } from "@/lib/types";
import { SeverityChip } from "./SeverityChip";

export function FindingCard({ finding }: { finding: Finding }) {
  return (
    <article
      data-testid="finding-card"
      className="rounded-lg border border-zinc-200 bg-white p-4 dark:border-zinc-800 dark:bg-zinc-900"
    >
      <div className="flex flex-wrap items-center gap-2">
        <SeverityChip severity={finding.severity} />
        <span className="text-xs text-zinc-500 dark:text-zinc-400">{finding.rule}</span>
      </div>
      <p className="mt-2 text-sm">{finding.message}</p>
      <ul className="mt-3 flex flex-wrap gap-1.5">
        {Object.entries(finding.evidence).map(([key, value]) => (
          <li key={key} className="rounded bg-zinc-100 px-2 py-0.5 text-xs dark:bg-zinc-800">
            <span className="text-zinc-500 dark:text-zinc-400">{humanizeKey(key)}:</span>{" "}
            <span className="font-medium tabular-nums">{formatEvidence(key, value)}</span>
          </li>
        ))}
      </ul>
      <p className="mt-3">
        <span className="rounded-full border border-zinc-300 px-2 py-0.5 text-xs dark:border-zinc-700">{finding.suggestion}</span>
      </p>
    </article>
  );
}
