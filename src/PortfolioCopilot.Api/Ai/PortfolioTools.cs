using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Api.Portfolio;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Ai;

/// <summary>What the tools did during one Ask turn: names (for toolsUsed) and redacted outputs (NumberCheck's allowed set).</summary>
public sealed class ToolRun
{
    public List<string> Used { get; } = [];
    public List<string> Outputs { get; } = [];
}

/// <summary>
/// The six read-only Ask tools (doc 06). Each calls the same services and Core mappers as the endpoints,
/// returns JSON after <see cref="Redactor.Redact"/>, starts a <c>tool.{Name}</c> activity and is recorded in toolsUsed.
/// </summary>
public static class PortfolioTools
{
    private static readonly JsonSerializerOptions Json = PlanStore.Json;

    public static IList<AITool> Create(PortfolioService portfolio, AiSettings ai, ToolRun run) =>
    [
        AIFunctionFactory.Create(
            async () => await Run(run, ai, "GetPlan", async () =>
            {
                var p = (await portfolio.GetAsync()).Input.Plan;
                return (new
                {
                    p.TargetAllocation, p.DriftBandPoints, p.MaxSingleStockPercent, p.MaxSingleFundPercent,
                    p.MonthlySipBudget, p.PreferDirectPlans, p.HybridEquityPercent, p.AssetClassOverrides,
                }, 1);
            }),
            "GetPlan",
            "The owner's own investment plan: target allocation % per asset class, drift band (points), max % for a single stock and a single fund, monthly SIP budget, whether Direct plans are preferred, the hybrid fund equity split, and asset class overrides."),

        AIFunctionFactory.Create(
            async ([Description("Which holdings: \"stocks\", \"funds\" or \"all\".")] string kind) => await Run(run, ai, "GetHoldings", async () =>
            {
                var s = await portfolio.GetAsync();
                var h = ContractMapper.Holdings(s.Input, s.Computed, s.AsOfUtc);
                var k = (kind ?? "all").Trim().ToLowerInvariant();
                var stocks = k is "stocks" or "all"
                    ? h.Stocks.Select(r => (object)new { name = r.Symbol, kind = "stock", assetClass = r.AssetClassLabel, r.PercentOfTotal, r.Value, r.Pnl }).ToList()
                    : [];
                var funds = k is "funds" or "all"
                    ? h.Funds.Select(r => (object)new { name = r.Fund, kind = "fund", assetClass = r.AssetClassLabel, plan = r.Plan.ToString(), r.PercentOfTotal, r.Value, r.Pnl }).ToList()
                    : [];
                return (new { holdings = stocks.Concat(funds).ToList() }, stocks.Count + funds.Count);
            }),
            "GetHoldings",
            "The owner's holdings with name, asset class, plan type (funds: Direct/Regular/Unknown) and percentOfTotal."),

        AIFunctionFactory.Create(
            async () => await Run(run, ai, "ComputeAllocation", async () =>
            {
                var s = await portfolio.GetAsync();
                var a = ContractMapper.Allocation(s.Computed, portfolio.Evaluate(s), s.AsOfUtc);
                return (new { classes = a.Classes }, a.Classes.Count);
            }),
            "ComputeAllocation",
            "Allocation by asset class (hybrid funds already split): actualPercent, targetPercent, driftPoints and the drift severity (null = within the band)."),

        AIFunctionFactory.Create(
            async () => await Run(run, ai, "GetFindings", async () =>
            {
                var s = await portfolio.GetAsync();
                var findings = portfolio.Evaluate(s).Select(ContractMapper.Finding).ToList();
                return (new { findings }, findings.Count);
            }),
            "GetFindings",
            "Every rule finding for the portfolio, most important first: rule, subject, severity (Act/Watch/Info), evidence numbers, message and the allowed suggestion."),

        AIFunctionFactory.Create(
            async () => await Run(run, ai, "GetSips", async () =>
            {
                var s = await portfolio.GetAsync();
                var sips = ContractMapper.Sips(s.Input, s.Computed, s.Clock, s.AsOfUtc);
                return (sips, sips.Sips.Count);
            }),
            "GetSips",
            "The owner's SIPs (fund, status, frequency), how much of the monthly SIP budget active SIPs use (budgetUsedPercent), and SIP orders that failed in the last 7 days."),

        AIFunctionFactory.Create(
            async ([Description("A rule name, e.g. AllocationDrift, StockConcentration, FundConcentration, RegularPlan, SipNotActive, SipFailedOrder, SipOverBudget, StaleNav.")] string rule) =>
                await Run(run, ai, "ExplainRule", () =>
                {
                    var known = RuleText.TryGetValue(rule?.Trim() ?? "", out var text);
                    return Task.FromResult<(object, int)>((new { rule, description = known ? text : $"Unknown rule. Known rules: {string.Join(", ", RuleText.Keys)}." }, known ? 1 : 0));
                }),
            "ExplainRule",
            "What a review rule checks and its formula."),
    ];

    /// <summary>Static rule descriptions from doc 05.</summary>
    public static readonly IReadOnlyDictionary<string, string> RuleText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [RuleNames.AllocationDrift] = "Fires when an asset class's actual % differs from its target % by more than the drift band. Watch above the band, Act above twice the band. Equity, Debt and Gold are always checked; Other when it has a value or a target.",
        [RuleNames.StockConcentration] = "Fires when a single equity stock is more than the max single stock % of the whole portfolio. Watch above the cap, Act above 1.5 times the cap.",
        [RuleNames.FundConcentration] = "Fires when a single fund is more than the max single fund % of the whole portfolio. Watch above the cap, Act above 1.5 times the cap.",
        [RuleNames.RegularPlan] = "Fires (Watch) for each Regular-plan fund when the plan prefers Direct plans. It names the Direct plan of the same scheme when one exists.",
        [RuleNames.SipNotActive] = "Fires (Watch) for each SIP whose status isn't ACTIVE, e.g. PAUSED or CANCELLED.",
        [RuleNames.SipFailedOrder] = "Fires (Watch) for a fund with at least one SIP order REJECTED or CANCELLED in the last 7 days.",
        [RuleNames.SipOverBudget] = "Fires (Watch) when the monthly equivalent of all active SIPs is above the monthly SIP budget. Weekly SIPs count 52/12 times a month, fortnightly 26/12, quarterly one third.",
        [RuleNames.StaleNav] = "Fires (Info) when any fund's price date is older than the allowed number of days before today.",
        [RuleNames.EmptyPortfolio] = "Fires (Info) when the portfolio's total value is zero.",
    };

    private static async Task<string> Run(ToolRun run, AiSettings ai, string name, Func<Task<(object Result, int Count)>> body)
    {
        using var activity = KiteClient.Activity.StartActivity($"tool.{name}");
        activity?.SetTag("tool.name", name);
        if (!run.Used.Contains(name))
            run.Used.Add(name);

        var (result, count) = await body();
        activity?.SetTag("tool.result_count", count);

        var redacted = Redactor.Redact(JsonSerializer.SerializeToNode(result, Json), ai.ShareAmounts)?.ToJsonString(Json) ?? "{}";
        run.Outputs.Add(redacted);
        return redacted;
    }
}
