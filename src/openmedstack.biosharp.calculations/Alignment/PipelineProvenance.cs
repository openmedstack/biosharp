namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;

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