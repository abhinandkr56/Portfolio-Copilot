# Prompts for Claude Code (paste one at a time)

Start every phase in a **fresh** Claude Code conversation, or after `/clear`, so the context stays small. `CLAUDE.md` is read automatically.

---

### Phase 0
```
Read CLAUDE.md, docs/02-architecture.md and tasks/TASKS.md.
Do Phase 0 only. Before adding any NuGet package, npm package or template, check its current
version (for Next.js, the latest 16.x patch; for Aspire, the current way to host a Next.js app
on aspire.dev) and record it in docs/DECISIONS.md. When the "Done when" checks pass, tick the boxes in
TASKS.md and tell me exactly how to see the result. Then stop.
```

### Phase 1
```
Read CLAUDE.md, docs/05-domain-and-rules.md, docs/08-testing-and-evals.md and the files in
samples/. Do Phase 1 only. Core must stay free of IO and DateTime.Now.
If a golden scenario's expected findings look inconsistent with the rules in doc 05,
stop and show me the calculation. Do not change the expected files to make tests pass.
```

### Phase 2
```
Read CLAUDE.md and docs 03, 04 and 09. Do Phase 2 only. Kite calls must be GET-only, except
session token create and delete, and the write-guard test must prove it. Never log tokens.
```

### Phase 3
```
Read CLAUDE.md and docs 03 and 05. Do Phase 3 only. Reuse Core for every calculation;
endpoints only orchestrate. Show me the /api/review output for the demo scenario at the end.
```

### Phase 4
```
Read CLAUDE.md and docs 03 and 07. Do Phase 4 only (no AI yet), in src/web with Next.js.
Start by generating the TypeScript types from the Api's OpenAPI; never hand-write DTO types.
Tailwind and hand-written SVG only: no UI kit or chart library. The frontend formats numbers
and never computes them. At the end, run lint, typecheck, test, build and the Playwright smoke
test, and list the manual checks I should click through.
```

### Phase 5
```
Read CLAUDE.md and docs 02 and 06. Do Phase 5. First verify the current Groq and Gemini model
ids on their official model lists, and the current Microsoft Agent Framework package and API
on Microsoft Learn. Record both in DECISIONS.md before writing AI code.
GROQ_API_KEY is set in my environment.
```

### Phase 6
```
Read CLAUDE.md and doc 08. Do Phase 6. Run the evals, show me the table, and if anything
fails, propose prompt changes (doc 06) and re-run. Never edit the expected files.
```

### Phase 7
```
Read CLAUDE.md, doc 05 §AMFI NAV parser and Phase 7 in TASKS.md. First fetch the live AMFI
NAV file and show me its first 15 lines, then adapt the parser if needed. Do Phase 7.
```

### Phase 8
```
Read CLAUDE.md, doc 04 and Phase 8 in TASKS.md. Print the human checklist and wait for me
to confirm I'm connected in Live mode. Then do the Claude Code part. Never print or log
values from my real account, only the shapes.
```

---

## Useful one-off prompts

- `Run dotnet build and dotnet test and fix any warnings or failures without changing the behaviour described in the docs.`
- `Review the whole solution against CLAUDE.md's non-negotiable rules and list every violation with a file and line.`
- `Explain how a request to POST /api/ask flows through the code, file by file.`
- `Add a finding rule: <describe>. Update doc 05, add tests, and add a golden scenario.`
