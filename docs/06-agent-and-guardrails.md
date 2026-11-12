# 06 · AI agent and guardrails

## Two AI features

| Feature | Endpoint | How |
|---|---|---|
| **Explain** | `POST /api/review { explain: true }` | One chat call, **no tools**. Input: the redacted review JSON (allocation + findings + plan targets). Output: a 4–8 sentence summary. |
| **Ask** | `POST /api/ask` | An agent **with tools** (below), with conversation history. |

Both go through the same pipeline:

```
IChatClient (provider from config)
  └─ .UseOpenTelemetry()                         traces + token counts
  └─ .UseFunctionInvocation()                    (Ask only; or an Agent Framework agent)
then, in the endpoint:
  answer → ForbiddenLanguageCheck → NumberCheck → (fail: retry once with feedback → fail: fallback)
         → append disclaimer if missing → audit row in ReviewRuns
```

## System prompt (both features)

```
You are Portfolio Copilot, a review assistant for ONE person: the account owner.
You explain their portfolio against THEIR OWN PLAN.

Rules:
- Use only facts from tool results or the review JSON you are given. Never invent a number,
  a fund, a stock, a price, a return or a date.
- Never recommend buying, selling, holding, switching into or exiting any specific security.
  Never predict prices, returns or market direction.
- When a plan rule is broken, name the rule, quote the evidence numbers exactly as given,
  and use only these suggestions: "Review", "Rebalance toward target",
  "Consider Direct plan", "Check SIP mandate".
- If the user asks for a recommendation or a prediction, say politely that you can't give
  investment advice, then show the facts from their plan that are relevant to the question.
- If data is missing or stale, say so plainly.
- Be brief: short sentences, no tables, no markdown headings.
- End with exactly: "Information only, not investment advice. You make the decisions."
```

Explain adds: `Summarise the review below in 4-8 sentences, most important finding first.` + the JSON.

## Tools (Ask), read-only, each one an `AIFunction` with a clear description

| Tool | Parameters | Returns (redacted JSON) |
|---|---|---|
| `GetPlan` | – | the plan: targets, band, caps, SIP budget, preferDirect |
| `GetHoldings` | `kind`: `"stocks"` \| `"funds"` \| `"all"` | holdings: name, asset class, plan type, percentOfTotal, (value if ShareAmounts) |
| `ComputeAllocation` | – | the allocation rows from doc 03 |
| `GetFindings` | – | the full findings list (runs the rules) |
| `GetSips` | – | SIPs + active monthly equivalent + recent failed orders |
| `ExplainRule` | `rule` | a static description of the rule and its formula (from doc 05) |

Every tool:
- calls the same services as the endpoints (no duplicate logic),
- starts an `Activity` named `tool.{Name}` with attributes `tool.name` and `tool.result_count`,
- returns JSON **after** `Redactor.Redact(...)`,
- is recorded in `toolsUsed`.

Use the Microsoft Agent Framework if it restores cleanly (instructions = the system prompt, tools = above, thread = per `conversationId`). Otherwise use `ChatClientBuilder.UseFunctionInvocation()` with a `List<ChatMessage>` per conversation. Record the choice in `DECISIONS.md`.

## Redaction (`Core/Redactor`)

Applied to everything that's sent to the LLM:
- `folio` → `****` + last 4 · `user_id` → first 2 + `***` + last 2 · drop `email`, `user_name`, `access_token`, `order_id`, `sip_id`
- **Amounts:** when `AI:ShareAmounts` is `false` (the default), remove the `value`, `pnl`, `averagePrice`, `lastPrice`, `instalmentAmount`, `activeMonthlyEquivalent` and `monthlyBudget` fields and keep the percentages. When it's `true`, keep them.
- A unit test asserts that no masked or removed field survives.

## Guardrail 1: ForbiddenLanguageCheck (`Core`)

Case-insensitive regexes over the answer **without** the disclaimer sentence:

```
\b(you should|i recommend|i suggest|consider)\s+(buy|buying|sell|selling|exit|exiting|accumulat\w*|switch\w*\s+to)\b
\b(buy|sell)\s+(more\s+)?[A-Z]{2,}\b            (applied case-sensitively to tickers)
\btarget price\b
\b(will|is going to|likely to)\s+(rise|fall|go up|go down|outperform|underperform)\b
\bguarantee(d)?\b
```

`Consider Direct plan` is allowed: it's an allowed suggestion. Match the allowed phrases first, then test the rest.

## Guardrail 2: NumberCheck (`Core`)

1. Extract the numbers from the answer (without the disclaimer) with `-?\d{1,3}(?:,\d{2,3})*(?:\.\d+)?|-?\d+(?:\.\d+)?`. Strip commas, `%`, `₹`, and a leading `+`.
2. Build the **allowed set** from every number in the tool-result JSON and the review JSON sent this turn (Ask also allows the numbers in the user's own question). For each one, add its absolute value and roundings to 0, 1 and 2 dp.
3. Always allow: integers 0–12 (counts, months), years 2000–2100, and numbers inside dates matching `\d{4}-\d{2}-\d{2}`.
4. Any extracted number not in the allowed set (compare as decimal) → **fail**, listing the unknown numbers.

On a failure of either guardrail: retry **once**, appending a user message such as `Your previous answer contained numbers not present in the data: 23.4, 31. Rewrite using only the numbers given.` (or names the forbidden phrase). If it fails again:
- **Explain:** replace the summary with the C# fallback, the finding messages joined, plus the disclaimer, and set `numberCheckPassed=false`.
- **Ask:** return `I couldn't produce an answer I can verify. Here are the facts:` + `GetFindings` messages, plus the disclaimer.

## Structured output

Explain returns plain text, not JSON, to keep it simple. (`NativeJsonSchema` is present in config for consistency with the course and for future use.)

## Providers

`AI:Provider` = `Groq` (default) | `Gemini` | `Ollama`. The API's `/api/session` response and the Web footer show the current provider and model. Provider failures (401, 404 model, 429) become short user-facing errors, never stack traces: Explain returns 200 with `ai.error`; Ask returns 503 `ai-unavailable`.

**Gemini 3 tool calls** need their `extra_content.google.thought_signature` sent back on the next turn (otherwise 400). The .NET OpenAI client drops it, so `GeminiThoughtSignatureHandler` (Gemini only) re-attaches it by tool-call id.

**Telemetry:** chat, `invoke_agent`, `execute_tool` and `tool.{Name}` spans with token counts. Message content is **not** recorded unless `AI:TraceContent=true` (Aspire sets `OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT`, which would otherwise record prompts, answers and tool results).
