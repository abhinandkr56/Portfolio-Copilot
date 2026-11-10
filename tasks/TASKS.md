# Build plan: phases

Claude Code: do **one phase per request**. Tick the boxes as you go. A phase is done only when every "Done when" check passes **and** the Definition of done in `CLAUDE.md` holds. Then stop and summarise: what you built, how to see it, and the decisions you recorded.

---

## Phase 0: Scaffold

Docs: 02

- [x] `git init` if this isn't already a repo; add a .NET `.gitignore`
- [x] `dotnet new sln -n PortfolioCopilot --format sln`
- [x] Create the .NET projects from doc 02 (AppHost, ServiceDefaults, Core, Api, MockKite, NavSync, Core.Tests, Api.Tests, Evals), all `net10.0`, and add them to the sln
- [x] Create the Next.js app in `src/web` (doc 07 §Stack: `npx create-next-app@latest web` with TypeScript, ESLint, Tailwind, App Router, no `src/` dir, alias `@/*`); add the scripts from doc 07; add `.gitignore` entries for `node_modules` and `.next`
- [x] `Directory.Build.props`: `Nullable` enable, `ImplicitUsings` enable, `TreatWarningsAsErrors` true, `LangVersion` latest
- [x] Api: `launchSettings.json` http **127.0.0.1:5080** (https 7080); web (Next.js) **127.0.0.1:3000**; MockKite http 5100
- [x] AppHost wiring from doc 02 (with Postgres, parameters, explicit-start NavSync and Evals, and the Next.js app via `Aspire.Hosting.JavaScript`)
- [x] Each .NET web project calls `AddServiceDefaults()` + `MapDefaultEndpoints()`
- [x] Api: `GET /api/ping` → `{ "ok": true }`. web: `next.config.ts` rewrite for `/api/*` (doc 07); the home page (a Server Component) fetches `${API_URL}/api/ping` and shows "Api: ok"; a tiny client component also fetches `/api/ping` through the rewrite and shows "Rewrite: ok"
- [x] `samples/` copied to the output of MockKite, Core.Tests, Api.Tests and Evals (`<None Include="..\..\samples\**" LinkBase="samples" CopyToOutputDirectory="PreserveNewest" />`)

**Done when:** `dotnet build` has 0 warnings · `dotnet run --project src/PortfolioCopilot.AppHost` shows api, web, mockkite and pg healthy in the dashboard · http://127.0.0.1:3000 shows "Api: ok" and "Rewrite: ok" · `npm run lint`, `npm run typecheck` and `npm run build` pass in `src/web`.

---

## Phase 1: Core domain and rules (no IO)

Docs: 05, 08 §1

- [ ] Records and enums from doc 05
- [ ] `Classifier` (asset class, plan type, direct twin)
- [ ] `PortfolioCalculator` (values, hybrid split, allocation)
- [ ] `RulesEngine.Evaluate(PortfolioInput, TimeProvider) → IReadOnlyList<Finding>` with all rules, the ordering and the message templates
- [ ] `PlanValidator`
- [ ] `AmfiNavParser`
- [ ] `ScenarioLoader` (test helper in Core.Tests, or a `Core/Testing` folder): reads `samples/scenarios/*.json` + the instruments CSV + the AMFI sample into `PortfolioInput`
- [ ] All Core.Tests from doc 08 §1, **including every golden scenario**

**Done when:** `dotnet test` is green, and each scenario's findings equal `expected.findings` exactly (rule, subject, severity, order) with the allocation matching to 1 dp.

---

## Phase 2: Mock Kite, Kite client and login

Docs: 04, 03 §Session, 09

- [ ] MockKite: all routes from doc 04, scenario switching, failure injection, gzipped instruments CSV, auth header check, unexpected routes logged and 404
- [ ] Api: `KiteClient` (typed `HttpClient`): exchange, logout, profile, holdings, MF holdings, SIPs, MF orders, instruments; envelope parsing; the error mapping table; one 429 retry; a 10-second timeout
- [ ] `KiteSessionStore` (EF Core `KiteSessions`, Data Protection), with an expiry computed for 06:00 IST
- [ ] `/kite/login` (with the `state` cookie), `/kite/callback`, `GET /api/session`, `POST /api/session/logout`
- [ ] Logging redaction filter (doc 09)
- [ ] EF Core migrations; apply them at startup in Development
- [ ] Api.Tests: the login round trip, the expiry → 401, the rate-limit retry, the **write-guard test**

**Done when:** in the AppHost, opening `http://localhost:5080/kite/login` lands on the Web app with `?connected=1`, `/api/session` shows `connected: true, mode: "Mock"`, and the tests are green.

---

## Phase 3: API endpoints (no AI)

Docs: 03, 05

- [ ] `PortfolioService`: fetch from Kite (with a 5-minute `IMemoryCache`) + instruments (24 h) + NAVs from the DB → `PortfolioInput`
- [ ] Seed `Plans` from `samples/plan.sample.json`; `GET/PUT /api/plan` with validation
- [ ] `POST /api/nav/import` (Dev) and an auto-import of `samples/amfi/NAVAll.sample.txt` at startup in Mock mode when `NavRecords` is empty
- [ ] `GET /api/holdings`, `/api/sips`, `/api/allocation`, `POST /api/review` (`explain:false` only for now)
- [ ] `POST /api/dev/scenario/{name}` (Mock only)
- [ ] Daily snapshot: the first successful `/api/holdings` of the day writes a `Snapshots` row
- [ ] `ReviewRuns` audit row for each review
- [ ] OpenAPI + Swagger/Scalar in Development
- [ ] Api.Tests: every scenario through `/api/review` equals `expected.findings`

