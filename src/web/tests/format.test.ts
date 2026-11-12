import { describe, expect, it } from "vitest";
import { formatDate, formatEvidence, formatInr, formatIst, formatPct, formatSignedPoints, humanizeKey } from "@/lib/format";

describe("format", () => {
  it("formats rupees with Indian grouping", () => {
    expect(formatInr(100000)).toBe("₹1,00,000");
    expect(formatInr(23666.67)).toBe("₹23,667");
  });

  it("formats percentages to 1 dp", () => {
    expect(formatPct(84.5)).toBe("84.5%");
    expect(formatPct(4)).toBe("4.0%");
  });

  it("formats signed points", () => {
    expect(formatSignedPoints(14.5)).toBe("+14.5");
    expect(formatSignedPoints(-8.5)).toBe("-8.5");
    expect(formatSignedPoints(0)).toBe("0.0");
  });

  it("formats times in IST", () => {
    // 03:30 UTC = 09:00 IST
    expect(formatIst("2026-11-10T03:30:00Z")).toMatch(/10 Nov 2026, 9:00\s?am/i);
  });

  it("formats date-only values without shifting the day", () => {
    expect(formatDate("2026-11-07")).toBe("7 Nov 2026");
    expect(formatDate(null)).toBe("–");
  });

  it("formats evidence values by key", () => {
    expect(formatEvidence("value", 18000)).toBe("₹18,000");
    expect(formatEvidence("percentOfTotal", 18)).toBe("18.0%");
    expect(formatEvidence("driftPoints", -6)).toBe("-6.0");
    expect(formatEvidence("bandPoints", 5)).toBe("5.0");
    expect(formatEvidence("activeMonthlyEquivalent", 23666.67)).toBe("₹23,667");
    expect(formatEvidence("failedCount", 1)).toBe("1");
    expect(formatEvidence("status", "PAUSED")).toBe("PAUSED");
  });

  it("humanizes evidence keys", () => {
    expect(humanizeKey("driftPoints")).toBe("drift points");
  });
});
