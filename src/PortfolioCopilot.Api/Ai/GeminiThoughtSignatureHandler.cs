using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;

namespace PortfolioCopilot.Api.Ai;

/// <summary>
/// Gemini 3 (OpenAI-compatible endpoint) returns <c>extra_content.google.thought_signature</c> on every tool call and
/// rejects the next turn with 400 "Function call is missing a thought_signature" unless it is sent back. The .NET OpenAI
/// client has no field for it and drops it. This handler (Gemini only) remembers each tool call's <c>extra_content</c>
/// by call id from responses and puts it back on the assistant message's tool call in later requests. The signature is
/// opaque and is never logged.
/// </summary>
public sealed class GeminiThoughtSignatureHandler : DelegatingHandler
{
    private const int MaxRemembered = 2000;
    private readonly ConcurrentDictionary<string, JsonNode> _extraByCallId = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is not null && IsChatCompletions(request))
            request.Content = await AddSignaturesAsync(request.Content, ct);

        var response = await base.SendAsync(request, ct);

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "application/json" && IsChatCompletions(request))
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            Remember(text);
            var replaced = new StringContent(text, Encoding.UTF8, "application/json");
            response.Content = replaced;
        }
        return response;
    }

    private static bool IsChatCompletions(HttpRequestMessage r) =>
        r.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) == true;

    private async Task<HttpContent> AddSignaturesAsync(HttpContent content, CancellationToken ct)
    {
        var text = await content.ReadAsStringAsync(ct);
        if (JsonNode.Parse(text) is not JsonObject body || body["messages"] is not JsonArray messages)
            return new StringContent(text, Encoding.UTF8, "application/json");

        foreach (var message in messages.OfType<JsonObject>())
        {
            if (message["role"]?.GetValue<string>() != "assistant" || message["tool_calls"] is not JsonArray calls)
                continue;
            foreach (var call in calls.OfType<JsonObject>())
            {
                if (call["extra_content"] is null && call["id"]?.GetValue<string>() is { } id && _extraByCallId.TryGetValue(id, out var extra))
                    call["extra_content"] = extra.DeepClone();
            }
        }
        return new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    private void Remember(string responseJson)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(responseJson); }
        catch (System.Text.Json.JsonException) { return; }
        if (root?["choices"] is not JsonArray choices)
            return;

        if (_extraByCallId.Count > MaxRemembered)
            _extraByCallId.Clear(); // bounded memory; old conversations simply lose their (stale) signatures

        foreach (var call in choices.Select(c => c?["message"]?["tool_calls"]).OfType<JsonArray>().SelectMany(a => a).OfType<JsonObject>())
        {
            if (call["id"]?.GetValue<string>() is { } id && call["extra_content"] is { } extra)
                _extraByCallId[id] = extra.DeepClone();
        }
    }
}
