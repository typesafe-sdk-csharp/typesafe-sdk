using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>Source-generated JSON for the supported wire graph; reflection-based serialization is
/// not used anywhere in the core. Optional members use per-property null-ignore so choice criteria
/// can carry explicit null values for undescribed labels.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    AllowOutOfOrderMetadataProperties = true, RespectNullableAnnotations = true)]
[JsonSerializable(typeof(SystemOneRequest))]
[JsonSerializable(typeof(SystemOneResponse))]
[JsonSerializable(typeof(TypeSafeQuestion))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, TypeSafeQuestion>))]
[JsonSerializable(typeof(TypeSafeAnswer))]
[JsonSerializable(typeof(NoulAnswer))]
[JsonSerializable(typeof(ChoiceAnswer))]
[JsonSerializable(typeof(ScoreAnswer))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, double>))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, string>))]
[JsonSerializable(typeof(TypeSafeContent))]
[JsonSerializable(typeof(TypeSafeUsage))]
internal partial class TypeSafeJsonContext : JsonSerializerContext;
