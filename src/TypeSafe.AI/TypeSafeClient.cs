using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Polly.Timeout;
using Polly;
using Microsoft.Extensions.Http.Resilience;

namespace TypeSafe.AI;

internal static partial class TypeSafeDiagnostics
{
    public const string SourceName = "TypeSafe.AI";
    public static readonly ActivitySource Source = new(SourceName, SourceVersion);
}

/// <summary>
/// System One client implementation for TypeSafe AI. Supports both caller-owned and self-owned HTTP transport.
/// When created via <see cref="Create(TypeSafeClientOptions)"/>, the client owns the
/// <see cref="HttpClient"/> and disposes it. When constructed directly, the caller retains
/// ownership and is responsible for the transport's lifetime.
/// </summary>
public sealed class TypeSafeClient : ITypeSafeClient, IDisposable
{
    /// <summary>
    /// The logical HTTP client name used when registering and resolving TypeSafe transports with IHttpClientFactory.
    /// </summary>
    public const string HttpClientName = "TypeSafe";
    internal const string RequestPath = "v1/systemone";
    private readonly HttpClient _client;
    private readonly TypeSafeClientSettings _settings;
    private readonly Uri _endpoint;
    private readonly bool _ownsClient;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeSafeClient"/> class using a caller-provided <see cref="HttpClient"/>.
    /// The caller retains ownership of the transport lifetime; this constructor does not install retry or per-attempt timeout handlers.
    /// </summary>
    /// <param name="client">The HTTP client to use for sending requests.</param>
    /// <param name="options">Configuration options for the TypeSafe client.</param>
    public TypeSafeClient(HttpClient client, TypeSafeClientOptions options)
        : this(client, TypeSafeClientSettings.Create(options), ownsClient: false) { }

    internal TypeSafeClient(HttpClient client, TypeSafeClientSettings settings, bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
        _settings = settings;
        _endpoint = new Uri(new Uri(settings.BaseUrl), RequestPath);
        _ownsClient = ownsClient;
    }

    /// <summary>
    /// Creates a self-contained client that owns its <see cref="HttpClient"/> and resilience pipeline.
    /// Dispose this instance when finished to release the underlying connection resources.
    /// </summary>
    /// <param name="options">Configuration options for the TypeSafe client.</param>
    /// <returns>A new owned <see cref="TypeSafeClient"/> instance.</returns>
    public static TypeSafeClient Create(TypeSafeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = TypeSafeClientSettings.Create(options);
        return CreateOwned(settings, new SocketsHttpHandler { AllowAutoRedirect = false });
    }

    internal static TypeSafeClient CreateOwned(TypeSafeClientSettings settings, HttpMessageHandler primaryHandler)
    {
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>();
        TypeSafeResiliencePipeline.Configure(pipeline, settings);
        var handler = new ResilienceHandler(pipeline.Build())
        {
            InnerHandler = primaryHandler
        };
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        return new TypeSafeClient(client, settings, ownsClient: true);
    }

    /// <inheritdoc />
    public Task<SystemOneResponse> SystemOneAsync(TypeSafeContent state,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions, string? model = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = SystemOneRequest.Create(state, questions, model ?? _settings.DefaultModel);
        return SystemOneAsync(request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SystemOneResponse> SystemOneAsync(SystemOneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = TypeSafeDiagnostics.Source.StartActivity("TypeSafe.SystemOne", ActivityKind.Client);
        activity?.SetTag("typesafe.model", request.Model);
        try
        {
            var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("typesafe.usage.input_tokens", response.Usage.InputTokens);
            activity?.SetTag("typesafe.usage.output_tokens", response.Usage.OutputTokens);
            activity?.SetTag("typesafe.request_id", response.RequestId);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception exception)
        {
            // Never attach provider bodies, credentials, or user content.
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }

    private async Task<SystemOneResponse> SendAsync(SystemOneRequest request, CancellationToken callerToken)
    {
        using var deadline = StartDeadline(callerToken);
        var token = deadline?.Token ?? callerToken;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(request, TypeSafeJsonContext.Default.SystemOneRequest)
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            message.Headers.UserAgent.Add(new ProductInfoHeaderValue("typesafe-dotnet", TypeSafeDiagnostics.SourceVersion));
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw await MapFailureAsync(response, token).ConfigureAwait(false);

            var requestId = Header(response, "x-typesafe-request-id");
            await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            return await TypeSafeResponseReader.ReadAsync(stream, requestId, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new TypeSafeTimeoutException("The TypeSafe evaluation exceeded its timeout.", exception);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new TypeSafeTimeoutException("The TypeSafe evaluation exceeded its timeout.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new TypeSafeConnectionException("The TypeSafe HTTP exchange failed.", exception);
        }
        catch (IOException exception)
        {
            throw new TypeSafeConnectionException("The TypeSafe response could not be read.", exception);
        }
    }

    private CancellationTokenSource? StartDeadline(CancellationToken callerToken)
    {
        if (_settings.TotalTimeoutBudget is not { } budget)
            return null;
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        deadline.CancelAfter(budget);
        return deadline;
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static async Task<TypeSafeException> MapFailureAsync(HttpResponseMessage response, CancellationToken token)
    {
        var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        var body = text.Length == 0 ? null : text;
        var requestId = Header(response, "x-typesafe-request-id");
        var retryAfter = Header(response, "Retry-After");
        return (int)response.StatusCode switch
        {
            401 => new TypeSafeAuthenticationException(requestId, body, retryAfter),
            422 => new TypeSafeValidationException(requestId, body, retryAfter),
            429 => new TypeSafeRateLimitException(requestId, body, retryAfter),
            529 => new TypeSafeOverloadedException(requestId, body, retryAfter),
            _ => new TypeSafeException($"TypeSafe returned the unexpected status code {(int)response.StatusCode}.",
                response.StatusCode, requestId: requestId, responseBody: body, retryAfter: retryAfter)
        };
    }

    /// <summary>
    /// Disposes the underlying <see cref="HttpClient"/> if this instance owns it
    /// (i.e. was created via <see cref="Create(TypeSafeClientOptions)"/>). Calling
    /// <see cref="SystemOneAsync(SystemOneRequest, CancellationToken)"/> after disposal throws <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsClient)
            _client.Dispose();
    }
}


