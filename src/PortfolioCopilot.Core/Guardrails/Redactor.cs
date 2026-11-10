using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PortfolioCopilot.Core;

/// <summary>
/// Redaction applied to everything sent to the LLM (doc 06 §Redaction). Field names match case-insensitively
/// and ignoring underscores, so <c>user_id</c> and <c>userId</c> are the same field.
/// </summary>
public static partial class Redactor
{
    private static readonly HashSet<string> Dropped = ["email", "username", "accesstoken", "orderid", "sipid"];

    private static readonly HashSet<string> Amounts =
        ["value", "pnl", "averageprice", "lastprice", "instalmentamount", "activemonthlyequivalent", "monthlybudget"];

    /// <summary>Returns a redacted deep copy; the input is not changed.</summary>
    public static JsonNode? Redact(JsonNode? node, bool shareAmounts)
    {
        var copy = node?.DeepClone();
        Walk(copy, shareAmounts);
        return copy;
    }

    public static string MaskFolio(string folio) =>
        "****" + (folio.Length <= 4 ? folio : folio[^4..]);

    public static string MaskUserId(string userId) =>
        userId.Length <= 4 ? "***" : userId[..2] + "***" + userId[^2..];

    private static void Walk(JsonNode? node, bool shareAmounts)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var k = Normalise(key);
                    if (Dropped.Contains(k) || (!shareAmounts && Amounts.Contains(k)))
                    {
                        obj.Remove(key);
                        continue;
                    }

                    var child = obj[key];
                    if (k == "folio" && child is JsonValue folio && folio.TryGetValue<string>(out var f))
                        obj[key] = MaskFolio(f);
                    else if (k == "userid" && child is JsonValue user && user.TryGetValue<string>(out var u))
                        obj[key] = MaskUserId(u);
                    else if (!shareAmounts && child is JsonValue text && text.TryGetValue<string>(out var s) && s.Contains('₹'))
                        obj[key] = RupeeAmount().Replace(s, "₹***"); // amounts inside messages
                    else
                        Walk(child, shareAmounts);
                }
                break;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (!shareAmounts && arr[i] is JsonValue v && v.TryGetValue<string>(out var s) && s.Contains('₹'))
                        arr[i] = RupeeAmount().Replace(s, "₹***");
                    else
                        Walk(arr[i], shareAmounts);
                }
                break;
        }
    }

    private static string Normalise(string key) => key.Replace("_", "").ToLowerInvariant();

    [GeneratedRegex(@"₹\s?\d[\d,]*(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex RupeeAmount();
}
