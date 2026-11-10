extern alias mockkite;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Api.Kite;
using MockKiteMarker = mockkite::PortfolioCopilot.MockKite.MockKiteMarker;
using MockRequest = mockkite::PortfolioCopilot.MockKite.MockRequest;

namespace PortfolioCopilot.Api.Tests;

/// <summary>MockKite hosted in memory.</summary>
public sealed class MockKiteFactory : WebApplicationFactory<MockKiteMarker>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}

/// <summary>Records every request KiteClient sends (method + path + whether a query string was present).</summary>
public sealed class RecordingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    public ConcurrentQueue<(HttpMethod Method, string Path)> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Enqueue((request.Method, request.RequestUri!.AbsolutePath));
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Captures every log line the Api writes, after redaction.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<string> Lines { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> kv ? string.Join(" ", kv.Select(p => $"{p.Key}={p.Value}")) : "";
            owner.Lines.Enqueue($"{category}: {formatter(state, exception)} | {values} | {exception}");
        }
    }
}

/// <summary>
/// The Api in memory, with SQLite in place of Postgres and KiteClient wired to the in-memory MockKite.
/// Browser-like clients don't follow redirects, so tests can walk the login round trip hop by hop.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Origin = "http://127.0.0.1:5080";

    private readonly SqliteConnection _db = new("DataSource=:memory:");
    private WebApplicationFactory<Program>? _api;

    public MockKiteFactory Mock { get; } = new();
    public RecordingHandler KiteTraffic { get; private set; } = null!;
    public CapturingLoggerProvider Logs { get; } = new();

    public WebApplicationFactory<Program> Api => _api ?? throw new InvalidOperationException("Not started.");

    public async Task InitializeAsync()
    {
        await _db.OpenAsync();
        KiteTraffic = new RecordingHandler(Mock.Server.CreateHandler());

        _api = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("ConnectionStrings:copilotdb", "Host=unused;Database=unused");
            b.UseSetting("Kite:Mode", "Mock");
            b.ConfigureLogging(l => l.AddProvider(Logs));
            b.ConfigureTestServices(services =>
            {
                // Drop the Npgsql registrations (options, pool, lease, configuration callbacks) and use SQLite instead.
                foreach (var d in services.Where(d => d.ServiceType == typeof(CopilotDbContext)
                             || (d.ServiceType.IsGenericType && d.ServiceType.GenericTypeArguments.Contains(typeof(CopilotDbContext)))).ToList())
                    services.Remove(d);
                services.AddDbContext<CopilotDbContext>(o => o.UseSqlite(_db));

                services.AddHttpClient<KiteClient>().ConfigurePrimaryHttpMessageHandler(() => KiteTraffic);
                services.AddTransient<IStartupFilter, KiteProbeStartupFilter>();
            });
        });

        using var scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().Database.EnsureCreatedAsync();
    }

    public HttpClient Browser() =>
        Api.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri(Origin) });

    public HttpClient MockClient() =>
        Mock.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://127.0.0.1:5100") });

    /// <summary>Clears the session, the mock's scenario/failures/logs and the recorded traffic.</summary>
    public async Task ResetAsync()
    {
        using (var scope = Api.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<KiteSessionStore>().DeleteAsync();
            (scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>() as Microsoft.Extensions.Caching.Memory.MemoryCache)?.Clear();
        }
        (await MockClient().PostAsync("/__reset", null)).EnsureSuccessStatusCode();
        KiteTraffic.Requests.Clear();
    }

    /// <summary>Walks /kite/login → MockKite /connect/login → /kite/callback with one browser and returns the final redirect.</summary>
    public async Task<Uri> LoginAsync(HttpClient browser)
    {
        var toKite = await browser.GetAsync("/kite/login");
        Assert.Equal(HttpStatusCode.Redirect, toKite.StatusCode);

        var toCallback = await MockClient().GetAsync(toKite.Headers.Location!.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, toCallback.StatusCode);
        Assert.Equal(Origin + "/kite/callback", toCallback.Headers.Location!.GetLeftPart(UriPartial.Path));

        var toWeb = await browser.GetAsync(toCallback.Headers.Location.PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, toWeb.StatusCode);
        return toWeb.Headers.Location!;
    }

    public async Task<MockRequest[]> MockRequestsAsync() =>
        (await MockClient().GetFromJsonAsync<MockRequest[]>("/__requests"))!;

    public async Task<string[]> MockUnexpectedAsync() =>
        (await MockClient().GetFromJsonAsync<string[]>("/__unexpected"))!;

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_api is not null)
            await _api.DisposeAsync();
        await Mock.DisposeAsync();
        await _db.DisposeAsync();
    }
}

/// <summary>
/// Test-only route <c>GET /test/kite/holdings</c>, added after the app's own pipeline so it runs inside the real
/// exception handler. It lets the tests drive the Kite error mapping before Phase 3 adds /api/holdings.
/// </summary>
public sealed class KiteProbeStartupFilter : IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
    {
        next(app);
        app.Use(async (ctx, nextMiddleware) =>
        {
            if (ctx.Request.Path != "/test/kite/holdings")
            {
                await nextMiddleware(ctx);
                return;
            }
            var holdings = await ctx.RequestServices.GetRequiredService<KiteClient>().GetHoldingsAsync(ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(new { count = holdings.Count });
        });
    };
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
