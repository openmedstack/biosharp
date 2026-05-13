namespace OpenMedStack.BioSharp.Calculations;

using System;
using System.Runtime.CompilerServices;
using Model;

/// <summary>
/// Position-weight-matrix (PWM) based splice site predictor.
///
/// Donor site (5' splice site): 9-position window from -3 to +6
///   relative to the exon|intron boundary, where position +1 is the
///   first intronic nucleotide (canonical: GT).
///
/// Acceptor site (3' splice site): 23-position window from -20 to +3
///   relative to the intron|exon boundary, where position +1 is the
///   first exonic nucleotide (canonical: ...AG at -2/-1).
///
/// PWM log-odds scores (log₂(freq/0.25)) derived from human consensus
/// splice site frequencies (Shapiro &amp; Senapathy, 1987).
/// </summary>
public static class SpliceSitePredictor
{
    // ── Donor PWM (5'ss): 9 positions, indices 0..8 = positions -3..+6 ────
    // Each row [A, C, G, T] as log₂(freq / 0.25).
    private static ReadOnlySpan<double> DonorPwm
    {
        get
        {
            return
            [
                //   A       C       G       T     (position)
                0.49, 0.53, -0.60, -1.01, // -3  (C/A biased)
                1.29, -0.95, -0.40, -1.80, // -2  (A biased)
                -1.37, -1.87, 1.64, -2.24, // -1  (G biased)
                -6.64, -7.20, 1.99, -7.57, // +1  (near-invariant G)
                -7.97, -7.83, -7.48, 1.99, // +2  (near-invariant T)
                1.30, -1.36, -0.72, -0.88, // +3  (A biased)
                1.50, -1.64, -1.11, -1.34, // +4  (A biased)
                -1.32, -1.63, 1.51, -1.22, // +5  (G biased)
                -1.77, -1.61, -1.02, 1.53 // +6  (T biased)
            ];
        }
    }

    // ── Acceptor PWM (3'ss): 23 positions, indices 0..22 = positions -20..+3 ─
    // Pyrimidine tract (-20 to -3), followed by AG (-2,-1), then exon (+1 to +3).
    private static ReadOnlySpan<double> AcceptorPwm
    {
        get
        {
            return
            [
                //   A       C       G       T     (position)
                -0.93, 0.26, -0.57, 0.79, // -20
                -0.93, 0.26, -0.57, 0.79, // -19
                -0.93, 0.26, -0.57, 0.79, // -18
                -0.93, 0.26, -0.57, 0.79, // -17
                -0.93, 0.26, -0.57, 0.79, // -16
                -0.93, 0.26, -0.57, 0.79, // -15
                -0.93, 0.26, -0.57, 0.79, // -14
                -0.93, 0.26, -0.57, 0.79, // -13
                -0.93, 0.26, -0.57, 0.79, // -12
                -0.93, 0.26, -0.57, 0.79, // -11
                -0.93, 0.26, -0.57, 0.79, // -10
                -0.93, 0.26, -0.57, 0.79, // -9
                -0.93, 0.26, -0.57, 0.79, // -8
                -0.93, 0.26, -0.57, 0.79, // -7
                -0.93, 0.26, -0.57, 0.79, // -6
                -0.93, 0.26, -0.57, 0.79, // -5
                -0.93, 0.26, -0.57, 0.79, // -4
                -0.93, 0.26, -0.57, 0.79, // -3
                -5.30, 0.80, -5.30, 0.30, // -2  (near-invariant A/C before AG)
                -5.30, -5.30, 1.95, -5.30, // -1  (near-invariant G of AG)
                0.49, -0.33, 0.53, -0.50, // +1
                0.62, -0.88, 0.20, -0.30, // +2
                0.20, -0.20, 0.48, -0.30 // +3
            ];
        }
    }

    private const int DonorWindowSize = 9;    // positions -3..+6
    private const int AcceptorWindowSize = 23; // positions -20..+3

    // ── Base indexing helper ─────────────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BaseIndex(char c) => char.ToUpperInvariant(c) switch
    {
        'A' => 0,
        'C' => 1,
        'G' => 2,
        'T' => 3,
        _   => -1  // N / unknown → skip
    };

