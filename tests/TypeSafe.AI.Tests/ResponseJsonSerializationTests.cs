using System.Text.Json;
using TypeSafe.AI;
using Xunit;

namespace TypeSafe.AI.Tests;

public class ResponseJsonSerializationTests
{
    private static SystemOneResponse CreateSampleResponse(string? requestId = "req-test-123")
    {
        var answers = new Dictionary<string, TypeSafeAnswer>
        {
            ["urgent"] = new NoulAnswer { Noul = 0.95 },
            ["category"] = new ChoiceAnswer
            {
                Choice = "billing",
                Confidence = 0.88,
                Probabilities = new Dictionary<string, double> { ["billing"] = 0.88, ["tech"] = 0.12 }
            },
            ["sentiment"] = new ScoreAnswer
            {
                Score = 4.2,
                Confidence = 0.91,
                Legend = new Dictionary<string, string> { ["1"] = "negative", ["5"] = "positive" },
                Probabilities = new Dictionary<string, double> { ["4"] = 0.4, ["5"] = 0.6 }
            },
            ["future_feature"] = new UnknownAnswer
            {
                Type = "graph",
                Raw = JsonDocument.Parse("""{"type":"graph","nodes":[1,2,3]}""").RootElement.Clone()
            }
        };

        return new SystemOneResponse(
            model: "jev-latest",
            answers: answers,
            usage: new TypeSafeUsage { InputTokens = 150, OutputTokens = 42 },
            requestId: requestId);
    }

    [Fact]
    public void DefaultJsonSerializer_RoundTripsAccurately()
    {
        var original = CreateSampleResponse();
        var json = JsonSerializer.Serialize(original);

        var restored = JsonSerializer.Deserialize<SystemOneResponse>(json);
        Assert.NotNull(restored);
        Assert.Equal(original.Model, restored.Model);
        Assert.Equal(original.RequestId, restored.RequestId);
        Assert.Equal(original.Usage.InputTokens, restored.Usage.InputTokens);
        Assert.Equal(original.Usage.OutputTokens, restored.Usage.OutputTokens);

        Assert.Equal(4, restored.Answers.Count);
        Assert.Equal(0.95, restored.Nouls["urgent"].Noul);
        Assert.Equal("billing", restored.Choices["category"].Choice);
        Assert.Equal(0.88, restored.Choices["category"].Confidence);
        Assert.Equal(4.2, restored.Scores["sentiment"].Score);

        var unknown = Assert.IsType<UnknownAnswer>(restored.Answers["future_feature"]);
        Assert.Equal("graph", unknown.Type);
        Assert.Equal("[1,2,3]", unknown.Raw.GetProperty("nodes").GetRawText());
    }

    [Fact]
    public void TypeSafeJsonResponseTypeInfo_RoundTripsAccurately()
    {
        var original = CreateSampleResponse();
        var json = JsonSerializer.Serialize(original, TypeSafeJson.ResponseTypeInfo);

        var restored = JsonSerializer.Deserialize(json, TypeSafeJson.ResponseTypeInfo);
        Assert.NotNull(restored);
        Assert.Equal(original.Model, restored.Model);
        Assert.Equal(original.RequestId, restored.RequestId);
        Assert.Equal(0.95, restored.Nouls["urgent"].Noul);
        Assert.Equal("billing", restored.Choices["category"].Choice);
        Assert.Equal(4.2, restored.Scores["sentiment"].Score);
    }

    [Fact]
    public void SerializedJson_DoesNotDuplicateProjectedProperties()
    {
        var response = CreateSampleResponse();
        var json = JsonSerializer.Serialize(response);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("model", out _));
        Assert.True(root.TryGetProperty("answers", out _));
        Assert.True(root.TryGetProperty("usage", out _));
        Assert.True(root.TryGetProperty("request_id", out _));

        // Projected properties should NEVER appear in wire JSON:
        Assert.False(root.TryGetProperty("nouls", out _));
        Assert.False(root.TryGetProperty("Nouls", out _));
        Assert.False(root.TryGetProperty("choices", out _));
        Assert.False(root.TryGetProperty("Choices", out _));
        Assert.False(root.TryGetProperty("scores", out _));
        Assert.False(root.TryGetProperty("Scores", out _));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"model":null,"answers":{},"usage":{}}""")]
    [InlineData("""{"model":"m","answers":[],"usage":{}}""")]
    [InlineData("""{"model":"m","answers":{"q":{}},"usage":{}}""")]
    [InlineData("""{"model":"m","answers":{},"usage":{},"request_id":1}""")]
    [InlineData("""{"model":"m","answers":{"q":{"type":""}},"usage":{}}""")]
    public void MalformedJson_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemOneResponse>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, TypeSafeJson.ResponseTypeInfo));
    }
}
