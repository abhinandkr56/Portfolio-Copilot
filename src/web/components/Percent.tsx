import { formatPct } from "@/lib/format";

export function Percent({ value }: { value: number }) {
  return <span className="tabular-nums">{formatPct(value)}</span>;
}
