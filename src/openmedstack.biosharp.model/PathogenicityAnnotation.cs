namespace OpenMedStack.BioSharp.Model;

/// <summary>
/// Pre-computed SIFT and PolyPhen-2 pathogenicity scores for a missense variant,
/// retrieved from a dbNSFP-format database.
/// </summary>
public sealed class PathogenicityAnnotation
{
    /// <summary>
    /// SIFT score (0.0–1.0). Values &lt;0.05 indicate a damaging prediction.
    /// Null when not available in the database (reported as "." in VCF INFO).
    /// </summary>
    public double? SiftScore { get; }

    /// <summary>
    /// SIFT prediction string: "D" (Damaging) or "T" (Tolerated).
    /// "." when not available.
    /// </summary>
    public string SiftPrediction { get; }

    /// <summary>
    /// PolyPhen-2 HDIV score (0.0–1.0). Higher values indicate more damaging.
    /// Null when not available in the database.
    /// </summary>
    public double? PolyPhen2HdivScore { get; }

    /// <summary>
    /// PolyPhen-2 HDIV prediction string: "D" (Probably Damaging), "P" (Possibly Damaging), or "B" (Benign).
    /// "." when not available.
    /// </summary>
    public string PolyPhen2HdivPrediction { get; }

    public PathogenicityAnnotation(
        double? siftScore,
        string siftPrediction,
        double? polyPhen2HdivScore,
        string polyPhen2HdivPrediction)
    {
        SiftScore = siftScore;
        SiftPrediction = siftPrediction;
        PolyPhen2HdivScore = polyPhen2HdivScore;
        PolyPhen2HdivPrediction = polyPhen2HdivPrediction;
    }
}
