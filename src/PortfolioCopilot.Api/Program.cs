using System.Net;
using System.Text.Json.Serialization;
using PortfolioCopilot.Api;
using PortfolioCopilot.Api.Ai;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Api.Logging;
using PortfolioCopilot.Api.Portfolio;
using PortfolioCopilot.Core;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Every logger goes through redaction (doc 09): tokens, checksum, secret and the Authorization header never reach a log.
var loggerFactory = builder.Services.Last(d => d.ServiceType == typeof(ILoggerFactory));
builder.Services.Remove(loggerFactory);
builder.Services.AddSingleton<ILoggerFactory>(sp => new RedactingLoggerFactory(ActivatorUtilities.CreateInstance<LoggerFactory>(sp)));

builder.AddNpgsqlDbContext<CopilotDbContext>("copilotdb");
builder.Services.AddDataProtection()
    .SetApplicationName("PortfolioCopilot")
    .PersistKeysToDbContext<CopilotDbContext>();

builder.Services.Configure<KiteOptions>(builder.Configuration.GetSection("Kite"));
builder.Services.AddSingleton(sp => KiteCredentials.FromEnvironment(sp.GetRequiredService<IOptions<KiteOptions>>().Value));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<KiteSessionStore>();
builder.Services.AddTransient<KiteReadOnlyGuard>();

#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental (Microsoft.Extensions.Http.Resilience 10.11.0)
builder.Services.AddHttpClient<KiteClient>((sp, http) =>
    {
        var o = sp.GetRequiredService<IOptions<KiteOptions>>().Value;
        http.BaseAddress = new Uri(o.BaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(10);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
    .AddHttpMessageHandler<KiteReadOnlyGuard>()
    .RemoveAllResilienceHandlers(); // KiteClient does its own single 429 retry (doc 04); the defaults would retry writes too
#pragma warning restore EXTEXP0001

// Mock mode only: MockKite's test controls (/__scenario). Not KiteClient, whose read-only guard stays strict.
builder.Services.AddHttpClient<MockControl>((sp, http) =>
{
    var o = sp.GetRequiredService<IOptions<KiteOptions>>().Value;
    http.BaseAddress = new Uri(o.MockBaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddSingleton(sp => ClassificationOptions(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddScoped<PlanStore>();
builder.Services.AddScoped<NavStore>();
builder.Services.AddSingleton<InstrumentsCache>();
builder.Services.AddScoped<ReviewClock>();
builder.Services.AddScoped<PortfolioService>();

// AI (doc 06). One IChatClient for the configured provider; tests swap it for a fake.
builder.Services.AddSingleton(sp => AiSettings.FromConfig(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton(sp => ChatClientFactory.Create(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<Conversations>();
builder.Services.AddScoped<ExplainService>();
builder.Services.AddScoped<AskService>();

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict; // numbers are numbers (also in OpenAPI, so TS types are `number`)
});
builder.Services.AddOpenApi(o => o.AddPortfolioCopilotFixes());

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<KiteExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().Database.MigrateAsync();
}
if (app.Configuration.GetValue("Database:SeedOnStartup", true))
    await DataSeeder.SeedAsync(app.Services);

app.MapDefaultEndpoints();

app.MapGet("/api/ping", () => TypedResults.Ok(new PingResponse(true))).WithTags("Health");
app.MapKiteEndpoints();
app.MapPortfolioEndpoints();
app.MapAskEndpoint();

var isDevLike = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing");
if (isDevLike)
{
    app.MapOpenApi();                                                         // /openapi/v1.json
    app.UseSwaggerUI(o => o.SwaggerEndpoint("/openapi/v1.json", "Portfolio Copilot")); // /swagger
    app.MapDevEndpoints(app.Services.GetRequiredService<IOptions<KiteOptions>>().Value);
}

app.Run();

static CoreOptions ClassificationOptions(IConfiguration config)
{
    var section = config.GetSection("Classification");
    var map = section.GetSection("SchemeTypeMap").GetChildren()
        .ToDictionary(c => c.Key, c => Enum.Parse<AssetClass>(c.Value!, ignoreCase: true), StringComparer.OrdinalIgnoreCase);
    return new CoreOptions(map.Count > 0 ? map : CoreOptions.Default.SchemeTypeMap,
        section.GetValue("NavStaleDays", CoreOptions.Default.NavStaleDays));
}

public sealed record PingResponse(bool Ok);

// Exposed for WebApplicationFactory in PortfolioCopilot.Api.Tests.
public partial class Program;
