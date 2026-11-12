using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Core;

namespace PortfolioCopilot.Api.Tests;

/// <summary>Explain and Ask with a fake IChatClient (doc 08 §2): pass, invented number → retry → fallback, forbidden phrase → fallback.</summary>
[Collection(ApiCollection.Name)]
public class AiTests(ApiFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync() => fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private const string GoodExplain =
        "Equity is 84.5% against a target of 70.0%, a drift of +14.5 points, more than twice the band of 5 points. " +
        "INFY is 18.0% of the portfolio; your cap is 10.0%. Debt is 11.5% and Gold is 4.0%, both below target. " +
        "Two funds are Regular plans: Consider Direct plan. One SIP is paused: Check SIP mandate.";

    private async Task<HttpClient> ConnectedAsync()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);
        return browser;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpResponseMessage> ExplainAsync(HttpClient browser) => browser.PostAsJsonAsync("/api/review", new { explain = true });

    private Task<HttpResponseMessage> AskAsync(HttpClient browser, string message, string? conversationId = null) =>
        browser.PostAsJsonAsync("/api/ask", new { conversationId, message });

    // ---------------------------------------------------------------- Explain

    [Fact]
    public async Task Explain_passes_the_guardrails_and_ends_with_the_disclaimer()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says(GoodExplain);

        var review = await JsonAsync(await ExplainAsync(browser));

        var summary = review.GetProperty("summary").GetString()!;
        Assert.StartsWith("Equity is 84.5%", summary);
        Assert.EndsWith(Disclaimer.Text, summary);
        var ai = review.GetProperty("ai");
        Assert.Equal("Groq", ai.GetProperty("provider").GetString());
        Assert.Equal("openai/gpt-oss-120b", ai.GetProperty("model").GetString());
        Assert.True(ai.GetProperty("numberCheckPassed").GetBoolean());
        Assert.Equal(1, ai.GetProperty("attempts").GetInt32());
        Assert.Equal(8, review.GetProperty("findings").GetArrayLength()); // the findings are unchanged by the AI
    }

    [Fact]
    public async Task Explain_sends_the_system_prompt_and_redacted_review_without_tools()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says(GoodExplain);

        await ExplainAsync(browser);

        var (messages, options) = Assert.Single(fx.Llm.Requests);
        Assert.Equal(ChatRole.System, messages[0].Role);
        Assert.Contains("You are Portfolio Copilot", messages[0].Text);
        Assert.Contains("Summarise the review below in 4-8 sentences", messages[1].Text);
        var sent = FakeChatClient.AllText(messages);
        Assert.Contains("84.5", sent);
        Assert.DoesNotContain("18000", sent);        // amounts removed (AI:ShareAmounts=false)
        Assert.DoesNotContain("₹5,000", sent);       // ...also inside messages
        Assert.DoesNotContain("\"value\"", sent);
        Assert.DoesNotContain("40000", sent);        // the plan's SIP budget is an amount too
        Assert.Null(options?.Tools);
    }

    [Fact]
    public async Task Explain_with_an_invented_number_retries_once_then_falls_back_to_the_findings()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says("Equity is 91.2% of your portfolio.").Says("Equity is now 93.7%.");

        var review = await JsonAsync(await ExplainAsync(browser));

        var requests = fx.Llm.Requests.ToList();
        Assert.Equal(2, requests.Count);
        Assert.Contains("numbers not present in the data: 91.2", requests[1].Messages.Last().Text);
        var summary = review.GetProperty("summary").GetString()!;
        Assert.StartsWith("Equity is 84.5% against a target of 70.0% (drift +14.5 points, band ±5.0).", summary);
        Assert.DoesNotContain("91.2", summary);
        Assert.EndsWith(Disclaimer.Text, summary);
        Assert.False(review.GetProperty("ai").GetProperty("numberCheckPassed").GetBoolean());
        Assert.Equal(2, review.GetProperty("ai").GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task Explain_that_recovers_on_the_retry_passes()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says("Equity is 91.2%.").Says(GoodExplain);

        var ai = (await JsonAsync(await ExplainAsync(browser))).GetProperty("ai");

        Assert.True(ai.GetProperty("numberCheckPassed").GetBoolean());
        Assert.Equal(2, ai.GetProperty("attempts").GetInt32());
    }

    [Fact]
    public async Task Explain_with_a_forbidden_phrase_falls_back()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says("You should sell INFY.").Says("I recommend buying more TCS.");

        var review = await JsonAsync(await ExplainAsync(browser));

        Assert.Contains("isn't allowed", fx.Llm.Requests.ToList()[1].Messages.Last().Text);
        var summary = review.GetProperty("summary").GetString()!;
        Assert.DoesNotContain("sell", summary, StringComparison.OrdinalIgnoreCase);
        Assert.False(review.GetProperty("ai").GetProperty("numberCheckPassed").GetBoolean());
    }

    [Fact]
    public async Task Explain_provider_failure_still_returns_the_findings_with_a_short_error()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Throws(new HttpRequestException("connection refused (secret detail)"));

        var response = await ExplainAsync(browser);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var review = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, review.GetProperty("summary").ValueKind);
        Assert.Equal("Groq couldn't be reached.", review.GetProperty("ai").GetProperty("error").GetString());
        Assert.Equal(8, review.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public async Task Explain_is_audited_with_provider_answer_and_check_result()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says(GoodExplain);

        await ExplainAsync(browser);

        using var scope = fx.Api.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().ReviewRuns.OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal(("review", "Groq", "openai/gpt-oss-120b", true), (row.Kind, row.Provider, row.Model, row.NumberCheckPassed));
        Assert.EndsWith(Disclaimer.Text, row.AnswerText);
    }

    // ---------------------------------------------------------------- Ask

    [Fact]
    public async Task Ask_uses_a_tool_and_answers_with_verified_numbers()
    {
        var browser = await ConnectedAsync();
        fx.Llm.CallsTool("GetFindings").Says("INFY is 18.0% of the portfolio; your cap is 10.0%. Review it against your plan.");

        var answer = await JsonAsync(await AskAsync(browser, "Is any stock too big?"));

        Assert.Equal(["GetFindings"], answer.GetProperty("toolsUsed").EnumerateArray().Select(t => t.GetString()));
        Assert.True(answer.GetProperty("numberCheckPassed").GetBoolean());
        Assert.StartsWith("INFY is 18.0%", answer.GetProperty("answer").GetString());
        Assert.EndsWith(Disclaimer.Text, answer.GetProperty("answer").GetString());
        Assert.True(Guid.TryParse(answer.GetProperty("conversationId").GetString(), out _));
        Assert.Equal("Groq", answer.GetProperty("provider").GetString());

        // The agent offered all six tools, one call at a time, with the system prompt as instructions.
        var first = fx.Llm.Requests.First();
        Assert.Equal(["ComputeAllocation", "ExplainRule", "GetFindings", "GetHoldings", "GetPlan", "GetSips"],
            first.Options!.Tools!.Select(t => t.Name).Order());
        Assert.False(first.Options.AllowMultipleToolCalls);
        Assert.Contains("You are Portfolio Copilot", first.Options.Instructions ?? FakeChatClient.AllText(first.Messages));
    }

    [Fact]
    public async Task Tool_results_sent_to_the_model_are_redacted()
    {
        var browser = await ConnectedAsync();
        fx.Llm.CallsTool("GetHoldings", new() { ["kind"] = "all" }).CallsTool("GetSips").CallsTool("GetPlan").Says("You hold 9 holdings.");

        var answer = await JsonAsync(await AskAsync(browser, "What do I hold?"));

        Assert.Equal(["GetHoldings", "GetSips", "GetPlan"], answer.GetProperty("toolsUsed").EnumerateArray().Select(t => t.GetString()));
        var sent = FakeChatClient.AllText(fx.Llm.Requests.Last().Messages);
        Assert.Contains("INFY", sent);
        Assert.Contains("\"percentOfTotal\":18", sent);
        foreach (var hidden in new[] { "\"value\"", "\"pnl\"", "instalmentAmount", "activeMonthlyEquivalent", "monthlySipBudget", "sipId", "orderId", "FOLIOTEST", "SIPTEST", "MFORDTEST" })
            Assert.DoesNotContain(hidden, sent);
    }

    [Fact]
    public async Task Should_I_sell_INFY_with_a_forbidden_answer_falls_back_to_the_facts()
    {
        var browser = await ConnectedAsync();
        fx.Llm.CallsTool("GetFindings").Says("You should sell INFY.").Says("Sell INFY now.");

        var answer = await JsonAsync(await AskAsync(browser, "Should I sell INFY?"));

        var text = answer.GetProperty("answer").GetString()!;
        Assert.StartsWith("I couldn't produce an answer I can verify. Here are the facts:", text);
        Assert.Contains("INFY is 18.0% of the portfolio; your cap is 10.0%.", text);
        Assert.DoesNotContain("You should sell", text);
        Assert.EndsWith(Disclaimer.Text, text);
        Assert.False(answer.GetProperty("numberCheckPassed").GetBoolean());
    }

    [Fact]
    public async Task Ask_with_an_invented_number_retries_once_then_falls_back()
    {
        var browser = await ConnectedAsync();
        fx.Llm.CallsTool("ComputeAllocation").Says("Equity is 91.2%.").Says("Equity is 93.7%.");

        var answer = await JsonAsync(await AskAsync(browser, "Why is my equity allocation above target?"));

        Assert.Contains(fx.Llm.Requests, r => FakeChatClient.AllText(r.Messages).Contains("numbers not present in the data: 91.2"));
        Assert.StartsWith(Ai.Prompts.AskFallbackIntro, answer.GetProperty("answer").GetString());
        Assert.False(answer.GetProperty("numberCheckPassed").GetBoolean());
    }

    [Fact]
    public async Task Ask_may_repeat_numbers_from_the_question()
    {
        var browser = await ConnectedAsync();
        fx.Llm.CallsTool("ComputeAllocation").Says("Your equity target is 70.0%, not 75; equity is 84.5%.");

        var answer = await JsonAsync(await AskAsync(browser, "Is my equity target 75?"));

        Assert.True(answer.GetProperty("numberCheckPassed").GetBoolean());
    }

    [Fact]
    public async Task Conversation_history_is_kept_per_conversation_id()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says("Your plan targets 70.0% equity.").Says("Debt targets 20.0%.");

        var first = await JsonAsync(await AskAsync(browser, "What is my equity target?"));
        var id = first.GetProperty("conversationId").GetString();
        var second = await JsonAsync(await AskAsync(browser, "And debt?", id));

        Assert.Equal(id, second.GetProperty("conversationId").GetString());
        var sent = FakeChatClient.AllText(fx.Llm.Requests.Last().Messages);
        Assert.Contains("What is my equity target?", sent);
        Assert.Contains("Your plan targets 70.0% equity.", sent);
        Assert.Contains("And debt?", sent);
    }

    [Fact]
    public async Task A_new_conversation_starts_empty()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says("Your plan targets 70.0% equity.").Says("Debt targets 20.0%.");

        await AskAsync(browser, "What is my equity target?");
        await AskAsync(browser, "And debt?");

        Assert.DoesNotContain("What is my equity target?", FakeChatClient.AllText(fx.Llm.Requests.Last().Messages));
    }

    [Fact]
    public async Task Ask_needs_a_kite_session_and_never_calls_the_llm_without_one()
    {
        var response = await AskAsync(fx.Browser(), "How am I doing?");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fx.Llm.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_question_is_400(string message)
    {
        var browser = await ConnectedAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(browser, message)).StatusCode);
        Assert.Empty(fx.Llm.Requests);
    }

    [Fact]
    public async Task Ask_provider_failure_is_503_ai_unavailable_with_a_short_message()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Throws(new TaskCanceledException("timeout with internal detail"));

        var response = await AskAsync(browser, "How am I doing?");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ai-unavailable", problem.GetProperty("type").GetString());
        Assert.Equal("Groq didn't answer in time.", problem.GetProperty("detail").GetString());
        Assert.DoesNotContain("internal detail", problem.GetRawText());
    }

    [Fact]
    public async Task Ask_is_audited()
    {
        var browser = await ConnectedAsync();
        fx.Llm.CallsTool("GetPlan").Says("Your plan targets 70.0% equity."); // the number must come from a tool result

        await AskAsync(browser, "What is my equity target?");

        using var scope = fx.Api.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().ReviewRuns.OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal("ask", row.Kind);
        Assert.Contains("What is my equity target?", row.FindingsJson);
        Assert.EndsWith(Disclaimer.Text, row.AnswerText);
        Assert.True(row.NumberCheckPassed);
    }

    [Fact]
    public async Task A_number_without_a_tool_result_is_not_trusted()
    {
        var browser = await ConnectedAsync();
        fx.Llm.Says("Your plan targets 70.0% equity.").Says("It is 70.0%.");

        var answer = await JsonAsync(await AskAsync(browser, "What is my equity target?"));

        Assert.False(answer.GetProperty("numberCheckPassed").GetBoolean());
    }

    [Fact]
    public async Task Session_reports_the_ai_provider_and_model_for_the_footer()
    {
        var session = await JsonAsync(await fx.Browser().GetAsync("/api/session"));

        Assert.Equal("Groq", session.GetProperty("aiProvider").GetString());
        Assert.Equal("openai/gpt-oss-120b", session.GetProperty("aiModel").GetString());
    }
}
