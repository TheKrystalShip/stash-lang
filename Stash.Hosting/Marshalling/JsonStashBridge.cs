namespace Stash.Hosting.Marshalling;

using System;
using System.Collections.Generic;
using System.Text.Json;
using Stash.Runtime;
using Stash.Runtime.Types;

/// <summary>
/// Converts between <see cref="JsonElement"/> and <see cref="StashValue"/>.
/// Used by <see cref="HostMarshaller"/> when the caller passes a <c>JsonElement</c>
/// as an argument or requests <c>T = JsonElement</c> as a return type.
/// </summary>
internal static class JsonStashBridge
{
    /// <summary>
    /// Converts a <see cref="JsonElement"/> to the closest <see cref="StashValue"/> equivalent.
    /// </summary>
    /// <remarks>
    /// Conversion map:
    /// <list type="bullet">
    ///   <item><c>JsonValueKind.Null/Undefined</c> → <see cref="StashValue.Null"/></item>
    ///   <item><c>True/False</c> → bool <see cref="StashValue"/></item>
    ///   <item><c>Number</c> → long (when whole), double otherwise</item>
    ///   <item><c>String</c> → string <see cref="StashValue"/></item>
    ///   <item><c>Array</c> → <see cref="List{StashValue}"/> (recursive)</item>
    ///   <item><c>Object</c> → <see cref="StashDictionary"/> (recursive)</item>
    /// </list>
    /// </remarks>
    public static StashValue ToStash(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined =>
                StashValue.Null,

            JsonValueKind.True =>
                StashValue.True,

            JsonValueKind.False =>
                StashValue.False,

            JsonValueKind.Number when element.TryGetInt64(out long lv) =>
                StashValue.FromInt(lv),

            JsonValueKind.Number =>
                StashValue.FromFloat(element.GetDouble()),

            JsonValueKind.String =>
                StashValue.FromObj(element.GetString() ?? string.Empty),

            JsonValueKind.Array =>
                StashValue.FromObj(ToStashArray(element)),

            JsonValueKind.Object =>
                StashValue.FromObj(ToStashDict(element)),

            _ => StashValue.Null,
        };
    }

    /// <summary>
    /// Serializes a <see cref="StashValue"/> to a <see cref="JsonElement"/> by
    /// round-tripping through JSON text.
    /// </summary>
    public static JsonElement FromStash(StashValue value)
    {
        string json = SerializeToJson(value);
        using JsonDocument doc = JsonDocument.Parse(json);
        // Clone so the element survives after the JsonDocument is disposed.
        return doc.RootElement.Clone();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static List<StashValue> ToStashArray(JsonElement array)
    {
        var list = new List<StashValue>(array.GetArrayLength());
        foreach (JsonElement item in array.EnumerateArray())
            list.Add(ToStash(item));
        return list;
    }

    private static StashDictionary ToStashDict(JsonElement obj)
    {
        var dict = new StashDictionary();
        foreach (JsonProperty prop in obj.EnumerateObject())
            dict.Set(prop.Name, ToStash(prop.Value));
        return dict;
    }

    private static string SerializeToJson(StashValue value)
    {
        using var buf = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(buf);
        WriteValue(writer, value, depth: 0);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(buf.ToArray());
    }

    private const int MaxDepth = 64;

    private static void WriteValue(Utf8JsonWriter writer, StashValue value, int depth)
    {
        if (depth > MaxDepth)
        {
            writer.WriteNullValue();
            return;
        }

        switch (value.Tag)
        {
            case StashValueTag.Null:
                writer.WriteNullValue();
                break;

            case StashValueTag.Bool:
                writer.WriteBooleanValue(value.AsBool);
                break;

            case StashValueTag.Int:
                writer.WriteNumberValue(value.AsInt);
                break;

            case StashValueTag.Float:
                writer.WriteNumberValue(value.AsFloat);
                break;

            case StashValueTag.Byte:
                writer.WriteNumberValue((int)value.AsByte);
                break;

            case StashValueTag.Obj when value.AsObj is string s:
                writer.WriteStringValue(s);
                break;

            case StashValueTag.Obj when value.AsObj is List<StashValue> list:
                writer.WriteStartArray();
                foreach (StashValue item in list)
                    WriteValue(writer, item, depth + 1);
                writer.WriteEndArray();
                break;

            case StashValueTag.Obj when value.AsObj is StashDictionary dict:
                writer.WriteStartObject();
                foreach (KeyValuePair<object, StashValue> kv in dict.RawEntries())
                {
                    string keyStr = kv.Key is string ks ? ks : kv.Key.ToString() ?? string.Empty;
                    writer.WritePropertyName(keyStr);
                    WriteValue(writer, kv.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;

            case StashValueTag.Obj when value.AsObj is byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;

            case StashValueTag.Obj:
                throw new InvalidOperationException(
                    $"JsonStashBridge cannot serialize Stash Obj value of runtime type " +
                    $"'{value.AsObj?.GetType().FullName ?? "null"}' to JSON; " +
                    $"supported Obj types are string, array (List<StashValue>), " +
                    $"StashDictionary, and byte[].");

            default:
                writer.WriteNullValue();
                break;
        }
    }
}
