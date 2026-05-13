using System;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class Ann4SpliceSitePredictionTests
{
    // Donor site window: 9 characters, positions -3 to +6 relative to splice boundary
    // Canonical donor: C(-3) A(-2) G(-1) | G(+1) T(+2) A(+3) A(+4) G(+5) T(+6)
    private const string CanonicalDonorRef = "CAGGTAAGT";

    // Acceptor site window: 23 characters, positions -20 to +3
    // Canonical acceptor: TTTTTTTTTTTTTTTTTTAG|GT
    private const string CanonicalAcceptorRef = "TTTTTTTTTTTTTTTTTTTAGT";

    [Fact]
    public void SpliceSitePredictor_CanonicalDonorRef_HasHighScore()
    {
        var score = SpliceSitePredictor.ComputeDonorScore(CanonicalDonorRef.AsSpan());
        // Canonical GT donor should score well above 5.0
        Assert.True(score > 5.0, $"Canonical donor score {score} should be > 5.0");
    }

    [Fact]
    public void SpliceSitePredictor_DonorDisruption_GtAAtPlusOne_IsDisruptive()
    {
        // Change G→A at position +1 (index 3 in 0-indexed 9-char window)
        // "CAGGTAAGT" → "CAGATAAGT"
        const string altSeq = "CAGATAAGT";

        var classification = SpliceSitePredictor.ClassifyDonorVariant(
            refContext: CanonicalDonorRef.AsSpan(),
            altContext: altSeq.AsSpan(),
            threshold: 3.0);

        Assert.Equal(VariantConsequence.SpliceSiteDisruptive, classification);
    }

    [Fact]
    public void SpliceSitePredictor_ExonicPositionChange_NotDisruptive()
    {
        // Change C→A at position -3 (index 0, exonic) — small delta, not disruptive
        // "CAGGTAAGT" → "AAGGTAAGT"
        const string altSeq = "AAGGTAAGT";

        var classification = SpliceSitePredictor.ClassifyDonorVariant(
            refContext: CanonicalDonorRef.AsSpan(),
            altContext: altSeq.AsSpan(),
            threshold: 3.0);

        // Small delta → stays as SpliceSite (not disruptive)
        Assert.Equal(VariantConsequence.SpliceSite, classification);
    }

    [Fact]
    public void SpliceSitePredictor_DeepIntronicCrypticSite_IsActivation()
    {
        // Ref: no splice signal (all A's, low score)
        // Alt: canonical donor sequence in intronic position
        const string intronRef = "AAAAAAAAA"; // 9 A's → very low donor score
        const string intronAlt = "CAGGTAAGT"; // canonical donor → high score

        var classification = SpliceSitePredictor.ClassifyCrypticDonor(
            refContext: intronRef.AsSpan(),
            altContext: intronAlt.AsSpan(),
            crypticThreshold: 5.0);

        Assert.Equal(VariantConsequence.CrypticSpliceActivation, classification);
    }

    [Fact]
    public void SpliceSitePredictor_NonCrypticIntronic_StaysIntronic()
    {
        // Alt still has very low score (no splice signal)
        const string intronRef = "AAAAAAAAA";
        const string intronAlt = "AACAAAAAA"; // weak change, low score

        var classification = SpliceSitePredictor.ClassifyCrypticDonor(
            refContext: intronRef.AsSpan(),
            altContext: intronAlt.AsSpan(),
            crypticThreshold: 5.0);

        Assert.Equal(VariantConsequence.Intronic, classification);
    }

    [Fact]
    public void SpliceSitePredictor_DonorDisruption_TtoAAtPlusTwo_IsDisruptive()
    {
        // Change T→A at position +2 (index 4 in 0-indexed 9-char window): invariant T
        // "CAGGTAAGT" → "CAGGA AAGT" → "CAGGAAAGT"
        const string altSeq = "CAGGAAAGT";

        var classification = SpliceSitePredictor.ClassifyDonorVariant(
            refContext: CanonicalDonorRef.AsSpan(),
            altContext: altSeq.AsSpan(),
            threshold: 3.0);

        Assert.Equal(VariantConsequence.SpliceSiteDisruptive, classification);
    }

    [Fact]
    public void SpliceSitePredictor_EngineIntegration_AnnotatesSpliceSiteDisruptive()
    {
        // Build a transcript with canonical GT donor site at the exon|intron boundary.
        // CDS: positions 1-30 (exon), intron starts at position 31.
        // For a canonical donor: positions 31-32 must be 'G','T' (GT dinucleotide).
        // Variant: position 31, G→A — disrupts GT→AT, breaking the donor site.
        //
        // Transcript: 30-bp exon ending in "CAG" then "GTAAGT..." as intron
        //   position:  1234567890123456789012345678901234567890
        //   sequence:  ATGATGATGATGATGATGATGATGATGATGGTAAGTATG
        //                                             ^ pos 31 = 'G' (donor +1)
        const string transcriptSeq = "ATGATGATGATGATGATGATGATGATGATGGTAAGTATG";

        // CDS: 1-30, Exon: 1-30, Intron: 31-39
        var ctx = new AnnotationContext
        {
            CdsStart = 1,
            CdsEnd = 30,
            TranscriptLength = 40,
            GeneBoundaries = (1, 40),
            Introns = [(31, 39)],
            ExonBoundaries = [(1, 30)]
        };

        var transcript = new Sequence("TX_SPLICE", transcriptSeq.AsMemory(), new string('I', transcriptSeq.Length).AsMemory());

        using var engine = new VariantAnnotationEngine(ctx);
        engine.LoadTranscript(transcript);

        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 31,  // G at +1 of donor site — disrupts GT→AT
            Reference = "G",
            Alternate = "A",
            ErrorProbabilities = [60],
            FailedFilter = [],
            MarkerIdentifiers = ".",
            AdditionalInformation = "."
        };

        var anns = engine.AnnotateVariant(variant);
        Assert.NotNull(anns);
        Assert.NotEmpty(anns);
        Assert.All(anns, a => Assert.Equal(VariantConsequence.SpliceSiteDisruptive, a.Consequence));
    }
}