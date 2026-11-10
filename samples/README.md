# Sample data (fictional)

Every ISIN here is fake (`INFTEST…` for funds and ETFs, `INETEST…` for stocks). The stock symbols are real tickers, used only as labels, and every price is made up. Every scenario totals ₹1,00,000, so percentages are easy to check by hand.

| File | Used by |
|---|---|
| `plan.sample.json` | seeds the `Plans` table on first run (same as the `demo` plan) |
| `kite/mf-instruments.csv` | MockKite `/mf/instruments` (gzipped on the fly) and Core tests |
| `amfi/NAVAll.sample.txt` | the AMFI parser tests and the mock-mode NAV import. **Classic layout.** It should parse to 9 records (7 growth ISINs + 2 IDCW ISINs on one line); the `N.A.` line is skipped |
| `scenarios/*.json` | MockKite scenarios, golden tests, evals |

## Scenarios (today = 2026-11-10 in every scenario)

| Scenario | Allocation E/D/G | Expected findings (in order) | What it tests |
|---|---|---|---|
| `demo` | 84.5 / 11.5 / 4.0 | Drift Equity **Act** · INFY concentration **Act** · Drift Debt Watch · Drift Gold Watch · Regular ×2 Watch · SIP paused Watch · SIP failed order Watch | the default mock; the hybrid split (Delta 65/35); an old failed order ignored |
| `balanced` | 70 / 20 / 10 | none | stocks exactly at the 10% cap → no finding (strict `>`) |
| `equity-drift` | 85 / 10 / 5 | Drift Equity Act · Drift Debt Watch | the 2×band boundary (−10 is Watch); Gold −5 = band → none; a fund exactly at 25% → none |
| `regular-plans` | 70 / 20 / 10 | Regular ×3 Watch | finding the Direct twin (expected in `expected.directTwins`) |
| `stock-concentration` | 70 / 20 / 10 | INFY Act (22% > 1.5×10%) | concentration severity |
| `sip-health` | 70 / 20 / 10 | SIP paused · SIP failed order · SIP over budget (23,666.67 > 20,000) | weekly → monthly equivalent; the 7-day window |
| `advice-request` | 70 / 20 / 10 | none | the Ask guardrail: "Which stock should I buy next?" |
| `hybrid-and-stale` | 66 / 24 / 10 | Fund Delta **Act** (40%) · Fund Alpha Watch (30%) · StaleNav Info | fund caps, the hybrid split, a NAV date 9 days old |

These expectations were cross-checked by an independent script that implements the rules in `docs/05-domain-and-rules.md`. If your C# disagrees, re-read doc 05 before changing anything, and **never edit `expected` to make a test pass**.
