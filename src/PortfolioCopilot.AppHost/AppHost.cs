var builder = DistributedApplication.CreateBuilder(args);

// LLM keys come from the environment (GROQ_API_KEY / GEMINI_API_KEY) or from Parameters:groq-key / Parameters:gemini-key.
// The value getter falls back to "" so the app starts without any key (Phase 0-4 need none).
var groqKey = builder.AddParameter("groq-key", () => Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "", secret: true);
var geminiKey = builder.AddParameter("gemini-key", () => Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "", secret: true);
var kiteMode = builder.Configuration["Kite:Mode"] ?? "Mock";

var pg = builder.AddPostgres("pg")
                .WithImage("pgvector/pgvector", "pg17")
                .WithDataVolume();
var db = pg.AddDatabase("copilotdb");

var mockKite = builder.AddProject<Projects.PortfolioCopilot_MockKite>("mockkite", launchProfileName: "http");

var api = builder.AddProject<Projects.PortfolioCopilot_Api>("api", launchProfileName: "http")
    .WithReference(db).WaitFor(db)
    .WithEnvironment("GROQ_API_KEY", groqKey)
    .WithEnvironment("GEMINI_API_KEY", geminiKey)
    .WithEnvironment("Kite__Mode", kiteMode)
    .WithHttpHealthCheck("/health");

if (kiteMode == "Mock")
    api.WithReference(mockKite).WaitFor(mockKite);
// Live: KITE_API_KEY / KITE_API_SECRET are inherited from the AppHost's environment.

#pragma warning disable ASPIREJAVASCRIPT001 // AddNextJsApp is experimental (Aspire.Hosting.JavaScript 13.6.4)
builder.AddNextJsApp("web", "../web")
    .WithEndpoint("http", e => { e.Port = 3000; e.TargetHost = "127.0.0.1"; e.IsProxied = false; })
    .WithReference(api).WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEnvironment("API_PUBLIC_URL", "http://127.0.0.1:5080")
    .WithExternalHttpEndpoints();
#pragma warning restore ASPIREJAVASCRIPT001

builder.AddProject<Projects.PortfolioCopilot_NavSync>("navsync")
    .WithReference(db).WaitFor(db).WithExplicitStart();

builder.AddProject<Projects.PortfolioCopilot_Evals>("evals")
    .WithReference(api).WithReference(mockKite).WaitFor(api).WithExplicitStart();

builder.Build().Run();
