"use client";

import { useState } from "react";
import type { Finding, Severity } from "@/lib/types";
import { FindingCard } from "./FindingCard";

type Filter = "All" | Severity;
const filters: Filter[] = ["All", "Act", "Watch", "Info"];

const parse = (value: string | undefined): Filter => (filters as string[]).includes(value ?? "") ? (value as Filter) : "All";

/** Findings in Api order, filtered by severity. The filter lives in ?severity= so it survives a reload. */
export function FindingsList({ findings, initialSeverity }: { findings: Finding[]; initialSeverity?: string }) {
  const [filter, setFilter] = useState<Filter>(parse(initialSeverity));

  function choose(next: Filter) {
    setFilter(next);
    const url = new URL(window.location.href);
    if (next === "All") url.searchParams.delete("severity");
    else url.searchParams.set("severity", next);
    window.history.replaceState(null, "", url);
  }

  const shown = filter === "All" ? findings : findings.filter((f) => f.severity === filter);

  return (
    <section aria-label="Findings">
      <div role="group" aria-label="Filter by severity" className="mb-3 flex flex-wrap gap-2">
        {filters.map((f) => (
          <button
            key={f}
            type="button"
            aria-pressed={filter === f}
            onClick={() => choose(f)}
            className={`rounded-full px-3 py-1 text-sm font-medium ring-1 ring-inset ${
              filter === f
                ? "bg-zinc-900 text-white ring-zinc-900 dark:bg-zinc-100 dark:text-zinc-900 dark:ring-zinc-100"
                : "ring-zinc-300 hover:bg-zinc-100 dark:ring-zinc-700 dark:hover:bg-zinc-800"
            }`}
          >
            {f}
          </button>
        ))}
      </div>
      {findings.length === 0 ? (
        <p className="rounded-lg border border-green-200 bg-green-50 p-4 text-sm dark:border-green-900 dark:bg-green-950/40">
          No findings: the portfolio is in line with your plan.
        </p>
      ) : shown.length === 0 ? (
        <p className="text-sm text-zinc-500">No {filter} findings.</p>
      ) : (
        <div className="space-y-3">
          {shown.map((f) => (
            <FindingCard key={`${f.rule}/${f.subject}`} finding={f} />
          ))}
        </div>
      )}
    </section>
  );
}
