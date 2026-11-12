import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PlanForm } from "@/components/PlanForm";
import { demoPlan } from "./fixtures";

const refresh = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ refresh }) }));

afterEach(() => {
  vi.unstubAllGlobals();
  refresh.mockReset();
});

describe("PlanForm", () => {
  it("shows a green total at 100 and turns red at 95", async () => {
    render(<PlanForm plan={demoPlan} />);
    const total = screen.getByTestId("target-total");
    expect(total).toHaveTextContent("Total: 100%");
    expect(total.className).toContain("text-green");

    const gold = screen.getByLabelText("Gold");
    await userEvent.clear(gold);
    await userEvent.type(gold, "5");

    expect(total).toHaveTextContent("Total: 95% (must be 100)");
    expect(total.className).toContain("text-red");
  });

  it("renders the Api's field errors next to their inputs", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      status: 400,
      errors: { driftBandPoints: ["Must be between 1 and 20."], assetClassOverrides: ["GOLDBEES: Hybrid is not allowed; use Equity, Debt, Gold or Other."] },
    }), { status: 400 })));
    render(<PlanForm plan={demoPlan} />);

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Must be between 1 and 20.")).toBeInTheDocument();
    expect(screen.getByLabelText("Drift band (points)")).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByText(/GOLDBEES: Hybrid is not allowed/)).toBeInTheDocument();
    expect(refresh).not.toHaveBeenCalled();
  });

  it("saves, confirms and refreshes the page", async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(demoPlan), { status: 200 }));
    vi.stubGlobal("fetch", fetchMock);
    render(<PlanForm plan={demoPlan} />);

    await userEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Plan saved · review updated")).toBeInTheDocument();
    expect(refresh).toHaveBeenCalledOnce();
    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/plan");
    expect(init.method).toBe("PUT");
    expect(JSON.parse(init.body).targetAllocation).toEqual({ Equity: 70, Debt: 20, Gold: 10 });
  });
});
