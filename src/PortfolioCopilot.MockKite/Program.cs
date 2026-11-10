var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.MapDefaultEndpoints();

// Phase 2 adds the Kite routes (doc 04). Read-only: GET routes plus session token POST/DELETE only.
app.MapGet("/", () => "PortfolioCopilot.MockKite");

app.Run();
