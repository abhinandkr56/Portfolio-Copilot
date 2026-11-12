using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Api.Portfolio;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Ai;

/// <summary>In-memory Ask conversations: one Agent Framework session per conversationId, 2-hour sliding expiry (doc 03).</summary>
public sealed class Conversations(IMemoryCache cache)
{
    public static readonly TimeSpan Expiry = TimeSpan.FromHours(2);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public AgentSession? Get(string id) => cache.TryGetValue(Key(id), out AgentSession? s) ? s : null;

    public void Set(string id, AgentSession session) =>
        cache.Set(Key(id), session, new MemoryCacheEntryOptions { SlidingExpiration = Expiry });

    /// <summary>One turn at a time per conversation.</summary>
    public SemaphoreSlim Lock(string id) => _locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    private static string Key(string id) => "ask:" + id;
}

/// <summary>
/// Ask (doc 06): a Microsoft Agent Framework <see cref="ChatClientAgent"/> with the six read-only tools and per-conversation
/// history (last 20 messages). Guardrails → one retry with feedback → facts-only fallback. Always ends with the disclaimer.
/// </summary>
public sealed class AskService(
    IChatClient chat,
    AiSettings ai,
    PortfolioService portfolio,
    Conversations conversations,
    CopilotDbContext db,
    TimeProvider time,
    ILoggerFactory loggerFactory)
{
    public const int MaxAttempts = 2;
    public const int MaxHistoryMessages = 20;
    public const int MaxQuestionLength = 2000;

    public async Task<AskResponse> AskAsync(AskRequest request, CancellationToken ct)
    {
        if (!ai.KeyConfigured)
            throw AiUnavailableException.KeyMissing(ai);

        // Fails fast with 401 kite-session-required before any LLM call, and warms the 5-minute cache for the tools.
        await portfolio.GetAsync(ct);

        var conversationId = Guid.TryParse(request.ConversationId, out var g) ? g.ToString() : Guid.NewGuid().ToString();
        var run = new ToolRun();
        var agent = BuildAgent(run);

        var gate = conversations.Lock(conversationId);
        await gate.WaitAsync(ct);
        try
        {
            var session = conversations.Get(conversationId) ?? await agent.CreateSessionAsync(ct);
            conversations.Set(conversationId, session);

            string answer;
            var passed = false;
            try
            {
                var message = request.Message.Trim();
                answer = "";
                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    answer = (await agent.RunAsync(message, session, cancellationToken: ct)).Text ?? "";
                    // Allowed numbers: every tool result this turn, plus the user's own question (echoing it isn't inventing).
                    var guard = Guardrails.Check(answer, [.. run.Outputs, request.Message]);
                    if (guard.Passed)
                    {
                        passed = true;
                        break;
                    }
                    message = guard.Feedback!;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                if (ex is Kite.KiteException or Kite.KiteUnavailableException)
                    throw;
                throw AiUnavailableException.From(ex, ai);
            }

            if (!passed)
                answer = await FallbackAsync(ct);

            var final = Disclaimer.Ensure(answer);
            await AuditAsync(request.Message, run.Used, final, passed, ct);
            return new AskResponse(conversationId, final, run.Used, ai.Provider, ai.Model, passed);
        }
        finally
        {
            gate.Release();
        }
    }

    private AIAgent BuildAgent(ToolRun run)
    {
        var agent = new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = "PortfolioCopilot",
            ChatOptions = new ChatOptions
            {
                Instructions = Prompts.System,
                Tools = PortfolioTools.Create(portfolio, ai, run),
                AllowMultipleToolCalls = false, // Groq gpt-oss-120b: no parallel tool calls
            },
#pragma warning disable MEAI001 // MessageCountingChatReducer is experimental in Microsoft.Extensions.AI 10.11.0
            ChatHistoryProvider = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
            {
                ChatReducer = new MessageCountingChatReducer(MaxHistoryMessages), // keep the last 20 messages (doc 03)
            }),
#pragma warning restore MEAI001
        }, loggerFactory);

        return agent.AsBuilder()
            .UseOpenTelemetry(sourceName: "PortfolioCopilot", configure: otel => otel.EnableSensitiveData = ai.TraceContent)
            .Build();
    }

    /// <summary>Doc 06: "I couldn't produce an answer I can verify. Here are the facts:" + the findings' messages.</summary>
    private async Task<string> FallbackAsync(CancellationToken ct)
    {
        var s = await portfolio.GetAsync(ct);
        var findings = portfolio.Evaluate(s);
        var facts = findings.Count == 0 ? "No findings: the portfolio is in line with the plan." : string.Join(" ", findings.Select(f => f.Message));
        return $"{Prompts.AskFallbackIntro} {facts}";
    }

    private async Task AuditAsync(string question, IReadOnlyList<string> toolsUsed, string answer, bool passed, CancellationToken ct)
    {
        db.ReviewRuns.Add(new ReviewRunRow
        {
            Utc = time.GetUtcNow(),
            Kind = "ask",
            Provider = ai.Provider,
            Model = ai.Model,
            FindingsJson = JsonSerializer.Serialize(new { question, toolsUsed }, PlanStore.Json),
            AnswerText = answer,
            NumberCheckPassed = passed,
        });
        await db.SaveChangesAsync(ct);
    }
}
