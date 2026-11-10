namespace PortfolioCopilot.Core.Tests;

public class GoldenScenarioTests
{
    public static TheoryData<string> Names => new(Samples.ScenarioNames);

    [Fact]
    public void All_eight_scenarios_are_present() => Assert.Equal(8, Samples.ScenarioNames.Count());

    [Theory]
    [MemberData(nameof(Names))]
    public void Findings_equal_expected_in_order(string name)
    {
        var scenario = Samples.Scenario(name);

        var findings = RulesEngine.Evaluate(scenario.Input, Time.At(scenario.Today));

        var actual = findings.Select(f => (f.Rule, f.Subject, f.Severity)).ToList();
        var expected = scenario.Expected.Findings.Select(f => (f.Rule, f.Subject, f.Severity)).ToList();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Allocation_matches_expected_to_one_dp(string name)
    {
        var scenario = Samples.Scenario(name);

        var allocation = PortfolioCalculator.Compute(scenario.Input).Allocation
            .ToDictionary(r => r.AssetClass, r => Math.Round(r.ActualPercent, 1, MidpointRounding.AwayFromZero));

        Assert.Equal(scenario.Expected.Allocation.OrderBy(kv => kv.Key), allocation.OrderBy(kv => kv.Key));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Every_finding_carries_evidence_message_and_suggestion(string name)
    {
        var scenario = Samples.Scenario(name);

        foreach (var f in RulesEngine.Evaluate(scenario.Input, Time.At(scenario.Today)))
        {
            Assert.NotEmpty(f.Evidence);
            Assert.False(string.IsNullOrWhiteSpace(f.Message));
            Assert.False(string.IsNullOrWhiteSpace(f.Suggestion));
        }
    }

    [Fact]
    public void Regular_plans_scenario_finds_each_direct_twin()
    {
        var scenario = Samples.Scenario("regular-plans");

        var twins = RulesEngine.Evaluate(scenario.Input, Time.At(scenario.Today))
            .Where(f => f.Rule == RuleNames.RegularPlan)
            .ToDictionary(f => f.Subject, f => (string)f.Evidence["directTwinIsin"]);

        Assert.Equal(scenario.Expected.DirectTwins!.OrderBy(kv => kv.Key), twins.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void Sip_health_active_monthly_equivalent_matches()
    {
        var scenario = Samples.Scenario("sip-health");

        Assert.Equal(scenario.Expected.ActiveMonthlyEquivalent, PortfolioCalculator.Compute(scenario.Input).ActiveMonthlyEquivalent);
    }

    [Fact]
    public void Demo_messages_are_exact()
    {
        var scenario = Samples.Scenario("demo");

        var messages = RulesEngine.Evaluate(scenario.Input, Time.At(scenario.Today)).Select(f => f.Message).ToList();

        Assert.Equal(
        [
            "Equity is 84.5% against a target of 70.0% (drift +14.5 points, band ±5.0).",
            "INFY is 18.0% of the portfolio; your cap is 10.0%.",
            "Debt is 11.5% against a target of 20.0% (drift -8.5 points, band ±5.0).",
            "Gold is 4.0% against a target of 10.0% (drift -6.0 points, band ±5.0).",
            "Alpha Flexi Cap Fund - Regular Plan - Growth is a Regular plan. A Direct plan of the same scheme exists: Alpha Flexi Cap Fund - Direct Plan - Growth.",
            "Gamma Short Duration Fund - Regular Plan - Growth is a Regular plan. A Direct plan of the same scheme exists: Gamma Short Duration Fund - Direct Plan - Growth.",
            "The monthly SIP of ₹5,000 in Beta Large Cap Fund - Direct Plan - Growth is PAUSED.",
            "1 SIP order for Alpha Flexi Cap Fund - Regular Plan - Growth failed in the last 7 days; the latest on 2026-11-05: SIP: Insufficient balance.",
        ], messages);
    }
}
