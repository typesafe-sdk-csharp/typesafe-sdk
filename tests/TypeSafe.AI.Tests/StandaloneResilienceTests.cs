using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace TypeSafe.AI.Tests;

public sealed class StandaloneResilienceTests
{
    private static TypeSafeClient Create(StubHttpHandler handler, Action<TypeSafeClientOptions>? configure = null)
    {
        var options = new TypeSafeClientOptions();
        TypeSafeResilienceTests.FastRetry()(options);
        configure?.Invoke(options);
        return TypeSafeClient.CreateOwned(TypeSafeClientSettings.Create(options), handler);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(529)]
    public async Task Standalone_RetriesTransientStatusAndExhaustion(int status)
    {
        var handler = new StubHttpHandler();
        handler.Enqueue((HttpStatusCode)status);
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);
        using var client = Create(handler);
        await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        Assert.Equal(2, handler.Requests.Count);
        handler.Enqueue((HttpStatusCode)status);
        handler.Enqueue((HttpStatusCode)status);
        handler.Enqueue((HttpStatusCode)status);
        var error = await Assert.ThrowsAnyAsync<TypeSafeException>(() => client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions()));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task Standalone_RetriesTransportAndHonorsRetryAfter()
    {
        var handler = new StubHttpHandler();
        handler.EnqueueThrow(new HttpRequestException("reset"));
        handler.Enqueue(HttpStatusCode.TooManyRequests, configure: r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)));
        handler.Enqueue(HttpStatusCode.OK, TypeSafeClientTests.SuccessBody);
        using var client = Create(handler);
        await client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions());
        Assert.Equal(3, handler.Requests.Count);
        Assert.True(handler.AttemptTimes[2] - handler.AttemptTimes[1] >= TimeSpan.FromMilliseconds(700));
    }

    [Fact]
    public async Task Standalone_PreservesCallerCancellationDuringBodyRead()
    {
        var handler = new StubHttpHandler();
        handler.EnqueueStalledBody();
        using var client = Create(handler);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var task = client.SystemOneAsync("state", TypeSafeClientTests.DefaultQuestions(), cancellationToken: cancel.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.Single(handler.Requests);
    }
}
