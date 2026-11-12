import type { Severity } from "@/lib/types";

// Colour is always paired with the text label (doc 07). Act = red, Watch = amber, Info = blue, OK = green.
const styles: Record<Severity | "OK", string> = {
  Act: "bg-red-100 text-red-800 ring-red-600/30 dark:bg-red-950 dark:text-red-200 dark:ring-red-400/40",
  Watch: "bg-amber-100 text-amber-900 ring-amber-600/30 dark:bg-amber-950 dark:text-amber-200 dark:ring-amber-400/40",
  Info: "bg-blue-100 text-blue-800 ring-blue-600/30 dark:bg-blue-950 dark:text-blue-200 dark:ring-blue-400/40",
  OK: "bg-green-100 text-green-800 ring-green-600/30 dark:bg-green-950 dark:text-green-200 dark:ring-green-400/40",
};

export function SeverityChip({ severity }: { severity: Severity | null | undefined }) {
  const label = severity ?? "OK";
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ring-1 ring-inset ${styles[label]}`}>
      {label}
    </span>
  );
}
