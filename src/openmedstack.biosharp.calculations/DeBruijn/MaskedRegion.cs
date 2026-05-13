namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Holds masked regions produced by RepeatMasker.
/// </summary>
public class MaskedRegion
{
    /// <summary>Start position (0-based, inclusive).</summary>
    public int Start { get; set; }

    /// <summary>End position (0-based, exclusive).</summary>
    public int End { get; set; }

    /// <summary>Length of the masked region.</summary>
    public int Length
    {
        get { return End - Start; }
    }

    /// <summary>Repeat element that caused masking.</summary>
    public RepeatElement? Repeat { get; set; }

    /// <summary>Repeat type (SAT, SINE, LINE, etc).</summary>
    public string RepeatType
    {
        get { return Repeat?.Type ?? "unknown"; }
    }

    /// <summary>The masked sequence segment (N-masked or original).</summary>
    public string MaskedSequence { get; set; } = null!;

    public override string ToString()
    {
        return $"Masked [{Start}-{End}] ({Repeat?.Name ?? "unknown"}) len={Length}";
    }
}