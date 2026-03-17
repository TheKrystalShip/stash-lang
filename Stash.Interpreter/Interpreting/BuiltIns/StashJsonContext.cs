namespace Stash.Interpreting.BuiltIns;

using System.Text.Json.Serialization;

/// <summary>Source-generated JSON serializer context for AOT-compatible string serialization. Used by JSON and config built-ins.</summary>
[JsonSerializable(typeof(string))]
internal partial class StashJsonContext : JsonSerializerContext
{
}
