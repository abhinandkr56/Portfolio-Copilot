using System.Globalization;

namespace PortfolioCopilot.Core.Contracts;

/// <summary>Turns Core results into the doc 03 response records. Formatting only: no rule logic lives here.</summary>
public static class ContractMapper
{
    public static HoldingsResponse Holdings(PortfolioInput input, ComputedPortfolio p, DateTimeOffset asOfUtc)
    {
        var plan = input.Plan;
        var stockRows = new List<StockRow>();
        var fundRows = new List<FundRow>();

        // ComputedPortfolio.Holdings keeps the input order: all stocks, then all funds.
        var i = 0;
        foreach (var s in input.Stocks)
        {
            var h = p.Holdings[i++];
            stockRows.Add(new StockRow(s.Symbol, s.Isin, s.Exchange, s.Quantity, Money(s.AveragePrice), Money(s.LastPrice),
                Money(h.Value), Money(s.Pnl), h.AssetClass, Label(h.AssetClass, plan), Pct(h.PercentOfTotal)));
        }
        foreach (var f in input.Funds)
        {
            var h = p.Holdings[i++];
            var lastPrice = h.UsedNavFallback ? input.Navs[f.Isin].Nav : f.LastPrice;
            fundRows.Add(new FundRow(f.Fund, f.Isin, Redactor.MaskFolio(f.Folio), f.Quantity, Money(f.AveragePrice), lastPrice,
                h.PriceDate, Money(h.Value), Money(f.Pnl), h.AssetClass, Label(h.AssetClass, plan),
                h.PlanType ?? PlanType.Unknown, Pct(h.PercentOfTotal)));
        }
        return new HoldingsResponse(asOfUtc, Money(p.Total), stockRows, fundRows);
    }

    public static SipsResponse Sips(PortfolioInput input, ComputedPortfolio p, TimeProvider clock, DateTimeOffset asOfUtc)
    {
        var sips = p.Sips.Select(r => new SipDto(r.Sip.SipId, r.Sip.Fund, r.Sip.Isin, r.Sip.Status, r.Sip.Frequency,
            r.Sip.InstalmentAmount, r.MonthlyEquivalent, r.Sip.NextInstalment, r.Sip.CompletedInstalments)).ToList();

        var failed = RulesEngine.RecentFailedSipOrders(input.MfOrders, clock)
            .Select(o => new FailedOrderDto(o.OrderId, o.Fund, Ist.ToIstDate(o.OrderTimestamp), o.StatusMessage))
            .ToList();

        return new SipsResponse(asOfUtc, p.ActiveMonthlyEquivalent, input.Plan.MonthlySipBudget, sips, failed);
    }

    public static AllocationResponse Allocation(ComputedPortfolio p, IReadOnlyList<Finding> findings, DateTimeOffset asOfUtc)
    {
        var drift = findings.Where(f => f.Rule == RuleNames.AllocationDrift).ToDictionary(f => f.Subject, f => f.Severity);
        var classes = p.Allocation.Select(r => new AllocationClassDto(
            r.AssetClass, Money(r.Value), Pct(r.ActualPercent), Pct(r.TargetPercent), Pct(r.DriftPoints),
            drift.TryGetValue(r.AssetClass.ToString(), out var sev) ? sev : null)).ToList();
        return new AllocationResponse(asOfUtc, Money(p.Total), classes);
    }

    public static PortfolioReview Review(ComputedPortfolio p, IReadOnlyList<Finding> findings, DateTimeOffset asOfUtc) =>
        new(asOfUtc,
            p.Holdings.Where(h => h.Kind == HoldingKind.Fund && h.PriceDate is not null).Select(h => h.PriceDate).Max(),
            Allocation(p, findings, asOfUtc),
            findings.Select(Finding).ToList(),
            Summary: null,
            Ai: null,
            Disclaimer.Text);

    public static FindingDto Finding(Finding f) => new(f.Rule, f.Subject, f.Severity, f.Evidence, f.Message, f.Suggestion);

    /// <summary>"Equity", or "Hybrid (65/35)" using the plan's hybrid split.</summary>
    public static string Label(AssetClass assetClass, Plan plan) =>
        assetClass == AssetClass.Hybrid
            ? string.Create(CultureInfo.InvariantCulture, $"Hybrid ({plan.HybridEquityPercent:0.##}/{100 - plan.HybridEquityPercent:0.##})")
            : assetClass.ToString();

    private static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    private static decimal Pct(decimal v) => Math.Round(v, 1, MidpointRounding.AwayFromZero);
}
