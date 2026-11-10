using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PortfolioCopilot.Core;

/// <summary>
/// Guardrail 2 (doc 06): every number in the answer must come from the data sent to the model this turn
/// (as is, or rounded to 0, 1 or 2 dp). Integers 0–12, years 2000–2100 and numbers inside ISO dates are always allowed.
/// </summary>
public static partial class NumberCheck
{
    public static GuardrailResult Check(string answer, IEnumerable<string> sourceJson)
    {
        var allowed = new HashSet<decimal>();
        foreach (var json in sourceJson)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(json); }
            catch (System.Text.Json.JsonException) { node = null; }

            if (node is null)
                AddFromText(json, allowed);
            else
                AddFromNode(node, allowed);
        }

        var text = IsoDate().Replace(Disclaimer.Strip(answer), " ");
        var unknown = new List<string>();
        foreach (var n in ExtractNumbers(text))
        {
            if (IsAlwaysAllowed(n) || allowed.Contains(n) || allowed.Contains(Math.Abs(n)))
                continue;
            unknown.Add(n.ToString(CultureInfo.InvariantCulture));
        }
        return new GuardrailResult(unknown.Count == 0, unknown.Distinct().ToList());
    }

    public static IReadOnlyList<decimal> ExtractNumbers(string text)
    {
        var result = new List<decimal>();
        foreach (Match m in NumberPattern().Matches(text))
        {
            var raw = m.Value.Replace(",", "").Replace("%", "").Replace("₹", "").TrimStart('+');
            if (decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
                result.Add(d);
        }
        return result;
    }

    private static bool IsAlwaysAllowed(decimal n) =>
        (n == decimal.Truncate(n) && n is >= 0 and <= 12) ||
        (n == decimal.Truncate(n) && n is >= 2000 and <= 2100);

    private static void AddFromNode(JsonNode node, HashSet<decimal> allowed)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, child) in obj)
                    if (child is not null) AddFromNode(child, allowed);
                break;
            case JsonArray arr:
                foreach (var child in arr)
                    if (child is not null) AddFromNode(child, allowed);
                break;
            case JsonValue value:
                if (value.TryGetValue<decimal>(out var d))
                    AddWithRoundings(d, allowed);
                else if (value.TryGetValue<string>(out var s))
                    AddFromText(s, allowed); // numbers inside messages and names, e.g. "₹18,000" or "Nifty 50"
                break;
        }
    }

    private static void AddFromText(string text, HashSet<decimal> allowed)
    {
        foreach (Match date in IsoDate().Matches(text))
            foreach (var part in date.Value.Split('-'))
                AddWithRoundings(decimal.Parse(part, CultureInfo.InvariantCulture), allowed);
        foreach (var n in ExtractNumbers(IsoDate().Replace(text, " ")))
            AddWithRoundings(n, allowed);
    }

    private static void AddWithRoundings(decimal d, HashSet<decimal> allowed)
    {
        foreach (var v in new[] { d, Math.Abs(d) })
        {
            allowed.Add(v);
            for (var dp = 0; dp <= 2; dp++)
                allowed.Add(Math.Round(v, dp, MidpointRounding.AwayFromZero));
        }
    }

    [GeneratedRegex(@"[+-]?₹?\d{1,3}(?:,\d{2,3})+(?:\.\d+)?|[+-]?₹?\d+(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();
}
