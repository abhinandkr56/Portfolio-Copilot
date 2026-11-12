using PortfolioCopilot.Core;

namespace PortfolioCopilot.Api.Ai;

/// <summary>Runs both guardrails (doc 06) and builds the retry feedback.</summary>
public sealed record GuardOutcome(bool Passed, string? Feedback, IReadOnlyList<string> ForbiddenPhrases, IReadOnlyList<string> UnknownNumbers);

public static class Guardrails
{
    public static GuardOutcome Check(string answer, IEnumerable<string> sources)
    {
        var language = ForbiddenLanguageCheck.Check(answer);
        var numbers = NumberCheck.Check(answer, sources);
        if (language.Passed && numbers.Passed)
            return new GuardOutcome(true, null, [], []);

        var feedback = string.Join(" ", new[]
        {
            language.Passed ? null : Prompts.LanguageFeedback(language.Problems),
            numbers.Passed ? null : Prompts.NumbersFeedback(numbers.Problems),
        }.Where(x => x is not null));
        return new GuardOutcome(false, feedback, language.Problems, numbers.Problems);
    }
}

/// <summary>A provider failure turned into a short, user-facing message (doc 06 §Providers).</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner)
{
    public static AiUnavailableException From(Exception ex, AiSettings ai) => Unwrap(ex) switch
    {
        AiUnavailableException a => a,
        System.ClientModel.ClientResultException { Status: 401 or 403 } =>
            new($"{ai.Provider} rejected the API key. Check {ai.KeyVariable}.", ex),
        System.ClientModel.ClientResultException { Status: 404 } =>
            new($"{ai.Provider} doesn't have the model '{ai.Model}'.", ex),
        System.ClientModel.ClientResultException { Status: 429 } =>
            new($"{ai.Provider} is rate limiting requests; try again in a minute.", ex),
        System.ClientModel.ClientResultException c =>
            new($"{ai.Provider} returned an error ({c.Status}).", ex),
        TaskCanceledException or TimeoutException =>
            new($"{ai.Provider} didn't answer in time.", ex),
        HttpRequestException =>
            new($"{ai.Provider} couldn't be reached.", ex),
        _ => new($"The AI call failed ({ex.GetType().Name}).", ex),
    };

    /// <summary>The SDK's retry wraps the real failure in an AggregateException; report the last attempt's cause.</summary>
    private static Exception Unwrap(Exception ex)
    {
        while (ex is AggregateException { InnerExceptions.Count: > 0 } agg)
            ex = agg.InnerExceptions[^1];
        return ex;
    }

    public static AiUnavailableException KeyMissing(AiSettings ai) =>
        new($"No API key for {ai.Provider}: set {ai.KeyVariable} and restart.");
}
