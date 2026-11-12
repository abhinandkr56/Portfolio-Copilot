"use client";

import { useRef, useState } from "react";
import { ApiError, apiSend, isSessionRequired } from "@/lib/api";
import type { AskResponse } from "@/lib/types";

type Message =
  | { role: "user"; text: string }
  | { role: "assistant"; text: string; toolsUsed: string[]; numberCheckPassed: boolean; provider: string; model: string }
  | { role: "error"; text: string; connect?: boolean };

export const SUGGESTED_PROMPTS = [
  "Why is my equity allocation above target?",
  "Which of my funds are Regular plans?",
  "Am I within my SIP budget?",
  "Should I sell INFY?",
];

/** The Ask chat (doc 07 §/ask). Enter sends, Shift+Enter adds a new line. The conversation id lives in component state. */
export function ChatPanel() {
  const [messages, setMessages] = useState<Message[]>([]);
  const [conversationId, setConversationId] = useState<string | null>(null);
  const [draft, setDraft] = useState("");
  const [busy, setBusy] = useState(false);
  const input = useRef<HTMLTextAreaElement>(null);

  async function send(text: string) {
    const message = text.trim();
    if (!message || busy) return;
    setDraft("");
    setBusy(true);
    setMessages((m) => [...m, { role: "user", text: message }]);
    try {
      const r = await apiSend<AskResponse>("POST", "/api/ask", { conversationId, message });
      setConversationId(r.conversationId);
      setMessages((m) => [...m, { role: "assistant", text: r.answer, toolsUsed: r.toolsUsed, numberCheckPassed: r.numberCheckPassed, provider: r.provider, model: r.model }]);
    } catch (e) {
      const text = isSessionRequired(e)
        ? "Your Zerodha session ended."
        : e instanceof ApiError
          ? (e.errors.message?.[0] ?? e.detail ?? e.message)
          : "Something went wrong.";
      setMessages((m) => [...m, { role: "error", text, connect: isSessionRequired(e) }]);
    } finally {
      setBusy(false);
      input.current?.focus();
    }
  }

  function newConversation() {
    setMessages([]);
    setConversationId(null);
    setDraft("");
    input.current?.focus();
  }

  return (
    <section aria-label="Ask about your portfolio" className="space-y-4">
      <div className="flex justify-end">
        <button type="button" onClick={newConversation} disabled={busy}
          className="rounded-md border border-zinc-300 px-3 py-1.5 text-sm hover:bg-zinc-100 disabled:opacity-60 dark:border-zinc-700 dark:hover:bg-zinc-800">
          New conversation
        </button>
      </div>

      {messages.length === 0 && (
        <div>
          <p className="mb-2 text-sm text-zinc-600 dark:text-zinc-400">Try one of these:</p>
          <div className="flex flex-wrap gap-2">
            {SUGGESTED_PROMPTS.map((p) => (
              <button key={p} type="button" onClick={() => send(p)} disabled={busy}
                className="rounded-full border border-zinc-300 px-3 py-1 text-sm hover:bg-zinc-100 dark:border-zinc-700 dark:hover:bg-zinc-800">
                {p}
              </button>
            ))}
          </div>
        </div>
      )}

      <ol aria-live="polite" className="space-y-3">
        {messages.map((m, i) => (
          <li key={i} data-testid={`chat-${m.role}`}
            className={m.role === "user"
              ? "ml-auto max-w-[85%] rounded-lg bg-blue-700 px-3 py-2 text-sm text-white"
              : m.role === "error"
                ? "max-w-[85%] rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm dark:border-red-900 dark:bg-red-950/40"
                : "max-w-[85%] rounded-lg border border-zinc-200 bg-white px-3 py-2 text-sm dark:border-zinc-800 dark:bg-zinc-900"}>
            {m.role === "assistant" && !m.numberCheckPassed && (
              <p className="mb-1 text-xs font-semibold text-amber-800 dark:text-amber-300" title="The AI answer failed the checks, so these are the verified facts">
                ⚠ Verified facts shown instead of the AI answer
              </p>
            )}
            <p className="whitespace-pre-line">{m.text}</p>
            {m.role === "error" && m.connect && (
              <a href="/connect" className="mt-1 inline-block font-semibold text-blue-700 underline dark:text-blue-400">Connect Zerodha</a>
            )}
            {m.role === "assistant" && (
              <details className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">
                <summary className="cursor-pointer">Tools used ({m.toolsUsed.length})</summary>
                {m.toolsUsed.length === 0 ? <p className="mt-1">None</p> : (
                  <ul className="mt-1 list-inside list-disc">{m.toolsUsed.map((t) => <li key={t}>{t}</li>)}</ul>
                )}
                <p className="mt-1">{m.provider} · {m.model}</p>
              </details>
            )}
          </li>
        ))}
        {busy && <li className="text-sm text-zinc-500" aria-label="Thinking">Thinking…</li>}
      </ol>

      <form onSubmit={(e) => { e.preventDefault(); void send(draft); }} className="flex items-end gap-2">
        <label htmlFor="ask-input" className="sr-only">Your question</label>
        <textarea
          id="ask-input"
          ref={input}
          rows={2}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              void send(draft);
            }
          }}
          placeholder="Ask about your portfolio and plan…"
          className="min-h-[2.5rem] flex-1 resize-y rounded-md border border-zinc-300 bg-white px-3 py-2 text-sm dark:border-zinc-700 dark:bg-zinc-900"
        />
        <button type="submit" disabled={busy || !draft.trim()}
          className="rounded-md bg-blue-700 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-800 disabled:opacity-60">
          Send
        </button>
      </form>
    </section>
  );
}
