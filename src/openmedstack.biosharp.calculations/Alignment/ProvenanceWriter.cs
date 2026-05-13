using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.DeBruijn;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// Atomically writes a <see cref="PipelineProvenance"/> record alongside a VCF output file.
/// </summary>
public static class ProvenanceWriter
{
    /// <summary>
    /// Writes provenance JSON atomically next to <paramref name="vcfOutputPath"/>.
    /// The file is written to a temp file and then renamed to prevent partial writes.
    /// </summary>
    public static async Task Write(this PipelineProvenance provenance, string vcfOutputPath)
    {
        var targetPath = $"{vcfOutputPath}.provenance.json";
        var dir = Path.GetDirectoryName(targetPath) ?? ".";
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        var json = JsonSerializer.Serialize(provenance, DefaultJsonContext.Default.PipelineProvenance);

        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

        // Atomic rename (on same filesystem)
        File.Move(tempPath, targetPath, overwrite: true);
    }
}
