namespace PortfolioCopilot.Core;

public static class Disclaimer
{
    public const string Text = "Information only, not investment advice. You make the decisions.";

    /// <summary>The answer without the disclaimer sentence(s), which the guardrails ignore.</summary>
    public static string Strip(string answer) => answer.Replace(Text, " ", StringComparison.OrdinalIgnoreCase);

    /// <summary>The answer ending with exactly one disclaimer (CLAUDE.md rule 6), whatever the model did with it.</summary>
    public static string Ensure(string answer)
    {
        var body = answer.Replace(Text, "", StringComparison.OrdinalIgnoreCase).Trim();
        return body.Length == 0 ? Text : body + "\n\n" + Text;
    }
}
