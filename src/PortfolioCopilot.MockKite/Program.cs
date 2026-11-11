using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PortfolioCopilot.MockKite;

// A minimal imitation of Kite Connect v3 (doc 04 §The mock). Read-only like the real app's usage:
// the only write-shaped Kite routes are session token create/delete. Anything else is logged and 404s.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddSingleton(new MockState(Path.Combine(AppContext.BaseDirectory, "samples")));

var app = builder.Build();

app.MapDefaultEndpoints();

const string AccessToken = "mock-access-token";
const string RequestToken = "mock-request-token";
const string DefaultCallback = "http://127.0.0.1:5080/kite/callback";

var log = app.Logger;

// Failure injection and the request log apply to Kite routes only (not /__*, /health, /alive).
app.Use(async (ctx, next) =>
{
    var state = ctx.RequestServices.GetRequiredService<MockState>();
    var path = ctx.Request.Path.Value ?? "";
    var isKiteRoute = !path.StartsWith("/__") && path is not ("/health" or "/alive") && !path.StartsWith("/connect/");

    if (isKiteRoute && state.TakeFailure() is { } mode)
    {
        var (status, errorType, message) = mode switch
        {
            "token" => (403, "TokenException", "Incorrect `api_key` or `access_token`."),
            "ratelimit" => (429, "NetworkException", "Too many requests"),
            "down" => (503, "NetworkException", "Service unavailable"),
            _ => (500, "GeneralException", $"Unknown failure mode {mode}"),
        };
        await KiteError(status, errorType, message).ExecuteAsync(ctx);
    }
    else
    {
        await next(ctx);
    }

    if (isKiteRoute)
    {
        string? checksum = null;
        if (path == "/session/token" && HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.HasFormContentType)
            checksum = (await ctx.Request.ReadFormAsync())["checksum"];
        state.Requests.Enqueue(new MockRequest(ctx.Request.Method, path, ctx.Response.StatusCode, checksum));
    }
});

// --- Login -------------------------------------------------------------------------------------------

app.MapGet("/connect/login", (HttpContext ctx) =>
{
    var q = ctx.Request.Query;
    if (q["v"] != "3" || string.IsNullOrEmpty(q["api_key"]))
        return Results.BadRequest("v=3 and api_key are required");

    var target = q["redirect"].FirstOrDefault() ?? DefaultCallback;
    if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Host is not ("127.0.0.1" or "localhost"))
        return Results.BadRequest("redirect must be a local URL");

    // Kite returns the redirect_params (a URL-encoded query string) on the redirect, like a real login.
    var extra = q["redirect_params"].FirstOrDefault();
    var url = $"{target}{(target.Contains('?') ? '&' : '?')}request_token={RequestToken}&action=login&type=login&status=success";
    if (!string.IsNullOrEmpty(extra))
        url += "&" + extra;
    return Results.Redirect(url);
});

app.MapPost("/session/token", async (HttpContext ctx, MockState state) =>
{
    if (!HasVersionHeader(ctx))
        return KiteError(400, "InputException", "Missing X-Kite-Version header");
    if (!ctx.Request.HasFormContentType)
        return KiteError(400, "InputException", "Expected a form body");

    var form = await ctx.Request.ReadFormAsync();
    string apiKey = form["api_key"].ToString(), requestToken = form["request_token"].ToString(), checksum = form["checksum"].ToString();
    if (apiKey.Length == 0 || requestToken.Length == 0 || checksum.Length == 0)
        return KiteError(400, "InputException", "Missing api_key, request_token or checksum");
    if (!Regex.IsMatch(checksum, "^[0-9a-f]{64}$"))
        return KiteError(403, "TokenException", "Invalid checksum");
    if (requestToken != RequestToken)
        return KiteError(403, "TokenException", "Token is invalid or has expired.");

    ctx.Response.Headers["X-Mock-Checksum"] = checksum; // test-only echo (doc 08)
    var data = KiteShapes.Profile(state.Scenario);
    data["api_key"] = apiKey;
    data["access_token"] = AccessToken;
    data["public_token"] = "mock-public-token";
    data["refresh_token"] = "";
    data["login_time"] = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).ToString("yyyy-MM-dd HH:mm:ss");
    return Success(data);
});

