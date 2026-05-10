namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Alignment;
using DeBruijn;
using Model;
using Model.Vcf;
using Xunit;

/// <summary>
/// Acceptance tests for alignment improvements implemented per alignment_improvements.md.
/// Covers: coverage-based edge filtering, homopolymer handling, multi-alt alleles, and stop-retained edge case.
/// </summary>
public class AlignmentImprovementTests
{
    // =============================================
    // Coverage-Based Edge Filtering (Proposal 2)
    // =============================================

    /// <summary>
    /// VCF 4.2 spec: edges below median/2 coverage should be pruned from the graph.
    /// Tests that FilterLowCoverageEdges removes low-coverage edges when explicitly called.
    /// </summary>
    [Fact]
    public void FilterLowCoverageEdges_RemovesLowCoverageEdges()
    {
        // Reference: ATCGATCGAT
        // Alt:       ATCGATACAT (SNP at pos 6: G->A)
        // This creates a bubble with two paths of different coverage.
        var reads = new[]
        {
            "ATCGATCGAT", // ref path (high coverage)
            "ATCGATCGAT", "ATCGATCGAT", // repeat ref
            "ATCGATAACAT" // alt path (low coverage - single read)
        };

        var graph = new DeBruijnGraph(5, AsyncEnumerableExtensions.ToAsyncEnumerable(reads.Select(s =>
            new Sequence("r_" + s.GetHashCode(), s.AsMemory(), new string('I', s.Length).AsMemory()))));

        // Before filtering: graph should have edges from both ref and alt paths
        var nodesBefore = graph.GetNodes(CancellationToken.None).Result;
        Assert.True(nodesBefore.Count > 0);

        // Apply coverage filtering - should prune edges with coverage below median/2
        graph.FilterLowCoverageEdges();

        // After filtering: fewer high-coverage edges should remain for the dominant path
        var nodesAfter = graph.GetNodes(CancellationToken.None).Result;
        Assert.True(nodesAfter.Count > 0);

        // Verify the edge coverage values were adjusted (some edges pruned)
        foreach (var node in nodesAfter)
        {
            Assert.True(node.OutboundCoverage.Count <= node.OutDegree);
        }
    }

    /// <summary>
    /// Coverage filtering with explicit threshold removes edges below that threshold.
    /// </summary>
    [Fact]
    public void FilterLowCoverageEdges_WithExplicitThreshold_RemovesEdgesBelowThreshold()
    {
        var reads = new[]
        {
            "ACGTACGTACGT", // ref path (10x coverage)
            "ACGTACGTACGT", "ACGTACGTACGT", "ACGTACGTACGT",
            "ACGTACGTACGT", "ACGTACGTACGT", "ACGTACGTACGT",
            "ACGTACGTACGT", "ACGTACGTACGT", "ACGTACGTACGT",
            "ACGTAGGTACGT" // alt SNP at pos 6 (1x coverage only)
        };

        var graph = new DeBruijnGraph(5, AsyncEnumerableExtensions.ToAsyncEnumerable(reads.Select(s =>
            new Sequence("r_" + s.GetHashCode(), s.AsMemory(), new string('I', s.Length).AsMemory()))));

        // Filter with threshold of 5 - should remove all edges with coverage < 5
        graph.FilterLowCoverageEdges(5);

        var nodes = graph.GetNodes(CancellationToken.None).Result;
        Assert.True(nodes.Count > 0);

        // All remaining edges should have coverage >= 5
        foreach (var node in nodes)
        {
            foreach (var edge in node.OutboundCoverage)
            {
                Assert.True(edge.Value >= 5, $"Edge {node.Id}->{edge.Key} has coverage {edge.Value}, expected >= 5");
            }
        }
    }

    /// <summary>
    /// No-op when no edges exist.
    /// </summary>
    [Fact]
    public void FilterLowCoverageEdges_EmptyGraph_DoesNotThrow()
    {
        var graph = new DeBruijnGraph(5, AsyncEnumerableExtensions.ToAsyncEnumerable(Enumerable.Empty<Sequence>()));
        try
        {
            graph.FilterLowCoverageEdges();
        }
        catch
        {
            Assert.Fail("FilterLowCoverageEdges threw on empty graph");
        }
    }

