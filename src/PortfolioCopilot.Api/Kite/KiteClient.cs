using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Core;

namespace PortfolioCopilot.Api.Kite;

public sealed record KiteProfile(string UserId, string UserName);

public sealed record KiteLogin(string UserId, string UserName, string AccessToken);

/// <summary>
/// Typed Kite Connect v3 client (doc 04). Read-only: every method is a GET, except <see cref="ExchangeTokenAsync"/>
/// (POST /session/token) and <see cref="LogoutAsync"/> (DELETE /session/token). <see cref="KiteReadOnlyGuard"/> enforces it.
/// Never logs tokens, the secret, the checksum or query strings.
/// </summary>
public sealed class KiteClient(
    HttpClient http,
    KiteSessionStore sessions,
    KiteCredentials credentials,
    TimeProvider time,
    ILogger<KiteClient> logger)
{
    public static readonly ActivitySource Activity = new("PortfolioCopilot");
    public static readonly TimeSpan RateLimitRetryDelay = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    // --- Session (the only non-GET calls) -------------------------------------------------------------

    /// <summary>POST /session/token: exchanges the one-time request token for an access token.</summary>
    public async Task<KiteLogin> ExchangeTokenAsync(string requestToken, CancellationToken ct = default)
    {
        var key = credentials.ApiKey ?? throw new KiteException("InputException", "The Kite API key is not configured.");
        var secret = credentials.ApiSecret ?? throw new KiteException("InputException", "The Kite API secret is not configured.");

        var data = await SendAsync<SessionData>(() => new HttpRequestMessage(HttpMethod.Post, "session/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["api_key"] = key,
                ["request_token"] = requestToken,
                ["checksum"] = Checksum(key, requestToken, secret),
            }),
        }, authenticated: false, ct);

        if (string.IsNullOrEmpty(data.AccessToken) || string.IsNullOrEmpty(data.UserId))
            throw new KiteException("DataException", "Kite returned no access token.");
        return new KiteLogin(data.UserId, data.UserName ?? "", data.AccessToken);
    }

    /// <summary>DELETE /session/token: invalidates the access token at Kite.</summary>
    public async Task LogoutAsync(string accessToken, CancellationToken ct = default)
    {
        var key = credentials.ApiKey ?? throw new KiteException("InputException", "The Kite API key is not configured.");
        var query = $"api_key={Uri.EscapeDataString(key)}&access_token={Uri.EscapeDataString(accessToken)}";
        await SendAsync<JsonElement>(() => new HttpRequestMessage(HttpMethod.Delete, "session/token?" + query), authenticated: false, ct);
    }

    /// <summary>lowercase hex SHA-256(api_key + request_token + api_secret).</summary>
    public static string Checksum(string apiKey, string requestToken, string apiSecret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey + requestToken + apiSecret)));

    // --- Data (GET only) -----------------------------------------------------------------------------

    public async Task<KiteProfile> GetProfileAsync(CancellationToken ct = default)
    {
        var p = await GetAsync<ProfileData>("user/profile", ct);
        return new KiteProfile(p.UserId ?? "", p.UserName ?? "");
    }

    public async Task<IReadOnlyList<StockHolding>> GetHoldingsAsync(CancellationToken ct = default) =>
        (await GetAsync<List<HoldingData>>("portfolio/holdings", ct))
        .Select(h => new StockHolding(h.Tradingsymbol ?? "", h.Isin ?? "", h.Exchange ?? "",
            h.Quantity + h.T1Quantity, h.AveragePrice, h.LastPrice, h.Pnl))
        .ToList();

    public async Task<IReadOnlyList<FundHolding>> GetMfHoldingsAsync(CancellationToken ct = default) =>
        (await GetAsync<List<MfHoldingData>>("mf/holdings", ct))
        .Select(m => new FundHolding(m.Tradingsymbol ?? "", m.Fund ?? "", m.Folio ?? "", m.Quantity,
            m.AveragePrice, m.LastPrice, ParseDate(m.LastPriceDate), m.Pnl))
        .ToList();

    public async Task<IReadOnlyList<Sip>> GetSipsAsync(CancellationToken ct = default) =>
        (await GetAsync<List<SipData>>("mf/sips", ct))
        .Select(s => new Sip(s.SipId ?? "", s.Tradingsymbol ?? "", s.Fund ?? "", s.Status ?? "", s.Frequency ?? "",
            s.InstalmentAmount, ParseDate(s.NextInstalment), s.CompletedInstalments))
        .ToList();

    /// <summary>GET /mf/orders: Kite returns the last 7 days.</summary>
    public async Task<IReadOnlyList<MfOrder>> GetMfOrdersAsync(CancellationToken ct = default) =>
        (await GetAsync<List<MfOrderData>>("mf/orders", ct))
        .Select(o => new MfOrder(o.OrderId ?? "", o.Tradingsymbol ?? "", o.Fund ?? "", o.Status ?? "", o.StatusMessage ?? "",
            o.Variety ?? "", ParseTimestamp(o.OrderTimestamp)))
        .ToList();

    /// <summary>GET /mf/instruments: the gzipped CSV, returned as text.</summary>
    public async Task<string> GetMfInstrumentsCsvAsync(CancellationToken ct = default)
    {
        using var response = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Get, "mf/instruments"), authenticated: true, ct);
        await ThrowIfErrorAsync(response, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        if (bytes is [0x1f, 0x8b, ..]) // gzip magic: the body wasn't decompressed by the handler
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return await reader.ReadToEndAsync(ct);
        }
        return Encoding.UTF8.GetString(bytes);
    }

    // --- Plumbing ------------------------------------------------------------------------------------

    private Task<T> GetAsync<T>(string path, CancellationToken ct) =>
        SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, path), authenticated: true, ct);

    private async Task<T> SendAsync<T>(Func<HttpRequestMessage> build, bool authenticated, CancellationToken ct)
    {
        using var response = await SendRawAsync(build, authenticated, ct);
        await ThrowIfErrorAsync(response, ct);

        Envelope<T>? envelope;
        try
        {
            envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(Json, ct);
        }
        catch (JsonException ex)
        {
            throw new KiteException("DataException", $"Kite returned a body that isn't the expected JSON ({ex.GetType().Name}).");
        }
        if (envelope is null || !string.Equals(envelope.Status, "success", StringComparison.OrdinalIgnoreCase) || envelope.Data is null)
            throw new KiteException(envelope?.ErrorType ?? "DataException", envelope?.Message ?? "Kite returned an empty response.");
        return envelope.Data;
    }

    /// <summary>Sends with auth headers; on 429 waits 1 s and retries exactly once. Maps transport failures to <see cref="KiteUnavailableException"/>.</summary>
    private async Task<HttpResponseMessage> SendRawAsync(Func<HttpRequestMessage> build, bool authenticated, CancellationToken ct)
    {
        string? token = null;
        if (authenticated)
        {
            token = await sessions.GetAccessTokenAsync(ct) ?? throw new KiteSessionExpiredException();
        }

        for (var attempt = 1; ; attempt++)
        {
            using var request = build();
            request.Headers.Add("X-Kite-Version", "3");
            if (token is not null)
                request.Headers.TryAddWithoutValidation("Authorization", $"token {credentials.ApiKey}:{token}");

            var path = "/" + request.RequestUri!.OriginalString.Split('?')[0].TrimStart('/');
            using var activity = Activity.StartActivity($"kite {request.Method} {path}", ActivityKind.Client);
            activity?.SetTag("kite.path", path);
            activity?.SetTag("kite.attempt", attempt);
            var started = time.GetTimestamp();

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning("Kite {Method} {Path} timed out after {Elapsed} ms", request.Method, path, time.GetElapsedTime(started).TotalMilliseconds);
                throw new KiteUnavailableException("Kite did not respond in time.", ex);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Kite {Method} {Path} failed: {Error}", request.Method, path, ex.HttpRequestError);
                throw new KiteUnavailableException("Kite could not be reached.", ex);
            }

            activity?.SetTag("http.response.status_code", (int)response.StatusCode);
            logger.LogInformation("Kite {Method} {Path} -> {Status} in {Elapsed} ms (attempt {Attempt})",
                request.Method, path, (int)response.StatusCode, Math.Round(time.GetElapsedTime(started).TotalMilliseconds), attempt);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 1)
            {
                response.Dispose();
                await Task.Delay(RateLimitRetryDelay, time, ct);
                continue;
            }
            return response;
        }
    }

    private async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new KiteUnavailableException("Kite is rate limiting requests.");
        if (status >= 500)
            throw new KiteUnavailableException($"Kite returned {status}.");

        Envelope<JsonElement>? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<Envelope<JsonElement>>(Json, ct);
        }
        catch (JsonException)
        {
            // not Kite JSON; fall through with the status code
        }

        if (response.StatusCode == HttpStatusCode.Forbidden && error?.ErrorType == "TokenException")
        {
            await sessions.DeleteAsync(ct);
            throw new KiteSessionExpiredException();
        }
        throw new KiteException(error?.ErrorType ?? "GeneralException", error?.Message ?? $"Kite returned {status}.");
    }

    private static DateOnly? ParseDate(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length >= 10
            && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d) ? d : null;

    private static DateTimeOffset ParseTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return DateTimeOffset.MinValue;
        try { return Ist.ParseKiteTimestamp(text); }
        catch (FormatException) { return DateTimeOffset.MinValue; }
    }

    // --- Kite JSON shapes (snake_case) ---------------------------------------------------------------

    private sealed record Envelope<T>(string? Status, T? Data, string? Message, string? ErrorType);
    private sealed record SessionData(string? UserId, string? UserName, string? AccessToken);
    private sealed record ProfileData(string? UserId, string? UserName);
    private sealed record HoldingData(string? Tradingsymbol, string? Exchange, string? Isin, decimal Quantity, decimal T1Quantity,
        decimal AveragePrice, decimal LastPrice, decimal Pnl);
    private sealed record MfHoldingData(string? Folio, string? Fund, string? Tradingsymbol, decimal Quantity,
        decimal AveragePrice, decimal LastPrice, string? LastPriceDate, decimal Pnl);
    private sealed record SipData(string? SipId, string? Tradingsymbol, string? Fund, string? Status, string? Frequency,
        decimal InstalmentAmount, string? NextInstalment, int CompletedInstalments);
    private sealed record MfOrderData(string? OrderId, string? Tradingsymbol, string? Fund, string? Status, string? StatusMessage,
        string? Variety, string? OrderTimestamp);
}
