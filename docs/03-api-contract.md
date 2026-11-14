# 03 · API contract (`PortfolioCopilot.Api`, http://localhost:5080)

All JSON is camelCase. All `/api/*` errors use RFC 7807 `ProblemDetails`. The Next.js app reaches `/api/*` through a rewrite or a server-side fetch, so the Api needs **no CORS**. The OpenAPI document must describe every response type, because the frontend's TypeScript types are generated from it. Two error types are special:

| Situation | Status | `type` | Web behaviour |
|---|---|---|---|
| No Kite session, or the session expired | 401 | `kite-session-required` | show the **Connect Zerodha** banner |
| Kite rate limit (429) or Kite is down | 503 | `kite-unavailable` | show "Zerodha is not responding, try again in a minute" |

Enable OpenAPI (`MapOpenApi`) and Swagger UI or Scalar in Development. (Built: `/openapi/v1.json` and Swagger UI at `/swagger`.)

---

## Session

### `GET /kite/login`
302 redirect to the Kite login URL (`{LoginUrl}?v=3&api_key={key}`). In mock mode it redirects to `MockKite /connect/login`, which immediately redirects back to the callback with `request_token=mock-request-token&status=success`.

### `GET /kite/callback?request_token=…&status=success`
Exchanges the token (doc 04), stores the session and redirects 302 to `{Kite:WebAppUrl}/?connected=1`. On `status!=success` or a failed exchange, redirect to `{WebAppUrl}/?connected=0&reason=…` (short, no secrets).

### `GET /api/session`
```json
{ "connected": true, "mode": "Mock", "userIdMasked": "AB***12", "userName": "Demo User",
  "createdUtc": "2026-11-10T03:30:00Z", "expiresUtc": "2026-11-11T00:30:00Z" }
```
`expiresUtc` = the next 06:00 Asia/Kolkata after `createdUtc`. Not connected: `{ "connected": false, "mode": "Mock" }` (200, not 401).

### `POST /api/session/logout`
Calls Kite `DELETE /session/token` (best effort), deletes the stored session, returns 204.

---

## Portfolio data (needs a session)

