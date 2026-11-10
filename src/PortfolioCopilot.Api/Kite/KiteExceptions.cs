namespace PortfolioCopilot.Api.Kite;

/// <summary>Any Kite error that isn't a session or availability problem. Maps to 502.</summary>
public class KiteException(string errorType, string message) : Exception(message)
{
    public string ErrorType { get; } = errorType;
}

/// <summary>No session, or Kite said 403 TokenException. Maps to 401 <c>kite-session-required</c>.</summary>
public sealed class KiteSessionExpiredException(string message = "The Kite session is missing or has expired.")
    : KiteException("TokenException", message);

/// <summary>Kite rate-limited us twice, returned 5xx, timed out or was unreachable. Maps to 503 <c>kite-unavailable</c>.</summary>
public sealed class KiteUnavailableException(string message, Exception? inner = null) : Exception(message, inner);
