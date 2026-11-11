using Microsoft.Extensions.Options;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Api.Portfolio;

/// <summary>First-run data: the sample plan when <c>Plans</c> is empty, and in Mock mode the sample AMFI file when <c>NavRecords</c> is empty.</summary>
public static class DataSeeder
{
    public static string SamplesDir => Path.Combine(AppContext.BaseDirectory, "samples");

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DataSeeder));

        var plans = sp.GetRequiredService<PlanStore>();
        if (!await plans.AnyAsync(ct))
        {
            var plan = ScenarioParser.ParsePlan(await File.ReadAllTextAsync(Path.Combine(SamplesDir, "plan.sample.json"), ct));
            await plans.SaveAsync(plan, ct);
            logger.LogInformation("Seeded the plan from samples/plan.sample.json");
        }

        var navs = sp.GetRequiredService<NavStore>();
        if (sp.GetRequiredService<IOptions<KiteOptions>>().Value.IsMock && !await navs.AnyAsync(ct))
        {
            var result = await navs.ImportAsync(await File.ReadAllTextAsync(Path.Combine(SamplesDir, "amfi", "NAVAll.sample.txt"), ct), ct);
            logger.LogInformation("Imported {Count} sample NAVs (NAV date {NavDate})", result.Imported, result.NavDate);
        }
    }
}
