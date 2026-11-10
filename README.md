# Portfolio Copilot: build spec for Claude Code

This folder is a **build specification**. Drop it into an empty folder, open it in VS Code, and let the Claude Code extension build the app phase by phase.

## What gets built

A personal web app (Next.js frontend + ASP.NET Core backend, run together by .NET Aspire) that:

- logs in to your Zerodha account through Kite Connect (read-only),
- shows your stocks, mutual funds and SIPs, classified into equity / debt / gold,
- checks them against **your own plan** (`plan.json`): allocation drift, concentration, Regular vs Direct plans, SIP health,
- explains the findings, and answers questions, with an LLM that is **not allowed to compute or recommend trades**.

It never places orders.

## Folder contents

```
CLAUDE.md                     ← instructions Claude Code reads automatically
README.md                     ← this file (for you)
docs/
  01-product-spec.md          ← scope, user stories, out of scope
  02-architecture.md          ← projects, config, AppHost wiring
  03-api-contract.md          ← every endpoint with JSON shapes
  04-kite-integration.md      ← login flow, Kite endpoints, mock server
  05-domain-and-rules.md      ← records, classification, rule formulas
  06-agent-and-guardrails.md  ← providers, tools, guardrails, prompts
  07-frontend.md              ← pages, components, states
  08-testing-and-evals.md     ← unit tests, golden scenarios, evals
  09-security-and-compliance.md
  DECISIONS.md                ← Claude Code records its decisions here
tasks/
  TASKS.md                    ← 9 phases with "done when" checks
  PROMPTS.md                  ← copy-paste prompts, one per phase
samples/
  plan.sample.json
  kite/                       ← mock Kite responses (fictional data)
  amfi/NAVAll.sample.txt      ← sample AMFI NAV file (classic layout)
  scenarios/                  ← golden portfolios + expected findings
```

## How to use it

1. **Prerequisites:** .NET 10 SDK, **Node.js LTS** (Next.js 16 needs 20.9 or later), Docker Desktop (or Podman) for Postgres, VS Code with the Claude Code extension, and a Groq API key (free). The Aspire CLI is optional.
2. Create an empty folder, e.g. `PortfolioCopilot/`, and copy the contents of this folder into it. `CLAUDE.md` must sit at the root.
3. Set environment variables in your shell (or the user environment on Windows), then restart VS Code so it picks them up:
   - `GROQ_API_KEY` (needed from Phase 5)
   - `GEMINI_API_KEY` (optional, second provider)
   - `KITE_API_KEY`, `KITE_API_SECRET` (only for Phase 8, live mode)
4. Open the folder in VS Code and start Claude Code.
5. Paste the prompts from `tasks/PROMPTS.md` **one phase at a time**. After each phase:
   - run `dotnet run --project src/PortfolioCopilot.AppHost`, open http://127.0.0.1:3000 and look at what changed,
   - read `docs/DECISIONS.md`,
   - commit (`git init` at the start; one commit per phase makes it easy to roll back).
6. **Phase 8 (going live)** needs you: create a **Personal** (free) app in the Kite developer console with the redirect URL `http://127.0.0.1:5080/kite/callback`, then follow the checklist in `TASKS.md`.

## Phases at a glance

| # | Phase | Needs keys? | You'll see |
|---|---|---|---|
| 0 | Scaffold | no | AppHost dashboard with empty Api + Web |
| 1 | Core domain + rules | no | green unit tests, golden scenarios pass in Core |
| 2 | Mock Kite + Kite client + login | no | "Connected (mock)" in the API |
| 3 | API endpoints (no AI) | no | `/api/holdings`, `/api/review` in Swagger |
| 4 | Frontend (no AI) | no | dashboard, holdings, SIPs, plan editor |
| 5 | AI: explain + ask | Groq | AI summary and the Ask page |
| 6 | Guardrails + evals | Groq | evals console, all scenarios pass |
| 7 | NAV sync + polish | no | fresh NAVs, stale-data warnings |
| 8 | Live Kite | Kite | your real portfolio |

## Before you go live, check these

- The Kite **Personal** plan is free and has no market data. Confirm that `/portfolio/holdings`, `/mf/holdings`, `/mf/sips`, `/mf/orders` and `/mf/instruments` all work on it.
- The Kite access token expires **every day around 06:00 IST**. Log in once a day with the **Connect Zerodha** button.
- The sample data is **fictional**. ISINs like `INFTEST00001` don't exist.
- **Personal use only.** In India, stock recommendations to others require SEBI registration. This app is built to *explain your own plan*, not to give advice.
