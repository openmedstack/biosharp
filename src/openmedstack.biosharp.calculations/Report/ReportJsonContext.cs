using System.Text.Json.Serialization;

namespace OpenMedStack.BioSharp.Calculations.Report;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClinicalReport))]
[JsonSerializable(typeof(VariantReport))]
internal partial class ReportJsonContext : JsonSerializerContext
{
}