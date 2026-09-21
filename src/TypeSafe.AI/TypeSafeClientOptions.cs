namespace TypeSafe.AI;

/// <summary>Retry, backoff, and deadline configuration. All durations must be positive; null
/// timeout budgets disable the corresponding strategy entirely (no infinite durations reach Polly).</summary>
public sealed class TypeSafeRetryOptions
{
    /// <summary>Retry attempts after the initial one. Zero disables the retry strategy.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Initial exponential-backoff delay. Jitter and Retry-After may change actual delays.</summary>
    public TimeSpan BackoffInitial { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Caps generated exponential backoff delays; Retry-After delays are not capped by it.</summary>
    public TimeSpan BackoffMax { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Adds randomness to generated backoff delays. Disable only in deterministic tests.</summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>Honors Retry-After response headers when the server supplies them.</summary>
    public bool RespectRetryAfter { get; set; } = true;

    /// <summary>Bounds each HTTP attempt (connection and response headers). Null disables it.</summary>
    public TimeSpan? PerAttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Budget including retries and body reads. Null disables the SDK deadline;
    /// caller cancellation and the supplied HTTP client's timeout still apply.</summary>
    public TimeSpan? TotalTimeoutBudget { get; set; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaxRetries is < 0 or > 24)
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), MaxRetries, "MaxRetries must be between 0 and 24.");
        if (BackoffInitial <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(BackoffInitial), BackoffInitial, "BackoffInitial must be positive.");
        if (BackoffMax < BackoffInitial)
            throw new ArgumentOutOfRangeException(nameof(BackoffMax), BackoffMax, "BackoffMax must not be smaller than BackoffInitial.");
        if (PerAttemptTimeout is { } attempt && attempt <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(PerAttemptTimeout), attempt, "PerAttemptTimeout must be positive when enabled.");
        if (TotalTimeoutBudget is { } budget && budget <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(TotalTimeoutBudget), budget, "TotalTimeoutBudget must be positive when enabled.");
    }
}

/// <summary>Configuration for the TypeSafe HTTP client. Validate secrets at startup and
/// injected-client construction; the reusable library never reads them from process-global state
/// except through <see cref="ApplyEnvironmentVariables"/>.</summary>
public sealed class TypeSafeClientOptions
{
    /// <summary>Default model used when a call does not override it.</summary>
    public string DefaultModel { get; set; } = "jev-latest";

    /// <summary>The TypeSafe API key. Supply from a secret store or the TYPESAFE_API_KEY environment variable.</summary>
    public string? ApiKey { get; set; }

    /// <summary>HTTPS API root ending in '/'; the request path is appended relatively, preserving gateway prefixes.</summary>
    public string BaseUrl { get; set; } = "https://api.typesafe.ai/";

    /// <summary>Permits an http:// base URL for local development only.</summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>Retry, backoff, and deadline configuration.</summary>
    public TypeSafeRetryOptions Retry { get; set; } = new();

    /// <summary>Applies the documented environment aliases: TYPESAFE_API_KEY, TYPESAFE_BASE_URL,
    /// and TYPESAFE_DEFAULT_MODEL. Values from a later configuration source (the registration
    /// callback) still override these.</summary>
    public void ApplyEnvironmentVariables()
    {
        if (Environment.GetEnvironmentVariable("TYPESAFE_API_KEY") is { Length: > 0 } apiKey)
            ApiKey = apiKey;
        if (Environment.GetEnvironmentVariable("TYPESAFE_BASE_URL") is { Length: > 0 } baseUrl)
            BaseUrl = baseUrl;
        if (Environment.GetEnvironmentVariable("TYPESAFE_DEFAULT_MODEL") is { Length: > 0 } model)
            DefaultModel = model;
    }

    /// <summary>Creates options with the environment aliases applied.</summary>
    /// <returns>A new <see cref="TypeSafeClientOptions"/> populated from environment variables.</returns>
    public static TypeSafeClientOptions FromEnvironment()
    {
        var options = new TypeSafeClientOptions();
        options.ApplyEnvironmentVariables();
        return options;
    }

    /// <summary>Validates the whole option graph, throwing with the offending member named.</summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DefaultModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(ApiKey);
        ValidateBaseUrl();
        ArgumentNullException.ThrowIfNull(Retry);
        Retry.Validate();
    }

    private void ValidateBaseUrl()
    {
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException("BaseUrl must be an absolute URI.", nameof(BaseUrl));
        if (uri.Scheme is not ("https" or "http"))
            throw new ArgumentException($"BaseUrl must use https or http, but was '{uri.Scheme}'.", nameof(BaseUrl));
        if (uri.Scheme == "http" && !AllowInsecureHttp)
            throw new ArgumentException("BaseUrl uses http; allow it explicitly with AllowInsecureHttp for local development.", nameof(BaseUrl));
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            throw new ArgumentException("BaseUrl must not contain a query, fragment, or user information.", nameof(BaseUrl));
        if (!BaseUrl.EndsWith('/'))
            throw new ArgumentException("BaseUrl must end with '/' so the request path is appended relatively.", nameof(BaseUrl));
    }
}
