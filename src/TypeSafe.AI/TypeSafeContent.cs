using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace TypeSafe.AI;

/// <summary>
/// Represents input content for state, instructions, or criteria descriptions (text, JSON object, or JSON array).
/// Numbers, booleans, and null are not valid root content types.
/// </summary>
/// <remarks>
/// Instances are immutable: the wrapped JSON structure is deep-cloned at construction, so callers
/// may reuse or mutate their own nodes after conversion without affecting this instance.
/// </remarks>
[JsonConverter(typeof(TypeSafeContentJsonConverter))]
public sealed class TypeSafeContent
{
    private readonly JsonNode _root;

    internal TypeSafeContent(JsonNode root) => _root = root;

    /// <summary>
    /// Creates a new <see cref="TypeSafeContent"/> instance from plain text.
    /// </summary>
    /// <param name="text">The string content.</param>
    /// <returns>A new <see cref="TypeSafeContent"/> instance wrapping the text.</returns>
    public static TypeSafeContent FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TypeSafeContent(JsonValue.Create(text)!);
    }

    /// <summary>
    /// Creates a new <see cref="TypeSafeContent"/> instance from a JSON node; the node is validated and deep-cloned.
    /// </summary>
    /// <param name="node">The JSON node (must be a string, object, or array).</param>
    /// <returns>A new <see cref="TypeSafeContent"/> instance wrapping a clone of the node.</returns>
    public static TypeSafeContent FromJson(JsonNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var kind = node.GetValueKind();
        if (kind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            throw new ArgumentException($"TypeSafe content must be a string, object, or array, but was {kind}.", nameof(node));
        return new TypeSafeContent(node.DeepClone());
    }

    /// <summary>
    /// Implicitly converts a string to a <see cref="TypeSafeContent"/> instance.
    /// </summary>
    /// <param name="text">The string content to convert.</param>
    public static implicit operator TypeSafeContent(string text) => FromString(text);

    /// <summary>
    /// Implicitly converts a <see cref="JsonNode"/> to a <see cref="TypeSafeContent"/> instance.
    /// </summary>
    /// <param name="node">The JSON node to convert.</param>
    public static implicit operator TypeSafeContent(JsonNode node) => FromJson(node);

    /// <summary>
    /// Creates content from a strongly-typed value serialized through the caller's source-generated metadata.
    /// Anonymous objects are deliberately unsupported to maintain NativeAOT compatibility.
    /// </summary>
    /// <typeparam name="T">The type of the object to serialize.</typeparam>
    /// <param name="value">The object instance to serialize.</param>
    /// <param name="typeInfo">The source-generated JSON type info contract for <typeparamref name="T"/>.</param>
    /// <returns>A new <see cref="TypeSafeContent"/> instance wrapping the serialized JSON structure.</returns>
    public static TypeSafeContent FromObject<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var node = JsonSerializer.SerializeToNode(value, typeInfo)
            ?? throw new ArgumentException("The value serialized to a null JSON root; TypeSafe content must be a string, object, or array.", nameof(value));
        return FromJson(node);
    }

    /// <summary>
    /// Returns an independent deep clone of the underlying JSON node.
    /// </summary>
    /// <returns>A clone of the wrapped <see cref="JsonNode"/>.</returns>
    public JsonNode ToJsonNode() => _root.DeepClone();

    internal void WriteTo(Utf8JsonWriter writer) => _root.WriteTo(writer);
}

/// <summary>
/// Custom JSON converter that serializes <see cref="TypeSafeContent"/> as raw JSON and validates root kinds when reading.
/// </summary>
public sealed class TypeSafeContentJsonConverter : JsonConverter<TypeSafeContent>
{
    /// <inheritdoc />
    public override TypeSafeContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var node = JsonNode.Parse(ref reader);
        if (node is null)
            throw new JsonException("TypeSafe content root must be a non-null string, object, or array, but was null.");

        var kind = node.GetValueKind();
        return kind switch
        {
            JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array
                => new TypeSafeContent(node),
            _ => throw new JsonException($"TypeSafe content must be a string, object, or array, but was {kind}.")
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TypeSafeContent value, JsonSerializerOptions options)
    {
        // Null is meaningful on the wire: choice criteria map undescribed labels to explicit nulls.
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }
        value.WriteTo(writer);
    }
}
