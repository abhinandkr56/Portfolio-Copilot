# Portfolio Copilot

A personal, **read-only** web app. It reads your Zerodha stocks, mutual funds and SIPs through Kite Connect, checks them against your own written plan (allocation targets, concentration caps, Direct vs Regular, SIP health) and explains what it found in plain English with an LLM.

- It **never places, changes or cancels** orders, GTTs, SIPs or alerts. Kite calls are `GET` only, plus login and logout.
- Every number is computed in C#. The LLM only explains those numbers and never says what to buy or sell.
- Personal use only. *Information only, not investment advice. You make the decisions.*

Next.js frontend (`src/web`), ASP.NET Core API (`src/PortfolioCopilot.Api`) and PostgreSQL, run together by .NET Aspire.

---

## 1. Setup

You need:

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10 | `dotnet --version` |
| Node.js | 24 LTS (≥ 22.12) | `src/web/.nvmrc` says 24. Older Node 22 releases fail with `ERR_REQUIRE_ESM` |
| Docker Desktop (or Podman) | any recent | Aspire runs Postgres in a container. It must be **running** |
| Aspire CLI | optional | `dotnet tool install -g Aspire.Cli`, for `aspire start`, `aspire resource …` |

```bash
git clone <this repo> && cd portfolio-copilot
cd src/web && npm install && cd ../..      # once; Aspire's web-installer also runs it
dotnet build PortfolioCopilot.sln
```

## 2. Environment variables

Secrets come only from environment variables (or Aspire user-secret parameters). They never go in `appsettings*.json`.

| Variable | Needed for | |
|---|---|---|
| `GROQ_API_KEY` | AI explain and Ask (default provider) | free at console.groq.com |
| `GEMINI_API_KEY` | optional second provider (`--AI:Provider=Gemini`) | aistudio.google.com |
| `KITE_API_KEY`, `KITE_API_SECRET` | **Live** mode only | from your Kite Connect **Personal** app |

Without an LLM key everything except the AI summary and Ask still works. Without Kite keys, use Mock mode (the default).

Set them in your shell profile (`~/.zshrc`) or user environment, then **restart VS Code and the terminal** so Aspire inherits them.

## 3. Run

```bash
dotnet run --project src/PortfolioCopilot.AppHost                          # mock Kite, Groq
dotnet run --project src/PortfolioCopilot.AppHost -- --Kite:Mode=Live      # your real Zerodha account
dotnet run --project src/PortfolioCopilot.AppHost -- --AI:Provider=Gemini  # other LLM
```

| What | Where |
|---|---|
| App | http://127.0.0.1:3000 |
| API + Swagger (Development) | http://127.0.0.1:5080/swagger |
| Aspire dashboard | the URL printed at startup (logs, traces, resources) |

