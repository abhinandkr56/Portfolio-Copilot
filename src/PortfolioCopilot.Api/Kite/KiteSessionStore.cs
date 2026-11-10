using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Core;

namespace PortfolioCopilot.Api.Kite;

public sealed record KiteSession(string UserIdMasked, DateTimeOffset CreatedUtc, DateTimeOffset ExpiresUtc);

/// <summary>Stores the single Kite session. The access token is encrypted with Data Protection and never leaves this class except to KiteClient.</summary>
public sealed class KiteSessionStore(CopilotDbContext db, IDataProtectionProvider protection, TimeProvider time)
{
    private const int SessionId = 1;
    private readonly IDataProtector _protector = protection.CreateProtector("PortfolioCopilot.KiteSession.AccessToken.v1");

    /// <summary>The current session, or null when there is none or it has expired (an expired row is deleted).</summary>
    public async Task<KiteSession?> GetAsync(CancellationToken ct = default)
    {
        var row = await CurrentRowAsync(ct);
        return row is null ? null : new KiteSession(row.UserIdMasked, row.CreatedUtc, row.ExpiresUtc);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        var row = await CurrentRowAsync(ct);
        if (row is null)
            return null;
        try
        {
            return _protector.Unprotect(row.AccessTokenProtected);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            await DeleteAsync(ct); // keys were lost: treat as logged out
            return null;
        }
    }

    public async Task<KiteSession> SaveAsync(string accessToken, string userId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var row = await db.KiteSessions.FindAsync([SessionId], ct);
        if (row is null)
        {
            row = new KiteSessionRow { Id = SessionId, UserIdMasked = "", AccessTokenProtected = "" };
            db.KiteSessions.Add(row);
        }
        row.UserIdMasked = Redactor.MaskUserId(userId);
        row.AccessTokenProtected = _protector.Protect(accessToken);
        row.CreatedUtc = now;
        row.ExpiresUtc = Ist.NextKiteExpiry(now);
        await db.SaveChangesAsync(ct);
        return new KiteSession(row.UserIdMasked, row.CreatedUtc, row.ExpiresUtc);
    }

    public async Task DeleteAsync(CancellationToken ct = default)
    {
        await db.KiteSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync(ct);
        db.ChangeTracker.Clear();
    }

    private async Task<KiteSessionRow?> CurrentRowAsync(CancellationToken ct)
    {
        var row = await db.KiteSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SessionId, ct);
        if (row is null)
            return null;
        if (row.ExpiresUtc <= time.GetUtcNow())
        {
            await DeleteAsync(ct);
            return null;
        }
        return row;
    }
}
