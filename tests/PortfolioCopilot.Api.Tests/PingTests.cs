using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PortfolioCopilot.Api.Tests;

public class PingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Ping_returns_ok_true()
    {
        var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<PingResponse>("/api/ping");

        Assert.NotNull(body);
        Assert.True(body.Ok);
    }
}