**Done when:** in Swagger, `/api/review` on the `demo` scenario returns the 8 expected findings in order, and the tests are green.

---

## Phase 4: Frontend (no AI)

Docs: 07

- [ ] `npm run gen:api` → `lib/api-types.ts` (fix the Api's OpenAPI metadata first if types come out as `unknown`)
- [ ] `lib/api.ts` (ApiError, ProblemDetails, server vs browser base URL) and `lib/format.ts`
- [ ] `/connect` route handler; layout, nav, ConnectionPill, RefreshButton, footer
- [ ] Dashboard (summary cards, SVG `AllocationBars`, `FindingsList` with filters); the Explain button disabled with the tooltip "Available in Phase 5"
- [ ] Holdings, SIPs and Plan pages (`PlanForm` saves and refreshes)
- [ ] `loading.tsx` / `error.tsx`; ConnectBanner for 401, ErrorPanel for 503
- [ ] Dark/light theme; checked at 390 px width
- [ ] Vitest tests and the Playwright smoke test from doc 07

**Done when:** after Connect (mock), the dashboard shows the `demo` findings; editing the plan targets to Equity 85 · Debt 11 · Gold 4 and saving removes all three AllocationDrift findings (drifts -0.5, +0.5, 0.0); `POST /__fail/token` on MockKite then Refresh shows the Connect banner.

---

## Phase 5: AI explain and ask

Docs: 06 (everything except guardrail tuning), 02 §config

- [ ] **Verify the model ids** on Groq's and Gemini's current model lists; update `appsettings.json`; record them in `DECISIONS.md`
- [ ] `ChatClientFactory` (the CLAUDE.md convention) + `.UseOpenTelemetry()`; ServiceDefaults listens to the M.E.AI source and meter
- [ ] `Redactor` wired in; `AI:ShareAmounts` (default false)
- [ ] Explain: `POST /api/review {explain:true}`
- [ ] Ask: the agent with the 6 tools (Agent Framework, or `UseFunctionInvocation`; record which), in-memory conversations, `toolsUsed`
- [ ] Both guardrails, the retry and the fallbacks from doc 06; disclaimer enforcement; `ReviewRuns` audit
- [ ] Web: the Explain button and the Ask page (suggested prompts, tools used, ⚠ badge)
- [ ] Api.Tests with a fake `IChatClient`: pass, invented number → fallback, forbidden phrase → fallback

**Done when:** with `GROQ_API_KEY` set, Explain on `demo` produces a summary with `numberCheckPassed: true`; asking "Should I sell INFY?" gets a refusal plus the INFY concentration facts; the chat span and tool spans appear in the Aspire dashboard; `--AI:Provider=Gemini` also works (if a key is set).

---

## Phase 6: Evals

Docs: 08 §3

- [ ] `PortfolioCopilot.Evals` console as specified; a pass/fail table; exit code
- [ ] Runs from the AppHost (explicit start), and from the CLI with `RunsAgainst=core` (deterministic part only, no Api)
- [ ] Tune the prompts (doc 06 only, never the expected files) until every scenario passes on Groq. Record the pass rate per provider in `DECISIONS.md`.

**Done when:** the evals pass 100% of the deterministic checks and at least 5 of 6 scenarios on all AI checks on Groq, and the failures (if any) are written up in `DECISIONS.md` with an example.

---

## Phase 7: NAV sync and polish

- [ ] NavSync: download the current AMFI NAV file over HTTP, parse it, upsert into `NavRecords`; log the counts. **First check the live file's layout**; if it differs from the classic format, extend the parser, add a test with a trimmed real sample, and record it.
- [ ] `StaleNav` visible on the dashboard when it applies
- [ ] README at the repo root: setup, env vars, run, daily login, troubleshooting
- [ ] `DELETE /api/dev/purge`

**Done when:** running NavSync from the dashboard imports thousands of NAV rows, and the tests are still green.

---

## Phase 8: Go live (the human does the Kite part)

Human checklist (Claude Code: print this list and wait):
- [ ] Kite developer console → create a **Personal** app → redirect URL `http://127.0.0.1:5080/kite/callback`
- [ ] Set `KITE_API_KEY` and `KITE_API_SECRET` in the environment; restart VS Code and the terminal
- [ ] `dotnet run --project src/PortfolioCopilot.AppHost -- --Kite:Mode=Live`
- [ ] Connect Zerodha → log in → back on the dashboard

Claude Code, after the human confirms it's connected:
- [ ] Add a temporary Dev endpoint that logs the **shape** (keys and types, never values) of each Kite response; compare it with doc 04; fix the mappings; remove the endpoint
- [ ] Record the distinct `scheme_type` and `plan` values from `/mf/instruments` (or that it's unavailable on Personal) and adjust `SchemeTypeMap`
- [ ] Check that every holding has an asset class and every fund has a plan type; add overrides to the plan for any leftovers (e.g. gold ETFs → Gold)

**Done when:** the dashboard shows the real portfolio, every holding is classified, and no Kite write call exists in the code (the write-guard test is still green).

---

## Later (not now)

Console tradebook CSV import → XIRR · weekly scheduled review · RAG over fund factsheets and an investment journal (pgvector) · history charts from Snapshots · Ollama by default.
