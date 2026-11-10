using System.Net.Http.Json;

namespace PortfolioCopilot.Api.Tests;

[Collection(ApiCollection.Name)]
public class PingTests(ApiFixture fx)
{
    [Fact]
    public async Task Ping_returns_ok_true()
    {
        var body = await fx.Browser().GetFromJsonAsync<PingResponse>("/api/ping");

        Assert.NotNull(body);
        Assert.True(body.Ok);
    }
}
