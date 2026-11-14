using System.Globalization;

namespace PortfolioCopilot.Core;

public sealed class AmfiFormatException(string message) : Exception(message);

/// <summary>
/// Parses the AMFI NAVAll text file (doc 05 §AMFI NAV parser). Takes text, never touches files or HTTP.
/// Two layouts are recognised by their header, and anything else throws <see cref="AmfiFormatException"/>:
/// the classic 6 columns, and the 8-column layout (live since 2026) that splits <c>Plan</c> and <c>Option</c>
/// out of the scheme name. For the 8-column layout the scheme name is rebuilt as <c>Name - Plan - Option</c>,
/// so names read the same as in the classic file.
/// </summary>
public static class AmfiNavParser
{
    private const string Code = "Scheme Code";
    private const string Isin1 = "ISIN Div Payout/ ISIN Growth";
    private const string Isin2 = "ISIN Div Reinvestment";
    private const string Name = "Scheme Name";
    private const string PlanColumn = "Plan";
    private const string OptionColumn = "Option";
    private const string Nav = "Net Asset Value";
    private const string Date = "Date";

    private static readonly string[][] KnownHeaders =
    [
        [Code, Isin1, Isin2, Name, Nav, Date],                             // classic
        [Code, Isin1, Isin2, Name, PlanColumn, OptionColumn, Nav, Date],   // 2026: Plan and Option columns
    ];

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
                header = line.Trim().TrimStart('﻿');
                break;
            }
        }

        if (header is null)
            throw new AmfiFormatException("Unrecognised AMFI header: the file is empty.");

        var layout = ReadHeader(header)
            ?? throw new AmfiFormatException($"Unrecognised AMFI header: {Truncate(header)}");

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
                var close = trimmed.LastIndexOf(')'); // the last one: "(Exchange Traded Funds (ETFs) - Gold ETF)"
                category = close > open ? trimmed[open..close].Trim() : trimmed[open..].Trim();
                continue;
            }

            if (!trimmed.Contains(layout.Delimiter))
                continue; // AMC name line (unused for now)

            var f = trimmed.Split(layout.Delimiter);
            if (f.Length != layout.Columns)
                continue;

            if (!decimal.TryParse(f[layout.Nav].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var nav))
                continue; // "N.A." and anything else that isn't a number
            if (!DateOnly.TryParseExact(f[layout.Date].Trim(), "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;

            var code = f[0].Trim();
            var name = layout.Plan is { } p && layout.Option is { } o
                ? string.Join(" - ", new[] { f[3], f[p], f[o] }.Select(s => s.Trim()).Where(s => s.Length > 0))
                : f[3].Trim();
            foreach (var isin in new[] { f[1].Trim(), f[2].Trim() })
            {
                if (LooksLikeIsin(isin))
                    records.Add(new NavRecord(isin, code, name, category, nav, date));
            }
        }
        return records;
    }

    /// <summary>One record per ISIN (the file lists a few ISINs twice): the latest NAV date wins, then the first seen.</summary>
    public static IReadOnlyList<NavRecord> LatestPerIsin(IEnumerable<NavRecord> records) =>
        records.GroupBy(r => r.Isin, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(r => r.NavDate)!)
            .ToList();

    private sealed record Layout(char Delimiter, int Columns, int Nav, int Date, int? Plan, int? Option);

    private static Layout? ReadHeader(string header)
    {
        if (!header.StartsWith(Code, StringComparison.OrdinalIgnoreCase))
            return null;
        char delimiter = header.Contains(';') ? ';' : header.Contains('|') ? '|' : '\0';
        if (delimiter == '\0')
            return null;

        var columns = header.Split(delimiter).Select(c => c.Trim()).ToArray();
        var known = KnownHeaders.FirstOrDefault(k => k.SequenceEqual(columns, StringComparer.OrdinalIgnoreCase));
        if (known is null)
            return null;

        int? IndexOf(string column) => Array.IndexOf(known, column) is var i and >= 0 ? i : null;
        return new Layout(delimiter, known.Length, IndexOf(Nav)!.Value, IndexOf(Date)!.Value, IndexOf(PlanColumn), IndexOf(OptionColumn));
    }

    private static bool LooksLikeIsin(string s) => s.Length == 12 && s.StartsWith("IN", StringComparison.Ordinal);

    private static string Truncate(string s) => s.Length <= 120 ? s : s[..120] + "…";
}
