namespace PortfolioCopilot.Core.Contracts;

/// <summary><c>GET /api/session</c> (doc 03). Not connected: only <see cref="Connected"/> and <see cref="Mode"/> are set.</summary>
public sealed record SessionResponse(
    bool Connected,
    string Mode,
    string? UserIdMasked = null,
    string? UserName = null,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? ExpiresUtc = null);
