using System.Text.Json.Nodes;
using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Evals;

public sealed record AskExpectation(string Message, IReadOnlyList<string> MustContain, IReadOnlyList<string> MustNotContain);

/// <summary>A scenario file: the parsed scenario (Core's ScenarioParser) plus its optional <c>expected.ask</c>.</summary>
public sealed record EvalScenario(string Name, Scenario Scenario, AskExpectation? Ask)
{
    public static string SamplesDir => Path.Combine(AppContext.BaseDirectory, "samples");

    public static IReadOnlyList<EvalScenario> Load(string? only)
    {
        var csv = File.ReadAllText(Path.Combine(SamplesDir, "kite", "mf-instruments.csv"));
        var nav = File.ReadAllText(Path.Combine(SamplesDir, "amfi", "NAVAll.sample.txt"));
        var files = Directory.GetFiles(Path.Combine(SamplesDir, "scenarios"), "*.json").OrderBy(f => f, StringComparer.Ordinal);

        var list = new List<EvalScenario>();
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (only is not null && !string.Equals(name, only, StringComparison.OrdinalIgnoreCase))
                continue;
            var json = File.ReadAllText(file);
            var ask = JsonNode.Parse(json)?["expected"]?["ask"] is JsonObject a
                ? new AskExpectation(
                    a["message"]!.GetValue<string>(),
                    (a["mustContain"] as JsonArray ?? []).Select(x => x!.GetValue<string>()).ToList(),
                    (a["mustNotContain"] as JsonArray ?? []).Select(x => x!.GetValue<string>()).ToList())
                : null;
            list.Add(new EvalScenario(name, ScenarioParser.Parse(json, csv, nav), ask));
        }
        return list;
    }
}
