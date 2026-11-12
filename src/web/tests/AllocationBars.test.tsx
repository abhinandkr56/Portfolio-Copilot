import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AllocationBars } from "@/components/AllocationBars";
import { demoAllocation, demoPlan } from "./fixtures";

describe("AllocationBars", () => {
  it("renders one bar per class for the demo allocation with the Api's numbers", () => {
    render(<AllocationBars classes={demoAllocation} bandPoints={demoPlan.driftBandPoints} />);

    expect(screen.getAllByTestId("allocation-bar")).toHaveLength(3);
    expect(screen.getByText("84.5% / 70.0% (+14.5)")).toBeInTheDocument();
    expect(screen.getByText("11.5% / 20.0% (-8.5)")).toBeInTheDocument();
    expect(screen.getByText("4.0% / 10.0% (-6.0)")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Equity: 84.5% / 70.0% (+14.5)" })).toBeInTheDocument();
  });

  it("pairs each bar with its severity label", () => {
    render(<AllocationBars classes={demoAllocation} bandPoints={demoPlan.driftBandPoints} />);

    expect(screen.getAllByText("Act")).toHaveLength(1);
    expect(screen.getAllByText("Watch")).toHaveLength(2);
  });
});
