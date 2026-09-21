using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>
/// Abstract base record for TypeSafe question primitives: Noul (yes/no), Choice (multiple choice), and Score (rubric).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Noul), "noul")]
[JsonDerivedType(typeof(Choice), "choice")]
[JsonDerivedType(typeof(Score), "score")]
public abstract record TypeSafeQuestion
{
    /// <summary>
    /// Gets the instructions defining what the model should evaluate for this question.
    /// Can be plain text, a JSON object, or a JSON array.
    /// </summary>
    public required TypeSafeContent Instructions { get; init; }
}

/// <summary>
/// A yes/no (noul) question: evaluates whether a statement is true, returning a probability between 0.0 and 1.0.
/// </summary>
public sealed record Noul : TypeSafeQuestion
{
    /// <summary>
    /// Gets optional evaluation criteria defining what qualifies as true or false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}

/// <summary>
/// Criteria describing the conditions under which a <see cref="Noul"/> question should evaluate to true or false.
/// </summary>
public sealed record NoulCriteria
{
    /// <summary>
    /// Gets the optional criteria or explanation for an affirmative (true) outcome.
    /// </summary>
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TypeSafeContent? WhenTrue { get; init; }

    /// <summary>
    /// Gets the optional criteria or explanation for a negative (false) outcome.
    /// </summary>
    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TypeSafeContent? WhenFalse { get; init; }
}

/// <summary>
/// A multiple-choice question that selects among named options described by <see cref="Criteria"/>.
/// </summary>
public sealed record Choice : TypeSafeQuestion
{
    /// <summary>
    /// Gets the dictionary mapping option labels to optional descriptions (<see langword="null"/> for undescribed labels).
    /// </summary>
    public required IReadOnlyDictionary<string, TypeSafeContent?> Criteria { get; init; }
}

/// <summary>
/// A rubric question that assigns an ordered score using the levels defined in <see cref="Criteria"/>.
/// </summary>
public sealed record Score : TypeSafeQuestion
{
    /// <summary>
    /// Gets the ordered rubric levels starting at index 0. The API requires between 2 and 10 levels.
    /// </summary>
    public required IReadOnlyList<TypeSafeContent> Criteria { get; init; }
}
