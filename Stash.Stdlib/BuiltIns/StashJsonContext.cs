namespace Stash.Stdlib.BuiltIns;

using System.Text.Json.Serialization;

/// <summary>
/// Source-generated JSON serializer context for AOT-compatible serialization within the Stash interpreter.
/// </summary>
/// <remarks>
/// <para>
/// Uses <see cref="System.Text.Json.Serialization.JsonSerializableAttribute"/> to pre-generate
/// serialization metadata at compile time, enabling trim-safe and Native AOT-compatible JSON
/// operations. Currently registers <see cref="string"/> for use by JSON and configuration built-ins.
/// </para>
/// </remarks>
[JsonSerializable(typeof(string))]
internal partial class StashJsonContext : JsonSerializerContext
{
}
