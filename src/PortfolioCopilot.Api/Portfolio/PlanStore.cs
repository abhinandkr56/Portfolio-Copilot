using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Api.Portfolio;

/// <summary>The current plan is the newest <c>Plans</c> row; saving adds a row, so history is kept.</summary>
public sealed class PlanStore(CopilotDbContext db, TimeProvider time)
{
    public static JsonSerializerOptions Json => ScenarioParser.JsonOptions;

    public async Task<Plan> GetCurrentAsync(CancellationToken ct = default)
    {
        var json = await db.Plans.AsNoTracking().OrderByDescending(p => p.Id).Select(p => p.Json).FirstOrDefaultAsync(ct)
                   ?? throw new InvalidOperationException("No plan has been saved yet; the startup seed should have added one.");
        return JsonSerializer.Deserialize<Plan>(json, Json)!;
    }

    public async Task SaveAsync(Plan plan, CancellationToken ct = default)
    {
        db.Plans.Add(new PlanRow { Json = JsonSerializer.Serialize(plan, Json), SavedUtc = time.GetUtcNow() });
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> AnyAsync(CancellationToken ct = default) => db.Plans.AnyAsync(ct);
}
