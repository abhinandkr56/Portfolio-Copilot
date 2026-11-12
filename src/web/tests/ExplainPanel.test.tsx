import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ExplainPanel } from "@/components/ExplainPanel";
import { demoAllocation, demoFindings } from "./fixtures";

const DISCLAIMER = "Information only, not investment advice. You make the decisions.";

function reviewWith(summary: string | null, ai: object | null) {
  return {
    asOfUtc: "2026-11-12T10:00:00Z", navDate: "2026-11-07",
    allocation: { asOfUtc: "2026-11-12T10:00:00Z", totalValue: 100000, classes: demoAllocation },
    findings: demoFindings, counts: { act: 2, watch: 6, info: 0 }, summary, ai, disclaimer: DISCLAIMER,
  };
}

const respond = (body: object, status = 200) =>
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify(body), { status })));

afterEach(() => vi.unstubAllGlobals());

describe("ExplainPanel", () => {
  it("posts explain:true and shows the summary with its provider", async () => {
    respond(reviewWith(`Equity is 84.5%.\n\n${DISCLAIMER}`, { provider: "Groq", model: "openai/gpt-oss-120b", numberCheckPassed: true, attempts: 1, error: null }));
    render(<ExplainPanel />);

    await userEvent.click(screen.getByRole("button", { name: "Explain with AI" }));

    expect(await screen.findByTestId("explain-summary")).toHaveTextContent("Equity is 84.5%.");
    expect(screen.getByText("Written by Groq · openai/gpt-oss-120b")).toBeInTheDocument();
    expect(screen.queryByText(/verified facts/)).not.toBeInTheDocument();
    const [url, init] = vi.mocked(fetch).mock.calls[0];
    expect(url).toBe("/api/review");
    expect(JSON.parse(init!.body as string)).toEqual({ explain: true });
  });

  it("says when the verified facts replace the AI summary", async () => {
    respond(reviewWith(`Equity is 84.5% against a target of 70.0%.\n\n${DISCLAIMER}`, { provider: "Groq", model: "m", numberCheckPassed: false, attempts: 2, error: null }));
    render(<ExplainPanel />);

    await userEvent.click(screen.getByRole("button", { name: "Explain with AI" }));

    expect(await screen.findByText(/Showing the verified facts instead of the AI summary/)).toBeInTheDocument();
  });

  it("shows a short provider error", async () => {
    respond(reviewWith(null, { provider: "Groq", model: "m", numberCheckPassed: null, attempts: null, error: "Groq is rate limiting requests; try again in a minute." }));
    render(<ExplainPanel />);

    await userEvent.click(screen.getByRole("button", { name: "Explain with AI" }));

    expect(await screen.findByText("AI unavailable: Groq is rate limiting requests; try again in a minute.")).toBeInTheDocument();
  });
});
