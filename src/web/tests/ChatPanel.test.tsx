import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ChatPanel, SUGGESTED_PROMPTS } from "@/components/ChatPanel";

const DISCLAIMER = "Information only, not investment advice. You make the decisions.";

function answer(text: string, extra: object = {}) {
  return new Response(JSON.stringify({
    conversationId: "11111111-1111-1111-1111-111111111111", answer: `${text}\n\n${DISCLAIMER}`,
    toolsUsed: ["GetFindings"], provider: "Groq", model: "openai/gpt-oss-120b", numberCheckPassed: true, ...extra,
  }), { status: 200 });
}

afterEach(() => vi.unstubAllGlobals());

describe("ChatPanel", () => {
  it("offers the suggested prompts when empty", () => {
    render(<ChatPanel />);

    for (const p of SUGGESTED_PROMPTS) expect(screen.getByRole("button", { name: p })).toBeInTheDocument();
  });

  it("Enter sends, the answer shows its tools, and the conversation id is reused", async () => {
    const fetchMock = vi.fn().mockResolvedValueOnce(answer("INFY is 18.0%.")).mockResolvedValueOnce(answer("Debt is 11.5%."));
    vi.stubGlobal("fetch", fetchMock);
    render(<ChatPanel />);

    await userEvent.type(screen.getByLabelText("Your question"), "Is any stock too big?{Enter}");
    const reply = await screen.findByTestId("chat-assistant");
    expect(reply).toHaveTextContent("INFY is 18.0%.");
    expect(within(reply).getByText("Tools used (1)")).toBeInTheDocument();
    expect(within(reply).getByText("GetFindings")).toBeInTheDocument();
    expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({ conversationId: null, message: "Is any stock too big?" });

    await userEvent.type(screen.getByLabelText("Your question"), "And debt?{Enter}");
    await screen.findByText(/Debt is 11.5%/);
    expect(JSON.parse(fetchMock.mock.calls[1][1].body).conversationId).toBe("11111111-1111-1111-1111-111111111111");
  });

  it("Shift+Enter adds a new line instead of sending", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    render(<ChatPanel />);

    await userEvent.type(screen.getByLabelText("Your question"), "line one{Shift>}{Enter}{/Shift}line two");

    expect(screen.getByLabelText("Your question")).toHaveValue("line one\nline two");
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("shows the ⚠ badge when the number check failed", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(answer("I couldn't produce an answer I can verify. Here are the facts: …", { numberCheckPassed: false })));
    render(<ChatPanel />);

    await userEvent.click(screen.getByRole("button", { name: "Should I sell INFY?" }));

    expect(await screen.findByText(/⚠ Verified facts shown instead of the AI answer/)).toBeInTheDocument();
  });

  it("New conversation clears the messages and the conversation id", async () => {
    const fetchMock = vi.fn().mockResolvedValue(answer("INFY is 18.0%."));
    vi.stubGlobal("fetch", fetchMock);
    render(<ChatPanel />);
    await userEvent.click(screen.getByRole("button", { name: SUGGESTED_PROMPTS[0] }));
    await screen.findByTestId("chat-assistant");

    await userEvent.click(screen.getByRole("button", { name: "New conversation" }));
    await userEvent.click(screen.getByRole("button", { name: SUGGESTED_PROMPTS[1] }));

    expect(JSON.parse(fetchMock.mock.calls[1][1].body).conversationId).toBeNull();
  });

  it("a 401 offers to reconnect", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({ type: "kite-session-required", status: 401 }), { status: 401 })));
    render(<ChatPanel />);

    await userEvent.click(screen.getByRole("button", { name: SUGGESTED_PROMPTS[0] }));

    expect(await screen.findByRole("link", { name: "Connect Zerodha" })).toHaveAttribute("href", "/connect");
  });
});
