using System.Globalization;

namespace PortfolioCopilot.Core;

public sealed class AmfiFormatException(string message) : Exception(message);

/// <summary>Parses the classic AMFI NAVAll text layout (doc 05 §AMFI NAV parser). Takes text, never touches files or HTTP.</summary>
public static class AmfiNavParser
{
    public static IReadOnlyList<NavRecord> Parse(string text)
    {
        using var reader = new StringReader(text);
        return Parse(reader);
    }

    public static IReadOnlyList<NavRecord> Parse(TextReader reader)
    {
        string? line;
        string? header = null;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Trim().Length > 0)
            {
                header = line.Trim();
                break;
            }
        }

        if (header is null)
            throw new AmfiFormatException("Unrecognised AMFI header: the file is empty.");
        if (!header.StartsWith("Scheme Code", StringComparison.OrdinalIgnoreCase))
            throw new AmfiFormatException($"Unrecognised AMFI header: {Truncate(header)}");

        char delimiter = header.Contains(';') ? ';' : header.Contains('|') ? '|' : '\0';
        if (delimiter == '\0' || header.Split(delimiter).Length != 6)
            throw new AmfiFormatException($"Unrecognised AMFI header: {Truncate(header)}");

        var records = new List<NavRecord>();
        var category = "";
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            var start = trimmed.IndexOf("Schemes(", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                var open = start + "Schemes(".Length;
                var close = trimmed.LastIndexOf(')');
                category = close > open ? trimmed[open..close].Trim() : trimmed[open..].Trim();
                continue;
            }

            if (!trimmed.Contains(delimiter))
                continue; // AMC name line (unused for now)

            var f = trimmed.Split(delimiter);
            if (f.Length != 6)
                continue;

            if (!decimal.TryParse(f[4].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var nav))
                continue; // "N.A." and anything else that isn't a number
            if (!DateOnly.TryParseExact(f[5].Trim(), "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            var code = f[0].Trim();
            var name = f[3].Trim();
            foreach (var isin in new[] { f[1].Trim(), f[2].Trim() })
            {
                if (LooksLikeIsin(isin))
                    records.Add(new NavRecord(isin, code, name, category, nav, date));
            }
        }
        return records;
    }

    private static bool LooksLikeIsin(string s) => s.Length == 12 && s.StartsWith("IN", StringComparison.Ordinal);

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120] + "…";
}
