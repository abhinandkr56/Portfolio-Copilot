import { formatInr, formatInrPrice } from "@/lib/format";

/** A rupee amount from the Api. `tone` colours P&L green/red; `price` shows 2 decimals. */
export function Money({ value, tone = false, price = false }: { value: number; tone?: boolean; price?: boolean }) {
  const text = price ? formatInrPrice(value) : formatInr(value);
  const color = !tone ? "" : value > 0 ? "text-green-700 dark:text-green-400" : value < 0 ? "text-red-700 dark:text-red-400" : "";
  return <span className={`tabular-nums ${color}`}>{text}</span>;
}
