# 09 · Security and compliance

## Threat model (single user, runs on a laptop)

| Risk | Control |
|---|---|
| Someone on the network uses the app | The Api and the Next.js app bind to `127.0.0.1` only (`next dev --hostname 127.0.0.1`; the Api's launch profile uses `127.0.0.1`). No `0.0.0.0`. No deployment in v1. |
| Secrets reach the browser | The Next.js app has no secrets. `API_URL` is server-only; nothing sensitive goes in `NEXT_PUBLIC_*`. The browser only ever sees `/api/*` responses (already redacted: masked folio and user id, no tokens). |
| Kite secrets leak | `KITE_API_KEY` / `KITE_API_SECRET` come only from env vars or Aspire parameters. `api_secret` is never logged, never stored, never sent to the browser. |
| Access token leaks | Stored only in Postgres, encrypted with ASP.NET Data Protection. Never in logs, traces, the browser, URLs or LLM prompts. A logging filter redacts `access_token`, `request_token`, `checksum`, `api_secret` and the `Authorization` header. |
| CSRF on the OAuth callback | `/kite/login` sets a random `state` in a short-lived, HttpOnly, SameSite=Lax cookie and passes it to Kite as a `redirect_params` value (`state=…`; check the Kite docs for how `redirect_params` is returned). `/kite/callback` rejects a missing or mismatched state. In mock mode the mock echoes it back. |
| The app places an order | No write endpoints exist (doc 04). A test enforces it (doc 08). |
| Financial data sent to a cloud LLM | Redaction (doc 06), `AI:ShareAmounts=false` by default, and the Ollama option. Read Groq's and Google's API data terms before using real data. Free tiers may have different data-use terms from paid tiers. |
| Prompt injection via fund names | Fund names are data. The system prompt says to use them as data only, and the guardrails catch the effects (forbidden language, invented numbers). |
| Wrong numbers presented as facts | NumberCheck + C# templates + golden tests. |

## Kite terms

- Use only the official API and the official login page. **Never** automate the Zerodha password or TOTP (scraping, headless login). It breaks Zerodha's terms and your own account security. The daily click on **Connect Zerodha** is intentional.
- Respect rate limits: cache for 5 minutes, retry 429 once only.

## Regulatory (India). Not legal advice.

- **Personal use only.** Giving securities-specific recommendations or research to others generally requires SEBI registration as a Research Analyst or Investment Adviser. The app is designed to explain the owner's own plan, and it uses fixed, non-directional suggestions.
- **No orders**, so SEBI's retail algo framework (in force from 1 April 2026: static IP whitelisting, exchange Algo ID) doesn't apply to v1. Any future order feature is a separate project with its own compliance review.
- The disclaimer appears on every AI answer and in the page footer.

## Data retention

- `Snapshots` and `ReviewRuns` stay in the local Postgres volume. Add a `DELETE /api/dev/purge` (Development only) to wipe everything.
- No telemetry leaves the machine. The Aspire dashboard is local.
