using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Core;

namespace PortfolioCopilot.Api.Portfolio;

/// <summary>Kite's MF instruments master, cached 24 h in memory and on disk (doc 04).</summary>
public sealed class InstrumentsCache(IMemoryCache cache, IOptions<KiteOptions> options, TimeProvider time, ILogger<InstrumentsCache> logger)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private const string CacheKey = "kite:instruments";

    public async Task<IReadOnlyDictionary<string, MfInstrument>> GetAsync(KiteClient kite, CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, MfInstrument>? cached) && cached is not null)
            return cached;

        var file = Path.Combine(options.Value.InstrumentsCacheDir, $"mf-instruments.{options.Value.Mode.ToLowerInvariant()}.csv");
        string? csv = null;
        if (File.Exists(file) && time.GetUtcNow() - File.GetLastWriteTimeUtc(file) < Lifetime)
            csv = await File.ReadAllTextAsync(file, ct);

        if (csv is null)
        {
            try
            {
                csv = await kite.GetMfInstrumentsCsvAsync(ct);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllTextAsync(file, csv, ct);
            }
            catch (KiteException ex) when (ex is not KiteSessionExpiredException)
            {
                // e.g. not available on this Kite plan: classification falls back to AMFI categories and fund names
                logger.LogWarning("MF instruments unavailable ({ErrorType}); using AMFI and fund names instead", ex.ErrorType);
                return new Dictionary<string, MfInstrument>();
            }
        }

        var instruments = MfInstrumentsCsvParser.Parse(csv);
        cache.Set(CacheKey, instruments, Lifetime);
        return instruments;
    }
}
