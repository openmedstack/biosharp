namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Represents a single synthetic SNV injected into a reference sequence.
/// </summary>
public sealed class SyntheticVariant
{
    /// <summary>0-based position in the reference.</summary>
    public int Position { get; init; }

    /// <summary>Reference allele at this position.</summary>
    public char ReferenceAllele { get; init; }

    /// <summary>Alternate allele that replaces the reference.</summary>
    public char AlternateAllele { get; init; }
}