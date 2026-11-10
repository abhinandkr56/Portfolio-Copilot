var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/api/ping", () => TypedResults.Ok(new PingResponse(true)));

app.Run();

public sealed record PingResponse(bool Ok);

// Exposed for WebApplicationFactory in PortfolioCopilot.Api.Tests.
public partial class Program;
