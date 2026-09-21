using System.Diagnostics;
using System.Net;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

/// <summary>
/// Verifies OpenTelemetry tracing for the TypeSafe client: the evaluation ActivitySource
/// records model, usage, status, and request-id metadata, parents to the ambient activity,
/// and ensures its tag values are sanitized so that sensitive payloads (state text,
/// question text, credentials, or provider error bodies) are never recorded.
/// </summary>
/// <remarks>
/// The ActivityListener is process-global and the test suite runs classes in parallel, so each
/// test stamps a unique per-call model and attributes its own activity by that tag.
/// </remarks>
public class TypeSafeTelemetryTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly System.Collections.Concurrent.ConcurrentBag<Activity> _activities = [];

    public TypeSafeTelemetryTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "TypeSafe.AI",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => _activities.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private Activity OwnActivity(string modelTag) =>
        _activities.Single(a => string.Equals(a.GetTagItem("typesafe.model") as string, modelTag, StringComparison.Ordinal));

    [Fact]
    public async Task SuccessfulEvaluation_RecordsModelUsageRequestId_WithoutSensitivePayloads()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody,
            response => response.Headers.TryAddWithoutValidation("x-typesafe-request-id", "req-telemetry-123"));
        var client = TypeSafeClientTests.CreateClient(handler);

        const string stateText = "SECRET-STATE-TEXT";
        var modelTag = $"telemetry-{Guid.NewGuid():N}";
        await client.SystemOneAsync(stateText, TypeSafeClientTests.DefaultQuestions(), model: modelTag);

        var activity = OwnActivity(modelTag);
        Assert.Equal("TypeSafe.SystemOne", activity.OperationName);
        Assert.Equal(ActivityKind.Client, activity.Kind);
        Assert.Equal(ActivityStatusCode.Ok, activity.Status);
        Assert.Equal(10, activity.GetTagItem("typesafe.usage.input_tokens"));
        Assert.Equal(2, activity.GetTagItem("typesafe.usage.output_tokens"));
        Assert.Equal("req-telemetry-123", activity.GetTagItem("typesafe.request_id"));

        // No tag value carries the state text, question text, ids, or the API key.
        foreach (var tag in activity.Tags)
        {
            Assert.DoesNotContain(stateText, tag.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("billing", tag.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("How urgent", tag.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("sk-test", tag.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task FailedEvaluation_RecordsErrorStatusWithExceptionTypeNameOnly()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.Unauthorized); // 401 is never retried
        var client = TypeSafeClientTests.CreateClient(handler);

        var modelTag = $"telemetry-{Guid.NewGuid():N}";
        await Assert.ThrowsAsync<TypeSafeAuthenticationException>(
            () => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions(), model: modelTag));

        var activity = OwnActivity(modelTag);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        // The status description is the exception type name only - no provider body, no credential.
        Assert.Equal("TypeSafeAuthenticationException", activity.StatusDescription);
        Assert.DoesNotContain("sk-test", activity.StatusDescription, StringComparison.Ordinal);
        foreach (var tag in activity.Tags)
        {
            Assert.DoesNotContain("sk-test", tag.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ClientActivity_ParentsToAmbientActivity()
    {
        var handler = new StubHttpHandler();
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);
        var client = TypeSafeClientTests.CreateClient(handler);

        var modelTag = $"telemetry-{Guid.NewGuid():N}";
        using var parent = new Activity("test.parent").SetIdFormat(ActivityIdFormat.W3C);
        parent.Start();
        try
        {
            await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions(), model: modelTag);
        }
        finally
        {
            parent.Stop();
        }

        var child = OwnActivity(modelTag);
        Assert.Equal(parent.TraceId, child.TraceId);
        Assert.Equal(parent.SpanId, child.ParentSpanId);
    }
}

