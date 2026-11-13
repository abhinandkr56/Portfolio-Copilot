namespace PortfolioCopilot.Evals;

/// <summary>
/// Command line: <c>--only &lt;scenario&gt;</c>, <c>--provider &lt;name&gt;</c> (checked against the Api's provider),
/// <c>--api &lt;url&gt;</c>, <c>RunsAgainst=core</c> (or <c>--runs-against core</c>): deterministic checks only, no Api.
/// </summary>
public sealed record EvalOptions(string? Only, string? Provider, Uri ApiBase, bool CoreOnly, int DelaySeconds)
{
    public static EvalOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i].Equals($"--{name}", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) return args[i + 1];
                if (args[i].StartsWith($"--{name}=", StringComparison.OrdinalIgnoreCase)) return args[i][(name.Length + 3)..];
            }
            return null;
        }

        var runsAgainst = Value("runs-against")
                          ?? args.FirstOrDefault(a => a.StartsWith("RunsAgainst=", StringComparison.OrdinalIgnoreCase))?.Split('=', 2)[1]
                          ?? Environment.GetEnvironmentVariable("RunsAgainst");

        // Under the AppHost, service discovery gives us the Api's endpoint; standalone, the fixed local port.
        var api = Value("api") ?? Environment.GetEnvironmentVariable("services__api__http__0") ?? "http://127.0.0.1:5080";

        return new EvalOptions(
            Value("only"),
            Value("provider"),
            new Uri(api.TrimEnd('/') + "/"),
            string.Equals(runsAgainst, "core", StringComparison.OrdinalIgnoreCase),
            int.TryParse(Value("delay"), out var d) ? d : 2);
    }
}
