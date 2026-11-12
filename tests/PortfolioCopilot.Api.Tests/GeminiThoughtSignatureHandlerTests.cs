using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using PortfolioCopilot.Api.Ai;

namespace PortfolioCopilot.Api.Tests;

public class GeminiThoughtSignatureHandlerTests
{
    private sealed class Inner(Func<string, string> reply) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            Bodies.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply(body), Encoding.UTF8, "application/json") };
        }
    }

    private const string Url = "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions";

    [Fact]
    public async Task Sends_the_tool_call_signature_back_on_the_next_turn()
    {
        var inner = new Inner(_ => """
            {"choices":[{"message":{"role":"assistant","tool_calls":[
              {"id":"call-1","type":"function","function":{"name":"GetPlan","arguments":"{}"},
               "extra_content":{"google":{"thought_signature":"sig-abc"}}}]},"finish_reason":"tool_calls"}]}
            """);
        using var http = new HttpClient(new GeminiThoughtSignatureHandler { InnerHandler = inner });

        var first = await http.PostAsync(Url, new StringContent("""{"messages":[{"role":"user","content":"Plan?"}]}""", Encoding.UTF8, "application/json"));
        Assert.Contains("sig-abc", await first.Content.ReadAsStringAsync()); // the response is still readable by the SDK

        await http.PostAsync(Url, new StringContent("""
            {"messages":[{"role":"user","content":"Plan?"},
              {"role":"assistant","tool_calls":[{"id":"call-1","type":"function","function":{"name":"GetPlan","arguments":"{}"}}]},
              {"role":"tool","tool_call_id":"call-1","content":"{}"}]}
            """, Encoding.UTF8, "application/json"));

        var sent = JsonNode.Parse(inner.Bodies[1])!;
        Assert.Equal("sig-abc", sent["messages"]![1]!["tool_calls"]![0]!["extra_content"]!["google"]!["thought_signature"]!.GetValue<string>());
    }

    [Fact]
    public async Task Leaves_requests_without_known_calls_unchanged()
    {
        var inner = new Inner(_ => """{"choices":[{"message":{"role":"assistant","content":"Hi"}}]}""");
        using var http = new HttpClient(new GeminiThoughtSignatureHandler { InnerHandler = inner });

        await http.PostAsync(Url, new StringContent("""{"messages":[{"role":"assistant","tool_calls":[{"id":"unknown","type":"function"}]}]}""", Encoding.UTF8, "application/json"));

        Assert.Null(JsonNode.Parse(inner.Bodies[0])!["messages"]![0]!["tool_calls"]![0]!["extra_content"]);
    }
}
