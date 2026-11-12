namespace PortfolioCopilot.Core.Contracts;

/// <summary>
/// <c>GET /api/session</c> (doc 03). Not connected: only <see cref="Connected"/>, <see cref="Mode"/> and the AI fields are set.
/// <see cref="AiProvider"/>/<see cref="AiModel"/> feed the Web footer (doc 07).
/// </summary>
public sealed record SessionResponse(
    bool Connected,
    string Mode,
    string? UserIdMasked = null,
    string? UserName = null,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? ExpiresUtc = null,
    string? AiProvider = null,
    string? AiModel = null);
