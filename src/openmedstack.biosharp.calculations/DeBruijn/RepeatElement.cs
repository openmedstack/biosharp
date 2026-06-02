namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Represents a repeat element loaded from a repeat library.
/// </summary>
public class RepeatElement
{
    /// <summary>Unique identifier for this repeat element.</summary>
    public string Id { get; set; } = null!;

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Repeat type classification.</summary>
    public string Type { get; set; } = null!;

    /// <summary>The repeat motif/sequence.</summary>
    public string Motif { get; set; } = null!;

    /// <summary>Description of the repeat.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Length of this repeat's motif.</summary>
    public int MotifLength
    {
        get { return Motif.Length; }
    }
}