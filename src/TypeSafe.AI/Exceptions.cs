using System.Net;

namespace TypeSafe.AI;

/// <summary>
/// Base exception thrown for all TypeSafe SDK errors and API failures.
/// </summary>
/// <remarks>
/// Provider response bodies and credentials are kept out of <see cref="Exception.Message"/>
/// to prevent accidental logging of sensitive payloads; inspect <see cref="ResponseBody"/> explicitly if needed.
/// </remarks>
public class TypeSafeException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null,
    string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : Exception(message, innerException)
{
    /// <summary>
    /// Gets the HTTP status code returned by the TypeSafe API, or <see langword="null"/> for client-side transport/timeout errors.
    /// </summary>
    public HttpStatusCode? StatusCode { get; } = statusCode;

    /// <summary>
    /// Gets the server-assigned request ID (from the x-typesafe-request-id response header), if available.
    /// </summary>
    public string? RequestId { get; } = requestId;

    /// <summary>
    /// Gets the raw response body received from the server, if available.
    /// </summary>
    public string? ResponseBody { get; } = responseBody;

    /// <summary>
    /// Gets the raw Retry-After header string returned by the server, if present.
    /// </summary>
    public string? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Exception thrown when authentication fails (HTTP 401 Unauthorized), typically due to an invalid or missing API key.
/// </summary>
/// <param name="requestId">The server-assigned request ID, if present.</param>
/// <param name="responseBody">The raw response body, if present.</param>
/// <param name="retryAfter">The Retry-After header value, if present.</param>
public sealed class TypeSafeAuthenticationException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe authentication failed.", HttpStatusCode.Unauthorized,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);

/// <summary>
/// Exception thrown when the TypeSafe API rejects a request as unprocessable (HTTP 422 Unprocessable Entity).
/// </summary>
/// <param name="requestId">The server-assigned request ID, if present.</param>
/// <param name="responseBody">The raw response body, if present.</param>
/// <param name="retryAfter">The Retry-After header value, if present.</param>
public sealed class TypeSafeValidationException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe rejected the request.", HttpStatusCode.UnprocessableEntity,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);

/// <summary>
/// Exception thrown when the TypeSafe API rate limit has been exceeded (HTTP 429 Too Many Requests).
/// </summary>
/// <param name="requestId">The server-assigned request ID, if present.</param>
/// <param name="responseBody">The raw response body, if present.</param>
/// <param name="retryAfter">The Retry-After header value, if present.</param>
public sealed class TypeSafeRateLimitException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe rate limit exceeded.", HttpStatusCode.TooManyRequests,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);

/// <summary>
/// Exception thrown when the TypeSafe upstream service is temporarily overloaded (HTTP 529).
/// </summary>
/// <param name="requestId">The server-assigned request ID, if present.</param>
/// <param name="responseBody">The raw response body, if present.</param>
/// <param name="retryAfter">The Retry-After header value, if present.</param>
public sealed class TypeSafeOverloadedException(string? requestId = null, string? responseBody = null, string? retryAfter = null)
    : TypeSafeException("TypeSafe is temporarily overloaded.", (HttpStatusCode)529,
        requestId: requestId, responseBody: responseBody, retryAfter: retryAfter);

/// <summary>
/// Exception thrown when a network transport, DNS, or I/O failure occurs while communicating with TypeSafe.
/// </summary>
/// <param name="message">The failure description.</param>
/// <param name="innerException">The underlying transport or I/O exception.</param>
public sealed class TypeSafeConnectionException(string message, Exception? innerException = null)
    : TypeSafeException(message, innerException: innerException);

/// <summary>
/// Exception thrown when an evaluation request times out, either per-attempt or across the total configured timeout budget.
/// </summary>
/// <param name="message">The timeout description.</param>
/// <param name="innerException">The underlying timeout or cancellation exception.</param>
public sealed class TypeSafeTimeoutException(string message = "TypeSafe evaluation timed out.", Exception? innerException = null)
    : TypeSafeException(message, innerException: innerException);

/// <summary>
/// Exception thrown when the response from TypeSafe violates the expected JSON schema or protocol contract.
/// </summary>
/// <param name="message">The protocol error description.</param>
/// <param name="innerException">The underlying parsing or deserialization exception.</param>
public sealed class TypeSafeProtocolException(string message, Exception? innerException = null)
    : TypeSafeException(message, innerException: innerException);
