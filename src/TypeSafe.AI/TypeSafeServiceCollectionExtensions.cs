using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace TypeSafe.AI;

internal sealed class TypeSafeOptionsValidator : IValidateOptions<TypeSafeClientOptions>
{
    public ValidateOptionsResult Validate(string? name, TypeSafeClientOptions options)
    {
        try { options.Validate(); return ValidateOptionsResult.Success; }
        catch (ArgumentException exception) { return ValidateOptionsResult.Fail(exception.Message); }
    }
}

/// <summary>
/// Extension methods for configuring and registering <see cref="ITypeSafeClient"/> in an <see cref="IServiceCollection"/>.
/// Configuration precedence: built-in defaults, environment variables, optional IConfiguration section ("TypeSafe"), then explicit callback.
/// </summary>
public static class TypeSafeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <see cref="ITypeSafeClient"/> and its resilient <see cref="HttpClient"/> transport with the service collection.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configuration">Optional root configuration containing a "TypeSafe" section.</param>
    /// <param name="configure">Optional delegate to configure <see cref="TypeSafeClientOptions"/> programmatically.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for configuring the underlying HTTP client.</returns>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services,
        IConfiguration? configuration = null, Action<TypeSafeClientOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var optionsBuilder = services.AddOptions<TypeSafeClientOptions>()
            .Configure(static options => options.ApplyEnvironmentVariables());
        if (configuration is not null)
            optionsBuilder.Configure(options => ApplyConfigurationSection(options, configuration.GetSection("TypeSafe")));
        if (configure is not null)
            optionsBuilder.Configure(configure);
        optionsBuilder.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TypeSafeClientOptions>, TypeSafeOptionsValidator>());
        services.TryAddSingleton(static provider =>
            TypeSafeClientSettings.Create(provider.GetRequiredService<IOptions<TypeSafeClientOptions>>().Value));

        var builder = services.AddHttpClient(TypeSafeClient.HttpClientName, static client =>
            client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { AllowAutoRedirect = false });
        builder.AddResilienceHandler("typesafe", static (pipeline, context) =>
            TypeSafeResiliencePipeline.Configure(pipeline, context.ServiceProvider.GetRequiredService<TypeSafeClientSettings>()));
        return builder.AddTypedClient<ITypeSafeClient>(static (client, provider) =>
            new TypeSafeClient(client, provider.GetRequiredService<TypeSafeClientSettings>()));
    }

    /// <summary>
    /// Registers the <see cref="ITypeSafeClient"/> and its resilient <see cref="HttpClient"/> transport with the service collection.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">A delegate to configure <see cref="TypeSafeClientOptions"/> programmatically.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> for configuring the underlying HTTP client.</returns>
    public static IHttpClientBuilder AddTypeSafeClient(this IServiceCollection services, Action<TypeSafeClientOptions> configure)
        => AddTypeSafeClient(services, configuration: null, configure);

    private static readonly HashSet<string> KnownRootKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "DefaultModel", "ApiKey", "BaseUrl", "AllowInsecureHttp", "Retry"
    };

    private static readonly HashSet<string> KnownRetryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "MaxRetries", "BackoffInitial", "BackoffMax", "RespectRetryAfter", "UseJitter", "PerAttemptTimeout", "TotalTimeoutBudget"
    };

    internal static void ApplyConfigurationSection(TypeSafeClientOptions options, IConfigurationSection section)
    {
        ValidateKnownConfigurationKeys(section);

        if (section["DefaultModel"] is { } model) options.DefaultModel = model;
        if (section["ApiKey"] is { } key) options.ApiKey = key;
        if (section["BaseUrl"] is { } url) options.BaseUrl = url;
        if (section["AllowInsecureHttp"] is { } insecure)
            options.AllowInsecureHttp = Parse<bool>(section.GetSection("AllowInsecureHttp"), insecure);
        var retry = section.GetSection("Retry");
        if (retry["MaxRetries"] is { } count)
            options.Retry.MaxRetries = Parse<int>(retry.GetSection("MaxRetries"), count);
        if (retry["BackoffInitial"] is { } initial)
            options.Retry.BackoffInitial = Parse<TimeSpan>(retry.GetSection("BackoffInitial"), initial);
        if (retry["BackoffMax"] is { } maximum)
            options.Retry.BackoffMax = Parse<TimeSpan>(retry.GetSection("BackoffMax"), maximum);
        if (retry["RespectRetryAfter"] is { } respect)
            options.Retry.RespectRetryAfter = Parse<bool>(retry.GetSection("RespectRetryAfter"), respect);
        if (retry["UseJitter"] is { } jitter)
            options.Retry.UseJitter = Parse<bool>(retry.GetSection("UseJitter"), jitter);
        if (retry["PerAttemptTimeout"] is { } attempt)
            options.Retry.PerAttemptTimeout = attempt.Length == 0 ? null : Parse<TimeSpan>(retry.GetSection("PerAttemptTimeout"), attempt);
        if (retry["TotalTimeoutBudget"] is { } total)
            options.Retry.TotalTimeoutBudget = total.Length == 0 ? null : Parse<TimeSpan>(retry.GetSection("TotalTimeoutBudget"), total);
    }

    private static void ValidateKnownConfigurationKeys(IConfigurationSection section)
    {
        foreach (var child in section.GetChildren())
        {
            if (!KnownRootKeys.Contains(child.Key))
            {
                throw new OptionsValidationException(Options.DefaultName, typeof(TypeSafeClientOptions),
                    [$"Unrecognized configuration key '{child.Path}'."]);
            }
        }

        var retrySection = section.GetSection("Retry");
        if (retrySection.Exists())
        {
            foreach (var child in retrySection.GetChildren())
            {
                if (!KnownRetryKeys.Contains(child.Key))
                {
                    throw new OptionsValidationException(Options.DefaultName, typeof(TypeSafeClientOptions),
                        [$"Unrecognized configuration key '{child.Path}'."]);
                }
            }
        }
    }

    private static T Parse<T>(IConfigurationSection section, string value) where T : IParsable<T>
    {
        if (T.TryParse(value, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        throw new OptionsValidationException(Options.DefaultName, typeof(TypeSafeClientOptions),
            [$"Configuration '{section.Path}' has an invalid {typeof(T).Name} value."]);
    }
}
