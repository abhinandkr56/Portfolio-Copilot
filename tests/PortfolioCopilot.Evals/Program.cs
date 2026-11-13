using PortfolioCopilot.Evals;

// Golden scenarios end to end (doc 08 §3). Exit code 0 only when every check passes.
var options = EvalOptions.Parse(args);
var scenarios = EvalScenario.Load(options.Only);
if (scenarios.Count == 0)
{
    Console.Error.WriteLine($"No scenario matches '{options.Only}'.");
    return 2;
}

if (options.CoreOnly)
{
    var coreResults = CoreRunner.Run(scenarios);
    Report.Print(coreResults, "Portfolio Copilot evals · RunsAgainst=core (deterministic checks only, no Api, no LLM)");
    return coreResults.All(r => r.DeterministicPassed) ? 0 : 1;
}

using var runner = new ApiRunner(options);
var session = await runner.SessionAsync();
if (options.Provider is not null && !string.Equals(options.Provider, session.AiProvider, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"The Api is using {session.AiProvider}. Restart the AppHost with --AI:Provider={options.Provider} to evaluate {options.Provider}.");
    return 2;
}

Console.WriteLine($"Api {options.ApiBase} · Kite {session.Mode} · AI {session.AiProvider} · {session.AiModel}");
await runner.LoginAsync();

var results = new List<ScenarioResult>();
foreach (var s in scenarios)
{
    Console.WriteLine($"→ {s.Name}");
    results.Add(await runner.RunAsync(s));
    await Task.Delay(TimeSpan.FromSeconds(options.DelaySeconds)); // be gentle with free-tier rate limits
}

Report.Print(results, $"Portfolio Copilot evals · {session.AiProvider} · {session.AiModel} · {DateTimeOffset.Now:yyyy-MM-dd HH:mm}");
return results.All(r => r.DeterministicPassed && r.AiPassed) ? 0 : 1;
