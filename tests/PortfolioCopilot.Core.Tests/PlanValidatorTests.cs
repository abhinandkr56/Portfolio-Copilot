namespace PortfolioCopilot.Core.Tests;

public class PlanValidatorTests
{
    [Fact]
    public void Sample_plan_is_valid()
    {
        var plan = Testing.ScenarioParser.ParsePlan(Samples.Read("plan.sample.json"));

        Assert.Empty(PlanValidator.Validate(plan));
    }

    [Theory]
    [InlineData(70, 20, 9)]
    [InlineData(70, 20, 10.02)]
    public void Targets_must_sum_to_100(decimal e, decimal d, decimal g) =>
        Assert.Contains("targetAllocation", PlanValidator.Validate(Build.Plan(e, d, g)).Keys);

    [Fact]
    public void Sum_within_one_hundredth_is_accepted() =>
        Assert.Empty(PlanValidator.Validate(Build.Plan(70, 20, 10.01m)));

    [Fact]
    public void Negative_target_is_rejected() =>
        Assert.Contains("Gold must be 0 or more.", PlanValidator.Validate(Build.Plan(90, 20, -10))["targetAllocation"]);

    [Fact]
    public void Hybrid_target_is_rejected()
    {
        var plan = Build.Plan(70, 20, 0);
        plan.TargetAllocation[AssetClass.Hybrid] = 10;

        Assert.Contains("targetAllocation", PlanValidator.Validate(plan).Keys);
    }

    [Fact]
    public void Hybrid_override_is_rejected() =>
        Assert.Equal(["XYZ: Hybrid is not allowed; use Equity, Debt, Gold or Other."],
            PlanValidator.Validate(Build.Plan(overrides: new() { ["XYZ"] = AssetClass.Hybrid }))["assetClassOverrides"]);

    [Theory]
    [InlineData(0.9, 10, 25, 0, 65, "driftBandPoints")]
    [InlineData(20.1, 10, 25, 0, 65, "driftBandPoints")]
    [InlineData(5, 0.5, 25, 0, 65, "maxSingleStockPercent")]
    [InlineData(5, 10, 101, 0, 65, "maxSingleFundPercent")]
    [InlineData(5, 10, 25, -1, 65, "monthlySipBudget")]
    [InlineData(5, 10, 25, 0, 100.1, "hybridEquityPercent")]
    [InlineData(5, 10, 25, 0, -0.1, "hybridEquityPercent")]
    public void Ranges(decimal band, decimal stockCap, decimal fundCap, decimal budget, decimal hybrid, string field)
    {
        var errors = PlanValidator.Validate(Build.Plan(band: band, stockCap: stockCap, fundCap: fundCap, sipBudget: budget, hybridEquity: hybrid));

        Assert.Equal([field], errors.Keys);
    }

    [Fact]
    public void Range_edges_are_valid() =>
        Assert.Empty(PlanValidator.Validate(Build.Plan(band: 20, stockCap: 1, fundCap: 100, sipBudget: 0, hybridEquity: 0)));
}
