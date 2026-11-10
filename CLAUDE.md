# CLAUDE.md: Portfolio Copilot

You are building **Portfolio Copilot**: a personal, **read-only** web app that reads one person's Zerodha stock and mutual fund holdings through the Kite Connect API, checks them against their own written investment plan (`plan.json`), and explains the findings in plain English with an LLM.

Read these before writing code, in this order:

1. `docs/01-product-spec.md`: what it does, and what it must never do
2. `docs/02-architecture.md`: solution layout, projects, config
3. `docs/03-api-contract.md`: every endpoint, with request and response shapes
4. `docs/04-kite-integration.md`: login flow, endpoints, response shapes, the mock
5. `docs/05-domain-and-rules.md`: data model, classification, rule formulas
6. `docs/06-agent-and-guardrails.md`: LLM providers, tools, guardrails
7. `docs/07-frontend.md`: pages and components
8. `docs/08-testing-and-evals.md`: unit tests, golden scenarios, evals
9. `docs/09-security-and-compliance.md`
10. `tasks/TASKS.md`: the phased build plan. **Work one phase at a time.**

---

## Non-negotiable rules

1. **Read-only.** Never implement, call or expose any Kite endpoint that places, modifies or cancels orders, GTTs, SIPs or alerts. Kite calls are `GET` only, except `POST /session/token` (login) and `DELETE /session/token` (logout). If a task seems to need a write, stop and ask.
2. **The LLM never computes and never predicts.** Every number shown to the user is computed in C# (`PortfolioCopilot.Core`). The LLM only explains numbers from tool results. No price predictions, no buy/sell/hold calls on any security.
3. **Secrets come from environment variables or Aspire parameters only.** Never put secrets in `appsettings*.json`, code, tests, logs, traces or the frontend (no secret in any `NEXT_PUBLIC_*` variable). Never log the Kite `access_token`, `api_secret` or `request_token`.
4. **Mock first.** Everything must build, run and pass its tests against `PortfolioCopilot.MockKite` with **no** Kite or LLM credentials, except the AI phases, which need one LLM key. Live Kite is a config switch (`Kite:Mode=Live`), tested by the human.
5. **Every finding carries evidence.** A finding without the numbers that triggered it is a bug.
6. **Every AI answer ends with the disclaimer**: `Information only, not investment advice. You make the decisions.`

## Stack (verify current versions on nuget.org before adding; pin exact versions)

- **.NET 10** (`net10.0`), C# latest, nullable enabled, implicit usings.
- **Aspire 13.x** AppHost + ServiceDefaults (`dotnet new aspire-apphost`, `dotnet new aspire-servicedefaults`). Use a classic `.sln` (`dotnet new sln --format sln`).
- **Backend:** ASP.NET Core minimal APIs (`PortfolioCopilot.Api`), **port 5080** (http) in `launchSettings.json`.
- **Frontend:** **Next.js** (latest stable 16.x patch), App Router, TypeScript strict, Tailwind CSS, npm, in `src/web/`. No UI kit, chart library or state library; charts are hand-written SVG components. Client code calls same-origin `/api/*`, which `next.config.ts` rewrites to the Api; server components call the Api through the server-only `API_URL`. TypeScript types are **generated** from the Api's OpenAPI document. Full rules are in doc 07.
- **Aspire + Next.js:** `Aspire.Hosting.JavaScript`, using `AddNextJsApp` (experimental: suppress `ASPIREJAVASCRIPT001`). If that's unavailable in the installed Aspire version, use `AddJavaScriptApp(...).WithRunScript("dev").WithHttpEndpoint(port: 3000, env: "PORT")`. Check aspire.dev for the current API.
- **Database:** PostgreSQL through Aspire (`AddPostgres(...).WithImage("pgvector/pgvector", "pg17").WithDataVolume()`), EF Core with `Npgsql.EntityFrameworkCore.PostgreSQL`. Apply migrations at startup in Development only.
- **AI:** `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.OpenAI`. Agent: the **Microsoft Agent Framework** if its current packages restore cleanly (check Microsoft Learn for the current API); otherwise `ChatClientBuilder.UseFunctionInvocation()`. Record which one you chose in `docs/DECISIONS.md`.
- **Tests:** xUnit (or xUnit v3 if the template defaults to it). No mocking framework is needed; prefer fakes. Frontend: Vitest + Testing Library, and one Playwright smoke test.

