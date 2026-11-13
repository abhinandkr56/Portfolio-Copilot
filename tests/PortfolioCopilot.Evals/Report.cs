namespace PortfolioCopilot.Evals;

/// <summary>Prints the scenario × check table (✓ / ✗ / –), the failures with details, and the totals.</summary>
public static class Report
{
    public static void Print(IReadOnlyList<ScenarioResult> results, string header)
    {
        var columns = results.SelectMany(r => r.Deterministic.Concat(r.Ai).Concat(r.Info)).Select(c => c.Column).Distinct().ToList();
        var nameWidth = Math.Max(8, results.Max(r => r.Name.Length));
        var widths = columns.Select(c => Math.Max(c.Length, 1)).ToList();

        Console.WriteLine();
        Console.WriteLine(header);
        Console.WriteLine();
        Console.WriteLine("| " + "Scenario".PadRight(nameWidth) + " | " + string.Join(" | ", columns.Select((c, i) => c.PadRight(widths[i]))) + " |");
        Console.WriteLine("|" + new string('-', nameWidth + 2) + "|" + string.Join("|", widths.Select(w => new string('-', w + 2))) + "|");
        foreach (var r in results)
        {
            var cells = columns.Select((c, i) =>
            {
                var check = r.Deterministic.Concat(r.Ai).Concat(r.Info).FirstOrDefault(x => x.Column == c);
                var mark = check?.Mark switch { Mark.Pass => "✓", Mark.Fail => "✗", Mark.Info => check.Detail ?? "", _ => "–" };
                return mark.PadRight(widths[i]);
            });
            Console.WriteLine("| " + r.Name.PadRight(nameWidth) + " | " + string.Join(" | ", cells) + " |");
        }

        var failures = results.SelectMany(r => r.Deterministic.Concat(r.Ai).Where(c => c.Mark == Mark.Fail).Select(c => (r.Name, c))).ToList();
        if (failures.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Failures:");
            foreach (var (name, c) in failures)
                Console.WriteLine($"  ✗ {name} · {c.Column}: {c.Detail}");
        }

        var detTotal = results.SelectMany(r => r.Deterministic).Count(c => c.Mark != Mark.NotApplicable);
        var detPass = results.SelectMany(r => r.Deterministic).Count(c => c.Mark == Mark.Pass);
        var aiScenarios = results.Count(r => r.Ai.Count > 0);
        var aiPass = results.Count(r => r.Ai.Count > 0 && r.AiPassed);
        Console.WriteLine();
        Console.WriteLine($"Deterministic checks: {detPass}/{detTotal} passed");
        if (aiScenarios > 0)
            Console.WriteLine($"AI checks: {aiPass}/{aiScenarios} scenarios passed every AI check ({results.SelectMany(r => r.Ai).Count(c => c.Mark == Mark.Pass)}/{results.SelectMany(r => r.Ai).Count(c => c.Mark != Mark.NotApplicable)} checks)");
        if (results.Any(r => r.Info.Count > 0))
            Console.WriteLine("Info columns (not pass/fail): \"verified\" = the AI answer passed NumberCheck/ForbiddenLanguage itself (no facts-only fallback); attempts = 1 or 2.");
        Console.WriteLine(results.All(r => r.DeterministicPassed && r.AiPassed) ? "RESULT: PASS" : "RESULT: FAIL");
    }
}
