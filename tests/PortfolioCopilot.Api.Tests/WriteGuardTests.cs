using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PortfolioCopilot.Api.Kite;

namespace PortfolioCopilot.Api.Tests;

/// <summary>CLAUDE.md rule 1: Kite calls are GET only, except POST/DELETE /session/token (login/logout).</summary>
[Collection(ApiCollection.Name)]
public class WriteGuardTests(ApiFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync() => fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly Dictionary<string, (HttpMethod Method, string Path)> AllowedWrites = new()
    {
        [nameof(KiteClient.ExchangeTokenAsync)] = (HttpMethod.Post, "/session/token"),
        [nameof(KiteClient.LogoutAsync)] = (HttpMethod.Delete, "/session/token"),
    };

    /// <summary>
    /// Reflects over every public KiteClient method, calls each one against MockKite and records what it sent.
    /// A new method is picked up automatically, so a write call can't be added without failing this test.
    /// </summary>
    [Fact]
    public async Task Every_kite_client_method_is_get_except_session_token_create_and_delete()
    {
        await fx.LoginAsync(fx.Browser());
        var methods = typeof(KiteClient)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .ToList();
        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            fx.KiteTraffic.Requests.Clear();
            using var scope = fx.Api.Services.CreateScope();
            var kite = scope.ServiceProvider.GetRequiredService<KiteClient>();
            var args = method.GetParameters().Select(p => p.ParameterType switch
            {
                var t when t == typeof(string) => (object?)(p.Name == "requestToken" ? "mock-request-token" : "mock-access-token"),
                var t when t == typeof(CancellationToken) => CancellationToken.None,
                var t => throw new InvalidOperationException($"Teach this test how to call {method.Name}({t.Name} {p.Name})."),
            }).ToArray();

            try
            {
                if (method.Invoke(kite, args) is Task task)
                    await task;
            }
            catch (KiteException)
            {
                // a Kite-level error is fine; we only care what was sent
            }

            var sent = fx.KiteTraffic.Requests.ToList();
            Assert.True(sent.Count > 0, $"{method.Name} sent nothing");
            foreach (var (verb, path) in sent)
            {
                if (verb == HttpMethod.Get)
                    continue;
                Assert.True(AllowedWrites.TryGetValue(method.Name, out var allowed) && allowed == (verb, path),
                    $"{method.Name} sent {verb} {path}: Kite calls must be GET only");
            }
        }

        // The two allowed writes really are the only non-GETs, and each is what we expect.
        Assert.Equal(AllowedWrites.Keys.Order(), methods.Select(m => m.Name).Where(AllowedWrites.ContainsKey).Order());
        Assert.Empty(await fx.MockUnexpectedAsync());

        // Re-login for any later test that expects a session.
        await fx.LoginAsync(fx.Browser());
    }

    [Theory]
    [InlineData("GET", "/portfolio/holdings", true)]
    [InlineData("POST", "/session/token", true)]
    [InlineData("DELETE", "/session/token", true)]
    [InlineData("POST", "/orders/regular", false)]
    [InlineData("PUT", "/orders/regular/123", false)]
    [InlineData("DELETE", "/orders/regular/123", false)]
    [InlineData("POST", "/mf/orders", false)]
    [InlineData("POST", "/mf/sips", false)]
    [InlineData("PUT", "/mf/sips/1", false)]
    [InlineData("POST", "/gtt/triggers", false)]
    [InlineData("POST", "/alerts", false)]
    [InlineData("PUT", "/session/token", false)]
    [InlineData("POST", "/session/token/extra", false)]
    public void Guard_allows_only_reads_and_session_token(string method, string path, bool allowed) =>
        Assert.Equal(allowed, KiteReadOnlyGuard.IsAllowed(new HttpMethod(method), path));

    [Fact]
    public async Task Guard_blocks_a_write_sent_through_the_kite_http_client()
    {
        var http = fx.Api.Services.GetRequiredService<IHttpClientFactory>().CreateClient(typeof(KiteClient).Name);
        fx.KiteTraffic.Requests.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => http.PostAsync("orders/regular", new StringContent("")));

        Assert.Contains("read-only", ex.Message);
        Assert.Empty(fx.KiteTraffic.Requests); // never left the process
    }

    [Fact]
    public async Task Mock_returns_404_for_any_other_route_and_logs_it()
    {
        var response = await fx.MockClient().PostAsync("/orders/regular", new StringContent(""));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("POST /orders/regular", await fx.MockUnexpectedAsync());
    }
}
