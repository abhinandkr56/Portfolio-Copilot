using System.Text.RegularExpressions;

namespace PortfolioCopilot.Core;

/// <summary>The result of classifying a holding. <see cref="IsFallback"/> is true when nothing matched and it fell to Other.</summary>
public readonly record struct Classification(AssetClass AssetClass, bool IsFallback);

/// <summary>Asset class, plan type and Direct twin rules from doc 05 §Classification. The first match wins.</summary>
public static partial class Classifier
{
    public static AssetClass ClassifyStock(StockHolding stock, Plan plan)
    {
        if (TryOverride(plan, stock.Symbol, out var byOverride) || TryOverride(plan, stock.Isin, out byOverride))
            return byOverride;
        return AssetClass.Equity;
    }

    public static Classification ClassifyFund(FundHolding fund, Plan plan,
        IReadOnlyDictionary<string, MfInstrument> instruments, IReadOnlyDictionary<string, NavRecord> navs,
        CoreOptions options)
    {
        if (TryOverride(plan, fund.Isin, out var byOverride))
            return new(byOverride, false);

        if (instruments.TryGetValue(fund.Isin, out var instrument)
            && !string.IsNullOrWhiteSpace(instrument.SchemeType)
            && TryMapSchemeType(options, instrument.SchemeType, out var mapped))
            return new(mapped, false);

        if (navs.TryGetValue(fund.Isin, out var nav) && ClassifyAmfiCategory(nav.Category, nav.SchemeName) is { } byCategory)
            return new(byCategory, false);

        return new(AssetClass.Other, true);
    }

    /// <summary>
    /// AMFI category fallback: "Equity Scheme" → Equity, "Debt Scheme" or "Income/Debt Oriented Scheme" → Debt, "Hybrid Scheme" → Hybrid,
    /// "Gold" in the category, or "FoF" in the category with "gold" in the scheme name → Gold.
    /// </summary>
    public static AssetClass? ClassifyAmfiCategory(string category, string schemeName)
    {
        if (Contains(category, "Equity Scheme")) return AssetClass.Equity;
        if (Contains(category, "Debt Scheme")) return AssetClass.Debt;
        if (Contains(category, "Income/Debt Oriented Scheme")) return AssetClass.Debt; // the live file's name for debt categories
        if (Contains(category, "Hybrid Scheme")) return AssetClass.Hybrid;
        if (Contains(category, "Gold")) return AssetClass.Gold;
        if (Contains(category, "FoF") && Contains(schemeName, "gold")) return AssetClass.Gold;
        return null;
    }

    public static PlanType ClassifyPlan(string isin, string fundName, IReadOnlyDictionary<string, MfInstrument> instruments)
    {
        if (instruments.TryGetValue(isin, out var instrument))
        {
            if (string.Equals(instrument.Plan, "direct", StringComparison.OrdinalIgnoreCase)) return PlanType.Direct;
            if (string.Equals(instrument.Plan, "regular", StringComparison.OrdinalIgnoreCase)) return PlanType.Regular;
        }

        if (Contains(fundName, "Direct")) return PlanType.Direct;
        if (Contains(fundName, "Regular")) return PlanType.Regular;
        return PlanType.Unknown;
    }

    /// <summary>
    /// The Direct twin of a Regular fund: same AMC, same dividend type, and a name equal to the Regular name
    /// with "Regular" replaced by "Direct" (case-insensitive, whitespace-normalised). Null when there is none.
    /// </summary>
    public static MfInstrument? FindDirectTwin(string regularIsin, IReadOnlyDictionary<string, MfInstrument> instruments)
    {
        if (!instruments.TryGetValue(regularIsin, out var regular))
            return null;

        var wanted = Normalise(RegularWord().Replace(regular.Name, "Direct"));
        return instruments.Values
            .Where(i => !string.Equals(i.Isin, regular.Isin, StringComparison.OrdinalIgnoreCase))
            .Where(i => string.Equals(i.Amc, regular.Amc, StringComparison.OrdinalIgnoreCase))
            .Where(i => string.Equals(i.DividendType, regular.DividendType, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.Isin, StringComparer.Ordinal)
            .FirstOrDefault(i => string.Equals(Normalise(i.Name), wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryOverride(Plan plan, string key, out AssetClass assetClass)
    {
        foreach (var (k, v) in plan.AssetClassOverrides)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            {
                assetClass = v;
                return true;
            }
        }
        assetClass = default;
        return false;
    }

    private static bool TryMapSchemeType(CoreOptions options, string schemeType, out AssetClass assetClass)
    {
        foreach (var (k, v) in options.SchemeTypeMap)
        {
            if (string.Equals(k, schemeType.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                assetClass = v;
                return true;
            }
        }
        assetClass = default;
        return false;
    }

    private static bool Contains(string text, string value) => text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string Normalise(string name) => Whitespace().Replace(name.Trim(), " ");

    [GeneratedRegex("regular", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegularWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
