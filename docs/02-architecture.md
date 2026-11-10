# 02 · Architecture

## Solution layout

```
PortfolioCopilot.sln                      (classic .sln)
src/
  PortfolioCopilot.AppHost/               Aspire AppHost: wires everything
  PortfolioCopilot.ServiceDefaults/       Aspire template, plus the M.E.AI telemetry source
  PortfolioCopilot.Core/                  class library: records, rules, classification,
                                          AMFI parser, redaction, number check. NO IO, NO HTTP.
  PortfolioCopilot.Api/                   ASP.NET Core minimal API on :5080
                                          Kite client, EF Core, endpoints, AI agent + tools
  web/                                    Next.js app (App Router, TypeScript, Tailwind); calls Api over HTTP
  PortfolioCopilot.MockKite/              minimal API that imitates Kite (serves samples/)
  PortfolioCopilot.NavSync/               console: downloads AMFI NAV file → Postgres
tests/
  PortfolioCopilot.Core.Tests/            xUnit: rules, classification, parser, redaction
  PortfolioCopilot.Api.Tests/             xUnit: endpoints with WebApplicationFactory + MockKite
  PortfolioCopilot.Evals/                 console: golden scenarios end to end (exit code 0/1)
samples/                                  fixtures (copied to output of MockKite and tests)
```

