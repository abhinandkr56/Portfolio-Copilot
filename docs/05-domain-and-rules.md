# 05 · Domain model and rules (`PortfolioCopilot.Core`)

Core is **pure**: no HTTP, no EF Core, no `DateTime.Now`. Time comes in through `TimeProvider`, and data comes in as records. That makes every rule unit-testable and lets the golden scenarios run without the API.

## Records

```csharp
public enum AssetClass { Equity, Debt, Gold, Other, Hybrid /* input only: split before allocation */ }
public enum PlanType   { Direct, Regular, Unknown }
public enum Severity   { Act, Watch, Info }          // sort order: Act first

public record StockHolding(string Symbol, string Isin, string Exchange, decimal Quantity,
    decimal AveragePrice, decimal LastPrice, decimal Pnl);            // Quantity already includes T1

public record FundHolding(string Isin, string Fund, string Folio, decimal Quantity,
    decimal AveragePrice, decimal LastPrice, DateOnly? LastPriceDate, decimal Pnl);

public record Sip(string SipId, string Isin, string Fund, string Status, string Frequency,
    decimal InstalmentAmount, DateOnly? NextInstalment, int CompletedInstalments);

public record MfOrder(string OrderId, string Isin, string Fund, string Status, string StatusMessage,
    string Variety, DateTimeOffset OrderTimestamp);

public record MfInstrument(string Isin, string Amc, string Name, string DividendType,
    string SchemeType, string Plan);                                   // Plan: "direct" | "regular"

public record NavRecord(string Isin, string SchemeCode, string SchemeName, string Category,
    decimal Nav, DateOnly NavDate);

public record Plan(
    Dictionary<AssetClass, decimal> TargetAllocation,   // Equity/Debt/Gold/Other, sums to 100
    decimal DriftBandPoints,
    decimal MaxSingleStockPercent,
    decimal MaxSingleFundPercent,
    bool PreferDirectPlans,
    decimal MonthlySipBudget,
    decimal HybridEquityPercent,                        // default 65
    Dictionary<string, AssetClass> AssetClassOverrides, // key: stock symbol or ISIN
    string? Notes);

public record PortfolioInput(Plan Plan, IReadOnlyList<StockHolding> Stocks,
    IReadOnlyList<FundHolding> Funds, IReadOnlyList<Sip> Sips, IReadOnlyList<MfOrder> MfOrders,
    IReadOnlyDictionary<string, MfInstrument> Instruments, IReadOnlyDictionary<string, NavRecord> Navs);

public record Finding(string Rule, string Subject, Severity Severity,
    IReadOnlyDictionary<string, object> Evidence, string Message, string Suggestion);
```

Contract DTOs for the API (`Contracts/`) mirror doc 03 exactly.

## Classification (in this order; the first match wins)

**Asset class**
1. `Plan.AssetClassOverrides[symbol or isin]`
2. Stocks → `Equity`
3. Funds → `Instruments[isin].SchemeType` through `Classification:SchemeTypeMap` (case-insensitive)
4. Funds → the AMFI `Category` text: contains `Equity Scheme` → Equity; `Debt Scheme` or `Income/Debt Oriented Scheme` → Debt; `Hybrid Scheme` → Hybrid; `Gold` or `FoF` with "gold" in the scheme name → Gold
5. Otherwise `Other` (with a warning logged once per ISIN)

`Hybrid` is never shown. Split its value into Equity (`HybridEquityPercent`) and Debt (the rest) **before** computing the allocation. The holdings table shows "Hybrid (65/35)".

**Plan type (funds)**
1. `Instruments[isin].Plan` (`direct` / `regular`)
2. Fund name contains `Direct` → Direct, `Regular` → Regular (case-insensitive)
3. `Unknown`

**Direct twin of a Regular fund:** an instrument with the same `Amc` and the same `DividendType`, whose name equals the Regular name with `Regular` replaced by `Direct` (case-insensitive, whitespace-normalised). If none is found, there's no twin (the finding still fires).

## Values

- Stock value = `Quantity × LastPrice`
- Fund value = `Quantity × LastPrice`. If `LastPrice == 0`, use `Navs[isin].Nav`.
- Total = the sum of all values. If total = 0, return no allocation and a single Info finding `EmptyPortfolio`.
- Percent = `value / total × 100` (full precision). Round to 1 dp **for display and evidence only**. Compare thresholds against the **unrounded** value.

## Rules

Thresholds use strict `>`. Severity levels: **Watch** when the threshold is crossed, **Act** when the stronger threshold is crossed.

