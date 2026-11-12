using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;            // never `using OpenAI.Chat;` (CLAUDE.md)

namespace PortfolioCopilot.Api.Ai;

/// <summary>The provider in use (from <c>AI:Provider</c>) and whether its key is set. Never holds the key itself.</summary>
public sealed record AiSettings(string Provider, string Model, bool KeyConfigured, bool ShareAmounts, string KeyVariable, bool TraceContent = false)
{
    public static AiSettings FromConfig(IConfiguration config)
    {
        var provider = config["AI:Provider"] ?? "Groq";
        var section = config.GetSection($"AI:{provider}");
        var keyVariable = section["KeyVariable"] ?? "";
        var keyConfigured = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase)
                            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(keyVariable));
        return new AiSettings(provider, section["Model"] ?? "", keyConfigured, config.GetValue("AI:ShareAmounts", false), keyVariable,
            config.GetValue("AI:TraceContent", false));
    }
}

/// <summary>Builds the one IChatClient for the configured provider, in the shape CLAUDE.md prescribes, plus OpenTelemetry.</summary>
public static class ChatClientFactory
{
    public static IChatClient Create(IConfiguration config, ILoggerFactory loggerFactory)
    {
        var provider = config["AI:Provider"]!;                       // "Groq" | "Gemini" | "Ollama"
        var section  = config.GetSection($"AI:{provider}");
        var key      = Environment.GetEnvironmentVariable(section["KeyVariable"]!) ?? "ollama";
        if (key.Length == 0)
            key = "not-set"; // the AppHost passes "" when no key is set; the SDK rejects an empty key. Calls fail with 401 → a short error.

        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(section["Endpoint"]!),
            NetworkTimeout = TimeSpan.FromSeconds(60),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 1), // the SDK default (3) can hold a request for minutes when a provider is overloaded
        };
        if (provider.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
        {
            // Gemini 3 needs its tool-call thought signatures sent back (see GeminiThoughtSignatureHandler).
            var http = new HttpClient(new GeminiThoughtSignatureHandler { InnerHandler = new SocketsHttpHandler() }) { Timeout = TimeSpan.FromSeconds(120) };
            options.Transport = new HttpClientPipelineTransport(http);
        }

        IChatClient client = new OpenAIClient(new ApiKeyCredential(key), options)
            .GetChatClient(section["Model"]!)
            .AsIChatClient();

        // Traces + token counts (doc 06). Message content is off unless AI:TraceContent=true: Aspire sets
        // OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT, which would otherwise record prompts, answers and tool results.
        var traceContent = config.GetValue("AI:TraceContent", false);
        return client.AsBuilder()
            .UseOpenTelemetry(loggerFactory, configure: otel => otel.EnableSensitiveData = traceContent)
            .Build();
    }
}