    // ── Scoring ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the donor PWM log-odds score for a 9-nucleotide context
    /// spanning positions -3 to +6 relative to the splice donor boundary.
    /// </summary>
    /// <param name="context">Exactly 9 nucleotides; must be the same length as the donor window.</param>
    public static double ComputeDonorScore(ReadOnlySpan<char> context)
    {
        if (context.Length != DonorWindowSize)
        {
            throw new ArgumentException($"Donor context must be exactly {DonorWindowSize} bases.", nameof(context));
        }

        var pwm = DonorPwm;
        var score = 0.0;
        for (var i = 0; i < DonorWindowSize; i++)
        {
            var idx = BaseIndex(context[i]);
            if (idx >= 0)
            {
                score += pwm[i * 4 + idx];
            }
        }
        return score;
    }

    /// <summary>
    /// Computes the acceptor PWM log-odds score for a 23-nucleotide context
    /// spanning positions -20 to +3 relative to the splice acceptor boundary.
    /// </summary>
    /// <param name="context">Exactly 23 nucleotides.</param>
    public static double ComputeAcceptorScore(ReadOnlySpan<char> context)
    {
        if (context.Length != AcceptorWindowSize)
        {
            throw new ArgumentException($"Acceptor context must be exactly {AcceptorWindowSize} bases.", nameof(context));
        }

        var pwm = AcceptorPwm;
        var score = 0.0;
        for (var i = 0; i < AcceptorWindowSize; i++)
        {
            var idx = BaseIndex(context[i]);
            if (idx >= 0)
            {
                score += pwm[i * 4 + idx];
            }
        }
        return score;
    }

    // ── Classification ───────────────────────────────────────────────────────

    /// <summary>
    /// Classifies a variant near a canonical donor splice site.
    /// Returns <see cref="VariantConsequence.SpliceSiteDisruptive"/> if the PWM score
    /// delta (alt − ref) is below −<paramref name="threshold"/>;
    /// otherwise returns <see cref="VariantConsequence.SpliceSite"/>.
    /// </summary>
    /// <param name="refContext">9-base donor window for the reference sequence.</param>
    /// <param name="altContext">9-base donor window for the alternate sequence.</param>
    /// <param name="threshold">
    /// Absolute score decrease (in bits) required to call disruption. Default 3.0.
    /// </param>
    public static VariantConsequence ClassifyDonorVariant(
        ReadOnlySpan<char> refContext,
        ReadOnlySpan<char> altContext,
        double threshold = 3.0)
    {
        var refScore = ComputeDonorScore(refContext);
        var altScore = ComputeDonorScore(altContext);
        var delta = altScore - refScore;

        return delta <= -threshold
            ? VariantConsequence.SpliceSiteDisruptive
            : VariantConsequence.SpliceSite;
    }

    /// <summary>
    /// Classifies a variant near a canonical acceptor splice site.
    /// Returns <see cref="VariantConsequence.SpliceSiteDisruptive"/> if the PWM score
    /// delta is below −<paramref name="threshold"/>;
    /// otherwise returns <see cref="VariantConsequence.SpliceSite"/>.
    /// </summary>
    public static VariantConsequence ClassifyAcceptorVariant(
        ReadOnlySpan<char> refContext,
        ReadOnlySpan<char> altContext,
        double threshold = 3.0)
    {
        var refScore = ComputeAcceptorScore(refContext);
        var altScore = ComputeAcceptorScore(altContext);
        var delta = altScore - refScore;

        return delta <= -threshold
            ? VariantConsequence.SpliceSiteDisruptive
            : VariantConsequence.SpliceSite;
    }

    /// <summary>
    /// Checks whether a variant in a deep intronic position creates a cryptic donor site.
    /// Returns <see cref="VariantConsequence.CrypticSpliceActivation"/> if the alternate
    /// donor PWM score exceeds <paramref name="crypticThreshold"/>;
    /// otherwise returns <see cref="VariantConsequence.Intronic"/>.
    /// </summary>
    /// <param name="refContext">9-base donor window for the reference sequence.</param>
    /// <param name="altContext">9-base donor window for the alternate sequence.</param>
    /// <param name="crypticThreshold">
    /// Minimum alt donor score (in bits) to call cryptic splice activation. Default 5.0.
    /// </param>
    public static VariantConsequence ClassifyCrypticDonor(
        ReadOnlySpan<char> refContext,
        ReadOnlySpan<char> altContext,
        double crypticThreshold = 5.0)
    {
        var altScore = ComputeDonorScore(altContext);
        return altScore >= crypticThreshold
            ? VariantConsequence.CrypticSpliceActivation
            : VariantConsequence.Intronic;
    }