### LLM client convention (keep exactly this shape)

```csharp
using OpenAI;            // never `using OpenAI.Chat;`
using System.ClientModel;
using Microsoft.Extensions.AI;

var provider = config["AI:Provider"]!;                       // "Groq" | "Gemini" | "Ollama"
var section  = config.GetSection($"AI:{provider}");
var key      = Environment.GetEnvironmentVariable(section["KeyVariable"]!) ?? "ollama";
IChatClient client = new OpenAIClient(new ApiKeyCredential(key),
        new OpenAIClientOptions { Endpoint = new Uri(section["Endpoint"]!) })
    .GetChatClient(section["Model"]!)
    .AsIChatClient();
```

Config shape, `AI:Provider` plus one block per provider with `{ Endpoint, Model, KeyVariable, NativeJsonSchema }`, is in `docs/02-architecture.md`. Swap providers with `--AI:Provider=Gemini`. **Check that each model id still exists** on the provider's model list before relying on it, and record what you found in `docs/DECISIONS.md`.

## How to work

- Do **one phase** from `tasks/TASKS.md` per request unless told otherwise. When the phase's "Done when" checks pass, tick its boxes in `TASKS.md`, then stop and summarise.
- Before each phase, re-read the docs it references.
- If the docs are ambiguous or contradict each other, **ask** rather than guess. Write down every decision you make (versions, model ids, field mappings) in `docs/DECISIONS.md`, dated.
- If you find that a Kite response field differs from `docs/04-kite-integration.md`, trust the official docs at https://kite.trade/docs/connect/v3/, update the doc and the mock, and note it in `DECISIONS.md`.
- Keep code plain: records for data, static or small classes for rules, no repository/unit-of-work layers, no MediatR, no AutoMapper.
- Money and quantities are `decimal`. Percentages are `decimal` from 0 to 100, rounded to 1 decimal place **only for display**.
- Dates: Kite returns IST strings. Store `DateTimeOffset` in UTC; display IST (`Asia/Kolkata`).

## Commands

```bash
dotnet build PortfolioCopilot.sln
dotnet test  PortfolioCopilot.sln
dotnet run --project src/PortfolioCopilot.AppHost            # everything, mock Kite, Groq
dotnet run --project src/PortfolioCopilot.AppHost -- --Kite:Mode=Live
dotnet run --project tests/PortfolioCopilot.Evals            # needs the AppHost running, or RunsAgainst=core

cd src/web && npm install                                    # once (unless the Aspire integration installs packages itself)
npm run lint && npm run typecheck && npm test && npm run build
npm run gen:api                                              # regenerate TS types (Api must be running)
npm run e2e                                                  # Playwright smoke (AppHost running, mock mode)
```

Environment variables: `GROQ_API_KEY`, `GEMINI_API_KEY` (optional), `KITE_API_KEY`, `KITE_API_SECRET` (Live mode only).

## Definition of done (every phase)

- `dotnet build` passes with **0 warnings** and `dotnet test` is green.
- If `src/web` changed: `npm run lint`, `npm run typecheck`, `npm test` and `npm run build` all pass, and `lib/api-types.ts` is regenerated if the API changed.
- Nothing in the diff writes to Kite, and no secrets appear in code, config, logs or tests.
- New behaviour is visible: an endpoint returns it, a page shows it, or a test proves it.
- `TASKS.md` is ticked, and `DECISIONS.md` is updated with anything you decided.
