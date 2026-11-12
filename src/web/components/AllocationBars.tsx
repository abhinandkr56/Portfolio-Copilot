import { formatPct, formatSignedPoints } from "@/lib/format";
import type { AllocationClass } from "@/lib/types";
import { SeverityChip } from "./SeverityChip";

const clamp = (n: number) => Math.min(100, Math.max(0, n));

/**
 * Hand-written SVG: one bar per class on a 0–100% scale. Filled = actual, line = target, shaded = target ± band.
 * The numbers in the label come straight from the Api; only the drawing positions are derived here.
 */
export function AllocationBars({ classes, bandPoints }: { classes: AllocationClass[]; bandPoints: number }) {
  return (
    <ul className="space-y-4">
      {classes.map((c) => {
        const label = `${formatPct(c.actualPercent)} / ${formatPct(c.targetPercent)} (${formatSignedPoints(c.driftPoints)})`;
        const bandFrom = clamp(c.targetPercent - bandPoints);
        const bandTo = clamp(c.targetPercent + bandPoints);
        const fill =
          c.severity === "Act" ? "fill-red-500" : c.severity === "Watch" ? "fill-amber-500" : "fill-green-600";
        return (
          <li key={c.assetClass} data-testid="allocation-bar">
            <div className="mb-1 flex flex-wrap items-center justify-between gap-2 text-sm">
              <span className="font-medium">{c.assetClass}</span>
              <span className="flex items-center gap-2">
                <span className="tabular-nums">{label}</span>
                <SeverityChip severity={c.severity} />
              </span>
            </div>
            <svg
              role="img"
              aria-label={`${c.assetClass}: ${label}`}
              viewBox="0 0 100 10"
              preserveAspectRatio="none"
              className="h-4 w-full overflow-visible"
            >
              <rect x="0" y="0" width="100" height="10" rx="1" className="fill-zinc-200 dark:fill-zinc-800" />
              <rect x={bandFrom} y="0" width={bandTo - bandFrom} height="10" className="fill-zinc-400/40 dark:fill-zinc-500/40" />
              <rect x="0" y="2" width={clamp(c.actualPercent)} height="6" rx="1" className={fill} />
              <line
                x1={clamp(c.targetPercent)}
                x2={clamp(c.targetPercent)}
                y1="-1"
                y2="11"
                vectorEffect="non-scaling-stroke"
                strokeWidth="2"
                className="stroke-zinc-900 dark:stroke-zinc-100"
              />
            </svg>
          </li>
        );
      })}
      <li className="text-xs text-zinc-500 dark:text-zinc-400">
        Bar = actual · line = target · shaded = target ± {bandPoints} points
      </li>
    </ul>
  );
}