**Mock mode** serves fictional portfolios from `samples/` (the ISINs like `INFTEST00001` don't exist). Click **Connect Zerodha** and the mock logs you in at once. Switch portfolios with `POST /api/dev/scenario/{name}` (see `samples/README.md`).

### Fresh mutual fund NAVs (NavSync)

In the Aspire dashboard, find the **navsync** resource and press **Start** (or run `aspire resource navsync start`). It downloads the current AMFI file (`https://portal.amfiindia.com/spages/NAVAll.txt`), upserts the latest NAV per ISIN into `NavRecords` and exits. Its log shows the counts:

```
Parsed 18,255 NAV records for 18,250 ISINs; NAV date 2026-11-13 for 12,108 of them, latest 2026-11-15
NavRecords upserted in 4.9s: 18,250 inserted, 0 updated, 0 unchanged
```

Run it once a day, after about 23:00 IST when AMFI has published the day's NAVs. If any fund's price is more than `Classification:NavStaleDays` (4) days old, the dashboard header warns you (the **StaleNav** finding).

### Checks

```bash
dotnet build PortfolioCopilot.sln && dotnet test PortfolioCopilot.sln
cd src/web && npm run lint && npm run typecheck && npm test && npm run build
npm run e2e                                    # Playwright smoke; the AppHost must be running in mock mode
dotnet run --project tests/PortfolioCopilot.Evals   # golden scenarios + AI checks; needs the AppHost and an LLM key
```

## 4. Daily login (Live mode)

Kite access tokens **expire every day around 06:00 IST**, and Kite has no refresh token. Each morning:

1. Open http://127.0.0.1:3000. The banner says *Connect your Zerodha account*.
2. Click **Connect Zerodha** and log in on Zerodha's page (with 2FA).
3. Zerodha sends you back to the dashboard with a *Connected* toast.

Only the API holds the token, encrypted with ASP.NET Data Protection, and it's never logged. To log out before it expires, call `POST http://127.0.0.1:5080/api/session/logout`, which also invalidates the token at Kite. The UI has no logout button yet.

One-time Kite setup: in the Kite developer console, create a **Personal** app with the redirect URL **`http://127.0.0.1:5080/kite/callback`**.

## 5. Wiping your data

`DELETE http://127.0.0.1:5080/api/dev/purge` (Development only) logs out of Kite and deletes the session, all plans, NAVs, snapshots and the review/ask audit. It also clears the in-memory caches and Ask conversations. Then it re-seeds the sample plan (and the sample NAVs in Mock mode) so the app keeps working. Run NavSync again afterwards for real NAVs.

To remove everything including the database volume, stop the AppHost and delete the `pg` data volume in Docker.

## 6. Troubleshooting

| Symptom | Fix |
|---|---|
| AppHost hangs at "waiting for pg" | Docker isn't running. Start Docker Desktop and run again |
| Dashboard keeps showing *Connect your Zerodha account* | The daily token expired (06:00 IST) or you logged in to Kite elsewhere with the same app. Connect again |
| Zerodha says the redirect URL doesn't match | The app's redirect URL must be exactly `http://127.0.0.1:5080/kite/callback`, with `127.0.0.1`, not `localhost` |
| *Zerodha is not responding, try again in a minute* | Kite is down or rate-limiting (one retry is built in). Wait a minute and press **Refresh** |
| Live login fails straight away, or the AI says *No API key for Groq: set GROQ_API_KEY and restart.* | Set the variable (`KITE_API_KEY`/`KITE_API_SECRET` or the LLM key), then restart VS Code **and** the terminal so Aspire inherits it |
| The AI summary is missing with a short error, or Ask returns 503 | The LLM provider is down or rate-limited (Groq's free tier is per minute). Retry, or use `--AI:Provider=Gemini`. The findings still show without AI |
| `web` fails: `Cannot find native binding` (rolldown) or `ERR_REQUIRE_ESM` | Use Node 24 (`node -v`), then `cd src/web && rm -rf node_modules && npm install` |
| Port 5080 or 3000 already in use | Another copy is running. `aspire stop`, or stop the old `dotnet`/`node` process |
| navsync: *Unrecognised AMFI header: …* | AMFI changed the file layout. Nothing was imported. Update `AmfiNavParser` (doc 05 §AMFI NAV parser) with a trimmed sample of the new file and a test |
| navsync: *The NavRecords table doesn't exist yet* | The API creates the tables at startup in Development. Let `api` become healthy, then start navsync again |
| navsync: *Couldn't download the AMFI NAV file* | Network or AMFI outage. Retry later; the old NAVs stay in place |
| A fund shows as **Other** | Neither Kite's scheme type nor the AMFI category matched. Add an override in the plan (`assetClassOverrides`, e.g. a gold ETF → `Gold`) |

---

## Project docs

The build spec this app was made from: `CLAUDE.md` (rules), `docs/01`–`09` (product, architecture, API contract, Kite, rules, AI guardrails, frontend, testing, security), `docs/DECISIONS.md` (every decision, dated) and `tasks/TASKS.md` (the phased plan).
