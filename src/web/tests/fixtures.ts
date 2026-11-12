// Builds test data from samples/scenarios/demo.json (doc 07: don't duplicate the demo numbers by hand).
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import type { AllocationClass, AssetClass, Finding, Plan, Severity } from "@/lib/types";

type DemoFile = {
  plan: Plan;
  expected: {
    allocation: Record<string, number>;
    findings: { rule: string; subject: string; severity: Severity }[];
  };
};

// Vitest runs from src/web; under jsdom import.meta.url isn't a file: URL, so resolve from the working directory.
const demoPath = resolve(process.cwd(), "../../samples/scenarios/demo.json");
export const demo = JSON.parse(readFileSync(demoPath, "utf8")) as DemoFile;

export const demoPlan: Plan = demo.plan;

/** The demo allocation as the Api returns it: actual from `expected`, target from the plan, severity from the expected drift findings. */
export const demoAllocation: AllocationClass[] = Object.entries(demo.expected.allocation).map(([cls, actual]) => {
  const target = demo.plan.targetAllocation[cls] ?? 0;
  const drift = demo.expected.findings.find((f) => f.rule === "AllocationDrift" && f.subject === cls);
  return {
    assetClass: cls as AssetClass,
    value: actual * 1000, // every scenario totals ₹1,00,000
    actualPercent: actual,
    targetPercent: target,
    driftPoints: Math.round((actual - target) * 10) / 10,
    severity: drift?.severity ?? null,
  };
});

export const demoFindings: Finding[] = demo.expected.findings.map((f) => ({
  ...f,
  evidence: { example: 1 },
  message: `${f.rule}: ${f.subject}`,
  suggestion: "Review",
}));
