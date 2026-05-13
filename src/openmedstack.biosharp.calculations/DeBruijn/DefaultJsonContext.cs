using System.Text.Json.Serialization;
using OpenMedStack.BioSharp.Calculations.Alignment;

namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PipelineProvenance))]
[JsonSerializable(typeof(RepeatLibrary))]
internal partial class DefaultJsonContext : JsonSerializerContext
{
}
