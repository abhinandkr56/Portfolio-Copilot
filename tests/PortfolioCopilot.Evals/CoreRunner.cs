using PortfolioCopilot.Core;

namespace PortfolioCopilot.Evals;

/// <summary><c>RunsAgainst=core</c>: the deterministic part only, straight through Core (no Api, no LLM).</summary>
public static class CoreRunner
{
    public static IReadOnlyList<ScenarioResult> Run(IReadOnlyList<EvalScenario> scenarios) =>
        scenarios.Select(s =>
        {
            var result = new ScenarioResult(s.Name);
            var input = s.Scenario.Input;
            var findings = RulesEngine.Evaluate(input, FixedClock.AtIstNoon(s.Scenario.Today));
            result.Deterministic.Add(Compare.Findings(s.Scenario.Expected.Findings.Select(f => (f.Rule, f.Subject, f.Severity.ToString())),
                findings.Select(f => (f.Rule, f.Subject, f.Severity.ToString()))));
            result.Deterministic.Add(Compare.Allocation(s.Scenario.Expected.Allocation,
                PortfolioCalculator.Compute(input).Allocation.ToDictionary(a => a.AssetClass, a => Math.Round(a.ActualPercent, 1, MidpointRounding.AwayFromZero))));
            return result;
        }).ToList();
}

public static class Compare
{
    public static Check Findings(IEnumerable<(string Rule, string Subject, string Severity)> expected, IEnumerable<(string Rule, string Subject, string Severity)> actual)
    {
        var e = expected.ToList();
        var a = actual.ToList();
        if (e.SequenceEqual(a))
            return Check.Of("Findings", true);
        string Fmt(IEnumerable<(string Rule, string Subject, string Severity)> x) => string.Join("; ", x.Select(f => $"{f.Severity} {f.Rule}/{f.Subject}"));
        return Check.Of("Findings", false, $"expected [{Fmt(e)}] got [{Fmt(a)}]");
    }

    public static Check Allocation(IReadOnlyDictionary<AssetClass, decimal> expected, IReadOnlyDictionary<AssetClass, decimal> actual)
    {
        var ok = expected.Count == actual.Count && expected.All(kv => actual.TryGetValue(kv.Key, out var v) && v == kv.Value);
        return Check.Of("Alloc", ok, $"expected {string.Join(", ", expected.Select(kv => $"{kv.Key} {kv.Value}"))} got {string.Join(", ", actual.Select(kv => $"{kv.Key} {kv.Value}"))}");
    }
}
