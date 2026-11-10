namespace PortfolioCopilot.Core;

/// <summary>Parses Kite's <c>/mf/instruments</c> CSV (doc 04). Columns are found by header name; quoted fields may contain commas.</summary>
public static class MfInstrumentsCsvParser
{
    public static IReadOnlyDictionary<string, MfInstrument> Parse(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var result = new Dictionary<string, MfInstrument>(StringComparer.OrdinalIgnoreCase);
        if (lines.Length == 0)
            return result;

        var header = SplitLine(lines[0]);
        int Col(string name)
        {
            var i = Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : throw new FormatException($"The instruments CSV has no '{name}' column.");
        }

        int symbol = Col("tradingsymbol"), amc = Col("amc"), name = Col("name"),
            dividend = Col("dividend_type"), scheme = Col("scheme_type"), plan = Col("plan");

        foreach (var line in lines.Skip(1))
        {
            var f = SplitLine(line);
            if (f.Length < header.Length)
                continue;
            result[f[symbol]] = new MfInstrument(f[symbol], f[amc], f[name], f[dividend], f[scheme], f[plan]);
        }
        return result;
    }

    private static string[] SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else current.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString().Trim());
        return fields.ToArray();
    }
}
