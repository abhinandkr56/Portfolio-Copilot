using System.Text.RegularExpressions;

namespace PortfolioCopilot.Api.Logging;

/// <summary>
/// Removes secrets from log text (doc 09): the values of access_token, request_token, checksum and api_secret
/// (as query parameters, form fields or JSON properties) and the Kite Authorization header value.
/// </summary>
public static partial class LogRedactor
{
    public const string Mask = "***";

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        text = QueryOrForm().Replace(text, m => m.Groups[1].Value + Mask);
        text = JsonProperty().Replace(text, m => m.Groups[1].Value + Mask + "\"");
        text = AuthorizationToken().Replace(text, m => m.Groups[1].Value + Mask);
        return text;
    }

    // access_token=abc, request_token: abc, "checksum"="abc" (form/query/log-template styles)
    [GeneratedRegex(@"((?:access_token|request_token|checksum|api_secret|enctoken|refresh_token|public_token)\s*[=:]\s*)(?!"")[^&\s,;""'}]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryOrForm();

    // "access_token": "abc"
    [GeneratedRegex(@"(""(?:access_token|accessToken|request_token|requestToken|checksum|api_secret|apiSecret|enctoken|refresh_token|public_token)""\s*:\s*"")[^""]*""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonProperty();

    // Authorization: token key:secret  /  token key:secret
    [GeneratedRegex(@"((?:authorization[""']?\s*[:=]\s*[""']?)?\btoken\s+)[A-Za-z0-9_\-]+:[A-Za-z0-9_\-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationToken();
}

/// <summary>
/// Wraps the app's <see cref="ILoggerFactory"/> so every logger, from every provider (console, OpenTelemetry, test
/// providers), sees redacted messages and redacted structured values.
/// </summary>
public sealed class RedactingLoggerFactory(ILoggerFactory inner) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new RedactingLogger(inner.CreateLogger(categoryName));

    public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

    public void Dispose() => inner.Dispose();

    private sealed class RedactingLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!inner.IsEnabled(logLevel))
                return;

            IReadOnlyList<KeyValuePair<string, object?>> values = state is IReadOnlyList<KeyValuePair<string, object?>> list
                ? list.Select(kv => kv.Value is string s ? new KeyValuePair<string, object?>(kv.Key, LogRedactor.Redact(s)) : kv).ToList()
                : [];
            var message = LogRedactor.Redact(formatter(state, exception));
            inner.Log(logLevel, eventId, new RedactedState(values, message), exception is null ? null : new RedactedException(exception),
                static (s, _) => s.Message);
        }
    }

    /// <summary>Structured values (redacted) plus the redacted message, which OpenTelemetry exports as attributes and body.</summary>
    private sealed class RedactedState(IReadOnlyList<KeyValuePair<string, object?>> values, string message)
        : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public string Message { get; } = message;
        public KeyValuePair<string, object?> this[int index] => values[index];
        public int Count => values.Count;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => values.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public override string ToString() => Message;
    }

    /// <summary>An exception whose message and stack trace text are redacted; the original type name is kept for diagnosis.</summary>
    private sealed class RedactedException(Exception inner) : Exception(LogRedactor.Redact($"{inner.GetType().FullName}: {inner.Message}"))
    {
        public override string? StackTrace => LogRedactor.Redact(inner.StackTrace ?? "");
        public override string ToString() => LogRedactor.Redact(inner.ToString());
    }
}
