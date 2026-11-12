using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PortfolioCopilot.Api.Kite;

/// <summary>Maps Kite and AI-provider exceptions to ProblemDetails (doc 03, doc 04 error table, doc 06 §Providers).</summary>
public sealed class KiteExceptionHandler(IProblemDetailsService problems, ILogger<KiteExceptionHandler> logger) : IExceptionHandler
{
    public const string SessionRequired = "kite-session-required";
    public const string Unavailable = "kite-unavailable";
    public const string AiUnavailable = "ai-unavailable";

    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            KiteSessionExpiredException => new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized, Type = SessionRequired,
                Title = "Connect your Zerodha account", Detail = "There is no Kite session, or it has expired.",
            },
            KiteUnavailableException => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable, Type = Unavailable,
                Title = "Zerodha is not responding", Detail = "Zerodha is not responding, try again in a minute.",
            },
            Ai.AiUnavailableException a => new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable, Type = AiUnavailable,
                Title = "The AI assistant is unavailable", Detail = a.Message,
            },
            KiteException k => new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway, Type = "kite-error",
                Title = "Zerodha returned an error", Detail = $"{k.ErrorType}: {k.Message}",
            },
            _ => null,
        };
        if (problem is null)
            return false;

        logger.LogWarning("Kite call failed: {Type} -> {Status}", exception.GetType().Name, problem.Status);
        ctx.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = problem, Exception = exception });
    }
}
