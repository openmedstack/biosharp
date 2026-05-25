namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Structural variant class types as defined in VCF 4.2 specification.
/// </summary>
public enum SvType
{
    /// <summary>Insertion: alt allele is longer than ref allele.</summary>
    Insertion,

    /// <summary>Deletion: ref allele is longer than alt allele.</summary>
    Deletion,

    /// <summary>Inversion: ref sequence reversed in alt allele.</summary>
    Inversion,

    /// <summary>Translocation: read aligns to different chromosomes.</summary>
    Translocation,

    /// <summary>Complex: multiple rearrangement events.</summary>
    Complex,

    /// <summary>Copy number variation: uncertain exact breakpoint.</summary>
    CopyNumber
}
