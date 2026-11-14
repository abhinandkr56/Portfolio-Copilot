using Microsoft.EntityFrameworkCore;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Portfolio;

/// <summary>AMFI NAVs in <c>NavRecords</c>: parsed by Core's <see cref="AmfiNavParser"/>, upserted by ISIN.</summary>
public sealed class NavStore(CopilotDbContext db)
{
    public async Task<NavImportResponse> ImportAsync(string amfiText, CancellationToken ct = default)
    {
        var records = AmfiNavParser.LatestPerIsin(AmfiNavParser.Parse(amfiText));

        var existing = await db.NavRecords.ToDictionaryAsync(n => n.Isin, StringComparer.OrdinalIgnoreCase, ct);
        foreach (var r in records)
        {
            if (!existing.TryGetValue(r.Isin, out var row))
            {
                row = new NavRow { Isin = r.Isin, SchemeCode = r.SchemeCode, SchemeName = r.SchemeName, Category = r.Category };
                db.NavRecords.Add(row);
            }
            row.SchemeCode = r.SchemeCode;
            row.SchemeName = r.SchemeName;
            row.Category = r.Category;
            row.Nav = r.Nav;
            row.NavDate = r.NavDate;
        }
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return new NavImportResponse(records.Count, records.Count == 0 ? null : records.Max(r => r.NavDate));
    }

    public async Task<IReadOnlyDictionary<string, NavRecord>> GetAsync(IEnumerable<string> isins, CancellationToken ct = default)
    {
        var wanted = isins.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var rows = await db.NavRecords.AsNoTracking().Where(n => wanted.Contains(n.Isin)).ToListAsync(ct);
        return rows.ToDictionary(n => n.Isin, n => new NavRecord(n.Isin, n.SchemeCode, n.SchemeName, n.Category, n.Nav, n.NavDate),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> AnyAsync(CancellationToken ct = default) => db.NavRecords.AnyAsync(ct);
}
