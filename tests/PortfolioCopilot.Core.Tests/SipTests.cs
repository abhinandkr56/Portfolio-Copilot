namespace PortfolioCopilot.Core.Tests;

public class SipTests
{
    [Theory]
    [InlineData("monthly", 1000, 1000)]
    [InlineData("weekly", 2000, 8666.67)]
    [InlineData("fortnightly", 1000, 2166.67)]
    [InlineData("quarterly", 3000, 1000)]
    [InlineData("Weekly", 1500, 6500)]
    public void Monthly_equivalent_per_frequency(string frequency, decimal amount, decimal expected) =>
        Assert.Equal(expected, PortfolioCalculator.MonthlyEquivalent(Build.Sip("X", amount, frequency)));

    [Fact]
    public void Unknown_frequency_counts_as_monthly_with_a_warning()
    {
        var warnings = new List<string>();

        Assert.Equal(500m, PortfolioCalculator.MonthlyEquivalent(Build.Sip("X", 500, "daily"), warnings));
        Assert.Single(warnings);
    }

    [Fact]
    public void Only_active_sips_count_toward_the_budget()
    {
        var input = Build.Input(stocks: [Build.Stock("A", 100)],
            sips: [Build.Sip("A", 10000), Build.Sip("B", 50000, status: "PAUSED"), Build.Sip("C", 50000, status: "CANCELLED")]);

        Assert.Equal(10000m, PortfolioCalculator.Compute(input).ActiveMonthlyEquivalent);
    }

    [Theory]
    [InlineData(20000, false)]   // exactly the budget → no finding
    [InlineData(20000.01, true)]
    public void SipOverBudget_is_strict(decimal active, bool fires)
    {
        var input = Build.Input(plan: Build.Plan(sipBudget: 20000), stocks: [Build.Stock("A", 100)], sips: [Build.Sip("A", active)]);

        Assert.Equal(fires, RulesEngine.Evaluate(input, Time.Default).Any(f => f.Rule == RuleNames.SipOverBudget));
    }

    [Fact]
    public void SipOverBudget_evidence_and_message()
    {
        var input = Build.Input(plan: Build.Plan(sipBudget: 20000), stocks: [Build.Stock("A", 100)],
            sips: [Build.Sip("A", 15000), Build.Sip("B", 2000, "weekly")]);

        var f = Assert.Single(RulesEngine.Evaluate(input, Time.Default), f => f.Rule == RuleNames.SipOverBudget);

        Assert.Equal(("SIPs", Severity.Watch), (f.Subject, f.Severity));
        Assert.Equal(["activeMonthlyEquivalent", "monthlyBudget"], f.Evidence.Keys);
        Assert.Equal(23666.67m, f.Evidence["activeMonthlyEquivalent"]);
        Assert.Equal("Active SIPs add up to ₹23,666.67 a month, above your budget of ₹20,000.", f.Message);
    }

    [Fact]
    public void SipNotActive_evidence_and_message()
    {
        var input = Build.Input(stocks: [Build.Stock("A", 100)], sips: [Build.Sip("Beta Fund", 5000, status: "PAUSED")]);

        var f = Assert.Single(RulesEngine.Evaluate(input, Time.Default), f => f.Rule == RuleNames.SipNotActive);

        Assert.Equal(["status", "instalmentAmount", "frequency"], f.Evidence.Keys);
        Assert.Equal("The monthly SIP of ₹5,000 in Beta Fund is PAUSED.", f.Message);
        Assert.Equal("Check SIP mandate", f.Suggestion);
    }

    private static readonly DateTimeOffset Now = Time.Default.GetUtcNow();

    [Theory]
    [InlineData(6.9, true)]
    [InlineData(7.1, false)]   // older than 7 days is ignored
    public void SipFailedOrder_window_is_seven_days(double daysAgo, bool fires)
    {
        var input = Build.Input(stocks: [Build.Stock("A", 100)], orders: [Build.Order("Alpha", Now.AddDays(-daysAgo))]);

        Assert.Equal(fires, RulesEngine.Evaluate(input, Time.Default).Any(f => f.Rule == RuleNames.SipFailedOrder));
    }

    [Theory]
    [InlineData("COMPLETE", "sip", false)]
    [InlineData("REJECTED", "regular", false)]
    [InlineData("CANCELLED", "sip", true)]
    public void SipFailedOrder_needs_a_failed_sip_order(string status, string variety, bool fires)
    {
        var input = Build.Input(stocks: [Build.Stock("A", 100)], orders: [Build.Order("Alpha", Now.AddDays(-1), status, variety)]);

        Assert.Equal(fires, RulesEngine.Evaluate(input, Time.Default).Any(f => f.Rule == RuleNames.SipFailedOrder));
    }

    [Fact]
    public void SipFailedOrder_groups_by_fund_with_count_and_latest()
    {
        var input = Build.Input(stocks: [Build.Stock("A", 100)],
            orders: [Build.Order("Alpha", Now.AddDays(-5)), Build.Order("Alpha", Now.AddDays(-1)), Build.Order("Alpha", Now.AddDays(-20))]);

        var f = Assert.Single(RulesEngine.Evaluate(input, Time.Default), f => f.Rule == RuleNames.SipFailedOrder);

        Assert.Equal(["failedCount", "lastMessage", "lastDate"], f.Evidence.Keys);
        Assert.Equal(2, f.Evidence["failedCount"]);
        Assert.Equal(new DateOnly(2026, 11, 9), f.Evidence["lastDate"]);
        Assert.Equal("2 SIP orders for Alpha failed in the last 7 days; the latest on 2026-11-09: SIP: Insufficient balance.", f.Message);
    }

    [Fact]
    public void Kite_ist_timestamps_are_stored_as_utc() =>
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 4, 30, 0, TimeSpan.Zero), Ist.ParseKiteTimestamp("2026-11-05 10:00:00"));
}

public class OrderingTests
{
    private static Finding F(string rule, string subject, Severity severity) =>
        new(rule, subject, severity, new Dictionary<string, object> { ["x"] = 1 }, "m", "s");

    [Fact]
    public void Severity_then_rule_order_then_subject()
    {
        var sorted = RulesEngine.Sort(
        [
            F(RuleNames.StaleNav, "NAV", Severity.Info),
            F(RuleNames.RegularPlan, "b", Severity.Watch),
            F(RuleNames.AllocationDrift, "Debt", Severity.Watch),
            F(RuleNames.RegularPlan, "B", Severity.Watch),
            F(RuleNames.StockConcentration, "INFY", Severity.Act),
            F(RuleNames.AllocationDrift, "Equity", Severity.Act),
        ]);

        Assert.Equal(
            ["AllocationDrift/Equity", "StockConcentration/INFY", "AllocationDrift/Debt", "RegularPlan/B", "RegularPlan/b", "StaleNav/NAV"],
            sorted.Select(f => $"{f.Rule}/{f.Subject}"));
    }
}
