using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Api.Tests;

[Collection(ApiCollection.Name)]
public class PortfolioEndpointTests(ApiFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync() => fx.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static string SamplesDir => Path.Combine(AppContext.BaseDirectory, "samples");

    public static TheoryData<string> Scenarios => new(
        Directory.GetFiles(Path.Combine(SamplesDir, "scenarios"), "*.json").Select(Path.GetFileNameWithoutExtension).Order()!);

    private async Task<HttpClient> ConnectedAsync(string scenario = "demo")
    {
        var browser = fx.Browser();
        (await browser.PostAsync($"/api/dev/scenario/{scenario}", null)).EnsureSuccessStatusCode();
        await fx.LoginAsync(browser);
        return browser;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Review_of_every_scenario_equals_expected_findings_and_allocation(string name)
    {
        var expected = ScenarioParser.Parse(
            File.ReadAllText(Path.Combine(SamplesDir, "scenarios", name + ".json")),
            File.ReadAllText(Path.Combine(SamplesDir, "kite", "mf-instruments.csv")),
            File.ReadAllText(Path.Combine(SamplesDir, "amfi", "NAVAll.sample.txt"))).Expected;
        var browser = await ConnectedAsync(name);

        var review = await JsonAsync(await browser.PostAsJsonAsync("/api/review", new { explain = false }));

        var findings = review.GetProperty("findings").EnumerateArray()
            .Select(f => (f.GetProperty("rule").GetString(), f.GetProperty("subject").GetString(), f.GetProperty("severity").GetString()))
            .ToList();
        Assert.Equal(expected.Findings.Select(f => ((string?)f.Rule, (string?)f.Subject, (string?)f.Severity.ToString())).ToList(), findings);

        var allocation = review.GetProperty("allocation").GetProperty("classes").EnumerateArray()
            .ToDictionary(c => Enum.Parse<AssetClass>(c.GetProperty("assetClass").GetString()!), c => c.GetProperty("actualPercent").GetDecimal());
        Assert.Equal(expected.Allocation.OrderBy(kv => kv.Key), allocation.OrderBy(kv => kv.Key));

        Assert.Equal(Disclaimer.Text, review.GetProperty("disclaimer").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("summary").ValueKind);
        Assert.Equal(JsonValueKind.Null, review.GetProperty("ai").ValueKind);
    }

    [Fact]
    public async Task Demo_review_findings_carry_evidence_and_messages()
    {
        var browser = await ConnectedAsync();

        var review = await JsonAsync(await browser.PostAsJsonAsync("/api/review", new { explain = false }));

        Assert.Equal("2026-11-07", review.GetProperty("navDate").GetString());
        var first = review.GetProperty("findings")[0];
        Assert.Equal("Equity is 84.5% against a target of 70.0% (drift +14.5 points, band ±5.0).", first.GetProperty("message").GetString());
        var evidence = first.GetProperty("evidence");
        Assert.Equal(84.5m, evidence.GetProperty("actualPercent").GetDecimal());
        Assert.Equal(14.5m, evidence.GetProperty("driftPoints").GetDecimal());
        Assert.All(review.GetProperty("findings").EnumerateArray(), f => Assert.NotEmpty(f.GetProperty("evidence").EnumerateObject()));
    }

    [Fact]
    public async Task Holdings_returns_the_demo_totals_masked_folios_and_labels()
    {
        var browser = await ConnectedAsync();

        var h = await JsonAsync(await browser.GetAsync("/api/holdings"));

        Assert.Equal(100000m, h.GetProperty("totalValue").GetDecimal());
        var infy = h.GetProperty("stocks").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "INFY");
        Assert.Equal(18000m, infy.GetProperty("value").GetDecimal());
        Assert.Equal(18.0m, infy.GetProperty("percentOfTotal").GetDecimal());
        Assert.Equal("Equity", infy.GetProperty("assetClass").GetString());
        var gold = h.GetProperty("stocks").EnumerateArray().Single(s => s.GetProperty("symbol").GetString() == "GOLDBEES");
        Assert.Equal("Gold", gold.GetProperty("assetClass").GetString());

        var funds = h.GetProperty("funds").EnumerateArray().ToList();
        var alpha = funds.Single(f => f.GetProperty("isin").GetString() == "INFTEST00002");
        Assert.Equal("****0001", alpha.GetProperty("folioMasked").GetString());
        Assert.Equal("Regular", alpha.GetProperty("plan").GetString());
        Assert.Equal(22.0m, alpha.GetProperty("percentOfTotal").GetDecimal());
        var delta = funds.Single(f => f.GetProperty("isin").GetString() == "INFTEST00007");
        Assert.Equal("Hybrid", delta.GetProperty("assetClass").GetString());
        Assert.Equal("Hybrid (65/35)", delta.GetProperty("assetClassLabel").GetString());

        var raw = h.GetRawText();
        Assert.DoesNotContain("FOLIOTEST", raw);
        Assert.DoesNotContain("mock-access-token", raw);
    }

    [Fact]
    public async Task Sips_returns_the_monthly_equivalent_budget_and_recent_failures()
    {
        var browser = await ConnectedAsync();

        var s = await JsonAsync(await browser.GetAsync("/api/sips"));

        Assert.Equal(16500m, s.GetProperty("activeMonthlyEquivalent").GetDecimal());
        Assert.Equal(40000m, s.GetProperty("monthlyBudget").GetDecimal());
        Assert.Equal(3, s.GetProperty("sips").GetArrayLength());
        var weekly = s.GetProperty("sips").EnumerateArray().Single(x => x.GetProperty("frequency").GetString() == "weekly");
        Assert.Equal(6500m, weekly.GetProperty("monthlyEquivalent").GetDecimal());
        var failed = Assert.Single(s.GetProperty("recentFailedOrders").EnumerateArray());
        Assert.Equal("2026-11-05", failed.GetProperty("date").GetString());
        Assert.Equal("SIP: Insufficient balance.", failed.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Allocation_lists_equity_debt_gold_with_drift_severity()
    {
        var browser = await ConnectedAsync();

        var a = await JsonAsync(await browser.GetAsync("/api/allocation"));

        var classes = a.GetProperty("classes").EnumerateArray().ToList();
        Assert.Equal(["Equity", "Debt", "Gold"], classes.Select(c => c.GetProperty("assetClass").GetString()));
        Assert.Equal(["Act", "Watch", "Watch"], classes.Select(c => c.GetProperty("severity").GetString()));
        Assert.Equal(84500m, classes[0].GetProperty("value").GetDecimal());
        Assert.Equal(14.5m, classes[0].GetProperty("driftPoints").GetDecimal());
        Assert.Equal(-8.5m, classes[1].GetProperty("driftPoints").GetDecimal());
    }

    [Fact]
    public async Task Severity_is_null_within_the_band()
    {
        var browser = await ConnectedAsync("balanced");

        var a = await JsonAsync(await browser.GetAsync("/api/allocation"));

        Assert.All(a.GetProperty("classes").EnumerateArray(), c => Assert.Equal(JsonValueKind.Null, c.GetProperty("severity").ValueKind));
    }

    [Fact]
    public async Task Kite_data_is_cached_between_requests()
    {
        var browser = await ConnectedAsync();
        fx.KiteTraffic.Requests.Clear();

        await browser.GetAsync("/api/holdings");
        await browser.GetAsync("/api/sips");
        await browser.PostAsJsonAsync("/api/review", new { explain = false });

        Assert.Equal(1, fx.KiteTraffic.Requests.Count(r => r.Path == "/portfolio/holdings"));
        Assert.True(fx.KiteTraffic.Requests.Count(r => r.Path == "/mf/instruments") <= 1); // 24 h cache, possibly already on disk
    }

    [Fact]
    public async Task Data_endpoints_need_a_session()
    {
        var browser = fx.Browser();

        foreach (var response in new[]
                 {
                     await browser.GetAsync("/api/holdings"), await browser.GetAsync("/api/sips"),
                     await browser.GetAsync("/api/allocation"), await browser.PostAsJsonAsync("/api/review", new { explain = false }),
                 })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("kite-session-required", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("type").GetString());
        }
    }

    [Fact]
    public async Task Plan_is_seeded_from_the_sample()
    {
        var plan = await JsonAsync(await fx.Browser().GetAsync("/api/plan"));

        Assert.Equal(70m, plan.GetProperty("targetAllocation").GetProperty("Equity").GetDecimal());
        Assert.Equal("Gold", plan.GetProperty("assetClassOverrides").GetProperty("GOLDBEES").GetString());
        Assert.Equal(40000m, plan.GetProperty("monthlySipBudget").GetDecimal());
    }

    [Fact]
    public async Task Invalid_plan_is_400_with_field_errors()
    {
        var browser = fx.Browser();
        var plan = await JsonAsync(await browser.GetAsync("/api/plan"));
        var body = JsonSerializer.Deserialize<Dictionary<string, object>>(plan.GetRawText())!;
        body["targetAllocation"] = new Dictionary<string, decimal> { ["Equity"] = 70, ["Debt"] = 20, ["Gold"] = 5 };
        body["driftBandPoints"] = 0;
        body["assetClassOverrides"] = new Dictionary<string, string> { ["GOLDBEES"] = "Hybrid" }; // a dictionary keeps the key's case

        var response = await browser.PutAsJsonAsync("/api/plan", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("targetAllocation", out _));
        Assert.True(errors.TryGetProperty("driftBandPoints", out _));
        Assert.Contains("GOLDBEES", errors.GetProperty("assetClassOverrides")[0].GetString());
        Assert.Equal(10m, (await JsonAsync(await browser.GetAsync("/api/plan"))).GetProperty("targetAllocation").GetProperty("Gold").GetDecimal()); // not saved
    }

    [Fact]
    public async Task Saving_a_valid_plan_changes_the_review()
    {
        var browser = await ConnectedAsync();
        var plan = await JsonAsync(await browser.GetAsync("/api/plan"));
        var body = JsonSerializer.Deserialize<Dictionary<string, object>>(plan.GetRawText())!;
        body["targetAllocation"] = new Dictionary<string, decimal> { ["Equity"] = 85, ["Debt"] = 11, ["Gold"] = 4 };

        var saved = await JsonAsync(await browser.PutAsJsonAsync("/api/plan", body));
        var review = await JsonAsync(await browser.PostAsJsonAsync("/api/review", new { explain = false }));

        Assert.Equal(85m, saved.GetProperty("targetAllocation").GetProperty("Equity").GetDecimal());
        var rules = review.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("rule").GetString()).ToList();
        Assert.DoesNotContain("AllocationDrift", rules);
        Assert.Equal(5, rules.Count); // 8 demo findings minus the three drifts (-0.5, +0.5, 0.0 are inside the band)
    }

    [Fact]
    public async Task Each_review_writes_an_audit_row()
    {
        var browser = await ConnectedAsync();
        int Count()
        {
            using var scope = fx.Api.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<CopilotDbContext>().ReviewRuns.Count();
        }
        var before = Count();

        await browser.PostAsJsonAsync("/api/review", new { explain = false });
        await browser.PostAsJsonAsync("/api/review", new { explain = false });

        Assert.Equal(before + 2, Count());
        using var scope = fx.Api.Services.CreateScope();
        var last = await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().ReviewRuns.OrderByDescending(r => r.Id).FirstAsync();
        Assert.Equal("review", last.Kind);
        Assert.Contains("AllocationDrift", last.FindingsJson);
        Assert.Null(last.AnswerText);
    }

    [Fact]
    public async Task First_holdings_of_the_day_writes_one_snapshot()
    {
        using (var scope = fx.Api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CopilotDbContext>().Snapshots.ExecuteDeleteAsync();
        var browser = await ConnectedAsync();

        await browser.GetAsync("/api/holdings");
        await browser.GetAsync("/api/holdings");

        using var check = fx.Api.Services.CreateScope();
        var snapshot = await check.ServiceProvider.GetRequiredService<CopilotDbContext>().Snapshots.SingleAsync();
        Assert.Equal(Ist.Today(TimeProvider.System), snapshot.Date);
        Assert.Contains("\"totalValue\":100000", snapshot.Json);
        Assert.Contains("\"classes\"", snapshot.Json);
    }

    [Fact]
    public async Task Nav_import_reads_a_file_under_the_api_folder()
    {
        var browser = fx.Browser();

        var ok = await JsonAsync(await browser.PostAsJsonAsync("/api/nav/import", new { path = "samples/amfi/NAVAll.sample.txt" }));

        Assert.Equal(9, ok.GetProperty("imported").GetInt32());
        Assert.Equal("2026-11-07", ok.GetProperty("navDate").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/nav/import", new { path = "samples/nope.txt" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/nav/import", new { path = "../../../../../../etc/hosts" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/api/nav/import", new { path = "samples/plan.sample.json" })).StatusCode);
    }

    [Fact]
    public async Task Unknown_scenario_is_404()
    {
        var response = await fx.Browser().PostAsync("/api/dev/scenario/does-not-exist", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Scenario_switch_loads_its_plan_and_reports_its_date()
    {
        var browser = fx.Browser();

        var result = await JsonAsync(await browser.PostAsync("/api/dev/scenario/sip-health", null));
        var plan = await JsonAsync(await browser.GetAsync("/api/plan"));

        Assert.Equal("2026-11-10", result.GetProperty("today").GetString());
        Assert.Equal(20000m, plan.GetProperty("monthlySipBudget").GetDecimal());
    }

    [Fact]
    public async Task OpenApi_document_describes_the_endpoints_and_types()
    {
        var doc = await JsonAsync(await fx.Browser().GetAsync("/openapi/v1.json"));

        var paths = doc.GetProperty("paths");
        foreach (var path in new[] { "/api/holdings", "/api/sips", "/api/allocation", "/api/review", "/api/plan", "/api/session" })
            Assert.True(paths.TryGetProperty(path, out _), $"{path} missing from OpenAPI");
        var schemas = doc.GetProperty("components").GetProperty("schemas");
        foreach (var schema in new[] { "PortfolioReview", "HoldingsResponse", "SipsResponse", "AllocationResponse", "Plan", "SessionResponse", "FindingDto" })
            Assert.True(schemas.TryGetProperty(schema, out _), $"{schema} schema missing");
        Assert.Equal(HttpStatusCode.OK, (await fx.Browser().GetAsync("/swagger/index.html")).StatusCode);
    }
}
