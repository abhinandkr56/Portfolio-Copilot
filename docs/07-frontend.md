# 07 · Frontend (`src/web`, Next.js)

## Stack

- **Next.js, latest stable 16.x.** Check npm for the newest patch: Next.js publishes regular security releases, so always take the latest patch. App Router, **TypeScript strict**, **Tailwind CSS**, ESLint, npm. Create it with `npx create-next-app@latest web` (TypeScript yes, ESLint yes, Tailwind yes, `src/` directory no, App Router yes, import alias `@/*`).
- **Node.js:** the test toolchain needs more than Next.js does: Vitest 5 and Vite 8 need Node ≥ 22.12, and jsdom 31 needs ≥ 22.22.2 or ≥ 24.15. Use Node 24 LTS (`src/web/.nvmrc`) or newer. Record the versions in `DECISIONS.md`.
- **No** UI component library, chart library, state library or data-fetching library. Use React state, URL search params and `router.refresh()`. Charts are hand-written SVG components. `lucide-react` for icons is fine.

## Talking to the Api

```
Browser ──▶ Next.js (127.0.0.1:3000)
              ├─ Server Components ──fetch──▶ API_URL/api/...            (server side, no CORS)
              ├─ Client Components ──fetch──▶ /api/... ──rewrite──▶ API_URL/api/...
              └─ /connect (route handler) ──302──▶ API_PUBLIC_URL/kite/login
                     ──▶ Zerodha (or MockKite) ──▶ 127.0.0.1:5080/kite/callback
                     ──302──▶ 127.0.0.1:3000/?connected=1
```

- **`API_URL`** is a server-only env var, set by the AppHost to the Api's http endpoint. Never expose it as `NEXT_PUBLIC_*`.
- **`next.config.ts` rewrites** `/api/:path*` to `${process.env.API_URL}/api/:path*`, so client components call the same origin and the Api needs **no CORS**.
- **The login round trip stays on the Api's origin**, so the Api's `state` cookie works (doc 09). The **Connect Zerodha** button links to `/connect`, a Route Handler (`app/connect/route.ts`) that redirects 302 to `${API_PUBLIC_URL}/kite/login` (`API_PUBLIC_URL` = `http://127.0.0.1:5080`). The Api finishes on `Kite:WebAppUrl` = `http://127.0.0.1:3000`.
- **Always use `127.0.0.1`, not `localhost`**, for both apps, so the cookies and the Kite redirect URL line up. The dev script is `next dev --hostname 127.0.0.1` (Next reads `PORT` from the environment).
- **`lib/api.ts`** is a small typed wrapper:
  - `apiGet<T>(path)` and `apiSend<T>(method, path, body?)`
  - base URL = `process.env.API_URL` on the server, `""` in the browser
  - `cache: "no-store"` (the Api does the caching)
  - non-2xx responses are parsed as ProblemDetails into `class ApiError { status; type; title; detail }`
  - helpers `isSessionRequired(e)` (`type === "kite-session-required"`) and `isKiteUnavailable(e)`
