using System.Net;
using System.Net.Http.Json;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;
using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Evals;

/// <summary>
/// The full run against the Api (doc 08 §3): switch the mock scenario, compare the deterministic review with
/// <c>expected.findings</c>, then check Explain and (when the scenario has one) Ask.
/// </summary>
public sealed class ApiRunner : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions Json = ScenarioParser.JsonOptions;
    private static readonly TimeSpan[] RateLimitWaits = [TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60)];
    private readonly HttpClient _http;
    private readonly EvalOptions _options;

    public ApiRunner(EvalOptions options)
    {
        _options = options;
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer(), UseCookies = true })
        {
            BaseAddress = options.ApiBase,
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    public async Task<SessionResponse> SessionAsync() => (await _http.GetFromJsonAsync<SessionResponse>("api/session", Json))!;

    /// <summary>Walks the mock login like a browser (Api → MockKite → callback), keeping cookies, and stops at the web app.</summary>
    public async Task LoginAsync()
    {
        var url = new Uri(_options.ApiBase, "kite/login");
        for (var hop = 0; hop < 10; hop++)
        {
            using var response = await _http.GetAsync(url);
            if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.TemporaryRedirect))
                throw new InvalidOperationException($"Login stopped at {url} with {(int)response.StatusCode}.");
            var next = new Uri(url, response.Headers.Location!);
            if (next.Query.Contains("connected=1"))
                return;
            if (next.Query.Contains("connected=0"))
                throw new InvalidOperationException($"Login failed: {next.Query}");
            url = next;
        }
        throw new InvalidOperationException("Login took too many redirects.");
    }

    public async Task<ScenarioResult> RunAsync(EvalScenario s)
    {
        var result = new ScenarioResult(s.Name);
        (await _http.PostAsync($"api/dev/scenario/{s.Name}", null)).EnsureSuccessStatusCode();

        // 1. Deterministic: the rules through the Api.
        var plain = await PostReviewAsync(explain: false);
        result.Deterministic.Add(Compare.Findings(
            s.Scenario.Expected.Findings.Select(f => (f.Rule, f.Subject, f.Severity.ToString())),
            plain.Findings.Select(f => (f.Rule, f.Subject, f.Severity.ToString()))));
        result.Deterministic.Add(Compare.Allocation(s.Scenario.Expected.Allocation,
            plain.Allocation.Classes.ToDictionary(c => c.AssetClass, c => c.ActualPercent)));

        // 2. Explain.
        var explained = await WithRateLimitRetryAsync(async () =>
        {
            var r = await PostReviewAsync(explain: true);
            return (r, IsRateLimited(r.Ai?.Error));
        });
        var summary = explained.Summary;
        if (explained.Ai?.Error is { } error)
        {
            result.Ai.Add(Check.Of("Ex:numbers", false, $"AI error: {error}"));
        }
        else
        {
            result.Ai.Add(Check.Of("Ex:numbers", explained.Ai?.NumberCheckPassed == true,
                $"numberCheckPassed=false after {explained.Ai?.Attempts} attempts (fallback shown)"));
            var language = ForbiddenLanguageCheck.Check(summary ?? "");
            result.Ai.Add(Check.Of("Ex:language", language.Passed, $"forbidden: {string.Join(", ", language.Problems)}"));
            var acts = s.Scenario.Expected.Findings.Where(f => f.Severity == Severity.Act).Select(f => f.Subject).ToList();
            var missing = acts.Where(subject => !Text.Mentions(summary ?? "", subject)).ToList();
            result.Ai.Add(acts.Count == 0 ? Check.NA("Ex:Act named") : Check.Of("Ex:Act named", missing.Count == 0,
                $"doesn't mention: {string.Join(", ", missing)} · summary: \"{Text.Snippet(summary)}\""));
            result.Ai.Add(Check.Of("Ex:disclaimer", Text.EndsWithDisclaimer(summary), $"ends: \"{Text.Snippet(summary?[^Math.Min(80, summary.Length)..])}\""));
        }
        result.Info.Add(new Check("Ex:attempts", Mark.Info, explained.Ai?.Attempts?.ToString() ?? "–"));

        // 3. Ask, when the scenario has a question.
        if (s.Ask is null)
        {
            foreach (var c in new[] { "Ask:language", "Ask:must", "Ask:must-not", "Ask:disclaimer" })
                result.Ai.Add(Check.NA(c));
            return result;
        }

        var (answer, askError, verified) = await WithRateLimitRetryAsync(() => AskAsync(s.Ask.Message));
        result.Info.Add(new Check("Ask:verified", Mark.Info, verified switch { true => "yes", false => "fallback", null => "–" }));

        if (askError is not null)
        {
            result.Ai.Add(Check.Of("Ask:language", false, $"Ask error: {askError}"));
            return result;
        }
        var askLanguage = ForbiddenLanguageCheck.Check(answer ?? "");
        result.Ai.Add(Check.Of("Ask:language", askLanguage.Passed, $"forbidden: {string.Join(", ", askLanguage.Problems)}"));
        var absent = s.Ask.MustContain.Where(p => !Text.Mentions(answer ?? "", p)).ToList();
        result.Ai.Add(s.Ask.MustContain.Count == 0 ? Check.NA("Ask:must") : Check.Of("Ask:must", absent.Count == 0,
            $"missing: {string.Join(", ", absent)} · answer: \"{Text.Snippet(answer)}\""));
        var present = s.Ask.MustNotContain.Where(p => Text.Mentions(answer ?? "", p)).ToList();
        result.Ai.Add(Check.Of("Ask:must-not", present.Count == 0, $"contains: {string.Join(", ", present)} · answer: \"{Text.Snippet(answer)}\""));
        result.Ai.Add(Check.Of("Ask:disclaimer", Text.EndsWithDisclaimer(answer), "no disclaimer at the end"));
        return result;
    }

    private async Task<PortfolioReview> PostReviewAsync(bool explain)
    {
        var response = await _http.PostAsJsonAsync("api/review", new ReviewRequest(explain), Json);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST /api/review {{explain:{explain.ToString().ToLowerInvariant()}}} → {(int)response.StatusCode}: {Text.Snippet(await response.Content.ReadAsStringAsync())}");
        return (await response.Content.ReadFromJsonAsync<PortfolioReview>(Json))!;
    }

    /// <summary>Returns (answer, null, verified) on success or (null, error, null) on failure, plus whether it was a rate limit.</summary>
    private async Task<((string? Answer, string? Error, bool? Verified) Value, bool RateLimited)> AskAsync(string message)
    {
        var response = await _http.PostAsJsonAsync("api/ask", new AskRequest(null, message), Json);
        if (response.IsSuccessStatusCode)
        {
            var ask = (await response.Content.ReadFromJsonAsync<AskResponse>(Json))!;
            return ((ask.Answer, null, ask.NumberCheckPassed), false);
        }
        var problem = await response.Content.ReadAsStringAsync();
        return ((null, $"{(int)response.StatusCode} {Text.Snippet(problem)}", null), IsRateLimited(problem));
    }

    private static bool IsRateLimited(string? text) => text?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Free-tier LLM limits are per minute: wait and retry a rate-limited call (up to 3 times).</summary>
    private static async Task<T> WithRateLimitRetryAsync<T>(Func<Task<(T Value, bool RateLimited)>> call)
    {
        for (var i = 0; ; i++)
        {
            var (value, limited) = await call();
            if (!limited || i >= RateLimitWaits.Length)
                return value;
            Console.WriteLine($"  rate limited; waiting {RateLimitWaits[i].TotalSeconds:0} s");
            await Task.Delay(RateLimitWaits[i]);
        }
    }

    public void Dispose() => _http.Dispose();
}
