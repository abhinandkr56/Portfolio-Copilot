using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace PortfolioCopilot.Api.Tests;

/// <summary>
/// A scripted IChatClient: each call takes the next reply from the queue (text, a tool call, or an exception)
/// and records what it was sent. When the queue is empty it repeats the last text reply.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly ConcurrentQueue<Func<ChatResponse>> _replies = new();
    private string _lastText = "No reply scripted.";

    public ConcurrentQueue<(List<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = new();

    public void Reset()
    {
        _replies.Clear();
        Requests.Clear();
        _lastText = "No reply scripted.";
    }

    public FakeChatClient Says(string text)
    {
        _replies.Enqueue(() =>
        {
            _lastText = text;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        });
        return this;
    }

    public FakeChatClient CallsTool(string name, Dictionary<string, object?>? args = null)
    {
        _replies.Enqueue(() => new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent("call-" + Guid.NewGuid().ToString("N")[..8], name, args ?? new Dictionary<string, object?>())])));
        return this;
    }

    public FakeChatClient Throws(Exception ex)
    {
        _replies.Enqueue(() => throw ex);
        return this;
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Enqueue((messages.ToList(), options));
        var reply = _replies.TryDequeue(out var next) ? next() : new ChatResponse(new ChatMessage(ChatRole.Assistant, _lastText));
        return Task.FromResult(reply);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() { }

    /// <summary>All text sent to the model in a request, including tool results.</summary>
    public static string AllText(List<ChatMessage> messages) =>
        string.Join("\n", messages.SelectMany(m => m.Contents).Select(c => c switch
        {
            TextContent t => t.Text,
            FunctionResultContent r => r.Result?.ToString() ?? "",
            _ => "",
        }));
}
