using System.Text.Json;
using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>
/// Reflection-free JSON converter for <see cref="SystemOneResponse"/> that preserves request IDs,
/// polymorphic answer types, and unrecognized answer payloads without duplicating projected dictionary properties.
/// </summary>
public sealed class SystemOneResponseJsonConverter : JsonConverter<SystemOneResponse>
{
    /// <inheritdoc />
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override SystemOneResponse Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            Require(root, "model", JsonValueKind.String);
            Require(root, "answers", JsonValueKind.Object);
            Require(root, "usage", JsonValueKind.Object);
            var answers = new Dictionary<string, TypeSafeAnswer>(StringComparer.Ordinal);
            foreach (var property in root.GetProperty("answers").EnumerateObject())
            {
                var raw = property.Value;
                var kind = Require(raw, "type", JsonValueKind.String).GetString()!;
                if (string.IsNullOrWhiteSpace(kind)) throw new JsonException("Answer type is required.");
                if (kind == "noul") Require(raw, "noul", JsonValueKind.Number);
                if (kind is "choice" or "score")
                {
                    Require(raw, "confidence", JsonValueKind.Number);
                    var probabilities = Require(raw, "probabilities", JsonValueKind.Object);
                    foreach (var probability in probabilities.EnumerateObject())
                        if (probability.Value.ValueKind != JsonValueKind.Number) throw new JsonException("Probabilities must be numbers.");
                }
                if (kind == "choice") Require(raw, "choice", JsonValueKind.String);
                if (kind == "score")
                {
                    Require(raw, "score", JsonValueKind.Number);
                    Require(raw, "legend", JsonValueKind.Object);
                }
                answers.Add(property.Name, kind switch
                {
                    "noul" => raw.Deserialize(TypeSafeJsonContext.Default.NoulAnswer)!,
                    "choice" => raw.Deserialize(TypeSafeJsonContext.Default.ChoiceAnswer)!,
                    "score" => raw.Deserialize(TypeSafeJsonContext.Default.ScoreAnswer)!,
                    _ => new UnknownAnswer { Type = kind, Raw = raw.Clone() }
                });
            }
            return new SystemOneResponse(root.GetProperty("model").GetString()!, answers,
                root.GetProperty("usage").Deserialize(TypeSafeJsonContext.Default.TypeSafeUsage)!,
                root.TryGetProperty("request_id", out var id) ? id.GetString() : null);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new JsonException("Malformed TypeSafe evaluation response.", ex);
        }
    }

    private static JsonElement Require(JsonElement element, string name, JsonValueKind kind)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw new JsonException($"Required property '{name}' must be {kind}.");
        return value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, SystemOneResponse value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("model", value.Model);
        writer.WritePropertyName("answers");
        writer.WriteStartObject();
        foreach (var (key, answer) in value.Answers)
        {
            writer.WritePropertyName(key);
            if (answer is UnknownAnswer unknown) { unknown.Raw.WriteTo(writer); continue; }
            writer.WriteStartObject();
            writer.WriteString("type", answer switch
            {
                NoulAnswer => "noul",
                ChoiceAnswer => "choice",
                ScoreAnswer => "score",
                _ => throw new JsonException("Unsupported answer.")
            });
            var payload = answer switch
            {
                NoulAnswer n => JsonSerializer.SerializeToElement(n, TypeSafeJsonContext.Default.NoulAnswer),
                ChoiceAnswer c => JsonSerializer.SerializeToElement(c, TypeSafeJsonContext.Default.ChoiceAnswer),
                ScoreAnswer s => JsonSerializer.SerializeToElement(s, TypeSafeJsonContext.Default.ScoreAnswer),
                _ => throw new JsonException("Unsupported answer.")
            };
            foreach (var property in payload.EnumerateObject()) property.WriteTo(writer);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WritePropertyName("usage");
        JsonSerializer.Serialize(writer, value.Usage, TypeSafeJsonContext.Default.TypeSafeUsage);
        if (value.RequestId is not null) writer.WriteString("request_id", value.RequestId);
        writer.WriteEndObject();
    }
}
