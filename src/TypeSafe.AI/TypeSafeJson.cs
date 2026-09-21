using System.Text.Json.Serialization.Metadata;

namespace TypeSafe.AI;

/// <summary>Reflection-free JSON serialization contracts for TypeSafe SDK models.</summary>
public static class TypeSafeJson
{
    /// <summary>Provides the source-generated contract for <see cref="SystemOneResponse"/>.</summary>
    public static JsonTypeInfo<SystemOneResponse> ResponseTypeInfo => TypeSafeJsonContext.Default.SystemOneResponse;
}
