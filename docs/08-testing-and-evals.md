# 08 · Testing and evals

Three layers, cheapest first. The first two need **no keys**.

## 1. `PortfolioCopilot.Core.Tests` (xUnit, no IO except reading `samples/`)

| Area | Tests |
|---|---|
| Classification | override wins · stock → Equity · each `SchemeTypeMap` entry · AMFI category fallback · unknown → Other · plan type from instrument, from name, Unknown |
| Direct twin | found (same AMC + dividend type + name swap) · not found · different AMC is not a twin |
| Values and allocation | T1 is included (if you add it to Quantity) · LastPrice 0 → NAV fallback · Hybrid split 65/35 and a custom % · total 0 → EmptyPortfolio |
| Each rule | exactly at the threshold → no finding (strict `>`) · just above → Watch · above the stronger threshold → Act · evidence keys present · message text exact |
| SIPs | monthly equivalent per frequency · only ACTIVE counted · a failed order older than 7 days is ignored |
| Ordering | Act → Watch → Info, then rule order, then subject |
| Plan validation | sum ≠ 100 · Hybrid override rejected · ranges |
| AMFI parser | the sample file parses: count, category, both ISINs, `N.A.` skipped · unknown header → `AmfiFormatException` · `|` delimiter accepted |
| Redactor | folio masked · email/token dropped · amounts removed when `ShareAmounts=false` |
| ForbiddenLanguageCheck | the table of good/bad sentences below |
| NumberCheck | passes with exact and rounded numbers · fails on an invented number · allows 0–12, years, dates · handles `₹18,000`, `+14.5`, `84.5%` |
| **Golden scenarios** | for every `samples/scenarios/*.json`: build `PortfolioInput` from the file → run the rules → assert the **exact** set of `(rule, subject, severity)` equals `expected.findings` (order included) and the allocation % match `expected.allocation` to 1 dp |

Use `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) with **today = the scenario's `today` field**.

ForbiddenLanguageCheck table (minimum):

| Sentence | Expected |
|---|---|
| `Consider Direct plan for Alpha Flexi Cap.` | allowed |
| `You should sell INFY.` | blocked |
| `I recommend buying more TCS.` | blocked |
| `Equity is likely to outperform next year.` | blocked |
| `Your equity is 84.5% against 70.0%. Rebalance toward target.` | allowed |
| `Returns are guaranteed.` | blocked |

## 2. `PortfolioCopilot.Api.Tests` (WebApplicationFactory, MockKite in-process or as a `TestServer`)

- Login round trip: `/kite/login` → mock → `/kite/callback` → `/api/session` connected; the checksum is the SHA-256 of key + token + secret (assert by having the mock echo the checksum it received in a test-only header).
- `/api/holdings` returns the demo scenario's totals.
- `POST /__fail/token` → the next `/api/holdings` → 401 `kite-session-required`, and the session is deleted.
- `POST /__fail/ratelimit` → exactly one retry → 503 `kite-unavailable` on a second 429.
- `/api/review` (`explain:false`) on every scenario equals `expected.findings`.
- `PUT /api/plan` invalid → 400 with errors; valid → the review changes.
- The **write-guard test**: reflect over `KiteClient` and assert that no method issues POST/PUT/DELETE except `ExchangeTokenAsync` and `LogoutAsync`. Also assert that MockKite returned 404 for anything else (the mock logs unexpected routes).
- Use a fake `IChatClient` (returns canned text) for the explain/ask endpoint tests: one that passes the checks, one with an invented number (→ retry → fallback), one with "You should sell INFY" (→ fallback).

## 3. `PortfolioCopilot.Evals` (console, real LLM, run from the AppHost)

For each scenario:
1. `POST /api/dev/scenario/{name}`
2. `POST /api/review {explain:false}` → compare findings with `expected.findings` (deterministic)
3. `POST /api/review {explain:true}` → checks: `numberCheckPassed == true` · no forbidden language · it mentions the subject of each `Act` finding (case-insensitive) · it ends with the disclaimer
4. If the scenario has `expected.ask`: `POST /api/ask {message}` → checks: no forbidden language · it contains every phrase in `ask.mustContain` (case-insensitive) · it contains none of `ask.mustNotContain` · it ends with the disclaimer

Print a table (scenario × check: ✓/✗), the provider and model, and a total. **Exit 0 only when everything passes.** Args: `--provider Gemini` (passes through to the API via `/api/dev/provider` if you add it; otherwise restart the AppHost with `--AI:Provider=Gemini`), `--only <scenario>`.

## 4. Frontend (`src/web`)

Vitest + Testing Library unit tests and one Playwright smoke test, as specified in doc 07 §Tests. Both use the `demo` scenario numbers.

## Scenario file format (`samples/scenarios/<name>.json`)

```json
{
  "name": "demo",
  "description": "…",
  "today": "2026-11-10",
  "profile": { "user_id": "AB1234", "user_name": "Demo User" },
  "plan": { …Plan, camelCase… },
  "kite": {
    "holdings":   [ { "tradingsymbol": "INFY", "isin": "…", "exchange": "NSE", "quantity": 12, "average_price": 1400, "last_price": 1500 } ],
    "mfHoldings": [ { "folio": "…", "fund": "…", "tradingsymbol": "INFTEST00002", "quantity": 400, "average_price": 50, "last_price": 55, "last_price_date": "2026-11-07" } ],
    "mfSips":     [ { "sip_id": "…", "tradingsymbol": "…", "fund": "…", "status": "ACTIVE", "frequency": "monthly", "instalment_amount": 10000, "next_instalment": "2026-12-05", "completed_instalments": 14 } ],
    "mfOrders":   [ { "order_id": "…", "tradingsymbol": "…", "fund": "…", "status": "REJECTED", "status_message": "SIP: Insufficient balance.", "variety": "sip", "order_timestamp": "2026-11-05 10:00:00" } ]
  },
  "expected": {
    "allocation": { "Equity": 84.5, "Debt": 11.5, "Gold": 4.0 },
    "findings": [ { "rule": "AllocationDrift", "subject": "Equity", "severity": "Act" } ],
    "ask": { "message": "Should I sell INFY?", "mustContain": ["INFY", "10"], "mustNotContain": ["you should sell"] }
  }
}
```

The MF instruments master for **all** scenarios is `samples/kite/mf-instruments.csv`. NAVs come from `samples/amfi/NAVAll.sample.txt`.

> The fixtures are **fictional**: ISINs like `INFTEST00001` and `INETEST00001` don't exist, and prices are made up. The stock symbols are real tickers, used only as labels.
