using System.Globalization;

namespace PortfolioCopilot.Core;

/// <summary>Formats rupee amounts with Indian digit grouping: ₹1,00,000 and ₹23,666.67.</summary>
public static class Inr
{
    public static string Format(decimal amount)
    {
        var rounded = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        var negative = rounded < 0;
        rounded = Math.Abs(rounded);

        var whole = decimal.Truncate(rounded);
        var fraction = rounded - whole;
        var digits = whole.ToString("0", CultureInfo.InvariantCulture);

        string grouped;
        if (digits.Length <= 3)
        {
            grouped = digits;
        }
        else
        {
            var head = digits[..^3];
            var tail = digits[^3..];
            var parts = new List<string>();
            for (var i = head.Length; i > 0; i -= 2)
                parts.Insert(0, head[Math.Max(0, i - 2)..i]);
            grouped = string.Join(",", parts) + "," + tail;
        }

        var text = fraction == 0 ? grouped : grouped + (fraction.ToString("0.00", CultureInfo.InvariantCulture)[1..]);
        return (negative ? "-₹" : "₹") + text;
    }
}