- **Types are generated, never hand-written:** `npm run gen:api` runs `openapi-typescript ${API_URL}/openapi/v1.json -o lib/api-types.ts` (check the Api's actual OpenAPI path). Commit the generated file and re-run it whenever the API changes.
- **`lib/format.ts`:**
  - `formatInr(n)` uses `Intl.NumberFormat("en-IN", { style: "currency", currency: "INR", maximumFractionDigits: 0 })`, so ₹1,00,000
  - `formatPct(n)` gives 1 dp
  - `formatSignedPoints(n)` gives `+14.5` / `-8.5`
  - `formatIst(iso)` uses `timeZone: "Asia/Kolkata"`

## Rendering rules

- Pages are **Server Components** that fetch on the server (dynamic rendering, no static caching of portfolio data). Interactive pieces are small **Client Components** (`"use client"`): the findings filter, Explain button, plan form, chat, and Refresh button.
- Each route has `loading.tsx` (a skeleton) and `error.tsx` (generic error + retry). A page catches `ApiError`:
  - **401 `kite-session-required`** → render `<ConnectBanner />` instead of the content
  - **503 `kite-unavailable`** → render `<ErrorPanel message="Zerodha is not responding, try again in a minute" />`
  - anything else → rethrow to `error.tsx`
- **Refresh** calls `POST /api/refresh` (drops the Api's cached Kite data) and then `router.refresh()`, so it really refetches from Zerodha.
- Theme: Tailwind `dark:` variants using the `media` strategy (`prefers-color-scheme`).
- Severity colours: Act = red, Watch = amber, Info = blue, OK = green. Always pair the colour with a text label.
- Every number on screen comes from the Api. The frontend **never computes** allocations or findings. It only formats them.

## Routes and files

```
src/web/
  app/
    layout.tsx            nav (Dashboard · Holdings · SIPs · Plan · Ask), ConnectionPill, Refresh, footer
    page.tsx              Dashboard
    holdings/page.tsx
    sips/page.tsx
    plan/page.tsx
    ask/page.tsx
    connect/route.ts      302 → API_PUBLIC_URL/kite/login
    loading.tsx, error.tsx (and per-route where useful)
  components/
    ConnectionPill.tsx  ConnectBanner.tsx  RefreshButton.tsx (client)
    SummaryCards.tsx    AllocationBars.tsx (SVG)   FindingCard.tsx
    FindingsList.tsx (client: filter chips)        ExplainPanel.tsx (client)
    HoldingsTable.tsx (client: sort)  PlanBadge.tsx  SeverityChip.tsx
    SipTable.tsx  SipBudgetBar.tsx  PlanForm.tsx (client)  ChatPanel.tsx (client)
    ErrorPanel.tsx  Money.tsx  Percent.tsx
  lib/  api.ts  api-types.ts (generated)  format.ts
  tests/ (Vitest)   e2e/ (Playwright)
```

## Pages

### `/` Dashboard
1. **Not connected:** `ConnectBanner`: "Connect your Zerodha account to start." plus a **Connect Zerodha** link to `/connect`. On `?connected=1`, show a "Connected" toast; on `?connected=0&reason=…`, show the reason.
2. **Summary cards:** Total value · Holdings count (stocks / funds) · Active SIPs per month · Findings (Act/Watch/Info counts).
3. **Allocation vs target** (`AllocationBars`, SVG): one horizontal bar per class showing the actual % filled, a target marker line and the band shaded (target ± band). Label: `84.5% / 70.0% (+14.5)`, plus the severity chip.
4. **Findings list:** cards in Api order: severity chip, message, evidence as small key/value pills, and the suggestion as a tag. Filter chips: All / Act / Watch / Info (stored in `?severity=`).
5. **Explain with AI** (client): `POST /api/review {explain:true}` → the summary in a quote block, "Written by {provider} · {model}". If `numberCheckPassed` is false, show "Showing the verified facts instead of the AI summary". Spinner while waiting; disabled during the call. **Phase 4:** render the button disabled with the tooltip "Available in Phase 5".
6. "As of {IST time} · NAV date {date}", plus a stale warning if there's a `StaleNav` finding.

### `/holdings`
Two tables, **Stocks** and **Mutual funds**. Columns: Name · Qty · Avg · LTP · Value · P&L (coloured) · % of total · Asset class (the Api's `assetClassLabel`, e.g. "Hybrid (65/35)") · (funds) Plan badge (`Direct` green / `Regular` amber / `Unknown` grey). Columns sort client-side when the header is clicked. Totals row from the Api's `stocksTotal` / `fundsTotal`. Folios are masked (they come masked from the Api).

### `/sips`
A table of SIPs: Fund · Status chip · Frequency · Amount · Monthly equivalent · Next instalment · Completed. Paused rows are dimmed. Above it, `SipBudgetBar`: "Active SIPs ₹16,500/month of a ₹40,000 budget" (bar length = the Api's `budgetUsedPercent`). Below it, a "Recent failed SIP orders (last 7 days)" list.

### `/plan`
`PlanForm` (client), bound to the `Plan` type:
- Target allocation: 4 number inputs and a live "Total: 100%" indicator (red when it isn't 100).
- Drift band, max stock %, max fund %, monthly SIP budget, hybrid equity %, and a Prefer Direct toggle.
- Overrides: an editable table (key = symbol or ISIN, class select), with add/remove rows.
- Notes (textarea).
- **Save** → `PUT /api/plan` → 400 errors shown next to their fields → on success, a "Plan saved · review updated" toast and `router.refresh()`.

### `/ask`
`ChatPanel` (client): a message list, a textarea, **Send** (Enter sends, Shift+Enter adds a new line).
- Suggested prompt chips when the chat is empty: "Why is my equity allocation above target?", "Which of my funds are Regular plans?", "Am I within my SIP budget?", "Should I sell INFY?" (that last one shows the guardrail).
- Each assistant message shows a collapsible **Tools used** list and a ⚠ badge when `numberCheckPassed=false`.
- **New conversation** resets `conversationId` (kept in component state).

### Footer (every page)
`AI: {provider} · {model}` (from `/api/session`) and the disclaimer: *Information only, not investment advice. You make the decisions.*

## Scripts (`package.json`)

| Script | Command |
|---|---|
| `dev` | `next dev --hostname 127.0.0.1` |
| `build` / `start` | `next build` / `next start --hostname 127.0.0.1` |
| `lint` | `eslint .` |
| `typecheck` | `tsc --noEmit` |
| `test` | `vitest run` |
| `e2e` | `playwright test` |
| `gen:api` | `openapi-typescript …` (above) |

## Tests

**Vitest + Testing Library (jsdom)**
- `format.ts`: `formatInr(100000)` → `₹1,00,000`; `formatSignedPoints(-8.5)` → `-8.5`; IST formatting
- `api.ts`: a ProblemDetails 401 becomes an `ApiError` and `isSessionRequired` is true
- `AllocationBars`: renders 3 bars for the demo allocation, with labels `84.5% / 70.0% (+14.5)`
- `FindingsList`: the "Act" filter shows 2 of the 8 demo findings
- `PlanForm`: the total indicator turns red at 95; server field errors render next to their inputs

Use the `demo` scenario numbers from `samples/scenarios/demo.json` as fixtures (copy them in a test helper rather than duplicating them by hand).

**Playwright (one smoke test, `e2e/smoke.spec.ts`)**, run with the AppHost already running in mock mode:
1. Open `http://127.0.0.1:3000` → see the Connect banner (call MockKite `POST /__fail/token` first, or log out via the Api).
2. Click **Connect Zerodha** → back on the dashboard → **8** finding cards, and the first contains "Equity".
3. `/holdings` shows an INFY row and the Regular badge on "Alpha Flexi Cap Fund - Regular Plan - Growth".

## Done when (frontend)

- `npm run lint`, `npm run typecheck`, `npm test` and `npm run build` all pass in `src/web`.
- With MockKite on the `demo` scenario, the dashboard shows exactly the findings in the `expected` section of `samples/scenarios/demo.json`.
- Each page renders correctly at 1280 px and at 390 px (phone).
- The not-connected and Kite-down states are visible after MockKite `POST /__fail/token` and `/__fail/down`.
- The Playwright smoke test passes.
