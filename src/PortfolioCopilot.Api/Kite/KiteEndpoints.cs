using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Kite;

/// <summary>Login round trip and session endpoints (doc 03 §Session, doc 09 CSRF control).</summary>
public static class KiteEndpoints
{
    public const string StateCookie = "pc_kite_state";
    private const string ProfileCacheKey = "kite:profile";

    public static void MapKiteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/kite/login", Login).ExcludeFromDescription();
        app.MapGet("/kite/callback", Callback).ExcludeFromDescription();

        var api = app.MapGroup("/api/session").WithTags("Session");
        api.MapGet("", GetSession);
        api.MapPost("/logout", Logout);
    }

    /// <summary>302 to Kite's login page with a random state, kept in a short-lived HttpOnly cookie.</summary>
    private static IResult Login(HttpContext ctx, IOptions<KiteOptions> options, KiteCredentials credentials)
    {
        var o = options.Value;

        // The state cookie must be set on the same origin Kite redirects back to (127.0.0.1:5080, not localhost).
        var callback = new Uri(o.RedirectUrl);
        if (!string.Equals(ctx.Request.Host.Value, callback.Authority, StringComparison.OrdinalIgnoreCase))
            return Results.Redirect(new Uri(callback, "/kite/login").ToString());

        if (string.IsNullOrEmpty(credentials.ApiKey))
            return Results.Redirect(WebApp(o, "0", "kite-api-key-missing"));

        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        ctx.Response.Cookies.Append(StateCookie, state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = ctx.Request.IsHttps,
            Path = "/kite",
            MaxAge = TimeSpan.FromMinutes(10),
            IsEssential = true,
        });

        var url = $"{o.LoginUrl}?v=3&api_key={Uri.EscapeDataString(credentials.ApiKey)}" +
                  $"&redirect_params={Uri.EscapeDataString("state=" + state)}";
        if (o.IsMock)
            url += $"&redirect={Uri.EscapeDataString(o.RedirectUrl)}";
        return Results.Redirect(url);
    }

    private static async Task<IResult> Callback(HttpContext ctx, IOptions<KiteOptions> options, KiteClient kite,
        KiteSessionStore sessions, IMemoryCache cache, ILogger<KiteClient> logger, CancellationToken ct)
    {
        var o = options.Value;
        var q = ctx.Request.Query;
        var expected = ctx.Request.Cookies[StateCookie];
        ctx.Response.Cookies.Delete(StateCookie, new CookieOptions { Path = "/kite" });

        if (!string.Equals(q["status"], "success", StringComparison.OrdinalIgnoreCase))
            return Results.Redirect(WebApp(o, "0", "login-cancelled"));
        if (string.IsNullOrEmpty(expected) || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(q["state"].ToString())))
        {
            logger.LogWarning("Kite callback rejected: missing or mismatched state");
            return Results.Redirect(WebApp(o, "0", "state-mismatch"));
        }
        var requestToken = q["request_token"].ToString();
        if (string.IsNullOrEmpty(requestToken))
            return Results.Redirect(WebApp(o, "0", "no-request-token"));

        try
        {
            var login = await kite.ExchangeTokenAsync(requestToken, ct);
            await sessions.SaveAsync(login.AccessToken, login.UserId, ct);
            cache.Set(ProfileCacheKey, new KiteProfile(login.UserId, login.UserName), TimeSpan.FromSeconds(o.CacheSeconds));
            logger.LogInformation("Kite session created ({Mode})", o.Mode);
            return Results.Redirect(WebApp(o, "1"));
        }
        catch (KiteUnavailableException)
        {
            return Results.Redirect(WebApp(o, "0", "kite-unavailable"));
        }
        catch (KiteException ex)
        {
            logger.LogWarning("Kite token exchange failed: {ErrorType}", ex.ErrorType);
            return Results.Redirect(WebApp(o, "0", "exchange-failed"));
        }
    }

    /// <summary>Connected or not (always 200). The user's name comes from the cached profile (5 min), so the e-mail and name are never stored.</summary>
    private static async Task<SessionResponse> GetSession(IOptions<KiteOptions> options, KiteSessionStore sessions,
        KiteClient kite, IMemoryCache cache, CancellationToken ct)
    {
        var o = options.Value;
        var session = await sessions.GetAsync(ct);
        if (session is null)
            return new SessionResponse(false, o.Mode);

        string? userName = null;
        try
        {
            if (!cache.TryGetValue(ProfileCacheKey, out KiteProfile? profile))
            {
                profile = await kite.GetProfileAsync(ct);
                cache.Set(ProfileCacheKey, profile, TimeSpan.FromSeconds(o.CacheSeconds));
            }
            userName = profile?.UserName;
        }
        catch (KiteSessionExpiredException)
        {
            return new SessionResponse(false, o.Mode); // KiteClient already deleted the session
        }
        catch (KiteUnavailableException)
        {
            // still connected as far as we know; the name is just unavailable right now
        }

        return new SessionResponse(true, o.Mode, session.UserIdMasked, userName, session.CreatedUtc, session.ExpiresUtc);
    }

    /// <summary>Kite DELETE /session/token (best effort), then forget the session. 204.</summary>
    private static async Task<IResult> Logout(KiteSessionStore sessions, KiteClient kite, IMemoryCache cache,
        ILogger<KiteClient> logger, CancellationToken ct)
    {
        var token = await sessions.GetAccessTokenAsync(ct);
        if (token is not null)
        {
            try
            {
                await kite.LogoutAsync(token, ct);
            }
            catch (Exception ex) when (ex is KiteException or KiteUnavailableException)
            {
                logger.LogWarning("Kite logout failed ({Type}); deleting the local session anyway", ex.GetType().Name);
            }
        }
        await sessions.DeleteAsync(ct);
        cache.Remove(ProfileCacheKey);
        return Results.NoContent();
    }

    private static string WebApp(KiteOptions o, string connected, string? reason = null) =>
        $"{o.WebAppUrl.TrimEnd('/')}/?connected={connected}" + (reason is null ? "" : $"&reason={Uri.EscapeDataString(reason)}");

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