### `GET /api/holdings`
```json
{
  "asOfUtc": "2026-11-10T09:12:00Z",
  "totalValue": 100000.00,
  "stocks": [ { "symbol": "INFY", "isin": "INETEST00001", "exchange": "NSE", "quantity": 12,
                "averagePrice": 1400.00, "lastPrice": 1500.00, "value": 18000.00, "pnl": 1200.00,
                "assetClass": "Equity", "assetClassLabel": "Equity", "percentOfTotal": 18.0 } ],
  "funds":  [ { "fund": "Alpha Flexi Cap Fund - Regular Plan - Growth", "isin": "INFTEST00002",
                "folioMasked": "****0884", "quantity": 400, "averagePrice": 50.00, "lastPrice": 55.00,
                "lastPriceDate": "2026-11-07", "value": 22000.00, "pnl": 2000.00,
                "assetClass": "Equity", "assetClassLabel": "Equity", "plan": "Regular", "percentOfTotal": 22.0 } ]
}
```
`assetClassLabel` is the display text, e.g. `"Hybrid (65/35)"` for a hybrid fund (from the plan's `hybridEquityPercent`), so the frontend never computes it. `value` is rounded to 2 dp and `percentOfTotal` to 1 dp.

### `GET /api/sips`
```json
{ "asOfUtc": "…", "activeMonthlyEquivalent": 16500.00, "monthlyBudget": 40000.00,
  "sips": [ { "sipId": "SIPTEST01", "fund": "…", "isin": "INFTEST00002", "status": "ACTIVE",
              "frequency": "monthly", "instalmentAmount": 10000.00, "monthlyEquivalent": 10000.00,
              "nextInstalment": "2026-12-05", "completedInstalments": 14 } ],
  "recentFailedOrders": [ { "orderId": "…", "fund": "…", "date": "2026-11-05", "message": "SIP: Insufficient balance." } ] }
```

### `GET /api/allocation`
```json
{ "asOfUtc": "…", "totalValue": 100000.00,
  "classes": [ { "assetClass": "Equity", "value": 84500.00, "actualPercent": 84.5,
                 "targetPercent": 70.0, "driftPoints": 14.5, "severity": "Act" } ] }
```
Always return Equity, Debt and Gold in that order, then Other only when it's non-zero. `severity` is `null` when within the band.

### `POST /api/refresh`
Drops the cached Kite data (not the 24 h instruments master), so the next data request fetches from Kite. 204. The Web's **Refresh** button calls it before `router.refresh()`. Data endpoints also refuse cached data when there's no session (401), and login/logout clear the cache.

---

## Plan

### `GET /api/plan` → the `Plan` record (doc 05)
### `PUT /api/plan`
Body: `Plan`. Validates (doc 05 §Plan validation). 400 with field errors, or 200 with the saved plan.
Error keys are the plan's top-level field names (`targetAllocation`, `driftBandPoints`, `assetClassOverrides`, …); the message names the entry, e.g. `"GOLDBEES: Hybrid is not allowed; …"`.

---

## Review

### `POST /api/review`
Body: `{ "explain": false }`. Runs all rules (deterministic) and returns a `PortfolioReview`:

```json
{
  "asOfUtc": "…",
  "navDate": "2026-11-07",
  "allocation": { …same as /api/allocation… },
  "findings": [
    { "rule": "AllocationDrift", "subject": "Equity", "severity": "Act",
      "evidence": { "actualPercent": 84.5, "targetPercent": 70.0, "driftPoints": 14.5, "bandPoints": 5.0 },
      "message": "Equity is 84.5% against a target of 70.0% (drift +14.5 points, band ±5.0).",
      "suggestion": "Rebalance toward target" }
  ],
  "summary": null,
  "ai": null,
  "disclaimer": "Information only, not investment advice. You make the decisions."
}
```
With `explain: true`, `summary` is the AI explanation and `ai` is `{ "provider": "Groq", "model": "…", "numberCheckPassed": true, "attempts": 1 }`. If the AI call fails, still return 200 with the findings, plus `summary: null` and `ai: { "error": "…short…" }`.

`message` is produced by a **C# template**, not the LLM.

---

## Ask

### `POST /api/ask`
```json
{ "conversationId": "optional-guid", "message": "Why is my debt allocation so low?" }
```
Response:
```json
{ "conversationId": "guid", "answer": "…ends with the disclaimer…",
  "toolsUsed": [ "ComputeAllocation", "GetPlan" ],
  "provider": "Groq", "model": "…", "numberCheckPassed": true }
```
Conversation history is kept in memory per `conversationId` (last 20 messages, 2-hour expiry).

---

## Maintenance (Development only)

### `POST /api/nav/import`
Body: `{ "path": "samples/amfi/NAVAll.sample.txt" }`. Imports a NAV file from disk (used by tests and mock mode; either AMFI layout). For the live file use NavSync. Returns `{ "imported": 12, "navDate": "2026-11-07" }`.

### `DELETE /api/dev/purge`
Invalidates the Kite token at Kite (best effort, like logout), then deletes every row of `KiteSessions`, `Plans`, `NavRecords`, `Snapshots` and `ReviewRuns` in one transaction, clears the in-memory caches (holdings, profile, Ask conversations) and runs the first-run seed again (the sample plan; in Mock mode the sample NAVs). Data Protection keys are kept. Returns the deleted counts:
```json
{ "kiteSessions": 1, "plans": 3, "navRecords": 18250, "snapshots": 4, "reviewRuns": 12, "reseeded": true }
```

### Mock-only: `POST /api/dev/scenario/{name}`
Only when `Kite:Mode=Mock`. Forwards to MockKite `POST /__scenario/{name}` and loads the scenario's plan into `Plans`, so the Evals can switch portfolios. Clears the holdings cache. Returns `{ "scenario": "demo", "today": "2026-11-10" }`; 404 for an unknown name.

In Mock mode the rules run **as of the current scenario's `today`** (12:00 IST), so a scenario gives the same findings on any calendar day. `asOfUtc` stays the real fetch time. Live mode uses the real clock.
