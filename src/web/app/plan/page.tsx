import { PlanForm } from "@/components/PlanForm";
import { apiGet } from "@/lib/api";
import type { Plan } from "@/lib/types";

export default async function PlanPage() {
  const plan = await apiGet<Plan>("/api/plan"); // the plan is local data: no Kite session needed
  return (
    <div className="space-y-4">
      <h1 className="text-2xl font-semibold">Your plan</h1>
      <p className="text-sm text-zinc-600 dark:text-zinc-400">
        The review checks your portfolio against these targets and limits. Saving updates the review straight away.
      </p>
      <PlanForm plan={plan} />
    </div>
  );
}
