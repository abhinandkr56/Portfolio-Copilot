using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Core.Tests;

public class CalculatorTests
{
    [Fact]
    public void T1_quantity_is_included_in_stock_value()
    {
        const string json = """
        { "name": "t1", "today": "2026-11-10",
          "plan": { "targetAllocation": { "Equity": 100 }, "driftBandPoints": 5, "maxSingleStockPercent": 100,
                    "maxSingleFundPercent": 100, "preferDirectPlans": true, "monthlySipBudget": 0,
                    "hybridEquityPercent": 65, "assetClassOverrides": {} },
          "kite": { "holdings": [ { "tradingsymbol": "INFY", "isin": "INETEST00001", "exchange": "NSE",
                                    "quantity": 10, "t1_quantity": 2, "average_price": 1400, "last_price": 1500, "pnl": 0 } ],
                    "mfHoldings": [], "mfSips": [], "mfOrders": [] },
          "expected": { "allocation": { "Equity": 100 }, "findings": [] } }
        """;
        var scenario = ScenarioParser.Parse(json, Samples.InstrumentsCsv, Samples.NavText);

        Assert.Equal(18000m, PortfolioCalculator.Compute(scenario.Input).Total);
    }

    [Fact]
    public void Last_price_zero_falls_back_to_nav()
    {
        var navs = new Dictionary<string, NavRecord> { ["INFTEST00001"] = new("INFTEST00001", "900001", "Alpha", "Equity Scheme", 50m, new DateOnly(2026, 11, 7)) };
        var fund = new FundHolding("INFTEST00001", "Alpha", "F1", 100, 40, 0, null, 0);

        var p = PortfolioCalculator.Compute(Build.Input(funds: [fund], navs: navs));

        var row = Assert.Single(p.Holdings);
        Assert.Equal(5000m, row.Value);
        Assert.True(row.UsedNavFallback);
        Assert.Equal(new DateOnly(2026, 11, 7), row.PriceDate);
    }

    [Fact]
    public void Missing_price_date_falls_back_to_nav_date()
    {
        var navs = new Dictionary<string, NavRecord> { ["INFTEST00001"] = new("INFTEST00001", "900001", "Alpha", "Equity Scheme", 50m, new DateOnly(2026, 11, 6)) };
        var fund = new FundHolding("INFTEST00001", "Alpha", "F1", 100, 40, 50, null, 0);

        var row = Assert.Single(PortfolioCalculator.Compute(Build.Input(funds: [fund], navs: navs)).Holdings);

        Assert.Equal(new DateOnly(2026, 11, 6), row.PriceDate);
        Assert.False(row.UsedNavFallback);
    }

    [Theory]
    [InlineData(65, 65, 35)]
    [InlineData(50, 50, 50)]
    [InlineData(80, 80, 20)]
    public void Hybrid_is_split_by_hybrid_equity_percent(decimal hybridEquity, decimal equityPct, decimal debtPct)
    {
        // INFTEST00007 is "balanced" in the instruments file → Hybrid.
        var input = Build.Input(plan: Build.Plan(hybridEquity: hybridEquity),
            funds: [Build.Fund("INFTEST00007", "Delta Balanced Advantage Fund - Direct Plan - Growth", 10000)]);

        var allocation = PortfolioCalculator.Compute(input).Allocation.ToDictionary(r => r.AssetClass, r => r.ActualPercent);

        Assert.Equal(equityPct, allocation[AssetClass.Equity]);
        Assert.Equal(debtPct, allocation[AssetClass.Debt]);
        Assert.DoesNotContain(AssetClass.Hybrid, allocation.Keys);
    }

    [Fact]
    public void Other_appears_only_when_actual_or_target_is_above_zero()
    {
        var without = PortfolioCalculator.Compute(Build.Input(stocks: [Build.Stock("INFY", 100)]));
        Assert.DoesNotContain(without.Allocation, r => r.AssetClass == AssetClass.Other);

        var withTarget = PortfolioCalculator.Compute(Build.Input(plan: Build.Plan(equity: 60, other: 10), stocks: [Build.Stock("INFY", 100)]));
        Assert.Contains(withTarget.Allocation, r => r.AssetClass == AssetClass.Other);

        var withValue = PortfolioCalculator.Compute(Build.Input(
            plan: Build.Plan(overrides: new() { ["XYZ"] = AssetClass.Other }), stocks: [Build.Stock("XYZ", 100)]));
        Assert.Contains(withValue.Allocation, r => r.AssetClass == AssetClass.Other && r.ActualPercent == 100);
    }

    [Fact]
    public void Percentages_keep_full_precision()
    {
        var p = PortfolioCalculator.Compute(Build.Input(stocks: [Build.Stock("A", 1), Build.Stock("B", 2)]));

        Assert.Equal(1m / 3m * 100, p.Holdings[0].PercentOfTotal);
    }

    [Fact]
    public void Total_zero_gives_no_allocation_and_a_single_empty_portfolio_finding()
    {
        var input = Build.Input(stocks: [Build.Stock("INFY", 0)], sips: [Build.Sip("X", 1000, status: "PAUSED")]);

        Assert.Empty(PortfolioCalculator.Compute(input).Allocation);
        var finding = Assert.Single(RulesEngine.Evaluate(input, Time.Default));
        Assert.Equal((RuleNames.EmptyPortfolio, "Portfolio", Severity.Info), (finding.Rule, finding.Subject, finding.Severity));
    }
}