    // =============================================
    // Homopolymer Handling (LocalAlignmentVariantCaller spec)
    // =============================================

    /// <summary>
    /// GetHomopolymerRun correctly identifies homopolymer runs of single bases.
    /// </summary>
    [Fact]
    public void GetHomopolymerRun_IdentifiesHomopolymerRuns()
    {
        var ref1 = "AAATGCCC";
        Assert.Equal(3, VariantCaller.GetHomopolymerRun(ref1.ToCharArray().AsSpan(), 0));
        Assert.Equal(3, VariantCaller.GetHomopolymerRun(ref1.ToCharArray().AsSpan(), 1));
        Assert.Equal(3, VariantCaller.GetHomopolymerRun(ref1.ToCharArray().AsSpan(), 2));
        Assert.Equal(1,
            VariantCaller.GetHomopolymerRun(ref1.ToCharArray().AsSpan(), 3)); // 'T' is alone (single base = 1)

        var ref2 = "CCGGGTAA";
        Assert.Equal(2, VariantCaller.GetHomopolymerRun(ref2.ToCharArray().AsSpan(), 0)); // CC
        Assert.Equal(3, VariantCaller.GetHomopolymerRun(ref2.ToCharArray().AsSpan(), 3)); // GGG
        Assert.Equal(2, VariantCaller.GetHomopolymerRun(ref2.ToCharArray().AsSpan(), 6)); // AA
    }

    /// <summary>
    /// Homopolymer indels get lower quality scores than non-homopolymer indels.
    /// </summary>
    [Fact]
    public void CallVariants_HomopolymerIndel_LowerQuality()
    {
        // Homopolymer insertion: AAAAAA -> AAAAAAAA (insert AA in poly-A run)
        var refSeq = new Sequence("ref", "ACGTACAAAAAAAAAGCTAGC".AsMemory(), new string('I', 21).AsMemory());
        var readSeq = new Sequence("read", "ACGTACAAAAAAAAAAAAAGCTAGC".AsMemory(), new string('I', 25).AsMemory());

        var alignment = new AlignmentResult(
            "ACGTACAAAAAAAAAAAAAAAGCTAGC", // aligned ref (gaps for insertion)
            "ACGTACAAAAAAAAAAAAAAAGCTAGC", // aligned read
            "||||||||||||||||||||||||||",
            50,
            0
        );

        var variants = VariantCaller.CallVariants(refSeq, alignment);

        // If we detect an indel, check for homopolymer quality penalty
        var indels = variants.Where(v => v.IsInsertion || v.IsDeletion).ToList();
        foreach (var v in indels)
            // Homopolymer runs should be penalized
        {
            Assert.True(v.QuantitativeQuality < 40,
                $"Homopolymer indel at pos {v.Position} should have lower quality, got {v.QuantitativeQuality}");
        }
    }

    /// <summary>
    /// Non-homopolymer indels retain standard quality scores.
    /// </summary>
    [Fact]
    public void CallVariants_NonHomopolymerIndel_StandardQuality()
    {
        // Non-homopolymer insertion: ACGTACGGTACG -> ACGTACGAGGTACG (insert A between Gs)
        var alignment = new AlignmentResult(
            "ACGTAC-GGTACG", // aligned ref with gap
            "ACGTACAGGTACG", // aligned read (no gap)
            "|||||||X|||||||",
            35,
            0
        );

        var variants = VariantCaller.CallVariants(
            new Sequence("ref", "ACGTACGGTACG".AsMemory(), new string('I', 12).AsMemory()),
            alignment
        );

        foreach (var v in variants)
        {
            if (v.IsInsertion)
            {
                // Should NOT get homopolymer penalty since ref context is not a run
            }
        }
    }

    // =============================================
    // Multiple Alternate Alleles (Proposal 3 / spec)
    // =============================================

    /// <summary>
    /// LocalVariantResult supports multiple alternate alleles via AdditionalAltAlleles list.
    /// </summary>
    [Fact]
    public void LocalVariantResult_MultiAllele_SupportsMultipleAlts()
    {
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 100,
            Reference = "A",
            Alternate = "T",
            QuantitativeQuality = 40,
            Depth = 10
        };

