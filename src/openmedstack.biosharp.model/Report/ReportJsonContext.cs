using System.Text.Json.Serialization;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Model.Report;

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ClinicalReport))]
[JsonSerializable(typeof(VariantReport))]
[JsonSerializable(typeof(QcSummary))]
internal partial class ReportJsonContext : JsonSerializerContext
{
}
