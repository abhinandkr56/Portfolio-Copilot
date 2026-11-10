# 04 · Kite Connect integration

Official docs: https://kite.trade/docs/connect/v3/. **These win over this file.** If a field differs, update this file, the mock and `DECISIONS.md`.

## Plan

Use the free **Personal** Kite Connect app. It includes account and portfolio APIs but **no market data** (no quotes, ticks or historical candles), and we don't need any.

## Authentication

1. **Login URL:** `https://kite.zerodha.com/connect/login?v=3&api_key={api_key}`
2. Zerodha redirects to the registered redirect URL: `…/kite/callback?request_token={rt}&action=login&status=success`.
3. **Exchange:** `POST https://api.kite.trade/session/token`, form-encoded:
   - `api_key`
   - `request_token`
   - `checksum` = lowercase hex `SHA256(api_key + request_token + api_secret)`
4. Response: `{ "status": "success", "data": { "user_id", "user_name", "email", "access_token", "public_token", "login_time", ... } }`. Store `access_token` (protected) and a masked `user_id`. Discard everything else; don't store the e-mail.
5. **Every request** sends the headers:
   - `X-Kite-Version: 3`
   - `Authorization: token {api_key}:{access_token}` (this is **not** `Bearer`)
6. **Expiry:** tokens expire daily around **06:00 IST**, and there's no refresh token. Logging in again invalidates the previous token. Only the Api owns the login.
7. **Logout:** `DELETE /session/token?api_key=…&access_token=…`.

Implement the exchange yourself in a typed `KiteClient` with `HttpClient`. **Do not** use the `Tech.Zerodha.KiteConnect` package (last updated in 2024; its dynamic dictionaries are hard to test).

## Response envelope and errors

Success: `{ "status": "success", "data": … }`
Error: `{ "status": "error", "message": "…", "error_type": "TokenException" | "InputException" | "NetworkException" | "GeneralException" | "PermissionException" | … }`

| Kite response | KiteClient throws | API maps to |
|---|---|---|
| 403 + `TokenException` | `KiteSessionExpiredException` (and deletes the stored session) | 401 `kite-session-required` |
| 429 | `KiteUnavailableException` (after **one** retry with a 1-second delay) | 503 `kite-unavailable` |
| 5xx / timeout (10 s) | `KiteUnavailableException` | 503 `kite-unavailable` |
| anything else | `KiteException(error_type, message)` | 502 ProblemDetails |

## Endpoints we call (GET only)

| Purpose | Endpoint | Cache |
|---|---|---|
| Profile (connection check) | `GET /user/profile` | 5 min |
| Stock holdings | `GET /portfolio/holdings` | 5 min |
| MF holdings | `GET /mf/holdings` | 5 min |
| SIPs | `GET /mf/sips` | 5 min |
| MF orders (last 7 days) | `GET /mf/orders` | 5 min |
| MF instruments master (CSV) | `GET /mf/instruments` | 24 h, cached in memory and on disk |

### `/portfolio/holdings` item (fields we use; **verify on the official docs**)
```json
{ "tradingsymbol": "INFY", "exchange": "NSE", "instrument_token": 408065, "isin": "INE009A01021",
  "product": "CNC", "quantity": 12, "t1_quantity": 0, "average_price": 1400.0,
  "last_price": 1500.0, "close_price": 1490.0, "pnl": 1200.0,
  "day_change": 10.0, "day_change_percentage": 0.67 }
```
Value = `(quantity + t1_quantity) × last_price`.

### `/mf/holdings` item (from the official docs)
```json
{ "folio": "3108290884", "fund": "INVESCO INDIA TAX PLAN - DIRECT PLAN", "tradingsymbol": "INF205K01NT8",
  "average_price": 78.43, "last_price": 84.86, "last_price_date": "", "pledged_quantity": 0,
  "pnl": 0, "quantity": 382.488 }
```
`tradingsymbol` **is the ISIN**. Value = `quantity × last_price`. `last_price_date` can be empty. If it is, fall back to the AMFI NAV date for that ISIN.

