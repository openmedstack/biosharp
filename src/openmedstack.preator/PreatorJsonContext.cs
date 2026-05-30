namespace OpenMedStack.Preator;

using System.Text.Json.Serialization;
using OpenMedStack.BioSharp.Calculations;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TrimSummary))]
[JsonSerializable(typeof(FastQReport))]
internal partial class PreatorJsonContext : JsonSerializerContext
{
}

