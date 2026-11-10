using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Api.Logging;

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

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<KiteExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

app.MapGet("/api/ping", () => TypedResults.Ok(new PingResponse(true)));
app.MapKiteEndpoints();

app.Run();

public sealed record PingResponse(bool Ok);

// Exposed for WebApplicationFactory in PortfolioCopilot.Api.Tests.
public partial class Program;
