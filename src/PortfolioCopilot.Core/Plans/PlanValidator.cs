namespace PortfolioCopilot.Core;

/// <summary>
/// Plan validation for <c>PUT /api/plan</c> (doc 05 §Plan validation). Keys are the plan's camelCase field names
/// (no dotted sub-keys: ASP.NET camel-cases each segment, which would turn "GOLDBEES" into "goldbees"); the message names the entry.
/// </summary>
public static class PlanValidator
{
    private static readonly AssetClass[] AllowedClasses = [AssetClass.Equity, AssetClass.Debt, AssetClass.Gold, AssetClass.Other];

    public static IReadOnlyDictionary<string, string[]> Validate(Plan plan)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
                errors[field] = list = [];
            list.Add(message);
        }

        if (plan.TargetAllocation is null || plan.TargetAllocation.Count == 0)
        {
            Add("targetAllocation", "Set at least one target.");
        }
        else
        {
            foreach (var (cls, value) in plan.TargetAllocation)
            {
                if (!AllowedClasses.Contains(cls))
                    Add("targetAllocation", $"{cls} is not allowed; use Equity, Debt, Gold or Other.");
                if (value < 0)
                    Add("targetAllocation", $"{cls} must be 0 or more.");
            }
            var sum = plan.TargetAllocation.Values.Sum();
            if (Math.Abs(sum - 100) > 0.01m)
                Add("targetAllocation", $"Targets must add up to 100 (they add up to {sum}).");
        }

        if (plan.DriftBandPoints is < 1 or > 20)
            Add("driftBandPoints", "Must be between 1 and 20.");
        if (plan.MaxSingleStockPercent is < 1 or > 100)
            Add("maxSingleStockPercent", "Must be between 1 and 100.");
        if (plan.MaxSingleFundPercent is < 1 or > 100)
            Add("maxSingleFundPercent", "Must be between 1 and 100.");
        if (plan.MonthlySipBudget < 0)
            Add("monthlySipBudget", "Must be 0 or more.");
        if (plan.HybridEquityPercent is < 0 or > 100)
            Add("hybridEquityPercent", "Must be between 0 and 100.");

        foreach (var (key, cls) in plan.AssetClassOverrides ?? [])
        {
            if (string.IsNullOrWhiteSpace(key))
                Add("assetClassOverrides", "An override needs a symbol or ISIN.");
            if (!AllowedClasses.Contains(cls))
                Add("assetClassOverrides", $"{key}: {cls} is not allowed; use Equity, Debt, Gold or Other.");
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }
}