app.MapDelete("/session/token", (HttpContext ctx) =>
{
    var q = ctx.Request.Query;
    if (string.IsNullOrEmpty(q["api_key"]) || string.IsNullOrEmpty(q["access_token"]))
        return KiteError(400, "InputException", "api_key and access_token are required");
    return Success(true);
});

// --- Data (GET only, authenticated) ------------------------------------------------------------------

app.MapGet("/user/profile", (HttpContext ctx, MockState s) => Authorised(ctx, () => Success(KiteShapes.Profile(s.Scenario))));
app.MapGet("/portfolio/holdings", (HttpContext ctx, MockState s) => Authorised(ctx, () => Success(KiteShapes.Items(s.Scenario, "holdings"))));
app.MapGet("/mf/holdings", (HttpContext ctx, MockState s) => Authorised(ctx, () => Success(KiteShapes.Items(s.Scenario, "mfHoldings"))));
app.MapGet("/mf/sips", (HttpContext ctx, MockState s) => Authorised(ctx, () => Success(KiteShapes.Items(s.Scenario, "mfSips"))));
app.MapGet("/mf/orders", (HttpContext ctx, MockState s) => Authorised(ctx, () => Success(KiteShapes.Items(s.Scenario, "mfOrders"))));

app.MapGet("/mf/instruments", (HttpContext ctx, MockState s) => Authorised(ctx, () =>
{
    var csv = File.ReadAllBytes(Path.Combine(s.SamplesDir, "kite", "mf-instruments.csv"));
    using var buffer = new MemoryStream();
    using (var gzip = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        gzip.Write(csv);
    ctx.Response.Headers.ContentEncoding = "gzip";
    return Results.Bytes(buffer.ToArray(), "text/csv");
}));

// --- Test controls -----------------------------------------------------------------------------------

app.MapPost("/__scenario/{name}", (string name, MockState s) =>
    s.TrySwitch(name) ? Results.Ok(new { scenario = name }) : Results.NotFound(new { error = $"No scenario '{name}'" }));

app.MapPost("/__fail/{mode}", (string mode, int? count, MockState s) =>
{
    if (mode is not ("token" or "ratelimit" or "down" or "none"))
        return Results.BadRequest(new { error = "mode must be token, ratelimit, down or none" });
    s.SetFailure(mode, count ?? 1);
    return Results.Ok(new { mode, count = mode == "none" ? 0 : Math.Max(1, count ?? 1) });
});

app.MapGet("/__scenario", (MockState s) =>
    Results.Ok(new { scenario = s.ScenarioName, today = s.Scenario["today"]?.GetValue<string>() }));

app.MapPost("/__reset", (MockState s) => { s.Reset(); return Results.Ok(); });
app.MapGet("/__requests", (MockState s) => Results.Ok(s.Requests.ToArray()));
app.MapGet("/__unexpected", (MockState s) => Results.Ok(s.Unexpected.ToArray()));
app.MapGet("/", (MockState s) => Results.Ok(new { service = "PortfolioCopilot.MockKite", scenario = s.ScenarioName }));

// Anything else (orders, GTT, alerts, SIP writes…) is unexpected: log it and 404.
app.MapFallback((HttpContext ctx, MockState s) =>
{
    var entry = $"{ctx.Request.Method} {ctx.Request.Path}";
    s.Unexpected.Enqueue(entry);
    log.LogWarning("Unexpected route {Route}", entry);
    return KiteError(404, "GeneralException", "Route not found");
});

app.Run();

static bool HasVersionHeader(HttpContext ctx) => ctx.Request.Headers["X-Kite-Version"] == "3";

static IResult Authorised(HttpContext ctx, Func<IResult> ok)
{
    var auth = ctx.Request.Headers.Authorization.ToString();
    var valid = HasVersionHeader(ctx)
                && auth.StartsWith("token ", StringComparison.Ordinal)
                && auth.EndsWith(":" + AccessToken, StringComparison.Ordinal)
                && auth.Length > "token ".Length + AccessToken.Length + 1;
    return valid ? ok() : KiteError(403, "TokenException", "Incorrect `api_key` or `access_token`.");
}

static IResult Success(object data) =>
    Results.Json(new JsonObject { ["status"] = "success", ["data"] = data is JsonNode n ? n : JsonValue.Create(data) });

static IResult KiteError(int status, string errorType, string message) =>
    Results.Json(new JsonObject { ["status"] = "error", ["message"] = message, ["error_type"] = errorType, ["data"] = null },
        statusCode: status);
