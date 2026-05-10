namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Machine-readable provenance record emitted alongside every VCF output.
/// </summary>
public sealed class PipelineProvenance
{
    /// <summary>Assembly/package version of the BioSharp pipeline.</summary>
    public string PipelineVersion { get; init; } = string.Empty;

    /// <summary>MD5 or SHA-256 checksum of the reference FASTA.</summary>
    public string ReferenceChecksum { get; init; } = string.Empty;

    /// <summary>Checksums of all input files keyed by file path.</summary>
    public Dictionary<string, string> InputFileChecksums { get; init; } = new();

    /// <summary>JSON-serialised pipeline options used for the run.</summary>
    public string Options { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the pipeline started.</summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>UTC timestamp when the pipeline completed.</summary>
    public DateTimeOffset EndTime { get; init; }

    /// <summary>Total reads processed.</summary>
    public long ReadsProcessed { get; init; }

    /// <summary>Reads that mapped with sufficient quality.</summary>
    public long ReadsMapped { get; init; }

    /// <summary>Reads that were filtered out.</summary>
    public long ReadsFiltered { get; init; }

    /// <summary>Total variant calls in the output.</summary>
    public long VariantsCalled { get; init; }
}

/// <summary>
/// Atomically writes a <see cref="PipelineProvenance"/> record alongside a VCF output file.
/// </summary>
public static class ProvenanceWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Writes provenance JSON atomically next to <paramref name="vcfOutputPath"/>.
    /// The file is written to a temp file and then renamed to prevent partial writes.
    /// </summary>
    public static async Task WriteAsync(PipelineProvenance provenance, string vcfOutputPath)
    {
        var targetPath = vcfOutputPath + ".provenance.json";
        var dir = Path.GetDirectoryName(targetPath) ?? ".";
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

#pragma warning disable IL2026
        var json = JsonSerializer.Serialize(provenance, SerializerOptions);
#pragma warning restore IL2026

        await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

        // Atomic rename (on same filesystem)
        File.Move(tempPath, targetPath, overwrite: true);
    }
}