| Rule | Subject | Fires when | Severity | Evidence keys | Suggestion |
|---|---|---|---|---|---|
| `AllocationDrift` | asset class | `abs(actual − target) > band` | Act if `> 2 × band`, else Watch | `actualPercent, targetPercent, driftPoints (signed), bandPoints` | `Rebalance toward target` |
| `StockConcentration` | symbol | an **Equity-class** stock's % of total `> MaxSingleStockPercent` | Act if `> 1.5 × cap`, else Watch | `percentOfTotal, capPercent, value` | `Review` |
| `FundConcentration` | fund name | a fund's % of total `> MaxSingleFundPercent` | Act if `> 1.5 × cap`, else Watch | `percentOfTotal, capPercent, value` | `Review` |
| `RegularPlan` | fund name | `PreferDirectPlans` and plan type = Regular | Watch | `isin, directTwinIsin?, directTwinName?` | `Consider Direct plan` |
| `SipNotActive` | fund name | SIP status ≠ `ACTIVE` | Watch | `status, instalmentAmount, frequency` | `Check SIP mandate` |
| `SipFailedOrder` | fund name | ≥ 1 MF order with `Variety == "sip"` and status in (`REJECTED`, `CANCELLED`) in the last 7 days | Watch | `failedCount, lastMessage, lastDate` | `Check SIP mandate` |
| `SipOverBudget` | `SIPs` | active monthly equivalent `> MonthlySipBudget` | Watch | `activeMonthlyEquivalent, monthlyBudget` | `Review` |
| `StaleNav` | `NAV` | any fund's price date is older than `NavStaleDays` days before today (IST) | Info (one finding listing the count) | `staleFunds, oldestDate` | `Review` |
| `EmptyPortfolio` | `Portfolio` | total = 0 | Info | `{}` | `Review` |

**AllocationDrift classes:** check Equity, Debt and Gold always, and Other whenever its actual or target is > 0 (a missing target counts as 0).

**Monthly equivalent of a SIP** (ACTIVE only): monthly ×1 · weekly ×52/12 · fortnightly ×26/12 · quarterly ÷3 · anything else ×1, logged as a warning. Round to 2 dp.

**Ordering:** Severity (Act, Watch, Info) → the rule order in the table above → Subject (ordinal).

**Messages** are built from C# templates, for example:
- AllocationDrift: `"{Class} is {actual:0.0}% against a target of {target:0.0}% (drift {drift:+0.0;-0.0} points, band ±{band:0.0})."`
- StockConcentration: `"{Symbol} is {pct:0.0}% of the portfolio; your cap is {cap:0.0}%."`
- RegularPlan: `"{Fund} is a Regular plan."` + (twin ? `" A Direct plan of the same scheme exists: {twin}."` : `""`)

All messages use `CultureInfo.InvariantCulture` and the ₹ symbol for amounts (`₹18,000`, Indian digit grouping optional: record your choice).

## Plan validation (`PUT /api/plan`)

- The `TargetAllocation` keys are a subset of Equity/Debt/Gold/Other; values are ≥ 0; the sum is 100 ± 0.01
- `DriftBandPoints` 1–20 · each cap 1–100 · `MonthlySipBudget` ≥ 0 · `HybridEquityPercent` 0–100
- Override values must be Equity/Debt/Gold/Other (not Hybrid)

## AMFI NAV parser

Two layouts are recognised **by their exact header** (column names, case-insensitive). Anything else throws.

**Classic** (6 columns; sample in `samples/amfi/NAVAll.sample.txt`):

```
Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Net Asset Value;Date

Open Ended Schemes(Equity Scheme - Flexi Cap Fund)

Alpha Mutual Fund

900001;INFTEST00001;-;Alpha Flexi Cap Fund - Direct Plan - Growth;56.1234;07-Nov-2026
```

**Live since 2026** (8 columns: `Plan` and `Option` split out of the name; checked 2026-11-14 at `https://portal.amfiindia.com/spages/NAVAll.txt`; trimmed real sample in `samples/amfi/NAVAll.live-2026-11-13.sample.txt`):

```
Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Plan;Option;Net Asset Value;Date

Open Ended Schemes(Children’s Fund - Childrens' Fund)

Axis Mutual Fund

135762;INF846K01WO1;-;Axis Children's Fund;Direct Plan;Growth Option;31.0814;13-Nov-2026
133776;INF178L01BU8;-;Kotak Corporate Bond Fund;;;1052.7994;21-Oct-2016
```

- A line starting with `Scheme Code` is the header; detect the delimiter from it (`;` or `|`), then match the columns against the two layouts.
- A line containing `Schemes(` sets the current **Category**: the text between `Schemes(` and the **last** `)` (the live file has `(Exchange Traded Funds (ETFs) - Gold ETF)`).
- A non-empty line without the delimiter is the current **AMC** (unused for now).
- Data line: as many fields as the header. ISIN 1 and ISIN 2 (trimmed) each produce a record when they look like ISINs (12 chars, start with `IN`). NAV that isn't a number (`N.A.`) → skip. Date format `dd-MMM-yyyy`, invariant culture.
- 8-column layout: `SchemeName` = `Name - Plan - Option`, leaving out empty parts, so names read as in the classic file (and the "Direct"/"Regular" fallback still works).
- The live file uses CRLF line endings and whitespace-only blank lines; lists a few ISINs twice (`LatestPerIsin` keeps the latest NAV date); and has NAV dates a day or two **ahead** for liquid/overnight funds, which publish weekend NAVs early.
- **Unrecognised header** → `AmfiFormatException("Unrecognised AMFI header: …")`. Don't guess; add the new layout with a trimmed real sample and a test.