### `/mf/sips` item (from the official docs)
```json
{ "sip_id": "892741486820670", "tradingsymbol": "INF209K01VD7", "fund": "…",
  "status": "ACTIVE", "frequency": "weekly", "instalment_amount": 500.0,
  "instalments": -1, "completed_instalments": 0, "pending_instalments": -1,
  "instalment_day": 0, "next_instalment": "2021-05-12", "last_instalment": "2021-05-05 05:56:27",
  "created": "2021-05-05 05:56:27", "dividend_type": "idcw", "transaction_type": "BUY",
  "sip_type": "sip", "sip_reg_num": null, "trigger_price": 0, "step_up": { "05-05": 10 }, "tag": "…" }
```
Treat `status` values other than `ACTIVE` (e.g. `PAUSED`, `CANCELLED`) as not active. `instalments: -1` means perpetual.

### `/mf/orders` item (fields we use)
`order_id`, `tradingsymbol`, `fund`, `status` (`COMPLETE` / `REJECTED` / `CANCELLED` / `OPEN` …), `status_message`, `variety` (`regular` | `sip`), `order_timestamp`, `amount`, `transaction_type`.
A **failed SIP order** is `variety == "sip"` and `status` in (`REJECTED`, `CANCELLED`).

### `/mf/instruments` (gzipped CSV, from the official docs)
Columns: `tradingsymbol, amc, name, purchase_allowed, redemption_allowed, minimum_purchase_amount, purchase_amount_multiplier, minimum_additional_purchase_amount, minimum_redemption_quantity, redemption_quantity_multiplier, dividend_type, scheme_type, plan, settlement_type, last_price, last_price_date`

We use `tradingsymbol` (ISIN), `amc`, `name`, `dividend_type`, `scheme_type` (→ asset class via `Classification:SchemeTypeMap`) and `plan` (`direct` / `regular`).
**Log the distinct `scheme_type` values seen** on first live use and record them in `DECISIONS.md`. Unknown values map to `Other` with a warning.

> **To verify in Phase 8:** whether `/mf/instruments` (and the `last_price` inside it) is available on the Personal plan. If it isn't, fall back to AMFI for the NAV and to the fund name (`Direct`/`Regular` in the name) for the plan type, and record that.

## The mock: `PortfolioCopilot.MockKite`

A minimal API that behaves like Kite, so everything is testable without credentials.

| Route | Behaviour |
|---|---|
| `GET /connect/login?v=3&api_key=…` | 302 to the `redirect` query value, or to `http://127.0.0.1:5080/kite/callback`, with `request_token=mock-request-token&status=success` |
| `POST /session/token` | checks that the form has all 3 fields and the checksum is 64 hex chars; returns a profile + `access_token: "mock-access-token"` |
| `DELETE /session/token` | `{status:"success",data:true}` |
| `GET /user/profile` | profile from the scenario |
| `GET /portfolio/holdings`, `/mf/holdings`, `/mf/sips`, `/mf/orders` | arrays from the **current scenario** in the Kite envelope |
| `GET /mf/instruments` | `samples/kite/mf-instruments.csv`, **gzipped**, `Content-Type: text/csv` |
| `POST /__scenario/{name}` | switches the current scenario (`demo` by default; others from `samples/scenarios/*.json`) |
| `POST /__fail/{mode}?count=N` | the next N Kite requests (default 1) fail: `token` (403 TokenException), `ratelimit` (429), `down` (503), `none` clears it |
| `POST /__reset` | back to `demo`, no failures, empty logs (tests) |
| `GET /__requests` | the Kite requests received: method, path, status, and the checksum for `POST /session/token` (tests; doc 08's checksum echo, also sent as the `X-Mock-Checksum` header) |
| `GET /__unexpected` | every request that hit no route (they're also logged as warnings and get 404) |

Every data route requires `Authorization: token {key}:mock-access-token` and `X-Kite-Version: 3`. Otherwise it returns 403 `TokenException`, which tests the session-expiry path for real.

Scenario files (doc 08) hold Kite-shaped arrays with minimal fields. The mock fills missing numeric fields with `0` and missing strings with `""` before serialising, in snake_case.
