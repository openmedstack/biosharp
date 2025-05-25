namespace OpenMedStack.BioSharp.Calculations.DeBruijn;

/// <summary>
/// Confidence level for a bubble detected in a De Bruijn graph.
/// Low-confidence bubbles are likely assembly artifacts (repeats, homopolymers)
/// and should be flagged for optional downstream filtering.
/// </summary>
public enum BubbleConfidence
{
    /// <summary>
    /// Low confidence: both paths traverse high-copy repeats or homopolymers.
    /// Likely an assembly artifact; report with a WARNING flag.
    /// </summary>
    Low,

    /// <summary>
    /// Medium confidence: some repeat content or moderate coverage imbalance.
    /// Likely a true variant but warrants manual review.
    /// </summary>
    Medium,

    /// <summary>
    /// High confidence: paths traverse unique k-mers with good coverage ratio.
    /// Reliable variant call, suitable for clinical reporting.
    /// </summary>
    High
}
