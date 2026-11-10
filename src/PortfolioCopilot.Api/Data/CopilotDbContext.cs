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

public sealed class CopilotDbContext(DbContextOptions<CopilotDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<KiteSessionRow> KiteSessions => Set<KiteSessionRow>();

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
    }
}
