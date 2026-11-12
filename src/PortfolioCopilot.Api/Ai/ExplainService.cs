using System.Text.Json;
using Microsoft.Extensions.AI;
using PortfolioCopilot.Api.Portfolio;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Ai;

/// <summary>
/// Explain (doc 06): one chat call, no tools, over the redacted review. Guardrails → one retry with feedback → C# fallback.
/// The LLM never computes: it only words the numbers it was given.
/// </summary>
public sealed class ExplainService(IChatClient chat, AiSettings ai, ILogger<ExplainService> logger)
{
    public const int MaxAttempts = 2;

    public async Task<(string? Summary, AiInfo Ai)> ExplainAsync(Plan plan, PortfolioReview review, CancellationToken ct)
    {
        if (!ai.KeyConfigured)
            return (null, new AiInfo(ai.Provider, ai.Model, null, 0, AiUnavailableException.KeyMissing(ai).Message));

        var input = new
        {
            plan = new { plan.TargetAllocation, plan.DriftBandPoints, plan.MaxSingleStockPercent, plan.MaxSingleFundPercent, plan.PreferDirectPlans, plan.MonthlySipBudget },
            review.NavDate,
            allocation = review.Allocation.Classes,
            review.Findings,
        };
        var json = Redactor.Redact(JsonSerializer.SerializeToNode(input, PlanStore.Json), ai.ShareAmounts)!.ToJsonString(PlanStore.Json);

        List<ChatMessage> messages =
        [
            new(ChatRole.System, Prompts.System),
            new(ChatRole.User, $"{Prompts.ExplainInstruction}\n\n{json}"),
        ];

        try
        {
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var answer = (await chat.GetResponseAsync(messages, cancellationToken: ct)).Text ?? "";
                var guard = Guardrails.Check(answer, [json]);
                if (guard.Passed)
                    return (Disclaimer.Ensure(answer), new AiInfo(ai.Provider, ai.Model, true, attempt, null));

                logger.LogWarning("Explain attempt {Attempt} failed the guardrails: {Phrases} {Numbers}",
                    attempt, guard.ForbiddenPhrases.Count, string.Join(", ", guard.UnknownNumbers));
                messages.Add(new ChatMessage(ChatRole.Assistant, answer));
                messages.Add(new ChatMessage(ChatRole.User, guard.Feedback!));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var error = AiUnavailableException.From(ex, ai);
            logger.LogWarning("Explain failed: {Error}", error.Message);
            return (null, new AiInfo(ai.Provider, ai.Model, null, null, error.Message));
        }

        // Both attempts failed the guardrails: the verified facts instead of the AI summary.
        var fallback = review.Findings.Count == 0
            ? "No findings: the portfolio is in line with the plan."
            : string.Join(" ", review.Findings.Select(f => f.Message));
        return (Disclaimer.Ensure(fallback), new AiInfo(ai.Provider, ai.Model, false, MaxAttempts, null));
    }
}
