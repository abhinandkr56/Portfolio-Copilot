namespace PortfolioCopilot.Core.Tests;

public class RuleTests
{
    private static IReadOnlyList<Finding> Run(PortfolioInput input) => RulesEngine.Evaluate(input, Time.Default);

    private static Finding? One(PortfolioInput input, string rule, string subject) =>
        Run(input).SingleOrDefault(f => f.Rule == rule && f.Subject == subject);

    /// <summary>Equity stocks worth <paramref name="equity"/>, debt fund worth the rest of 100, plan Equity 70 / Debt 30.</summary>
    private static PortfolioInput Drift(decimal equity) => Build.Input(
        plan: Build.Plan(equity: 70, debt: 30, gold: 0, stockCap: 100, fundCap: 100),
        stocks: [Build.Stock("A", equity)],
        funds: [Build.Fund("INFTEST00005", "Gamma Short Duration Fund - Direct Plan - Growth", 100 - equity)]);

    [Theory]
    [InlineData(75, null)]              // drift exactly the band → nothing
    [InlineData(75.01, Severity.Watch)]
    [InlineData(80, Severity.Watch)]     // exactly 2 × band → still Watch
    [InlineData(80.01, Severity.Act)]
    [InlineData(65, null)]
    [InlineData(59.99, Severity.Act)]
    public void AllocationDrift_thresholds(decimal equity, Severity? expected) =>
        Assert.Equal(expected, One(Drift(equity), RuleNames.AllocationDrift, "Equity")?.Severity);

    [Fact]
    public void AllocationDrift_evidence_and_message()
    {
        var f = One(Drift(84.46m), RuleNames.AllocationDrift, "Equity")!;

        Assert.Equal(["actualPercent", "targetPercent", "driftPoints", "bandPoints"], f.Evidence.Keys);
        Assert.Equal(84.5m, f.Evidence["actualPercent"]);
        Assert.Equal(14.5m, f.Evidence["driftPoints"]);
        Assert.Equal("Equity is 84.5% against a target of 70.0% (drift +14.5 points, band ±5.0).", f.Message);
        Assert.Equal("Rebalance toward target", f.Suggestion);
    }

    [Fact]
    public void AllocationDrift_compares_unrounded_values()
    {
        // 75.04 rounds to 75.0 for display but is above the band.
        var f = One(Drift(75.04m), RuleNames.AllocationDrift, "Equity")!;

        Assert.Equal(Severity.Watch, f.Severity);
        Assert.Equal("Equity is 75.0% against a target of 70.0% (drift +5.0 points, band ±5.0).", f.Message);
    }

    [Fact]
    public void AllocationDrift_checks_other_when_target_is_set()
    {
        var input = Build.Input(plan: Build.Plan(equity: 60, debt: 20, gold: 10, other: 10, stockCap: 100, fundCap: 100),
            stocks: [Build.Stock("A", 60), Build.Stock("GOLDBEES", 10)],
            funds: [Build.Fund("INFTEST00005", "Gamma Short Duration Fund - Direct Plan - Growth", 30)]);

        var f = One(input, RuleNames.AllocationDrift, "Other");

        Assert.Equal(Severity.Watch, f?.Severity); // actual 0 vs target 10: |−10| = 2 × band, not above it → Watch
    }

    /// <summary>One stock at <paramref name="pct"/>% of a 100-rupee portfolio, cap 10.</summary>
    private static PortfolioInput Stock(decimal pct) => Build.Input(
        plan: Build.Plan(equity: 100, debt: 0, gold: 0, band: 20, fundCap: 100),
        stocks: [Build.Stock("INFY", pct), Build.Stock("REST", 100 - pct)]);

    [Theory]
    [InlineData(10, null)]
    [InlineData(10.01, Severity.Watch)]
    [InlineData(15, Severity.Watch)]
    [InlineData(15.01, Severity.Act)]
    public void StockConcentration_thresholds(decimal pct, Severity? expected) =>
        Assert.Equal(expected, One(Stock(pct), RuleNames.StockConcentration, "INFY")?.Severity);

    [Fact]
    public void StockConcentration_evidence_and_message()
    {
        var f = One(Stock(18), RuleNames.StockConcentration, "INFY")!;

        Assert.Equal(["percentOfTotal", "capPercent", "value"], f.Evidence.Keys);
        Assert.Equal(18.0m, f.Evidence["percentOfTotal"]);
        Assert.Equal(18m, f.Evidence["value"]);
        Assert.Equal("INFY is 18.0% of the portfolio; your cap is 10.0%.", f.Message);
        Assert.Equal("Review", f.Suggestion);
    }

    [Fact]
    public void StockConcentration_ignores_non_equity_stocks()
    {
        var input = Build.Input(plan: Build.Plan(band: 100, fundCap: 100), stocks: [Build.Stock("GOLDBEES", 50), Build.Stock("A", 5), Build.Stock("B", 5), Build.Stock("C", 5), Build.Stock("D", 5), Build.Stock("E", 5), Build.Stock("F", 5), Build.Stock("G", 5), Build.Stock("H", 5), Build.Stock("I", 5), Build.Stock("J", 5)]);

        Assert.DoesNotContain(Run(input), f => f.Rule == RuleNames.StockConcentration);
    }

