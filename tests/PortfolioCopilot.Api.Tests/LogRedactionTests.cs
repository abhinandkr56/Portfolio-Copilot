using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Api.Logging;

namespace PortfolioCopilot.Api.Tests;

public class LogRedactorTests
{
    [Theory]
    [InlineData("GET /kite/callback?request_token=abc123&status=success", "GET /kite/callback?request_token=***&status=success")]
    [InlineData("DELETE /session/token?api_key=k&access_token=tok-1", "DELETE /session/token?api_key=k&access_token=***")]
    [InlineData("api_key=k&request_token=r&checksum=0f0f", "api_key=k&request_token=***&checksum=***")]
    [InlineData("{\"access_token\":\"tok\",\"user_id\":\"AB1234\"}", "{\"access_token\":\"***\",\"user_id\":\"AB1234\"}")]
    [InlineData("{\"api_secret\": \"s3cr3t\"}", "{\"api_secret\": \"***\"}")]
    [InlineData("Authorization: token mykey:mytoken", "Authorization: token ***")]
    [InlineData("api_secret=s3cr3t", "api_secret=***")]
    [InlineData("nothing secret here: INFY 18.0%", "nothing secret here: INFY 18.0%")]
    public void Redacts_secret_values(string input, string expected) => Assert.Equal(expected, LogRedactor.Redact(input));
}

[Collection(ApiCollection.Name)]
public class LogRedactionTests(ApiFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync() => fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task No_token_secret_or_checksum_reaches_any_log()
    {
        var browser = fx.Browser();
        fx.Logs.Lines.Clear();

        await fx.LoginAsync(browser);
        await browser.GetAsync("/api/session");
        await fx.MockClient().PostAsync("/__fail/token", null);
        await browser.GetAsync("/api/holdings");
        await fx.LoginAsync(browser);
        await browser.PostAsync("/api/session/logout", null);

        // Also try to log secrets on purpose, through the app's logger factory.
        var logger = fx.Api.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Probe");
        logger.LogInformation("Exchanging {Body}", "api_key=k&request_token=mock-request-token&checksum=deadbeef");
        logger.LogError(new InvalidOperationException("failed for access_token=mock-access-token"), "Oops");

        var creds = fx.Api.Services.GetRequiredService<KiteCredentials>();
        var checksum = KiteClient.Checksum(creds.ApiKey!, "mock-request-token", creds.ApiSecret!);
        var all = string.Join("\n", fx.Logs.Lines);
        Assert.NotEmpty(fx.Logs.Lines);
        Assert.Contains("Kite GET /portfolio/holdings", all); // the app does log Kite calls...
        foreach (var secret in new[] { "mock-access-token", "mock-request-token", creds.ApiSecret!, checksum, "deadbeef" })
            Assert.DoesNotContain(secret, all); // ...but never a secret
    }
}
