namespace OpenMedStack.BioSharp.Model.Alignment;

/// <summary>
/// Soft-clip information for a variant detected from a realigned clipped region.
/// </summary>
public class ClipInfo
{
    /// <summary>Position of the clip boundary on the original read (0-based).</summary>
    public int ClipPosition { get; }

    /// <summary>Number of bases in the clipped region.</summary>
    public int ClipSize { get; }

    /// <summary>Whether this was a 5' (left) or 3' (right) clip.</summary>
    public bool IsLeftClip { get; }

    /// <summary>Did the clipped region successfully realign?</summary>
    public bool WasRealigned { get; }

    /// <summary>
    /// Alignment score of the clipped region re-alignment.
    /// </summary>
    public int RealignScore { get; }

    public ClipInfo(int clipPosition, int clipSize, bool isLeftClip, bool wasRealigned, int realignScore)
    {
        ClipPosition = clipPosition;
        ClipSize = clipSize;
        IsLeftClip = isLeftClip;
        WasRealigned = wasRealigned;
        RealignScore = realignScore;
    }

    public override string ToString()
    {
        return
            $"[clip:{ClipPosition}, size:{ClipSize}, left:{IsLeftClip}, realigned:{WasRealigned}, score:{RealignScore}]";
    }
}