    /// <summary>One fund at <paramref name="pct"/>% of the portfolio, cap 25.</summary>
    private static PortfolioInput Fund(decimal pct) => Build.Input(
        plan: Build.Plan(equity: 100, debt: 0, gold: 0, band: 20, stockCap: 100),
        stocks: [Build.Stock("REST", 100 - pct)],
        funds: [Build.Fund("INFTEST00001", "Alpha Flexi Cap Fund - Direct Plan - Growth", pct)]);

    [Theory]
    [InlineData(25, null)]
    [InlineData(25.01, Severity.Watch)]
    [InlineData(37.5, Severity.Watch)]
    [InlineData(37.51, Severity.Act)]
    public void FundConcentration_thresholds(decimal pct, Severity? expected) =>
        Assert.Equal(expected, One(Fund(pct), RuleNames.FundConcentration, "Alpha Flexi Cap Fund - Direct Plan - Growth")?.Severity);

    [Fact]
    public void FundConcentration_evidence_and_message()
    {
        var f = One(Fund(40), RuleNames.FundConcentration, "Alpha Flexi Cap Fund - Direct Plan - Growth")!;

        Assert.Equal(["percentOfTotal", "capPercent", "value"], f.Evidence.Keys);
        Assert.Equal("Alpha Flexi Cap Fund - Direct Plan - Growth is 40.0% of the portfolio; your cap is 25.0%.", f.Message);
    }

    [Fact]
    public void FundConcentration_adds_up_folios_of_the_same_fund()
    {
        var input = Build.Input(plan: Build.Plan(equity: 100, debt: 0, gold: 0, band: 20, stockCap: 100),
            stocks: [Build.Stock("REST", 70)],
            funds: [Build.Fund("INFTEST00001", "Alpha Flexi Cap Fund - Direct Plan - Growth", 15), Build.Fund("INFTEST00001", "Alpha Flexi Cap Fund - Direct Plan - Growth", 15)]);

        Assert.Equal(Severity.Watch, One(input, RuleNames.FundConcentration, "Alpha Flexi Cap Fund - Direct Plan - Growth")?.Severity);
    }

    [Fact]
    public void RegularPlan_with_twin_evidence_and_message()
    {
        var input = Build.Input(funds: [Build.Fund("INFTEST00002", "Alpha Flexi Cap Fund - Regular Plan - Growth", 100)]);

        var f = One(input, RuleNames.RegularPlan, "Alpha Flexi Cap Fund - Regular Plan - Growth")!;

        Assert.Equal(Severity.Watch, f.Severity);
        Assert.Equal(["isin", "directTwinIsin", "directTwinName"], f.Evidence.Keys);
        Assert.Equal("INFTEST00001", f.Evidence["directTwinIsin"]);
        Assert.Equal("Alpha Flexi Cap Fund - Regular Plan - Growth is a Regular plan. A Direct plan of the same scheme exists: Alpha Flexi Cap Fund - Direct Plan - Growth.", f.Message);
        Assert.Equal("Consider Direct plan", f.Suggestion);
    }

    [Fact]
    public void RegularPlan_is_off_when_plan_does_not_prefer_direct()
    {
        var input = Build.Input(plan: Build.Plan(preferDirect: false), funds: [Build.Fund("INFTEST00002", "Alpha Flexi Cap Fund - Regular Plan - Growth", 100)]);

        Assert.DoesNotContain(Run(input), f => f.Rule == RuleNames.RegularPlan);
    }

    [Fact]
    public void StaleNav_counts_funds_older_than_the_stale_days()
    {
        // Today 2026-11-10 IST, NavStaleDays 4 → cutoff 2026-11-06. 11-06 is not stale, 11-05 and 11-01 are.
        var input = Build.Input(plan: Build.Plan(band: 100, stockCap: 100, fundCap: 100), funds:
        [
            Build.Fund("INFTEST00001", "A", 10, new DateOnly(2026, 11, 6)),
            Build.Fund("INFTEST00003", "B", 10, new DateOnly(2026, 11, 5)),
            Build.Fund("INFTEST00005", "C", 10, new DateOnly(2026, 11, 1)),
        ]);

        var f = One(input, RuleNames.StaleNav, "NAV")!;

        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal(2, f.Evidence["staleFunds"]);
        Assert.Equal(new DateOnly(2026, 11, 1), f.Evidence["oldestDate"]);
        Assert.Equal("2 fund prices are older than 4 days; the oldest is from 2026-11-01.", f.Message);
    }

    [Fact]
    public void StaleNav_uses_the_ist_date()
    {
        // 2026-11-10 20:00 UTC is already 2026-11-11 in IST → cutoff 11-07, so 11-06 becomes stale.
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 20, 0, 0, TimeSpan.Zero));
        var input = Build.Input(plan: Build.Plan(band: 100, fundCap: 100), funds: [Build.Fund("INFTEST00001", "A", 10, new DateOnly(2026, 11, 6))]);

        Assert.Contains(RulesEngine.Evaluate(input, time), f => f.Rule == RuleNames.StaleNav);
    }
}
