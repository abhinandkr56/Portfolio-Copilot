using Microsoft.AspNetCore.Http.HttpResults;
using PortfolioCopilot.Core.Contracts;

namespace PortfolioCopilot.Api.Ai;

public static class AskEndpoint
{
    public static void MapAskEndpoint(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/ask", Ask)
            .WithTags("AI")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    /// <summary>Ask a question about the portfolio (doc 03). 400 for an empty or overlong message; 503 <c>ai-unavailable</c> on provider errors.</summary>
    private static async Task<Results<Ok<AskResponse>, ValidationProblem>> Ask(AskRequest request, AskService ask, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["message"] = ["Type a question."] });
        if (request.Message.Length > AskService.MaxQuestionLength)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["message"] = [$"Keep it under {AskService.MaxQuestionLength} characters."] });
        return TypedResults.Ok(await ask.AskAsync(request, ct));
    }
}
