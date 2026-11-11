using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PortfolioCopilot.Api.Data;
using PortfolioCopilot.Api.Kite;
using PortfolioCopilot.Core;
using PortfolioCopilot.Core.Contracts;
using PortfolioCopilot.Core.Testing;

namespace PortfolioCopilot.Api.Portfolio;

/// <summary>Doc 03 endpoints. Each one only orchestrates: fetch → Core computes → ContractMapper formats.</summary>
public static class PortfolioEndpoints
{
    public static void MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        var data = app.MapGroup("/api").WithTags("Portfolio");
        data.MapGet("/holdings", GetHoldings).ProducesKiteProblems();
        data.MapGet("/sips", GetSips).ProducesKiteProblems();
        data.MapGet("/allocation", GetAllocation).ProducesKiteProblems();
        data.MapPost("/review", PostReview).ProducesKiteProblems();

        var plan = app.MapGroup("/api/plan").WithTags("Plan");
        plan.MapGet("", (PlanStore plans, CancellationToken ct) => plans.GetCurrentAsync(ct));
        plan.MapPut("", PutPlan);
    }

    public static void MapDevEndpoints(this IEndpointRouteBuilder app, KiteOptions kite)
    {
        var dev = app.MapGroup("/api").WithTags("Development");
        dev.MapPost("/nav/import", ImportNav);
        if (kite.IsMock)
            dev.MapPost("/dev/scenario/{name}", SwitchScenario);
    }

    private static async Task<HoldingsResponse> GetHoldings(PortfolioService portfolio, CopilotDbContext db,
        TimeProvider time, ILogger<PortfolioService> logger, CancellationToken ct)
    {
        var s = await portfolio.GetAsync(ct);
        var holdings = ContractMapper.Holdings(s.Input, s.Computed, s.AsOfUtc);
        await WriteDailySnapshotAsync(db, time, holdings, ContractMapper.Allocation(s.Computed, portfolio.Evaluate(s), s.AsOfUtc), logger, ct);
        return holdings;
    }

    private static async Task<SipsResponse> GetSips(PortfolioService portfolio, CancellationToken ct)
    {
        var s = await portfolio.GetAsync(ct);
        return ContractMapper.Sips(s.Input, s.Computed, s.Clock, s.AsOfUtc);
    }

    private static async Task<AllocationResponse> GetAllocation(PortfolioService portfolio, CancellationToken ct)
    {
        var s = await portfolio.GetAsync(ct);
        return ContractMapper.Allocation(s.Computed, portfolio.Evaluate(s), s.AsOfUtc);
    }

    /// <summary>Runs every rule (deterministic) and audits the run. <c>explain: true</c> arrives in Phase 5.</summary>
    private static async Task<PortfolioReview> PostReview(ReviewRequest? request, PortfolioService portfolio,
        CopilotDbContext db, TimeProvider time, CancellationToken ct)
    {
        var s = await portfolio.GetAsync(ct);
        var findings = portfolio.Evaluate(s);
        var review = ContractMapper.Review(s.Computed, findings, s.AsOfUtc);

        db.ReviewRuns.Add(new ReviewRunRow
        {
            Utc = time.GetUtcNow(),
            Kind = "review",
            FindingsJson = JsonSerializer.Serialize(review.Findings, PlanStore.Json),
        });
        await db.SaveChangesAsync(ct);
        return review;
    }

    private static async Task<Results<Ok<Plan>, ValidationProblem>> PutPlan(Plan plan, PlanStore plans, CancellationToken ct)
    {
        var errors = PlanValidator.Validate(plan);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        await plans.SaveAsync(plan, ct);
        return TypedResults.Ok(await plans.GetCurrentAsync(ct));
    }

    /// <summary>Imports an AMFI file from disk. The path is relative to the Api's output folder (where <c>samples/</c> is copied).</summary>
    private static async Task<Results<Ok<NavImportResponse>, ProblemHttpResult>> ImportNav(NavImportRequest request, NavStore navs, CancellationToken ct)
    {
        var root = Path.GetFullPath(AppContext.BaseDirectory);
        var full = Path.GetFullPath(Path.Combine(root, request.Path ?? ""));
        if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full))
            return TypedResults.Problem($"No file at '{request.Path}' under the Api's folder.", statusCode: 400);
        try
        {
            return TypedResults.Ok(await navs.ImportAsync(await File.ReadAllTextAsync(full, ct), ct));
        }
        catch (AmfiFormatException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: 400, title: "Unrecognised AMFI file");
        }
    }

    /// <summary>Mock only: switches MockKite's scenario, loads the scenario's plan, and clears the cached Kite data.</summary>
    private static async Task<Results<Ok<ScenarioResponse>, ProblemHttpResult>> SwitchScenario(
        string name, MockControl mock, PlanStore plans, IMemoryCache cache, CancellationToken ct)
    {
        var file = Path.Combine(DataSeeder.SamplesDir, "scenarios", name + ".json");
        if (name.Any(c => !(char.IsLetterOrDigit(c) || c == '-')) || !File.Exists(file))
            return TypedResults.Problem($"No scenario '{name}'.", statusCode: 404);

        var (plan, today) = ScenarioParser.ParseHeader(await File.ReadAllTextAsync(file, ct));
        await mock.SwitchAsync(name, ct);
        await plans.SaveAsync(plan, ct);
        PortfolioService.ClearCache(cache);
        return TypedResults.Ok(new ScenarioResponse(name, today));
    }

    private static async Task WriteDailySnapshotAsync(CopilotDbContext db, TimeProvider time, HoldingsResponse holdings,
        AllocationResponse allocation, ILogger logger, CancellationToken ct)
    {
        var today = Ist.Today(time);
        if (await db.Snapshots.AnyAsync(x => x.Date == today, ct))
            return;
        db.Snapshots.Add(new SnapshotRow { Date = today, Json = JsonSerializer.Serialize(new { holdings, allocation }, PlanStore.Json) });
        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Saved the daily snapshot for {Date}", today);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // another request wrote today's snapshot first
        }
    }

    private static RouteHandlerBuilder ProducesKiteProblems(this RouteHandlerBuilder b) =>
        b.ProducesProblem(StatusCodes.Status401Unauthorized)
         .ProducesProblem(StatusCodes.Status502BadGateway)
         .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
