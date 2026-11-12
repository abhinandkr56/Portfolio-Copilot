import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it } from "vitest";
import { FindingsList } from "@/components/FindingsList";
import { demoFindings } from "./fixtures";

describe("FindingsList", () => {
  it("shows all 8 demo findings in Api order", () => {
    render(<FindingsList findings={demoFindings} />);

    const cards = screen.getAllByTestId("finding-card");
    expect(cards).toHaveLength(8);
    expect(cards[0]).toHaveTextContent("AllocationDrift: Equity");
  });

  it("the Act filter shows 2 of the 8 and records it in ?severity=", async () => {
    render(<FindingsList findings={demoFindings} />);

    await userEvent.click(screen.getByRole("button", { name: "Act" }));

    expect(screen.getAllByTestId("finding-card")).toHaveLength(2);
    expect(window.location.search).toBe("?severity=Act");
  });

  it("starts from the filter in the URL", () => {
    render(<FindingsList findings={demoFindings} initialSeverity="Watch" />);

    expect(screen.getAllByTestId("finding-card")).toHaveLength(6);
    expect(screen.getByRole("button", { name: "Watch" })).toHaveAttribute("aria-pressed", "true");
  });
});
