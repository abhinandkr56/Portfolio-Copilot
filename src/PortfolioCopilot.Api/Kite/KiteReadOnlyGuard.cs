namespace PortfolioCopilot.Api.Kite;

/// <summary>
/// Last line of defence for the read-only rule (CLAUDE.md rule 1): every request leaving <see cref="KiteClient"/>
/// passes through here. Only GET is allowed, plus POST and DELETE on <c>/session/token</c> (login and logout).
/// </summary>
public sealed class KiteReadOnlyGuard : DelegatingHandler
{
    public static bool IsAllowed(HttpMethod method, string path) =>
        method == HttpMethod.Get
        || ((method == HttpMethod.Post || method == HttpMethod.Delete) && string.Equals(path, "/session/token", StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        if (!IsAllowed(request.Method, path))
            throw new InvalidOperationException($"Blocked a Kite {request.Method} {path}: this app is read-only.");
        return base.SendAsync(request, cancellationToken);
    }
}
