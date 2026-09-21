using System.Text.Json.Serialization;

namespace TypeSafe.AI;

/// <summary>
/// Base record for an answer to a single question, identified by its "type" discriminator.
/// </summary>
/// <remarks>
/// Known kinds deserialize through the source-generated polymorphic contract.
/// Unrecognized kinds never reach this hierarchy directly; the response decoder wraps them
/// in <see cref="UnknownAnswer"/> to ensure forward compatibility with future API question types.
/// </remarks>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
public abstract record TypeSafeAnswer;

/// <summary>
/// A yes/no (noul) evaluation answer representing the probability of a positive outcome.
/// </summary>
public sealed record NoulAnswer : TypeSafeAnswer
{
    /// <summary>
    /// Gets the probability of an affirmative ("yes") answer, from 0.0 (certain no) to 1.0 (certain yes).
    /// Values near 0.5 indicate high model uncertainty.
    /// </summary>
    public required double Noul { get; init; }
}

/// <summary>
/// A multiple-choice evaluation answer containing the top option, full distribution, and confidence.
/// </summary>
public sealed record ChoiceAnswer : TypeSafeAnswer
{
    /// <summary>
    /// Gets the option label selected with highest confidence.
    /// </summary>
    public required string Choice { get; init; }

    /// <summary>
    /// Gets the probability distribution across all configured choice options.
    /// </summary>
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }

    /// <summary>
    /// Gets the model's confidence score for the selected choice, ranging from 0.0 to 1.0.
    /// </summary>
    public required double Confidence { get; init; }
}

/// <summary>
/// A rubric evaluation answer providing a probability-weighted score across ordered levels.
/// </summary>
public sealed record ScoreAnswer : TypeSafeAnswer
{
    /// <summary>
    /// Gets the probability-weighted score value, which may fall between discrete rubric levels.
    /// </summary>
    public required double Score { get; init; }

    /// <summary>
    /// Gets the legend mapping discrete level indices to their descriptive labels.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Legend { get; init; }

    /// <summary>
    /// Gets the probability distribution across the rubric levels.
    /// </summary>
    public required IReadOnlyDictionary<string, double> Probabilities { get; init; }

    /// <summary>
    /// Gets the model's confidence score for the rubric evaluation, ranging from 0.0 to 1.0.
    /// </summary>
    public required double Confidence { get; init; }
}

/// <summary>
/// Represents an evaluation answer whose "type" discriminator is not recognized by this SDK version.
/// </summary>
/// <remarks>
/// Forward compatibility: unrecognized answer kinds are retained here rather than causing deserialization failures,
/// but are excluded from typed convenience collections such as <see cref="SystemOneResponse.Nouls"/>.
/// </remarks>
public sealed record UnknownAnswer : TypeSafeAnswer
{
    /// <summary>
    /// Gets the unrecognized type discriminator value received from the server.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets the raw JSON element of the unrecognized answer payload, retained for forward-compatible inspection.
    /// </summary>
    public required System.Text.Json.JsonElement Raw { get; init; }
}
