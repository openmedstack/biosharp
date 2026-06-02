namespace OpenMedStack.Preator;

using System.Text.Json.Serialization;
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Model;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TrimSummary))]
[JsonSerializable(typeof(FastQReport))]
[JsonSerializable(typeof(MarkDupSummary))]
[JsonSerializable(typeof(RepeatMaskSummary))]
[JsonSerializable(typeof(CoverageSummary))]
[JsonSerializable(typeof(IndexSummary))]
internal partial class PreatorJsonContext : JsonSerializerContext
{
}

