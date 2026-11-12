// Phase 4: disabled. Phase 5 turns this into ExplainPanel (POST /api/review { explain: true }).
export function ExplainButton() {
  return (
    <button
      type="button"
      disabled
      title="Available in Phase 5"
      aria-describedby="explain-hint"
      className="cursor-not-allowed rounded-md bg-zinc-200 px-3 py-1.5 text-sm font-medium text-zinc-500 dark:bg-zinc-800 dark:text-zinc-400"
    >
      Explain with AI
      <span id="explain-hint" className="sr-only">Available in Phase 5</span>
    </button>
  );
}
