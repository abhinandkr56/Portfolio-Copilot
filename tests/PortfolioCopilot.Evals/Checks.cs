using System.Text.RegularExpressions;
using PortfolioCopilot.Core;

namespace PortfolioCopilot.Evals;

public enum Mark { Pass, Fail, NotApplicable, Info }

public sealed record Check(string Column, Mark Mark, string? Detail = null)
{
    public static Check Of(string column, bool ok, string? detail = null) => new(column, ok ? Mark.Pass : Mark.Fail, ok ? null : detail);
    public static Check NA(string column) => new(column, Mark.NotApplicable);
}

/// <summary>One scenario's results. Deterministic checks are the rules; AI checks are Explain and Ask (doc 08 §3).</summary>
public sealed class ScenarioResult(string name)
{
    public string Name { get; } = name;
    public List<Check> Deterministic { get; } = [];
    public List<Check> Ai { get; } = [];
    /// <summary>Shown in the table but never fails the run (not a doc 08 check).</summary>
    public List<Check> Info { get; } = [];
    public bool DeterministicPassed => Deterministic.All(c => c.Mark != Mark.Fail);
    public bool AiPassed => Ai.All(c => c.Mark != Mark.Fail);
}

public static partial class Text
{
    /// <summary>Case-insensitive "mentions", ignoring dash and space variants (models write "–" or "‑" for "-").</summary>
    public static bool Mentions(string text, string phrase) =>
        Normalise(text).Contains(Normalise(phrase), StringComparison.OrdinalIgnoreCase);

    public static string Normalise(string s) => Spaces().Replace(Dashes().Replace(s, "-"), " ").Trim();

    public static bool EndsWithDisclaimer(string? text) =>
        text is not null && text.TrimEnd().EndsWith(Disclaimer.Text, StringComparison.Ordinal);

    public static string Snippet(string? text, int max = 160)
    {
        var t = Spaces().Replace(text ?? "", " ").Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }

    [GeneratedRegex(@"[‐-―−]")]
    private static partial Regex Dashes();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
