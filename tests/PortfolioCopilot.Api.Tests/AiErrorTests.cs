using PortfolioCopilot.Api.Ai;

namespace PortfolioCopilot.Api.Tests;

public class AiErrorTests
{
    private static readonly AiSettings Groq = new("Groq", "openai/gpt-oss-120b", true, false, "GROQ_API_KEY");

    [Fact]
    public void Retry_aggregate_is_unwrapped_to_the_real_cause() =>
        Assert.Equal("Groq didn't answer in time.",
            AiUnavailableException.From(new AggregateException(new HttpRequestException("first"), new TaskCanceledException("timeout")), Groq).Message);

    [Fact]
    public void Unreachable_provider() =>
        Assert.Equal("Groq couldn't be reached.", AiUnavailableException.From(new HttpRequestException("refused"), Groq).Message);

    [Fact]
    public void Missing_key_names_the_variable_not_the_value() =>
        Assert.Equal("No API key for Groq: set GROQ_API_KEY and restart.", AiUnavailableException.KeyMissing(Groq).Message);
}