**Dependency direction:** `Api → Core`, `Evals → Core` (and Api over HTTP), `web → Api` over HTTP only (TypeScript types generated from the Api's OpenAPI), `MockKite → nothing`.

**Shared DTOs:** the API response records live in `PortfolioCopilot.Core/Contracts/` so that the Api and Evals use the same types. The Next.js app gets them as TypeScript through `npm run gen:api` (OpenAPI → `lib/api-types.ts`). Make sure the OpenAPI document describes every response (typed `Results<…>` / `Produces<T>`) so the generated types are complete.

## Runtime picture

```
Browser ──▶ web (Next.js, 127.0.0.1:3000) ──/api rewrite or server fetch──▶ Api (127.0.0.1:5080) ──▶ Kite REST (live) or MockKite (mock)
                                            │
                                            ├──▶ Postgres "copilotdb" (sessions, nav, snapshots, plan, audit)
                                            └──▶ LLM (Groq / Gemini / Ollama) via IChatClient
NavSync (explicit start) ──▶ AMFI NAV file ──▶ Postgres
Evals   (explicit start) ──▶ Api (+ MockKite scenario switch)
```

## AppHost (sketch; adapt to the current Aspire API)

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var groqKey   = builder.AddParameter("groq-key", secret: true);
var geminiKey = builder.AddParameter("gemini-key", secret: true);   // may be left empty
var kiteMode  = builder.Configuration["Kite:Mode"] ?? "Mock";

var pg = builder.AddPostgres("pg")
                .WithImage("pgvector/pgvector", "pg17")
                .WithDataVolume();
var db = pg.AddDatabase("copilotdb");

var mockKite = builder.AddProject<Projects.PortfolioCopilot_MockKite>("mockkite");

var api = builder.AddProject<Projects.PortfolioCopilot_Api>("api")
    .WithReference(db).WaitFor(db)
    .WithEnvironment("GROQ_API_KEY", groqKey)
    .WithEnvironment("GEMINI_API_KEY", geminiKey)
    .WithEnvironment("Kite__Mode", kiteMode)
    .WithHttpHealthCheck("/health");

if (kiteMode == "Mock")
    api.WithReference(mockKite).WaitFor(mockKite);
// Live: KITE_API_KEY / KITE_API_SECRET are inherited from the AppHost's environment.

#pragma warning disable ASPIREJAVASCRIPT001          // AddNextJsApp is experimental (Aspire.Hosting.JavaScript)
builder.AddNextJsApp("web", "../web")
    .WithReference(api).WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEnvironment("API_PUBLIC_URL", "http://127.0.0.1:5080")
    .WithExternalHttpEndpoints();
// The web endpoint must be port 3000 (Kite:WebAppUrl). If AddNextJsApp doesn't fix the port, or isn't
// available, use: builder.AddJavaScriptApp("web", "../web").WithRunScript("dev")
//                   .WithHttpEndpoint(port: 3000, env: "PORT")...  Check aspire.dev and record it.

builder.AddProject<Projects.PortfolioCopilot_NavSync>("navsync")
    .WithReference(db).WaitFor(db).WithExplicitStart();

builder.AddProject<Projects.PortfolioCopilot_Evals>("evals")
    .WithReference(api).WithReference(mockKite).WaitFor(api).WithExplicitStart();

builder.Build().Run();
```

## Api configuration (`appsettings.json`, no secrets)

```json
{
  "AI": {
    "Provider": "Groq",
    "Groq":   { "Endpoint": "https://api.groq.com/openai/v1",
                "Model": "openai/gpt-oss-120b", "KeyVariable": "GROQ_API_KEY", "NativeJsonSchema": true },
    "Gemini": { "Endpoint": "https://generativelanguage.googleapis.com/v1beta/openai/",
                "Model": "gemini-3.8-flash", "KeyVariable": "GEMINI_API_KEY", "NativeJsonSchema": true },
    "Ollama": { "Endpoint": "http://localhost:11434/v1",
                "Model": "llama3.2", "KeyVariable": "OLLAMA_API_KEY", "NativeJsonSchema": false }
  },
  "Kite": {
    "Mode": "Mock",
    "LiveBaseUrl": "https://api.kite.trade",
    "LiveLoginUrl": "https://kite.zerodha.com/connect/login",
    "MockBaseUrl": "http://mockkite",
    "ApiKeyVariable": "KITE_API_KEY",
    "ApiSecretVariable": "KITE_API_SECRET",
    "RedirectUrl": "http://127.0.0.1:5080/kite/callback",
    "WebAppUrl": "http://127.0.0.1:3000",
    "CacheSeconds": 300
  },
  "Classification": {
    "SchemeTypeMap": { "equity": "Equity", "elss": "Equity", "debt": "Debt", "liquid": "Debt",
                       "balanced": "Hybrid", "hybrid": "Hybrid", "fof": "Other", "gold": "Gold" },
    "NavStaleDays": 4
  }
}
```

> **Model ids must be verified** (Groq's and Gemini's model lists) before Phase 5, and recorded in `DECISIONS.md`. The ids above were current in early November 2026 but weren't re-checked for this spec.
> `MockBaseUrl` uses Aspire service discovery (`http://mockkite`); call `builder.AddServiceDefaults()` and register the `HttpClient` with service discovery.

## Database (EF Core, `copilotdb`)

| Table | Purpose | Key columns |
|---|---|---|
| `KiteSessions` | the one current session | `Id` (always 1), `UserIdMasked`, `AccessTokenProtected` (Data Protection), `CreatedUtc`, `ExpiresUtc` |
| `Plans` | the current plan (JSON) + history | `Id`, `Json` (jsonb), `SavedUtc` |
| `NavRecords` | latest NAV per ISIN from AMFI | `Isin` (PK), `SchemeCode`, `SchemeName`, `Category`, `Nav`, `NavDate` |
| `Snapshots` | one per day, for later history | `Date` (PK), `Json` (jsonb: holdings + allocation) |
| `ReviewRuns` | audit of every review/ask | `Id`, `Utc`, `Kind` (review/ask), `Provider`, `Model`, `FindingsJson`, `AnswerText`, `NumberCheckPassed` |

On first run, seed `Plans` from `samples/plan.sample.json` when it's empty.
Data Protection keys are persisted to the database (or `%LOCALAPPDATA%/PortfolioCopilot/keys`) so tokens survive restarts.

## Observability

ServiceDefaults: add `.AddSource("Experimental.Microsoft.Extensions.AI")` and `.AddMeter("Experimental.Microsoft.Extensions.AI")` (verify the current source name). Wrap the chat client with `.UseOpenTelemetry()`. Create an `ActivitySource("PortfolioCopilot")` for tool calls and Kite calls. Never put tokens or secrets in span attributes.
