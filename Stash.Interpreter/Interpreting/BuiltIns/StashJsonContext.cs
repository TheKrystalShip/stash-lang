namespace Stash.Interpreting.BuiltIns;

using System.Text.Json.Serialization;

[JsonSerializable(typeof(string))]
internal partial class StashJsonContext : JsonSerializerContext
{
}
