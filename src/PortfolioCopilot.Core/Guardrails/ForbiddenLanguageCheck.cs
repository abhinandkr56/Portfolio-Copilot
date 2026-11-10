using System.Text.RegularExpressions;

namespace PortfolioCopilot.Core;

public sealed record GuardrailResult(bool Passed, IReadOnlyList<string> Problems);

/// <summary>Guardrail 1 (doc 06): blocks buy/sell calls, predictions and guarantees. Allowed suggestions are removed first.</summary>
public static partial class ForbiddenLanguageCheck
{
    private static readonly string[] AllowedPhrases = ["Consider Direct plan", "Rebalance toward target", "Check SIP mandate"];

    public static GuardrailResult Check(string answer)
    {
        var text = Disclaimer.Strip(answer);
        foreach (var allowed in AllowedPhrases)
            text = text.Replace(allowed, " ", StringComparison.OrdinalIgnoreCase);

        var problems = new List<string>();
        foreach (var regex in new[] { AdviceVerb(), BuySellTicker(), TargetPrice(), Prediction(), Guarantee() })
        {
            foreach (Match m in regex.Matches(text))
                problems.Add(m.Value.Trim());
        }
        return new GuardrailResult(problems.Count == 0, problems.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    [GeneratedRegex(@"\b(you should|i recommend|i suggest|consider)\s+(buy|buying|sell|selling|exit|exiting|accumulat\w*|switch\w*\s+to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdviceVerb();

    // Case-sensitive on purpose: the ticker must be upper case.
    [GeneratedRegex(@"\b([Bb]uy|[Ss]ell)\s+(more\s+)?[A-Z]{2,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex BuySellTicker();

    [GeneratedRegex(@"\btarget price\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetPrice();

    [GeneratedRegex(@"\b(will|is going to|likely to)\s+(rise|fall|go up|go down|outperform|underperform)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Prediction();

    [GeneratedRegex(@"\bguarantee(d)?\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Guarantee();
}
