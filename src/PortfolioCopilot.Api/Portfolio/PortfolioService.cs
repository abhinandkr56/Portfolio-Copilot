using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Core;

namespace PortfolioCopilot.Api.Portfolio;

/// <summary>Everything one request needs: the input, Core's computation, when the Kite data was fetched, and the rules' clock.</summary>
public sealed record PortfolioSnapshot(PortfolioInput Input, ComputedPortfolio Computed, DateTimeOffset AsOfUtc, TimeProvider Clock);

/// <summary>
/// Gathers the plan (DB), Kite data (5-minute cache), the instruments master (24 h) and NAVs (DB) into a
/// <see cref="PortfolioInput"/>, then hands it to Core. No calculation happens here.
/// </summary>
public sealed class PortfolioService(
    KiteClient kite,
    PlanStore plans,
    NavStore navs,
    InstrumentsCache instruments,
    ReviewClock reviewClock,
    IMemoryCache cache,
    CoreOptions coreOptions,
    IOptions<KiteOptions> options,
    TimeProvider time,
    ILogger<PortfolioService> logger)
{
    public const string KiteDataKey = "kite:data";
    private static readonly ConcurrentDictionary<string, byte> LoggedWarnings = new();

    private sealed record KiteData(IReadOnlyList<StockHolding> Stocks, IReadOnlyList<FundHolding> Funds,
        IReadOnlyList<Sip> Sips, IReadOnlyList<MfOrder> Orders, DateTimeOffset FetchedUtc);

    public async Task<PortfolioSnapshot> GetAsync(CancellationToken ct = default)
    {
        var plan = await plans.GetCurrentAsync(ct);
        var data = await GetKiteDataAsync(ct);
        var master = await instruments.GetAsync(kite, ct);
        var navRecords = await navs.GetAsync(data.Funds.Select(f => f.Isin), ct);

        var input = new PortfolioInput(plan, data.Stocks, data.Funds, data.Sips, data.Orders, master, navRecords);
        var computed = PortfolioCalculator.Compute(input, coreOptions);
        foreach (var warning in computed.Warnings.Where(w => LoggedWarnings.TryAdd(w, 0)))
            logger.LogWarning("{Warning}", warning);

        return new PortfolioSnapshot(input, computed, data.FetchedUtc, await reviewClock.GetAsync(ct));
    }

    public IReadOnlyList<Finding> Evaluate(PortfolioSnapshot s) =>
        RulesEngine.Evaluate(s.Input, s.Computed, s.Clock, coreOptions);

    /// <summary>Drops cached Kite data and the mock scenario, so the next request fetches again.</summary>
    public static void ClearCache(IMemoryCache cache)
    {
        cache.Remove(KiteDataKey);
        cache.Remove(ReviewClock.CacheKey);
    }

    private async Task<KiteData> GetKiteDataAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(KiteDataKey, out KiteData? cached) && cached is not null)
            return cached;

        // Sequential on purpose: KiteClient reads the session through a scoped DbContext.
        var data = new KiteData(
            await kite.GetHoldingsAsync(ct),
            await kite.GetMfHoldingsAsync(ct),
            await kite.GetSipsAsync(ct),
            await kite.GetMfOrdersAsync(ct),
            time.GetUtcNow());
        cache.Set(KiteDataKey, data, TimeSpan.FromSeconds(options.Value.CacheSeconds));
        return data;
    }
}
