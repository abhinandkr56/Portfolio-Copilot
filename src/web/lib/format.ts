// Formatting only. Every number comes from the Api; nothing here computes portfolio values.

const inr0 = new Intl.NumberFormat("en-IN", { style: "currency", currency: "INR", maximumFractionDigits: 0 });
const inr2 = new Intl.NumberFormat("en-IN", { style: "currency", currency: "INR", minimumFractionDigits: 2, maximumFractionDigits: 2 });
const qty = new Intl.NumberFormat("en-IN", { maximumFractionDigits: 3 });

/** ₹1,00,000 (no decimals). */
export const formatInr = (n: number): string => inr0.format(n);

/** ₹1,400.00, for prices and NAVs. */
export const formatInrPrice = (n: number): string => inr2.format(n);

/** 84.5% */
export const formatPct = (n: number): string => `${n.toFixed(1)}%`;

/** +14.5 / -8.5 / 0.0 */
export const formatSignedPoints = (n: number): string => (n > 0 ? `+${n.toFixed(1)}` : n.toFixed(1));

/** 382.488 (up to 3 decimals, Indian grouping). */
export const formatQty = (n: number): string => qty.format(n);

/** "11 Nov 2026, 9:07 pm" in IST. */
export const formatIst = (iso: string): string =>
  new Intl.DateTimeFormat("en-IN", { timeZone: "Asia/Kolkata", dateStyle: "medium", timeStyle: "short" }).format(new Date(iso));

/** A date-only value ("2026-11-07") as "7 Nov 2026", without any timezone shift. */
export const formatDate = (date: string | null | undefined): string =>
  date ? new Intl.DateTimeFormat("en-IN", { timeZone: "UTC", dateStyle: "medium" }).format(new Date(`${date}T00:00:00Z`)) : "–";

/** Evidence keys like "driftPoints" → "drift points". */
export const humanizeKey = (key: string): string => key.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase();

/** Formats one evidence value by its key: amounts in ₹, percentages with %, drift points signed. Strings pass through. */
export function formatEvidence(key: string, value: string | number | boolean): string {
  if (typeof value !== "number") return String(value);
  const k = key.toLowerCase();
  if (k.includes("percent")) return formatPct(value);
  if (k === "driftpoints") return formatSignedPoints(value);
  if (k.includes("points")) return value.toFixed(1);
  if (k === "value" || k.includes("amount") || k.includes("budget") || k.includes("equivalent")) return formatInr(value);
  return String(value);
}
