using System.Text.Json;

namespace TypeSafe.AI;

internal static class TypeSafeResponseReader
{
    public static async Task<SystemOneResponse> ReadAsync(Stream stream, string? requestId, CancellationToken token)
    {
        try
        {
            var response = await JsonSerializer.DeserializeAsync(stream,
                TypeSafeJsonContext.Default.SystemOneResponse, token).ConfigureAwait(false)
                ?? throw new JsonException("The response must be an object.");

            return (requestId is not null && response.RequestId != requestId)
                ? new SystemOneResponse(response.Model, response.Answers, response.Usage, requestId)
                : response;
        }
        catch (JsonException exception)
        {
            throw new TypeSafeProtocolException("The TypeSafe response has invalid JSON or an invalid structure.", exception);
        }
        catch (ArgumentException exception)
        {
            // Collection element nullability is not enforced by System.Text.Json.
            throw new TypeSafeProtocolException("The TypeSafe response contains an invalid required value.", exception);
        }
    }
}
