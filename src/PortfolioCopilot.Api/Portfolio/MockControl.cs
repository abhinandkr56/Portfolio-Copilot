using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Portfolio;

/// <summary>
/// Talks to MockKite's test controls (<c>/__scenario</c>). Deliberately not KiteClient: these aren't Kite routes,
/// and KiteClient's read-only guard stays strict. Only used in Mock mode.
/// </summary>
public sealed class MockControl(HttpClient http)
{
    public async Task<ScenarioResponse> GetScenarioAsync(CancellationToken ct = default) =>
        (await http.GetFromJsonAsync<ScenarioResponse>("__scenario", PlanStore.Json, ct))!;

    public async Task SwitchAsync(string name, CancellationToken ct = default) =>
        (await http.PostAsync($"__scenario/{Uri.EscapeDataString(name)}", null, ct)).EnsureSuccessStatusCode();
}

/// <summary>
/// The clock the rules run on. Live: the real clock. Mock: 12:00 IST on the current scenario's <c>today</c>,
/// so a scenario gives the same findings on any calendar day (the 7-day order window and StaleNav depend on "today").
/// </summary>
public sealed class ReviewClock(IOptions<KiteOptions> options, IServiceProvider services, IMemoryCache cache, TimeProvider system)
{
    public const string CacheKey = "mock:scenario";

    public async Task<TimeProvider> GetAsync(CancellationToken ct = default)
    {
        if (!options.Value.IsMock)
            return system;

        var scenario = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.Value.CacheSeconds);
            return await services.GetRequiredService<MockControl>().GetScenarioAsync(ct);
        });
        return FixedClock.AtIstNoon(scenario!.Today);
    }
}
