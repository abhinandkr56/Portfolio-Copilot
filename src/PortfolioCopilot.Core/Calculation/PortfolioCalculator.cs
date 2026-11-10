namespace PortfolioCopilot.Core;

public enum HoldingKind { Stock, Fund }

/// <summary>One holding with its value and classification. Funds held in several folios stay separate rows.</summary>
public sealed record HoldingRow(
    HoldingKind Kind,
    string Name,              // stock symbol or fund name
    string Isin,
    AssetClass AssetClass,    // may be Hybrid here; the allocation splits it
    PlanType? PlanType,       // funds only
    decimal Value,
    decimal PercentOfTotal,   // full precision
    DateOnly? PriceDate,      // funds only: Kite last_price_date, else the AMFI NAV date
    bool UsedNavFallback);

public sealed record AllocationRow(AssetClass AssetClass, decimal Value, decimal ActualPercent, decimal TargetPercent)
{
    public decimal DriftPoints => ActualPercent - TargetPercent;
}

public sealed record SipRow(Sip Sip, bool IsActive, decimal MonthlyEquivalent);

/// <summary>Everything the rules need, computed once. Percentages are full precision; round only for display.</summary>
public sealed record ComputedPortfolio(
    decimal Total,
    IReadOnlyList<HoldingRow> Holdings,
    IReadOnlyList<AllocationRow> Allocation,
    IReadOnlyList<SipRow> Sips,
    decimal ActiveMonthlyEquivalent,
    IReadOnlyList<string> Warnings);

public static class PortfolioCalculator
{
    private static readonly AssetClass[] AlwaysShown = [AssetClass.Equity, AssetClass.Debt, AssetClass.Gold];

    public static ComputedPortfolio Compute(PortfolioInput input, CoreOptions? options = null)
    {
        options ??= CoreOptions.Default;
        var plan = input.Plan;
        var warnings = new List<string>();
        var rows = new List<(HoldingKind Kind, string Name, string Isin, AssetClass Class, PlanType? Plan, decimal Value, DateOnly? Date, bool NavFallback)>();

        foreach (var s in input.Stocks)
            rows.Add((HoldingKind.Stock, s.Symbol, s.Isin, Classifier.ClassifyStock(s, plan), null, s.Quantity * s.LastPrice, null, false));

        var warnedIsins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in input.Funds)
        {
            var classification = Classifier.ClassifyFund(f, plan, input.Instruments, input.Navs, options);
            if (classification.IsFallback && warnedIsins.Add(f.Isin))
                warnings.Add($"No asset class for {f.Isin} ({f.Fund}); counted as Other.");

            input.Navs.TryGetValue(f.Isin, out var nav);
            var price = f.LastPrice;
            var date = f.LastPriceDate;
            var navFallback = false;
            if (price == 0 && nav is not null)
            {
                price = nav.Nav;
                date = nav.NavDate;
                navFallback = true;
            }
            date ??= nav?.NavDate;

            rows.Add((HoldingKind.Fund, f.Fund, f.Isin, classification.AssetClass,
                Classifier.ClassifyPlan(f.Isin, f.Fund, input.Instruments), f.Quantity * price, date, navFallback));
        }

        var total = rows.Sum(r => r.Value);
        var holdings = rows
            .Select(r => new HoldingRow(r.Kind, r.Name, r.Isin, r.Class, r.Plan, r.Value,
                total == 0 ? 0 : r.Value / total * 100, r.Date, r.NavFallback))
            .ToList();

        var (sips, activeMonthly) = ComputeSips(input.Sips, warnings);

        return new ComputedPortfolio(total, holdings, ComputeAllocation(holdings, plan, total), sips, activeMonthly, warnings);
    }

    /// <summary>Allocation by class after the Hybrid split. Equity, Debt and Gold always; Other when its actual or target is above 0.</summary>
    private static IReadOnlyList<AllocationRow> ComputeAllocation(IReadOnlyList<HoldingRow> holdings, Plan plan, decimal total)
    {
        if (total == 0)
            return [];

        var byClass = new Dictionary<AssetClass, decimal>
        {
            [AssetClass.Equity] = 0, [AssetClass.Debt] = 0, [AssetClass.Gold] = 0, [AssetClass.Other] = 0,
        };
        foreach (var h in holdings)
        {
            if (h.AssetClass == AssetClass.Hybrid)
            {
                var equity = h.Value * plan.HybridEquityPercent / 100;
                byClass[AssetClass.Equity] += equity;
                byClass[AssetClass.Debt] += h.Value - equity;
            }
            else
            {
                byClass[h.AssetClass] += h.Value;
            }
        }

        var result = new List<AllocationRow>();
        foreach (var cls in new[] { AssetClass.Equity, AssetClass.Debt, AssetClass.Gold, AssetClass.Other })
        {
            var value = byClass[cls];
            var target = plan.TargetAllocation.GetValueOrDefault(cls);
            if (AlwaysShown.Contains(cls) || value > 0 || target > 0)
                result.Add(new AllocationRow(cls, value, value / total * 100, target));
        }
        return result;
    }

    private static (IReadOnlyList<SipRow>, decimal) ComputeSips(IReadOnlyList<Sip> sips, List<string> warnings)
    {
        var rows = new List<SipRow>();
        foreach (var sip in sips)
        {
            var active = string.Equals(sip.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase);
            rows.Add(new SipRow(sip, active, active ? MonthlyEquivalent(sip, warnings) : 0));
        }
        return (rows, rows.Sum(r => r.MonthlyEquivalent));
    }

    /// <summary>monthly ×1 · weekly ×52/12 · fortnightly ×26/12 · quarterly ÷3 · anything else ×1 (with a warning). Rounded to 2 dp.</summary>
    public static decimal MonthlyEquivalent(Sip sip, List<string>? warnings = null)
    {
        var amount = sip.InstalmentAmount;
        var monthly = sip.Frequency.Trim().ToLowerInvariant() switch
        {
            "monthly" => amount,
            "weekly" => amount * 52 / 12,
            "fortnightly" => amount * 26 / 12,
            "quarterly" => amount / 3,
            _ => Unknown(),
        };
        return Math.Round(monthly, 2, MidpointRounding.AwayFromZero);

        decimal Unknown()
        {
            warnings?.Add($"Unknown SIP frequency '{sip.Frequency}' for {sip.Fund}; counted as monthly.");
            return amount;
        }
    }
}
