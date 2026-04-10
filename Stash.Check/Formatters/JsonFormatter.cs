namespace Stash.Check;

using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Stash.Analysis;

internal sealed class JsonFormatter : IOutputFormatter
{
    public string Format => "json";

    public void Write(CheckResult result, Stream output)
    {
        string cwd = Directory.GetCurrentDirectory();
        var items = new List<JsonDiagnosticItem>();

        foreach (var file in result.Files)
        {
            string filePath = file.Uri.IsFile ? file.Uri.LocalPath : file.Uri.ToString();
            string relativePath = Path.GetRelativePath(cwd, filePath).Replace('\\', '/');

            foreach (var err in file.Analysis.StructuredLexErrors)
            {
                items.Add(new JsonDiagnosticItem
                {
                    File = relativePath,
                    Line = err.Span.StartLine,
                    Column = err.Span.StartColumn,
                    Code = "STASH001",
                    Severity = "error",
                    Message = err.Message
                });
            }

            foreach (var err in file.Analysis.StructuredParseErrors)
            {
                items.Add(new JsonDiagnosticItem
                {
                    File = relativePath,
                    Line = err.Span.StartLine,
                    Column = err.Span.StartColumn,
                    Code = "STASH002",
                    Severity = "error",
                    Message = err.Message
                });
            }

            foreach (var diag in file.Analysis.SemanticDiagnostics)
            {
                string severity = diag.Level switch
                {
                    DiagnosticLevel.Error => "error",
                    DiagnosticLevel.Warning => "warning",
                    _ => "info"
                };
                items.Add(new JsonDiagnosticItem
                {
                    File = relativePath,
                    Line = diag.Span.StartLine,
                    Column = diag.Span.StartColumn,
                    Code = diag.Code ?? "SA0000",
                    Severity = severity,
                    Message = diag.Message
                });
            }
        }

        JsonSerializer.Serialize(output, items, JsonDiagnosticJsonContext.Default.ListJsonDiagnosticItem);
    }
}

internal sealed class JsonDiagnosticItem
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("column")]
    public int Column { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

[JsonSerializable(typeof(List<JsonDiagnosticItem>))]
[JsonSerializable(typeof(JsonDiagnosticItem))]
internal partial class JsonDiagnosticJsonContext : JsonSerializerContext { }
