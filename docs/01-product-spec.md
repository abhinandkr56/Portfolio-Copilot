# 01 · Product spec

## One-liner

A personal, read-only copilot that checks my Zerodha portfolio against **my own plan** and explains what has drifted, what costs me money and what needs a look, without ever telling me to buy or sell a specific security.

## User

One person: the account owner. They're a developer, comfortable running the app locally. There's no multi-user support, no sign-up, and no app-level auth beyond binding to localhost (see doc 09).

## User stories (MVP)

| # | As the owner I want to… | Acceptance |
|---|---|---|
| U1 | connect my Zerodha account once a day | **Connect Zerodha** → Zerodha login → back on the dashboard showing "Connected · expires ~06:00 IST". In mock mode the login is instant. |
| U2 | see all my holdings in one place | The Holdings page lists stocks and mutual funds with quantity, average price, last price, value, P&L, asset class and (for funds) the Direct/Regular badge. Totals at the bottom. |
| U3 | see my allocation against my plan | The dashboard shows equity/debt/gold/other: actual %, target %, drift in points, coloured by severity. |
| U4 | get a list of findings | The dashboard lists findings (rule, severity, evidence, suggestion), sorted Act → Watch → Info. Each finding shows the numbers that triggered it. |
| U5 | see my SIPs and their health | The SIPs page lists every SIP with status, amount, frequency, monthly equivalent and next instalment; paused SIPs and recent failed SIP orders are highlighted. |
| U6 | edit my plan | The Plan page edits targets, bands, caps, SIP budget and overrides; validation makes targets sum to 100; saving re-runs the review. |
| U7 | read an AI summary of the review | **Explain with AI** produces 4–8 sentences built only on the findings, ending with the disclaimer. Shows which provider and model wrote it. |
| U8 | ask questions about my portfolio | The Ask page offers chat with the agent. Answers cite findings and tools, and the page shows a "Tools used" list under each answer. Advice-seeking questions get a polite refusal plus the relevant facts. |
| U9 | trust the numbers | Every number in an AI answer appears in a tool result (enforced by middleware, doc 06). |
| U10 | know how fresh the data is | Each page shows "As of" for holdings (Kite fetch time) and NAV date; stale data shows a warning. |

## Out of scope (do not build)

- Placing, modifying or cancelling orders, GTTs, SIPs or alerts. **No write endpoints to Kite.**
- Price predictions, "top picks", news, sentiment, technical indicators.
- Live ticks, WebSockets, intraday data, historical candles (these need the paid Kite Connect plan).
- Fund overlap analysis (needs AMC portfolio disclosures).
- XIRR (the API doesn't return the full transaction history). A future phase imports the Console tradebook CSV.
- Multi-user support, cloud deployment, mobile app.

## Non-functional

- Runs on a laptop with one command (`dotnet run --project src/PortfolioCopilot.AppHost`).
- Works fully in **mock mode** without Kite credentials.
- Holdings fetched from Kite are cached for 5 minutes. The review runs in under 1 second without AI, and AI responses take under 15 seconds on Groq.
- Every AI call and tool call shows up as a trace in the Aspire dashboard.
