namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Holds a codon change for annotation.
/// </summary>
public record CodonChange
{
    /// <summary>
    /// Original 3-mer codon (or shorter for partial codon changes).
    /// </summary>
    public string OriginalCodon { get; init; } = null!;

    /// <summary>
    /// Mutated codon string after the variant is applied.
    /// </summary>
    public string MutatedCodon { get; init; } = null!;

    /// <summary>
    /// How many nucleotides were changed (delta = mutated.Length - original.Length).
    /// </summary>
    public int NucleotideDelta
    {
        get { return MutatedCodon.Length - OriginalCodon.Length; }
    }
}
