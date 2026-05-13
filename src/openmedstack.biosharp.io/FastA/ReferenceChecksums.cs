namespace OpenMedStack.BioSharp.Io.FastA;

/// <summary>
/// Checksums computed for a reference FASTA.
/// </summary>
public sealed class ReferenceChecksums
{
    /// <summary>MD5 hex digest of the whole file.</summary>
    public string Md5 { get; init; } = string.Empty;

    /// <summary>SHA-256 hex digest of the whole file.</summary>
    public string Sha256 { get; init; } = string.Empty;
}