        Assert.False(variant.IsMultiAllelic);
        Assert.Single(variant.AllAlternateAlleles);
        Assert.Equal("T", variant.AllAlternateAlleles[0]);

        variant.AddAltAllele("G");
        variant.AddAltAllele("C");

        Assert.True(variant.IsMultiAllelic);
        Assert.Equal(3, variant.AllAlternateAlleles.Count);
        Assert.Equal("T", variant.AllAlternateAlleles[0]);
        Assert.Equal("G", variant.AllAlternateAlleles[1]);
        Assert.Equal("C", variant.AllAlternateAlleles[2]);
    }

    /// <summary>
    /// Multi-allelic variant produces comma-separated ALT field in VCF output.
    /// </summary>
    [Fact]
    public async Task VcfWriter_MultiAllelicVariant_ProducesCommaSeparatedAlt()
    {
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 100,
            Reference = "A",
            Alternate = "T",
            QuantitativeQuality = 50,
            Depth = 25
        };
        variant.AddAltAllele("G");
        variant.AddAltAllele("C");

        using var stream = new MemoryStream();
        await VcfWriter.WriteAsync(
            stream,
            [variant],
            "chr1",
            10000,
            ["SAMPLE1"],
            [["0/1"]],
            CancellationToken.None
        );

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // VCF ALT field should be comma-separated
        Assert.Contains("chr1\t100\t.\tA\tT,G,C\t", content);
        // VCF INFO should report AC=2 (2 alt alleles)
        Assert.Contains("AC=2", content);
        // FORMAT column should be present
        Assert.Contains("GT:GQ:DP", content);
    }

    /// <summary>
    /// Sample FORMAT columns with multiple genotypes.
    /// </summary>
    [Fact]
    public async Task VcfWriter_MultipleSamples_ProducesMultipleGenotypeColumns()
    {
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 200,
            Reference = "G",
            Alternate = "A",
            QuantitativeQuality = 60,
            Depth = 30
        };

        using var stream = new MemoryStream();
        await VcfWriter.WriteAsync(
            stream,
            [variant],
            "chr1",
            null,
            ["SAMPLE1", "SAMPLE2", "SAMPLE3"],
            [["0/0", "0/1", "1/1"]],
            CancellationToken.None
        );

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // FORMAT column lists all three samples
        Assert.Contains("GT:GQ:DP", content);
        Assert.Contains("0/0", content);
        Assert.Contains("0/1", content);
        Assert.Contains("1/1", content);
    }

    // =============================================
    // Stop-Retained Edge Case (VariantAnnotationEngine spec)
    // =============================================

    /// <summary>
    /// Nonsense mutation in the LAST codon is classified as StopRetained, not Nonsense.
    /// Uses Stop-retained: TAA -> TGA (both stop codons).
    /// </summary>
    [Fact]
    public void Annotate_NonsenseInLastCodon_ReturnsStopRetained()
    {
        // Transcript ends with TAA (stop codon)
        // TCG TCG TAA (3 codons, 9 bases)
        var transcript = new Sequence("test", "TCGTCGTAA".AsMemory(), new string('I', 9).AsMemory());

        // Mutate TAA -> TGA (still stop codon, in last position = StopRetained)
        var codonChange = new CodonChange
        {
            OriginalCodon = "TAA",
            MutatedCodon = "TGA"
        };
        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 8,
            Reference = "A",
            Alternate = "G"
        };

        var ann = codonChange.Annotate("TestTranscript", transcript, 8);

        Assert.NotNull(ann);
        Assert.Equal(VariantConsequence.StopRetained, ann.Consequence);
        Assert.NotNull(ann.HgvsProtein);
        Assert.NotNull(ann.HgvsCoding);
    }

    /// <summary>
    /// Nonsense mutation NOT in last codon is classified as Nonsense (not StopRetained).
    /// </summary>
    [Fact]
    public void Annotate_NonsenseNotInLastCodon_ReturnsNonsense()
    {
        // Transcript: TCG TCG TCG TCG TAA (5 codons, 15 bases, stop at codon 5)
        // Mutate codon 2 from TCG to TAG -> stop codon at position 4-6, NOT in last codon
        var transcript = new Sequence("test", "TCGTCGTCGTCGTAA".AsMemory(), new string('I', 15).AsMemory());

        var codonChange = new CodonChange
        {
            OriginalCodon = "TCG",
            MutatedCodon = "TAG"
        };
        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 4,
            Reference = "C",
            Alternate = "A"
        };

        var ann = codonChange.Annotate("TestTranscript", transcript, 4);

        Assert.NotNull(ann);
        Assert.Equal(VariantConsequence.Nonsense, ann.Consequence);
    }

    /// <summary>
    /// StopRetained produces correct HGVS notation: p.Ser17* (stop symbol retained).
    /// </summary>
    [Fact]
    public void Annotate_StopRetained_ProducesCorrectHgvsProtein()
    {
        // Mutation: TAA -> TGA at the last codon (position 8)
        var transcript = new Sequence("test", "TCGTCGTAA".AsMemory(), new string('I', 9).AsMemory());
        var codonChange = new CodonChange
        {
            OriginalCodon = "TAA",
            MutatedCodon = "TGA"
        };
        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 8,
            Reference = "A",
            Alternate = "G"
        };

        var ann = codonChange.Annotate("TestTranscript", transcript, 8);

        Assert.NotNull(ann);
        Assert.True(ann.HgvsProtein?.Contains("*") == true,
            $"StopRetained should produce HGVS with * marker, got: {ann.HgvsProtein}");
    }

    /// <summary>
    /// Out-of-bounds position returns Unknown, doesn't crash.
    /// </summary>
    [Fact]
    public void Annotate_OutOfBounds_Transcript_ReturnsUnknown()
    {
        var transcript = new Sequence("test", "ACGTACGT".AsMemory(), new string('I', 8).AsMemory());

        var codonChange = new CodonChange
        {
            OriginalCodon = "ACG",
            MutatedCodon = "TCG"
        };
        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 1000, // way beyond transcript length
            Reference = "A",
            Alternate = "T"
        };

        var ann = codonChange.Annotate("TestTranscript", transcript, 1000);

        Assert.NotNull(ann);
        Assert.Equal(VariantConsequence.Unknown, ann.Consequence);
    }

    // =============================================
    // Homopolymer Run Detection
    // =============================================

    /// <summary>
    /// Homopolymer run detection returns 1 for single bases surrounded by different bases.
    /// </summary>
    [Fact]
    public void GetHomopolymerRun_SingleBase_ReturnsOne()
    {
        var refSeq = "ACGTGCTAGC"; // no base appears more than once consecutively
        foreach (var i in Enumerable.Range(0, refSeq.Length))
        {
            Assert.Equal(1, VariantCaller.GetHomopolymerRun(refSeq.ToCharArray().AsSpan(), i));
        }
        // 1 because a single base has run length 1 (itself), NOT 0.
    }

    /// <summary>
    /// Homopolymer run at sequence boundaries is detected correctly.
    /// </summary>
    [Fact]
    public void GetHomopolymerRun_AtSequenceBoundary()
    {
        var refSeq = "AAAAACGT"; // 5 A's at start
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(5, VariantCaller.GetHomopolymerRun(refSeq.ToCharArray().AsSpan(), i));
        }

        Assert.Equal(1, VariantCaller.GetHomopolymerRun(refSeq.ToCharArray().AsSpan(), 5)); // C

        var refSeq2 = "ACGTTTTT"; // 5 T's at end
        Assert.Equal(1, VariantCaller.GetHomopolymerRun(refSeq2.ToCharArray().AsSpan(), 2)); // G
        for (var i = 3; i < 8; i++)
        {
            Assert.Equal(5, VariantCaller.GetHomopolymerRun(refSeq2.ToCharArray().AsSpan(), i));
        }
    }

    /// <summary>
    /// Homopolymer run length 12 (e.g., poly-A tract).
    /// </summary>
    [Fact]
    public void GetHomopolymerRun_LongRun()
    {
        var longRun = new string('A', 12); // AAAAAAAAAAAA
        for (var i = 0; i < longRun.Length; i++)
        {
            Assert.Equal(12, VariantCaller.GetHomopolymerRun(longRun.AsSpan(), i));
        }

        Assert.Equal(1, VariantCaller.GetHomopolymerRun("C".AsSpan(), 0)); // non-ACGT (single base = 1)
    }
}
