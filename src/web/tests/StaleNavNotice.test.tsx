import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StaleNavNotice } from "@/components/StaleNavNotice";
import type { Finding } from "@/lib/types";
import { demoFindings } from "./fixtures";

const staleNav: Finding = {
  rule: "StaleNav",
  subject: "NAV",
  severity: "Info",
  evidence: { staleFunds: 2, oldestDate: "2026-11-03" },
  message: "2 fund prices are older than 4 days; the oldest is from 2026-11-03.",
  suggestion: "Review",
};

describe("StaleNavNotice", () => {
  it("shows the StaleNav message when the review has one", () => {
    render(<StaleNavNotice findings={[...demoFindings, staleNav]} />);

    expect(screen.getByRole("status")).toHaveTextContent("2 fund prices are older than 4 days; the oldest is from 2026-11-03.");
  });

  it("shows nothing when no fund price is stale", () => {
    const { container } = render(<StaleNavNotice findings={demoFindings} />);

    expect(container).toBeEmptyDOMElement();
  });
});
