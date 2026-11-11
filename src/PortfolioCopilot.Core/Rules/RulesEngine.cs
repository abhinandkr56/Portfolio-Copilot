using System.Globalization;

namespace PortfolioCopilot.Core;

public static class RuleNames
{
    public const string AllocationDrift = nameof(AllocationDrift);
    public const string StockConcentration = nameof(StockConcentration);
    public const string FundConcentration = nameof(FundConcentration);
    public const string RegularPlan = nameof(RegularPlan);
    public const string SipNotActive = nameof(SipNotActive);
    public const string SipFailedOrder = nameof(SipFailedOrder);
    public const string SipOverBudget = nameof(SipOverBudget);
    public const string StaleNav = nameof(StaleNav);
    public const string EmptyPortfolio = nameof(EmptyPortfolio);

    /// <summary>The rule order from the table in doc 05; used as the second sort key.</summary>
    public static readonly IReadOnlyList<string> Order =
    [
        AllocationDrift, StockConcentration, FundConcentration, RegularPlan,
        SipNotActive, SipFailedOrder, SipOverBudget, StaleNav, EmptyPortfolio,
    ];
}

/// <summary>
/// The rules from doc 05 §Rules. Thresholds use strict <c>&gt;</c> against unrounded percentages;
/// evidence and messages are rounded for display only.
/// </summary>
public static class RulesEngine
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly TimeSpan FailedOrderWindow = TimeSpan.FromDays(7);

    public static IReadOnlyList<Finding> Evaluate(PortfolioInput input, TimeProvider time) =>
        Evaluate(input, time, CoreOptions.Default);

    public static IReadOnlyList<Finding> Evaluate(PortfolioInput input, TimeProvider time, CoreOptions options) =>
        Evaluate(input, PortfolioCalculator.Compute(input, options), time, options);

    public static IReadOnlyList<Finding> Evaluate(PortfolioInput input, ComputedPortfolio portfolio, TimeProvider time, CoreOptions options)
    {
        if (portfolio.Total == 0)
        {
            return [new Finding(RuleNames.EmptyPortfolio, "Portfolio", Severity.Info, new Dictionary<string, object>(),
                "The portfolio has no holdings with a value.", "Review")];
        }

        var plan = input.Plan;
        var findings = new List<Finding>();
        findings.AddRange(AllocationDrift(portfolio, plan));
        findings.AddRange(StockConcentration(portfolio, plan));
        findings.AddRange(FundConcentration(portfolio, plan));
        findings.AddRange(RegularPlans(portfolio, plan, input.Instruments));
        findings.AddRange(SipNotActive(portfolio));
        findings.AddRange(SipFailedOrders(input.MfOrders, time));
        findings.AddRange(SipOverBudget(portfolio, plan));
        findings.AddRange(StaleNav(portfolio, time, options));
        return Sort(findings);
    }

    public static IReadOnlyList<Finding> Sort(IEnumerable<Finding> findings) =>
        findings
            .OrderBy(f => f.Severity)
            .ThenBy(f => IndexOfRule(f.Rule))
            .ThenBy(f => f.Subject, StringComparer.Ordinal)
            .ToList();

    private static int IndexOfRule(string rule)
    {
        for (var i = 0; i < RuleNames.Order.Count; i++)
            if (RuleNames.Order[i] == rule)
                return i;
        return int.MaxValue;
    }

    private static IEnumerable<Finding> AllocationDrift(ComputedPortfolio p, Plan plan)
    {
        var band = plan.DriftBandPoints;
        foreach (var row in p.Allocation)
        {
            var drift = row.DriftPoints;
            var abs = Math.Abs(drift);
            if (abs <= band)
                continue;

            var evidence = new Dictionary<string, object>
            {
                ["actualPercent"] = Round1(row.ActualPercent),
                ["targetPercent"] = Round1(row.TargetPercent),
                ["driftPoints"] = Round1(drift),
                ["bandPoints"] = band,
            };
            var message = string.Format(Inv,
                "{0} is {1:0.0}% against a target of {2:0.0}% (drift {3:+0.0;-0.0;0.0} points, band ±{4:0.0}).",
                row.AssetClass, Round1(row.ActualPercent), Round1(row.TargetPercent), Round1(drift), band);
            yield return new Finding(RuleNames.AllocationDrift, row.AssetClass.ToString(),
                abs > 2 * band ? Severity.Act : Severity.Watch, evidence, message, "Rebalance toward target");
        }
    }

    private static IEnumerable<Finding> StockConcentration(ComputedPortfolio p, Plan plan)
    {
        var cap = plan.MaxSingleStockPercent;
        var stocks = p.Holdings
            .Where(h => h.Kind == HoldingKind.Stock && h.AssetClass == AssetClass.Equity)
            .GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var g in stocks)
        {
            var value = g.Sum(h => h.Value);
            var pct = value / p.Total * 100;
            if (pct <= cap)
                continue;
            var message = string.Format(Inv, "{0} is {1:0.0}% of the portfolio; your cap is {2:0.0}%.", g.Key, Round1(pct), cap);
            yield return new Finding(RuleNames.StockConcentration, g.Key, ConcentrationSeverity(pct, cap),
                ConcentrationEvidence(pct, cap, value), message, "Review");
        }
    }

    private static IEnumerable<Finding> FundConcentration(ComputedPortfolio p, Plan plan)
    {
        var cap = plan.MaxSingleFundPercent;
        var funds = p.Holdings
            .Where(h => h.Kind == HoldingKind.Fund)
            .GroupBy(h => h.Isin, StringComparer.OrdinalIgnoreCase);   // one fund in several folios counts once
        foreach (var g in funds)
        {
            var name = g.First().Name;
            var value = g.Sum(h => h.Value);
            var pct = value / p.Total * 100;
            if (pct <= cap)
                continue;
            var message = string.Format(Inv, "{0} is {1:0.0}% of the portfolio; your cap is {2:0.0}%.", name, Round1(pct), cap);
            yield return new Finding(RuleNames.FundConcentration, name, ConcentrationSeverity(pct, cap),
                ConcentrationEvidence(pct, cap, value), message, "Review");
        }
    }

    private static Severity ConcentrationSeverity(decimal pct, decimal cap) => pct > 1.5m * cap ? Severity.Act : Severity.Watch;

    private static Dictionary<string, object> ConcentrationEvidence(decimal pct, decimal cap, decimal value) => new()
    {
        ["percentOfTotal"] = Round1(pct),
        ["capPercent"] = cap,
        ["value"] = Round2(value),
    };

    private static IEnumerable<Finding> RegularPlans(ComputedPortfolio p, Plan plan, IReadOnlyDictionary<string, MfInstrument> instruments)
    {
        if (!plan.PreferDirectPlans)
            yield break;

        var regulars = p.Holdings
            .Where(h => h.Kind == HoldingKind.Fund && h.PlanType == PlanType.Regular)
            .GroupBy(h => h.Isin, StringComparer.OrdinalIgnoreCase);
        foreach (var g in regulars)
        {
            var name = g.First().Name;
            var twin = Classifier.FindDirectTwin(g.Key, instruments);
            var evidence = new Dictionary<string, object> { ["isin"] = g.Key };
            var message = $"{name} is a Regular plan.";
            if (twin is not null)
            {
                evidence["directTwinIsin"] = twin.Isin;
                evidence["directTwinName"] = twin.Name;
                message += $" A Direct plan of the same scheme exists: {twin.Name}.";
            }
            yield return new Finding(RuleNames.RegularPlan, name, Severity.Watch, evidence, message, "Consider Direct plan");
        }
    }

    private static IEnumerable<Finding> SipNotActive(ComputedPortfolio p)
    {
        foreach (var row in p.Sips.Where(s => !s.IsActive))
        {
            var sip = row.Sip;
            var evidence = new Dictionary<string, object>
            {
                ["status"] = sip.Status,
                ["instalmentAmount"] = sip.InstalmentAmount,
                ["frequency"] = sip.Frequency,
            };
            var message = $"The {sip.Frequency} SIP of {Inr.Format(sip.InstalmentAmount)} in {sip.Fund} is {sip.Status}.";
            yield return new Finding(RuleNames.SipNotActive, sip.Fund, Severity.Watch, evidence, message, "Check SIP mandate");
        }
    }

    private static IEnumerable<Finding> SipFailedOrders(IReadOnlyList<MfOrder> orders, TimeProvider time)
    {
        var failed = RecentFailedSipOrders(orders, time).GroupBy(o => o.Isin, StringComparer.OrdinalIgnoreCase);
        foreach (var g in failed)
        {
            var last = g.OrderByDescending(o => o.OrderTimestamp).First();
            var lastDate = Ist.ToIstDate(last.OrderTimestamp);
            var count = g.Count();
            var evidence = new Dictionary<string, object>
            {
                ["failedCount"] = count,
                ["lastMessage"] = last.StatusMessage,
                ["lastDate"] = lastDate,
            };
            var message = string.Format(Inv, "{0} SIP order{1} for {2} failed in the last 7 days; the latest on {3:yyyy-MM-dd}: {4}",
                count, count == 1 ? "" : "s", last.Fund, lastDate, last.StatusMessage).TrimEnd(' ', ':');
            yield return new Finding(RuleNames.SipFailedOrder, last.Fund, Severity.Watch, evidence, message, "Check SIP mandate");
        }
    }

    /// <summary>SIP orders (variety "sip") that were REJECTED or CANCELLED in the last 7 days, newest first.</summary>
    public static IReadOnlyList<MfOrder> RecentFailedSipOrders(IReadOnlyList<MfOrder> orders, TimeProvider time)
    {
        var now = time.GetUtcNow();
        return orders
            .Where(o => string.Equals(o.Variety, "sip", StringComparison.OrdinalIgnoreCase))
            .Where(o => string.Equals(o.Status, "REJECTED", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(o.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            .Where(o => o.OrderTimestamp > now - FailedOrderWindow && o.OrderTimestamp <= now)
            .OrderByDescending(o => o.OrderTimestamp)
            .ToList();
    }

    private static IEnumerable<Finding> SipOverBudget(ComputedPortfolio p, Plan plan)
    {
        if (p.ActiveMonthlyEquivalent <= plan.MonthlySipBudget)
            yield break;
        var evidence = new Dictionary<string, object>
        {
            ["activeMonthlyEquivalent"] = p.ActiveMonthlyEquivalent,
            ["monthlyBudget"] = plan.MonthlySipBudget,
        };
        var message = $"Active SIPs add up to {Inr.Format(p.ActiveMonthlyEquivalent)} a month, above your budget of {Inr.Format(plan.MonthlySipBudget)}.";
        yield return new Finding(RuleNames.SipOverBudget, "SIPs", Severity.Watch, evidence, message, "Review");
    }

    /// <summary>A fund's price date is stale when it is before (today in IST − NavStaleDays). Funds with no date at all are not counted.</summary>
    private static IEnumerable<Finding> StaleNav(ComputedPortfolio p, TimeProvider time, CoreOptions options)
    {
        var cutoff = Ist.Today(time).AddDays(-options.NavStaleDays);
        var stale = p.Holdings
            .Where(h => h.Kind == HoldingKind.Fund && h.PriceDate is { } d && d < cutoff)
            .GroupBy(h => h.Isin, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Min(h => h.PriceDate!.Value))
            .ToList();
        if (stale.Count == 0)
            yield break;

        var oldest = stale.Min();
        var evidence = new Dictionary<string, object>
        {
            ["staleFunds"] = stale.Count,
            ["oldestDate"] = oldest,
        };
        var message = string.Format(Inv, "{0} fund price{1} older than {2} days; the oldest is from {3:yyyy-MM-dd}.",
            stale.Count, stale.Count == 1 ? " is" : "s are", options.NavStaleDays, oldest);
        yield return new Finding(RuleNames.StaleNav, "NAV", Severity.Info, evidence, message, "Review");
    }

    private static decimal Round1(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
