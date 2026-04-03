namespace Stash.Check;

using System.Collections.Generic;
using System.Text.Json.Serialization;

[JsonSerializable(typeof(SarifLog))]
[JsonSerializable(typeof(SarifRun))]
[JsonSerializable(typeof(SarifTool))]
[JsonSerializable(typeof(SarifToolComponent))]
[JsonSerializable(typeof(SarifReportingDescriptor))]
[JsonSerializable(typeof(SarifConfiguration))]
[JsonSerializable(typeof(SarifResult))]
[JsonSerializable(typeof(SarifMessage))]
[JsonSerializable(typeof(SarifLocation))]
[JsonSerializable(typeof(SarifPhysicalLocation))]
[JsonSerializable(typeof(SarifArtifactLocation))]
[JsonSerializable(typeof(SarifRegion))]
[JsonSerializable(typeof(SarifInvocation))]
[JsonSerializable(typeof(List<SarifRun>))]
[JsonSerializable(typeof(List<SarifResult>))]
[JsonSerializable(typeof(List<SarifLocation>))]
[JsonSerializable(typeof(List<SarifInvocation>))]
[JsonSerializable(typeof(List<SarifReportingDescriptor>))]
[JsonSerializable(typeof(Dictionary<string, SarifArtifactLocation>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(string[]))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SarifJsonContext : JsonSerializerContext
{
}
