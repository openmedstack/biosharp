namespace OpenMedStack.BioSharp.Calculations.Alignment;

/// <summary>
/// Type of alignment event that corresponds to a variant.
/// </summary>
public enum EventType
{
    /// <summary>Single-nucleotide polymorphism: substitution at a single position.</summary>
    Snp,

    /// <summary>Insertion: read has extra base(s) compared to reference.</summary>
    Insertion,

    /// <summary>Deletion: reference has extra base(s) compared to read.</summary>
    Deletion
}
