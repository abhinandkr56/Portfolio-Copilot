"use client";

import { useState } from "react";
import { ApiError, apiSend, isSessionRequired } from "@/lib/api";
import type { PortfolioReview } from "@/lib/types";

/** "Explain with AI": POST /api/review { explain: true } and show the summary (doc 07 §Dashboard 5). */
export function ExplainPanel() {
  const [busy, setBusy] = useState(false);
  const [review, setReview] = useState<PortfolioReview | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function explain() {
    setBusy(true);
    setError(null);
    try {
      setReview(await apiSend<PortfolioReview>("POST", "/api/review", { explain: true }));
    } catch (e) {
      setReview(null);
      setError(isSessionRequired(e) ? "Your Zerodha session ended. Connect again to explain." : e instanceof ApiError ? (e.detail ?? e.message) : "The explanation failed.");
    } finally {
      setBusy(false);
    }
  }

  const ai = review?.ai;
  return (
    <div className="space-y-3">
      <button
        type="button"
        onClick={explain}
        disabled={busy}
        className="inline-flex items-center gap-2 rounded-md bg-violet-700 px-3 py-1.5 text-sm font-semibold text-white hover:bg-violet-800 disabled:opacity-60"
      >
        {busy && <span aria-hidden className="h-3 w-3 animate-spin rounded-full border-2 border-white/40 border-t-white" />}
        {busy ? "Explaining…" : "Explain with AI"}
      </button>

      {error && <p role="alert" className="text-sm text-red-700 dark:text-red-400">{error}</p>}

      {ai?.error && (
        <p role="alert" className="text-sm text-amber-800 dark:text-amber-300">AI unavailable: {ai.error}</p>
      )}

      {review?.summary && (
        <figure data-testid="explain-summary" className="rounded-lg border border-violet-200 bg-violet-50 p-4 dark:border-violet-900 dark:bg-violet-950/40">
          {ai?.numberCheckPassed === false && (
            <p className="mb-2 text-sm font-medium text-amber-800 dark:text-amber-300">
              ⚠ Showing the verified facts instead of the AI summary
            </p>
          )}
          <blockquote className="whitespace-pre-line text-sm leading-relaxed">{review.summary}</blockquote>
          <figcaption className="mt-2 text-xs text-zinc-500 dark:text-zinc-400">
            Written by {ai?.provider} · {ai?.model}
          </figcaption>
        </figure>
      )}
    </div>
  );
}
