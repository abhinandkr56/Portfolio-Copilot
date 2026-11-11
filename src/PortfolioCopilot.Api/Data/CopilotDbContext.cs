using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PortfolioCopilot.Api.Data;

/// <summary>The one current Kite session (Id is always 1). The access token is stored Data-Protection encrypted.</summary>
public sealed class KiteSessionRow
{
    public int Id { get; set; } = 1;
    public required string UserIdMasked { get; set; }
    public required string AccessTokenProtected { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
}

/// <summary>The current plan is the newest row; older rows are history.</summary>
public sealed class PlanRow
{
    public int Id { get; set; }
    public required string Json { get; set; }
    public DateTimeOffset SavedUtc { get; set; }
}

/// <summary>Latest NAV per ISIN from the AMFI file.</summary>
public sealed class NavRow
{
    public required string Isin { get; set; }
    public required string SchemeCode { get; set; }
    public required string SchemeName { get; set; }
    public required string Category { get; set; }
    public decimal Nav { get; set; }
    public DateOnly NavDate { get; set; }
}

/// <summary>One per IST day: holdings + allocation as JSON, for later history.</summary>
public sealed class SnapshotRow
{
    public DateOnly Date { get; set; }
    public required string Json { get; set; }
}

/// <summary>Audit of every review/ask.</summary>
public sealed class ReviewRunRow
{
    public long Id { get; set; }
    public DateTimeOffset Utc { get; set; }
    public required string Kind { get; set; }   // "review" | "ask"
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public required string FindingsJson { get; set; }
    public string? AnswerText { get; set; }
    public bool? NumberCheckPassed { get; set; }
}

public sealed class CopilotDbContext(DbContextOptions<CopilotDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<KiteSessionRow> KiteSessions => Set<KiteSessionRow>();

    public DbSet<PlanRow> Plans => Set<PlanRow>();

    public DbSet<NavRow> NavRecords => Set<NavRow>();

    public DbSet<SnapshotRow> Snapshots => Set<SnapshotRow>();

    public DbSet<ReviewRunRow> ReviewRuns => Set<ReviewRunRow>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<KiteSessionRow>(e =>
        {
            e.ToTable("KiteSessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.UserIdMasked).HasMaxLength(32);
        });
        modelBuilder.Entity<PlanRow>(e =>
        {
            e.ToTable("Plans");
            e.Property(x => x.Json).HasColumnType("jsonb");
        });
        modelBuilder.Entity<NavRow>(e =>
        {
            e.ToTable("NavRecords");
            e.HasKey(x => x.Isin);
            e.Property(x => x.Isin).HasMaxLength(12);
            e.Property(x => x.Nav).HasPrecision(18, 4);
        });
        modelBuilder.Entity<SnapshotRow>(e =>
        {
            e.ToTable("Snapshots");
            e.HasKey(x => x.Date);
            e.Property(x => x.Json).HasColumnType("jsonb");
        });
        modelBuilder.Entity<ReviewRunRow>(e =>
        {
            e.ToTable("ReviewRuns");
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.FindingsJson).HasColumnType("jsonb");
        });
    }
}