    /// <summary>
    /// Checks whether a variant in a deep intronic position creates a cryptic acceptor site.
    /// Returns <see cref="VariantConsequence.CrypticSpliceActivation"/> if the alternate
    /// acceptor PWM score exceeds <paramref name="crypticThreshold"/>;
    /// otherwise returns <see cref="VariantConsequence.Intronic"/>.
    /// </summary>
    public static VariantConsequence ClassifyCrypticAcceptor(
        ReadOnlySpan<char> refContext,
        ReadOnlySpan<char> altContext,
        double crypticThreshold = 5.0)
    {
        var altScore = ComputeAcceptorScore(altContext);
        return altScore >= crypticThreshold
            ? VariantConsequence.CrypticSpliceActivation
            : VariantConsequence.Intronic;
    }

    // ── Internal helpers for engine integration ──────────────────────────────

    /// <summary>
    /// Tries to classify a variant at a known splice-site position by extracting the
    /// relevant donor window from the transcript sequence and applying the donor PWM.
    /// Returns the upgraded consequence or the original <paramref name="baseSpliceSite"/>.
    /// </summary>
    /// <param name="transcriptData">Full transcript sequence as a char span.</param>
    /// <param name="variantPos">1-based position of the variant on the transcript.</param>
    /// <param name="altBase">Alternate nucleotide.</param>
    /// <param name="donorBoundary">
    /// 1-based transcript position of the last exon base (= the exon|intron boundary).
    /// </param>
    /// <param name="threshold">PWM score delta threshold.</param>
    /// <param name="baseSpliceSite">Consequence to return when not disruptive.</param>
    internal static VariantConsequence UpgradeDonorConsequence(
        ReadOnlySpan<char> transcriptData,
        int variantPos,
        char altBase,
        int donorBoundary,
        double threshold,
        VariantConsequence baseSpliceSite)
    {
        // Window: positions [donorBoundary-2 .. donorBoundary+6] (1-based), total 9 bases
        var windowStart = donorBoundary - 2;  // position of -3 relative to boundary
        var windowEnd   = donorBoundary + 6;  // position of +6

        if (windowStart < 1 || windowEnd > transcriptData.Length)
        {
            return baseSpliceSite;
        }

        // Build ref context strings from transcript chars (convert to 0-based)
        Span<char> refCtx = stackalloc char[DonorWindowSize];
        for (var i = 0; i < DonorWindowSize; i++)
        {
            refCtx[i] = transcriptData[windowStart - 1 + i];
        }

        // Apply the single-base substitution in the alt context
        Span<char> altCtx = stackalloc char[DonorWindowSize];
        refCtx.CopyTo(altCtx);
        var variantOffset = variantPos - windowStart; // 0-based index within window
        if (variantOffset is >= 0 and < DonorWindowSize)
        {
            altCtx[variantOffset] = altBase;
        }

        return ClassifyDonorVariant(refCtx, altCtx, threshold);
    }

    /// <summary>
    /// Tries to classify a deep-intronic variant by checking whether the alternate allele
    /// creates a new cryptic donor site in the local sequence window.
    /// </summary>
    internal static VariantConsequence CheckCrypticDonor(
        ReadOnlySpan<char> transcriptData,
        int variantPos,
        char altBase,
        double crypticThreshold)
    {
        var windowStart = variantPos - 3;  // try to centre window around variant
        var windowEnd   = windowStart + DonorWindowSize - 1;

        if (windowStart < 1 || windowEnd > transcriptData.Length)
        {
            return VariantConsequence.Intronic;
        }

        Span<char> refCtx = stackalloc char[DonorWindowSize];
        for (var i = 0; i < DonorWindowSize; i++)
        {
            refCtx[i] = transcriptData[windowStart - 1 + i];
        }

        Span<char> altCtx = stackalloc char[DonorWindowSize];
        refCtx.CopyTo(altCtx);
        var variantOffset = variantPos - windowStart;
        if (variantOffset is >= 0 and < DonorWindowSize)
        {
            altCtx[variantOffset] = altBase;
        }

        return ClassifyCrypticDonor(refCtx, altCtx, crypticThreshold);
    }
}
