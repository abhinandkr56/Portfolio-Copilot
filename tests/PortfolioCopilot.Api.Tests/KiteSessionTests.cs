using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Tests;

[Collection(ApiCollection.Name)]
public class KiteSessionTests(ApiFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync() => fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private async Task<SessionResponse> SessionAsync(HttpClient browser) =>
        (await browser.GetFromJsonAsync<SessionResponse>("/api/session", Web))!;

    [Fact]
    public async Task Not_connected_session_is_200_with_mode()
    {
        var response = await fx.Browser().GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = await response.Content.ReadFromJsonAsync<SessionResponse>(Web);
        Assert.Equal(new SessionResponse(false, "Mock"), session);
    }

    [Fact]
    public async Task Login_round_trip_lands_on_the_web_app_connected()
    {
        var browser = fx.Browser();

        var landing = await fx.LoginAsync(browser);

        Assert.Equal("http://127.0.0.1:3000/?connected=1", landing.ToString());
        var session = await SessionAsync(browser);
        Assert.True(session.Connected);
        Assert.Equal("Mock", session.Mode);
        Assert.Equal("AB***34", session.UserIdMasked);
        Assert.Equal("Demo User", session.UserName);
        Assert.Equal(Ist.NextKiteExpiry(session.CreatedUtc!.Value), session.ExpiresUtc);
    }

    [Fact]
    public async Task Exchange_sends_the_sha256_checksum_of_key_token_and_secret()
    {
        await fx.LoginAsync(fx.Browser());

        var creds = fx.Api.Services.GetRequiredService<KiteCredentials>();
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(creds.ApiKey + "mock-request-token" + creds.ApiSecret)));
        var exchange = Assert.Single(await fx.MockRequestsAsync(), r => r is { Method: "POST", Path: "/session/token" });
        Assert.Equal(200, exchange.Status);
        Assert.Equal(expected, exchange.Checksum);
    }

    [Fact]
    public async Task Access_token_is_stored_encrypted()
    {
        await fx.LoginAsync(fx.Browser());

        using var scope = fx.Api.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().KiteSessions.AsNoTracking().SingleAsync();
        Assert.DoesNotContain("mock-access-token", row.AccessTokenProtected);
        Assert.Equal("AB***34", row.UserIdMasked);
        Assert.Equal("mock-access-token", await scope.ServiceProvider.GetRequiredService<KiteSessionStore>().GetAccessTokenAsync());
    }

    [Fact]
    public async Task Login_on_localhost_first_moves_to_127_0_0_1_so_the_state_cookie_matches()
    {
        var browser = fx.Api.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("http://localhost:5080") });

        var response = await browser.GetAsync("/kite/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://127.0.0.1:5080/kite/login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Login_sets_an_httponly_lax_state_cookie_and_passes_it_as_redirect_params()
    {
        var response = await fx.Browser().GetAsync("/kite/login");

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.StartsWith(KiteEndpoints.StateCookie + "=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        var state = cookie.Split(';')[0].Split('=', 2)[1];
        Assert.Contains("redirect_params=" + Uri.EscapeDataString("state=" + state), response.Headers.Location!.Query);
    }

    [Theory]
    [InlineData("/kite/callback?request_token=mock-request-token&status=success&state=forged", "state-mismatch")]
    [InlineData("/kite/callback?request_token=mock-request-token&status=success", "state-mismatch")]
    [InlineData("/kite/callback?status=cancelled", "login-cancelled")]
    public async Task Callback_without_a_matching_state_is_rejected(string callback, string reason)
    {
        var browser = fx.Browser();
        await browser.GetAsync("/kite/login"); // sets a real state cookie that the forged callback doesn't match

        var response = await browser.GetAsync(callback);

        Assert.Equal($"http://127.0.0.1:3000/?connected=0&reason={reason}", response.Headers.Location!.ToString());
        Assert.False((await SessionAsync(browser)).Connected);
        Assert.DoesNotContain(await fx.MockRequestsAsync(), r => r.Path == "/session/token");
    }

    [Fact]
    public async Task Token_exception_becomes_401_kite_session_required_and_deletes_the_session()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/test/kite/holdings")).StatusCode);

        await fx.MockClient().PostAsync("/__fail/token", null);
        var response = await browser.GetAsync("/test/kite/holdings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("kite-session-required", problem.GetProperty("type").GetString());
        Assert.False((await SessionAsync(browser)).Connected);
    }

    [Fact]
    public async Task No_session_is_401_without_calling_kite()
    {
        var response = await fx.Browser().GetAsync("/test/kite/holdings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(fx.KiteTraffic.Requests);
    }

    [Fact]
    public async Task Session_past_its_expiry_counts_as_not_connected()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);
        using (var scope = fx.Api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CopilotDbContext>();
            await db.KiteSessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }

        Assert.False((await SessionAsync(browser)).Connected);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/test/kite/holdings")).StatusCode);
    }

    [Fact]
    public async Task Rate_limit_is_retried_exactly_once_then_503()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);
        fx.KiteTraffic.Requests.Clear();

        await fx.MockClient().PostAsync("/__fail/ratelimit?count=2", null);
        var response = await browser.GetAsync("/test/kite/holdings");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("kite-unavailable", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString());
        Assert.Equal(2, fx.KiteTraffic.Requests.Count(r => r.Path == "/portfolio/holdings"));
        Assert.True((await SessionAsync(browser)).Connected); // a rate limit doesn't end the session
    }

    [Fact]
    public async Task One_rate_limit_then_success_returns_the_data()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);
        fx.KiteTraffic.Requests.Clear();

        await fx.MockClient().PostAsync("/__fail/ratelimit", null);
        var response = await browser.GetAsync("/test/kite/holdings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());
        Assert.Equal(2, fx.KiteTraffic.Requests.Count(r => r.Path == "/portfolio/holdings"));
    }

    [Fact]
    public async Task Kite_down_is_503_without_retry()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);
        fx.KiteTraffic.Requests.Clear();

        await fx.MockClient().PostAsync("/__fail/down", null);
        var response = await browser.GetAsync("/test/kite/holdings");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(fx.KiteTraffic.Requests);
    }

    [Fact]
    public async Task Logout_deletes_at_kite_and_locally()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);

        var response = await browser.PostAsync("/api/session/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(await fx.MockRequestsAsync(), r => r is { Method: "DELETE", Path: "/session/token", Status: 200 });
        Assert.False((await SessionAsync(browser)).Connected);
    }

    [Fact]
    public async Task Logout_still_clears_the_local_session_when_kite_is_down()
    {
        var browser = fx.Browser();
        await fx.LoginAsync(browser);
        await fx.MockClient().PostAsync("/__fail/down", null);

        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsync("/api/session/logout", null)).StatusCode);
        Assert.False((await SessionAsync(browser)).Connected);
    }

    [Fact]
    public async Task Kite_client_maps_the_demo_scenario()
    {
        await fx.LoginAsync(fx.Browser());
        using var scope = fx.Api.Services.CreateScope();
        var kite = scope.ServiceProvider.GetRequiredService<KiteClient>();

        var stocks = await kite.GetHoldingsAsync();
        var funds = await kite.GetMfHoldingsAsync();
        var sips = await kite.GetSipsAsync();
        var orders = await kite.GetMfOrdersAsync();
        var instruments = MfInstrumentsCsvParser.Parse(await kite.GetMfInstrumentsCsvAsync());

        Assert.Equal(new StockHolding("INFY", "INETEST00001", "NSE", 12, 1400, 1500, 1200), stocks[0]);
        Assert.Equal(new FundHolding("INFTEST00002", "Alpha Flexi Cap Fund - Regular Plan - Growth", "FOLIOTEST0001", 400, 50, 55, new DateOnly(2026, 11, 7), 2000), funds[0]);
        Assert.Equal(3, sips.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 4, 30, 0, TimeSpan.Zero), orders[0].OrderTimestamp);
        Assert.Equal(7, instruments.Count);
    }
}
