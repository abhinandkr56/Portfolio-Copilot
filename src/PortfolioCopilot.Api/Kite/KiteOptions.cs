namespace PortfolioCopilot.Api.Kite;

/// <summary>The <c>Kite</c> config section (doc 02). No secrets here: the key and secret are read from the env vars named below.</summary>
public sealed class KiteOptions
{
    public string Mode { get; set; } = "Mock";
    public string LiveBaseUrl { get; set; } = "https://api.kite.trade";
    public string LiveLoginUrl { get; set; } = "https://kite.zerodha.com/connect/login";
    public string MockBaseUrl { get; set; } = "http://mockkite";
    public string MockLoginUrl { get; set; } = "http://127.0.0.1:5100/connect/login";
    public string ApiKeyVariable { get; set; } = "KITE_API_KEY";
    public string ApiSecretVariable { get; set; } = "KITE_API_SECRET";
    public string RedirectUrl { get; set; } = "http://127.0.0.1:5080/kite/callback";
    public string WebAppUrl { get; set; } = "http://127.0.0.1:3000";
    public int CacheSeconds { get; set; } = 300;

    /// <summary>Where the 24-hour MF instruments CSV is kept on disk.</summary>
    public string InstrumentsCacheDir { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PortfolioCopilot", "cache");

    public bool IsMock => string.Equals(Mode, "Mock", StringComparison.OrdinalIgnoreCase);

    public string BaseUrl => IsMock ? MockBaseUrl : LiveBaseUrl;

    public string LoginUrl => IsMock ? MockLoginUrl : LiveLoginUrl;
}

/// <summary>
/// The Kite API key and secret, read from environment variables only. In Mock mode they default to fixed fake values,
/// so the app runs with no credentials. <see cref="ToString"/> never prints the secret.
/// </summary>
public sealed class KiteCredentials(string? apiKey, string? apiSecret)
{
    public const string MockApiKey = "mock-api-key";
    public const string MockApiSecret = "mock-api-secret";

    public string? ApiKey { get; } = apiKey;

    public string? ApiSecret { get; } = apiSecret;

    public bool IsComplete => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(ApiSecret);

    public static KiteCredentials FromEnvironment(KiteOptions options)
    {
        var key = Environment.GetEnvironmentVariable(options.ApiKeyVariable);
        var secret = Environment.GetEnvironmentVariable(options.ApiSecretVariable);
        return options.IsMock
            ? new KiteCredentials(string.IsNullOrEmpty(key) ? MockApiKey : key, string.IsNullOrEmpty(secret) ? MockApiSecret : secret)
            : new KiteCredentials(key, secret);
    }

    public override string ToString() => $"KiteCredentials(ApiKey set: {!string.IsNullOrEmpty(ApiKey)}, ApiSecret set: {!string.IsNullOrEmpty(ApiSecret)})";
}
