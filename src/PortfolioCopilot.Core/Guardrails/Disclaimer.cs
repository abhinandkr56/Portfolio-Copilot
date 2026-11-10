namespace PortfolioCopilot.Core;

public static class Disclaimer
{
    public const string Text = "Information only, not investment advice. You make the decisions.";

    /// <summary>The answer without the disclaimer sentence(s), which the guardrails ignore.</summary>
    public static string Strip(string answer) => answer.Replace(Text, " ", StringComparison.OrdinalIgnoreCase);
}
