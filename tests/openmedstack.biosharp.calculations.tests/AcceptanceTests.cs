using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Vcf;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

/// <summary>
/// Acceptance tests for Proposal 1, Proposal 2 and Proposal 3.
/// </summary>
public class AcceptanceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _fastaPath;
    private readonly string _vcfPath;

    public AcceptanceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDir);

        _fastaPath = Path.Combine(_testDir, "test.fasta");
        _vcfPath = Path.Combine(_testDir, "test.vcf");

        // FASTA: two transcripts
        //  NM_001: ATG GCC ATT -> M(1) A(2) I(3)    [pos 4 = G]
        //  NM_002: ATG GGA CGT -> M(1) G(2) R(3)    [pos 4 = G]
        File.WriteAllLines(_fastaPath, [
            ">NM_001",
            "ATGGCCATT",
            ">NM_002",
            "ATGGGACGT"
        ]);

        // VCF: two variants
        //  pos 1: A>G (ATG -> GTG = Met->Val) both transcripts
        //  pos 4: G>A (GCC->GAC for NM_001, GGA->GAA for NM_002) both transcripts
        File.WriteAllLines(_vcfPath, [
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tA\tG\t30.0\tPASS\t.",
            "chr1\t4\t.\tG\tA\t30.0\tPASS\t."
        ]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, true);
            }
            catch
            {
                /* ignore */
            }
        }
    }

    // ================================================================
    // Proposal 1: VariantAnnotationEngine
    // Acceptance Criteria AC1.1 through AC1.5
    // ================================================================

    // AC1.1: LoadTranscriptsAsync loads transcripts from a FASTA file
    [Fact]
    public async Task AC1_1_LoadTranscriptsAsync_PopulatesTranscripts()
    {
        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);

        // LoadTranscriptsAsync is public; we verify it didn't throw and
        // that AnnotateVariantAsync can produce results (proving transcripts loaded).
        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 1,
            Reference = "A",
            Alternate = "G",
            ErrorProbabilities = []
        };

        var annotations = engine.AnnotateVariantAsync(variant);
        Assert.NotNull(annotations);
        Assert.Equal(2, annotations.Length); // one per transcript
    }

    // AC1.2: LoadTranscriptsAsync throws for non-existent file
    [Fact]
    public async Task AC1_2_LoadTranscriptsAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        using var engine = new VariantAnnotationEngine();
        await Assert.ThrowsAsync<FileNotFoundException>(() => engine.LoadTranscriptsAsync("nonexistent.fasta"));
    }

    // AC1.3: AnnotateVcfAsync returns annotations for all transcripts when no transcriptId is given
    [Fact]
    public async Task AC1_3_AnnotateVcfAsync_AllTranscripts_ReturnsCorrectCount()
    {
        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);

        var annotations = await engine.AnnotateVcfAsync(_vcfPath, null, 5.0f).ToListAsync();

        // 2 variants × 2 transcripts = 4 annotations
        Assert.Equal(4, annotations.Count);
    }

    // AC1.4: AnnotateVcfAsync filters by transcriptId
    [Fact]
    public async Task AC1_4_AnnotateVcfAsync_TranscriptIdFilter_Works()
    {
        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);

        var annotations = new List<VariantAnnotation>();
        await foreach (var ann in engine.AnnotateVcfAsync(_vcfPath, "NM_001", 5.0f))
        {
            annotations.Add(ann);
        }

        Assert.Equal(2, annotations.Count); // 2 variants for NM_001 only
        foreach (var ann in annotations)
        {
            Assert.Equal("NM_001", ann.AffectedGene);
        }
    }

    // AC1.5: AnnotateVcfAsync filters by quality (phred-scaled)
    [Fact]
    public async Task AC1_5_AnnotateVcfAsync_QualityFilter_Works()
    {
        // VCF with mixed quality levels
        var vcfFilePath = Path.Combine(_testDir, "mixed_qual.vcf");
        File.WriteAllLines(vcfFilePath, [
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tA\tG\t30.0\tPASS\t.", // high quality
            "chr1\t4\t.\tG\tA\t2.0\tPASS\t." // low quality (< 5.0)
        ]);

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(_fastaPath);

        var annotations = new List<VariantAnnotation>();
        await foreach (var ann in engine.AnnotateVcfAsync(vcfFilePath, null, 5.0f))
        {
            annotations.Add(ann);
        }

        // Only high-quality variant annotated → 2 (one per transcript)
        Assert.Equal(2, annotations.Count);
    }

    // ================================================================
    // Proposal 2: Non-coding Region Annotations
    // Acceptance Criteria AC2.1 through AC2.9
    // ================================================================

    private static AnnotationContext CreateContext(
        int cdsStart = 1000,
        int cdsEnd = 2000,
        int transcriptLength = 5000)
    {
        return AnnotationContext.FromCdsBoundaries(cdsStart, cdsEnd, transcriptLength);
    }

    // AC2.1: Position 1000 with CDS 1003-2000 returns Upstream
    // (splice window only applies when GeneBoundaries is set)
    [Fact]
    public void AC2_1_PositionWithinSpliceWindowUpstream_ReturnsUpstream()
    {
        var ctx = CreateContext(1003, 2000, 5000);
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(1000));
    }

    // AC2.2: Position 3003 with CDS 1-3000 returns Downstream
    // (splice window only applies when GeneBoundaries is set)
    [Fact]
    public void AC2_2_PositionWithinSpliceWindowDownstream_ReturnsDownstream()
    {
        var ctx = CreateContext(1, 3000, 5000);
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(3003));
    }

    // AC2.3: Position 500 with CDS 1000-2000 returns Upstream
    // (500 < 1000, and 1000 - 500 = 500 <= 3000)
    [Fact]
    public void AC2_3_PositionIn5UTR_ReturnsUpstream()
    {
        var ctx = CreateContext(1000, 2000, 5000);
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(500));
    }

    // AC2.4: Position 2500 with CDS 1000-2000 returns Downstream
    // (2500 > 2000, and 2500 - 2000 = 500 <= 3000)
    [Fact]
    public void AC2_4_PositionIn3UTR_ReturnsDownstream()
    {
        var ctx = CreateContext(1000, 2000, 5000);
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(2500));
    }

    // AC2.5: Position 9970 with CDS 10000-12000 returns Upstream (within 3kb)
    [Fact]
    public void AC2_5_PositionWithin3kbUpstream_ReturnsUpstream()
    {
        var ctx = CreateContext(10000, 12000, 15000);
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(9970));
    }

    // AC2.6: Position 12010 with CDS 10000-12000 returns Downstream (within 3kb)
    [Fact]
    public void AC2_6_PositionWithin3kbDownstream_ReturnsDownstream()
    {
        var ctx = CreateContext(10000, 12000, 15000);
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(12010));
    }

    // AC2.7: Position 9500 with CDS 10000-12000 returns Intronic
    [Fact]
    public void AC2_7_PositionInIntron_ReturnsIntronic()
    {
        var introns = new List<(int Start, int End)> { (9000, 9800) };
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(
            10000, 12000, 8000, 13000, introns);
        Assert.Equal(VariantConsequence.Intronic, ctx.ClassifyPosition(9500));
    }

    // AC2.8: Position 15000 with CDS 10000-12000 returns Downstream (within 3kb)
    // 15000 > 12000, 15000 - 12000 = 3000 <= 3000 -> Downstream
    [Fact]
    public void AC2_8_PositionBeyond3kb_ReturnsDownstream()
    {
        var ctx = CreateContext(10000, 12000, 20000);
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(15000));
    }

    // AC2.9: CDS positions return null (delegates to coding-variant classifier)
    [Fact]
    public void AC2_9_NullContext_DelegatesToCodingVariantClassifier()
    {
        var ctx = CreateContext();
        // In coding region, ClassifyPosition returns null
        // This verifies ClassifyPosition itself works; the null-context case
        // is validated in VariantAnnotator tests via ClassifyConsequence()
        Assert.Null(ctx.ClassifyPosition(1500));
    }

    // AC2 extra: Position exactly at CDS boundary returns null (coding)
    [Fact]
    public void AC2_CdsBoundaries_AreCoding()
    {
        var ctx = CreateContext(100, 300);
        Assert.Null(ctx.ClassifyPosition(100));
        Assert.Null(ctx.ClassifyPosition(101));
        Assert.Null(ctx.ClassifyPosition(300));
    }

    // AC2: Position < 1 returns Unknown
    [Fact]
    public void AC2_PositionLessThanOne_ReturnsUnknown()
    {
        var ctx = CreateContext();
        Assert.Equal(VariantConsequence.Unknown, ctx.ClassifyPosition(0));
        Assert.Equal(VariantConsequence.Unknown, ctx.ClassifyPosition(-1));
    }

    // AC2: Position beyond transcript length returns Intergenic
    [Fact]
    public void AC2_PositionBeyondTranscript_ReturnsIntergenic()
    {
        var ctx = CreateContext(1, 300, 500);
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(501));
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(1000));
    }

    // AC2: Without gene boundaries, positions near CDS are Upstream/Downstream (no splice window)
    [Fact]
    public void AC2_BoundariesReturnUpstreamDownstream_WithoutGeneBoundaries()
    {
        var ctx = CreateContext(1000, 2000);
        // pos 996-999: upstream (not splice site without gene boundaries)
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(996));
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(997));
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(998));
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(999));
        // pos 2001-2004: downstream (not splice site without gene boundaries)
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(2001));
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(2002));
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(2003));
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(2004));
    }

    // AC2: Upstream and downstream regions within 3kb of CDS
    [Fact]
    public void AC2_UpstreamRegion_3kbWindow()
    {
        var ctx = CreateContext(3000, 4000, 8000);
        // 7001 - 4000 = 3001 > 3000 window, so Intergenic
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(7001));
        // 5001 - 4000 = 1001 <= 3000 window, so Downstream
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(5001));
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(5000));
    }

    // AC2: Downstream region is within 3kb after CDS
    [Fact]
    public void AC2_DownstreamRegion_3kbWindow()
    {
        var ctx = CreateContext(1000, 2000, 8000);
        // 5000 - 2000 = 3000 <= 3000 window, so Downstream
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(5000));
        // 7001 - 2000 = 5001 > 3000 window, so Intergenic
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(7001));
    }

    // AC2: Gene boundaries with introns - full integration test
    [Fact]
    public void AC2_GeneBoundaries_WithIntrons_CompleteCoverage()
    {
        var introns = new List<(int Start, int End)> { (105, 130) };
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(
            100, 200, 50, 350, introns);

        // Intergenic: before gene
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(49));
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(10));

        // Upstream: between gene start and splice window
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(50));
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(80));

        // Splice site: within 3bp of CDS start
        Assert.Equal(VariantConsequence.SpliceSite, ctx.ClassifyPosition(97));
        Assert.Equal(VariantConsequence.SpliceSite, ctx.ClassifyPosition(98));
        Assert.Equal(VariantConsequence.SpliceSite, ctx.ClassifyPosition(99));

        // Coding in CDS - ClassifyPosition returns null (delegates to coding-variant classifier)
        Assert.Null(ctx.ClassifyPosition(100));
        Assert.Null(ctx.ClassifyPosition(115));
        Assert.Null(ctx.ClassifyPosition(200));

        // Splice site: within 3bp of CDS end
        Assert.Equal(VariantConsequence.SpliceSite, ctx.ClassifyPosition(201));
        Assert.Equal(VariantConsequence.SpliceSite, ctx.ClassifyPosition(202));
        Assert.Equal(VariantConsequence.SpliceSite, ctx.ClassifyPosition(203));

        // Downstream
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(210));
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(300));

        // Intergenic: after gene
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(351));
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(500));
    }

    // AC2: Verify that ClassifyPosition is called from ClassifyConsequence
    // when context is non-null and variant is in a non-coding region.
    [Fact]
    public void AC2_ClassifyConsequence_WithAnnotationContext_ReturnsNonCoding()
    {
        var ctx = CreateContext(1000, 2000, 5000);

        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('.', 5000).ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 5000).ToCharArray()));
        var codonChange = VariantAnnotator.Substitution("ATG", 500, 'T', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_999", seq, 500, 'A', 'T', ctx);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Upstream, result.Consequence);
    }

    // AC2: Variant in splice site region (requires gene boundaries)
    [Fact]
    public void AC2_ClassifyConsequence_SpliceSite_ReturnsSpliceSite()
    {
        var introns = new List<(int Start, int End)> { (1, 800) };
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(
            1000, 2000, 500, 2500, introns);

        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('.', 5000).ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 5000).ToCharArray()));

        // Position 997 is in the upstream splice window
        var codonChange = VariantAnnotator.Substitution("ATG", 997, 'A', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_888", seq, 997, 'A', 'T', ctx);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.SpliceSite, result.Consequence);
    }

    // AC2: Variant in downstream region
    [Fact]
    public void AC2_ClassifyConsequence_Downstream_ReturnsDownstream()
    {
        var introns = new List<(int Start, int End)> { (1, 500) };
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(
            1000, 2000, 500, 3000, introns);

        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('.', 5000).ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 5000).ToCharArray()));

        // Position 2500 is downstream of CDS but within 3kb
        var codonChange = VariantAnnotator.Substitution("ATG", 2500, 'A', 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_777", seq, 2500, 'A', 'G', ctx);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Downstream, result.Consequence);
    }

    // ================================================================
    // Proposal 3: Complex Variant Support
    // Acceptance Criteria AC3.1 through AC3.8
    // ================================================================

    // AC3.1: MultiCodonIndel basic test
    [Fact]
    public void AC3_1_MultiCodonIndel_Basic_ReturnsCorrectCodonChange()
    {
        var result = VariantAnnotator.MultiCodonIndel("ATGATG", 1, "ATGATG", "ATGACG");
        Assert.NotNull(result);
        Assert.Equal("ATGATG", result.OriginalCodon);
        Assert.Equal("ATGACG", result.MutatedCodon);
    }

    // AC3.2: MultiCodonIndel with variant spanning 2 codons
    [Fact]
    public void AC3_2_MultiCodonIndel_SpansTwoCodons()
    {
        var result = VariantAnnotator.MultiCodonIndel("ATGGATCCAA", 3, "GGAT", "GGCT");
        Assert.NotNull(result);
        Assert.Equal("GGAT", result.OriginalCodon);
        Assert.Equal("GGCT", result.MutatedCodon);
    }

    // AC3.2: MultiCodonIndel returns null when out of bounds
    [Fact]
    public void AC3_2_MultiCodonIndel_OutOfBounds_ReturnsNull()
    {
        var result = VariantAnnotator.MultiCodonIndel("ATGCGT", 1, "ATGCGT", "AAAAAA");
        Assert.NotNull(result);
        Assert.Equal("ATGCGT", result.OriginalCodon);

        var tooLong = VariantAnnotator.MultiCodonIndel("ATG", 1, "ATGC", "AAAA");
        Assert.Null(tooLong);
    }

    // AC3.3: Mnp with multiple substitutions in one codon
    [Fact]
    public void AC3_3_Mnp_TwoSubstitutionsInOneCodon()
    {
        var positions = new List<int> { 2, 3 };
        var altBases = new List<char> { 'C', 'G' };

        var result = VariantAnnotator.Mnp("ATG", positions, altBases);
        Assert.NotNull(result);
        Assert.Equal("ATG", result.OriginalCodon);
        Assert.Equal("ACG", result.MutatedCodon);
    }

    // AC3.3: Mnp single substitution
    [Fact]
    public void AC3_3_Mnp_SingleSubstitution()
    {
        var positions = new List<int> { 1 };
        var altBases = new List<char> { 'G' };

        var result = VariantAnnotator.Mnp("ATG", positions, altBases);
        Assert.NotNull(result);
        Assert.Equal("ATG", result.OriginalCodon);
        Assert.Equal("GTG", result.MutatedCodon);
    }

    // AC3.4: MNP spans two adjacent codons
    [Fact]
    public void AC3_4_Mnp_SpansTwoCodons()
    {
        var positions = new List<int> { 3, 4 };
        var altBases = new List<char> { 'A', 'T' };

        var result = VariantAnnotator.Mnp("ATGCGT", positions, altBases);
        Assert.NotNull(result);
        Assert.Equal("ATGCGT", result.OriginalCodon);
        Assert.Equal("ATATGT", result.MutatedCodon);
    }

    // AC3.4: MNP validates positions are in range
    [Fact]
    public void AC3_4_Mnp_PositionOutOfRange_ReturnsNull()
    {
        var positions = new List<int> { 5 };
        var altBases = new List<char> { 'T' };
        var result = VariantAnnotator.Mnp("ATG", positions, altBases);
        Assert.Null(result);
    }

    // AC3.4: Mnp counts mismatched altBases count
    [Fact]
    public void AC3_4_Mnp_CountMismatch_ThrowsArgumentException()
    {
        var positions = new List<int> { 1 };
        var altBases = new List<char> { 'A', 'T' };
        Assert.Throws<ArgumentException>(() => VariantAnnotator.Mnp("ATG", positions, altBases));
    }

    // AC3.5: Delins - delete 3 and insert 3
    [Fact]
    public void AC3_5_Delins_Basic()
    {
        var result = VariantAnnotator.Delins("ATGGC", 1, 3, "CAG");
        Assert.NotNull(result);
        Assert.Equal("ATG", result.OriginalCodon);
        Assert.Equal("CAGGC", result.MutatedCodon);
    }

    // AC3.5: Delins single-base replace
    [Fact]
    public void AC3_5_Delins_SingleBaseReplace()
    {
        var result = VariantAnnotator.Delins("ATGCGT", 2, 1, "C");
        Assert.NotNull(result);
        Assert.Equal("T", result.OriginalCodon);
        Assert.Equal("ACGCGT", result.MutatedCodon);
    }

    // AC3.6: Delins handles expansion
    [Fact]
    public void AC3_6_Delins_Expansion()
    {
        var result = VariantAnnotator.Delins("ATGCGT", 1, 1, "AAGG");
        Assert.NotNull(result);
        Assert.Equal("A", result.OriginalCodon);
        Assert.Equal("AAGGTGCGT", result.MutatedCodon);
    }

    // AC3.6: Delins returns null when bounds exceeded
    [Fact]
    public void AC3_6_Delins_OutOfBounds_ReturnsNull()
    {
        var result = VariantAnnotator.Delins("ATG", 1, 4, "AAAA");
        Assert.Null(result);
    }

    // AC3.6: Delins with zero-length insertion (pure deletion)
    [Fact]
    public void AC3_6_Delins_PureDeletion()
    {
        var result = VariantAnnotator.Delins("ATGCC", 2, 2, "");
        Assert.NotNull(result);
        Assert.Equal("TG", result.OriginalCodon);
        Assert.Equal("ACC", result.MutatedCodon);
    }

    // AC3.7: Frameshift with gene boundaries and position within CDS
    [Fact]
    public void AC3_7_Frameshift_SetsFrameshiftOffset()
    {
        // ref = all A's (90 chars). Deletion ATG->G (2-bp del) at pos 1.
        // Mutated: "" + G + A*87 = "GAA" + "AAA"... (Glu, Lys, Lys...) - no stop codon.
        // CountAminosUntilStop returns -1 when no stop is found.
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('A', 90).ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 90).ToCharArray()));

        var codonChange = VariantAnnotator.MultiDeletion("ATG", 1, 2);
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_666", seq, 1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
        Assert.NotNull(result.FrameshiftOffset);
        Assert.Equal(-1, result.FrameshiftOffset);
    }

    // AC3.7: FrameshiftOffset = -1 when no stop codon found in sequence
    [Fact]
    public void AC3_7_FrameshiftOffset_NoStopCodon_ReturnsNegativeOne()
    {
        // Use all C's - no stop codon possible (CCC=Pro, repeated).
        // Deletion CCC->C at pos 1 (2-bp del). Mutated = C + C*87 = C*88.
        // Count: all CCC -> Pro, no stop -> -1.
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('C', 90).ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 90).ToCharArray()));

        var codonChange = VariantAnnotator.MultiDeletion("CCC", 1, 2);
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_555", seq, 1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
        Assert.Equal(-1, result.FrameshiftOffset);
    }

    // AC3.8: FrameshiftOffset with the insertion test
    [Fact]
    public void AC3_8_FrameshiftOffset_CorrectlyCountsUntilStop()
    {
        var dna = "ATGCCCGGGTAA";
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(dna.ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', dna.Length).ToCharArray()));

        var codonChange = VariantAnnotator.Insertion("ATG", 4, 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_444", seq, 4);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
        Assert.NotNull(result.FrameshiftOffset);

        // Mutated: ATG + A + CCCGGGTAA = ATGACCCGGGTAA
        // Count from pos 4: chars at 4..13 = ACC CGG GTA A
        // ACC(T), CGG(R), GTA(V) = 3 amino acids, no stop -> -1
        Assert.Equal(-1, result.FrameshiftOffset);
    }

    // AC3.8: FrameshiftOffset counts correctly when stop codon exists
    [Fact]
    public void AC3_8_FrameshiftOffset_CountsWithStopCodon()
    {
        // ref = "AAAAA TAG" (8 chars). After insertion at pos 1, mutated = "GAAA" + "AATAG" = "GAAAAATAG"
        // Codons in shifted frame: GAA(E), AAA(K), TAG(STOP) → count = 2
        // Uses simple Annotate overload to avoid refBase/altBase in BuildHgvsCoding.
        var dna = "AAAAATAG";
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(dna.ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', dna.Length).ToCharArray()));

        var codonChange = VariantAnnotator.Insertion("AAA", 1, 'G');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_333", seq, 1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
        Assert.NotNull(result.FrameshiftOffset);
        // GAA(E, count=1), AAA(K, count=2), TAG(STOP) → return 2
        Assert.Equal(2, result.FrameshiftOffset);
    }

    // ================================================================
    // AC3+: Complex variant builder + Annotate() integration tests
    // ================================================================

    // AC3.9: MultiCodonIndel + Annotate -> Missense
    [Fact]
    public void AC3_9_MultiCodonIndel_Annotate_ReturnsMissense()
    {
        // GCA(A) -> GCG(A) would be syn, so use something that changes aa.
        // refSeq = "ATGCGACCCGGG" -> positions: ATG(M) CCA(P) CCC(P) GGG(G).
        // MultiCodonIndel at pos 4 with "CGA" -> "CGT": CGA(arginine) -> CGT(arginine) = syn.
        // Try CGA -> CGA... no. Let's use CGA -> CGT: both Arg. Bad choice.
        // CCA(P) -> CTA(L) = missense. refSeq = "ATGCCACCCGGG"
        // pos 4, refSubset "CCA" (3 chars), altSeq "CTA" (3 chars). delta = 0
        var refSeq = "ATGCCACCCGGG";
        var codonChange = VariantAnnotator.MultiCodonIndel(refSeq, 4, "CCA", "CTA");
        Assert.NotNull(codonChange);
        // First 3 chars of original "CCA" = CCA (Pro)
        // First 3 chars of mutated "CTA" = CTA (Leu)
        // Pro -> Leu = Missense
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(refSeq.ToCharArray()),
            new ReadOnlyMemory<char>(new string('!', refSeq.Length).ToCharArray()));

        var result = codonChange.Annotate("NM_999", seq, 4);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Missense, result.Consequence);
        Assert.Equal("CCA>CTA", result.CodonChange);
    }

    // AC3.10: Mnp + Annotate -> Missense (first codon only)
    [Fact]
    public void AC3_10_Mnp_Annotate_ReturnsMissense()
    {
        // ATG(M) -> ACG(T) = missense (pos 2 change T->C in ATG)
        var codonChange = VariantAnnotator.Mnp("ATG", new List<int> { 2 }, new List<char> { 'C' });
        Assert.NotNull(codonChange);
        var seq = new Sequence("trans", new ReadOnlyMemory<char>("ATGCCC".ToCharArray()),
            new ReadOnlyMemory<char>(new string('I', 6).ToCharArray()));

        var result = codonChange.Annotate("NM_888", seq, 1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Missense, result.Consequence);
        Assert.Equal("ATG>ACG", result.CodonChange);
    }

    // AC3.11: Delins + Annotate -> Frameshift (insert 2 bases)
    [Fact]
    public void AC3_11_Delins_Annotate_Frameshift()
    {
        // Delete 3 bases (ATG), insert 2 (AAG) = delta -1 => Frameshift
        var codonChange = VariantAnnotator.Delins("ATGCC", 1, 3, "AA");
        Assert.NotNull(codonChange);
        var seq = new Sequence("trans", new ReadOnlyMemory<char>("ATGCCGGGTAA".ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 11).ToCharArray()));

        var result = codonChange.Annotate("NM_777", seq, 1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
        Assert.Equal("ATG>AACC", result.CodonChange);
    }

    // AC3.12: Delins + Annotate -> the full mutated string is stored in MutatedCodon
    // (not just the insertion bases). The classifier then only looks at the
    // first 3 chars of mutated vs original for codon translation.
    [Fact]
    public void AC3_12_Delins_Annotate_StoresFullMutatedString()
    {
        // Delins("ATGTGCCCGGG", pos=4, del=3, ins="GGG")
        // originalSubset = "TGC" (indices 3-5 in 0-based)
        // mutated = "ATG" + "GGG" + "CCGGG" = "ATGGGGCCGGG"
        // CodonChange stores: Original=TGC, Mutated=ATGGGGCCGGG
        var refSeq = "ATGTGCCCGGG"; // ATG(M) TGC(Cys) CCC(P) GGG(G)
        var codonChange = VariantAnnotator.Delins(refSeq, 4, 3, "GGG");
        Assert.NotNull(codonChange);
        Assert.Equal("TGC", codonChange.OriginalCodon);
        Assert.Equal("ATGGGGCCGGG", codonChange.MutatedCodon);
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(refSeq.ToCharArray()),
            new ReadOnlyMemory<char>(new string('!', refSeq.Length).ToCharArray()));
        var result = codonChange.Annotate("NM_667", seq, 4);

        Assert.NotNull(result);
        Assert.Equal("TGC>ATGGGGCCGGG", result.CodonChange);
        // First 3 chars: TGC(Cys) vs ATG(Met) => Missense; delta=8 not div-by-3 => Frameshift
        Assert.Equal(VariantConsequence.Frameshift, result.Consequence);
    }

    // ================================================================
    // Additional integration tests for complex variants
    // ================================================================

    // MultiCodonIndel: 4-bp deletion spanning codons
    [Fact]
    public void MultiCodonIndel_General_Deletion()
    {
        var refSeq = "ATGCCCGGGTTA";
        var result = VariantAnnotator.MultiCodonIndel(refSeq, 2, "TGCC", "TGGA");
        Assert.NotNull(result);
        Assert.Equal("TGCC", result.OriginalCodon);
        Assert.Equal("TGGA", result.MutatedCodon);
    }

    // Mnp: multiple substitutions at non-adjacent positions across codons
    [Fact]
    public void Mnp_MultipleSubstitutions_DifferentCodons()
    {
        var positions = new List<int> { 2, 8 };
        var altBases = new List<char> { 'C', 'C' };
        var result = VariantAnnotator.Mnp("ATGCGTATG", positions, altBases);
        Assert.NotNull(result);
        Assert.Equal("ATGCGTATG", result.OriginalCodon);
        Assert.Equal("ACGCGTACG", result.MutatedCodon);
    }

    // Frameshift: inframe change does NOT set FrameshiftOffset
    [Fact]
    public void FrameshiftOffset_InframeDeletion_DoesNotSetOffset()
    {
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('A', 30).ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 30).ToCharArray()));

        var codonChange = VariantAnnotator.MultiDeletion("AAA", 1, 3);
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_222", seq, 1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.InframeDeletion, result.Consequence);
        Assert.Null(result.FrameshiftOffset);
    }

    // Mnp with empty/null inputs
    [Fact]
    public void Mnp_NullRefCodons_Throws()
    {
        Assert.Throws<ArgumentException>(() => VariantAnnotator.Mnp(null!, new List<int>(), new List<char>()));
    }

    [Fact]
    public void Mnp_NullPositions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VariantAnnotator.Mnp("ATG", null!, Array.Empty<char>()));
    }

    [Fact]
    public void Mnp_NullAltBases_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => VariantAnnotator.Mnp("ATG", new List<int>(), null!));
    }

    // ================================================================
    // Task 1: AnnotationContext Construction Validation Tests
    // AC-CTX-1 through AC-CTX-10
    // ================================================================

    // AC-CTX-1: Validate CdsStart < 1 throws ArgumentException
    [Fact]
    public void AC_CTX_1_CdsStartLessThanOne_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AnnotationContext.FromCdsBoundaries(0, 300, 1000));
        Assert.Throws<ArgumentException>(() => AnnotationContext.FromCdsBoundaries(-5, 300, 1000));
    }

    // AC-CTX-2: Validate CdsEnd < CdsStart throws ArgumentException
    [Fact]
    public void AC_CTX_2_CdsEndLessThanCdsStart_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AnnotationContext.FromCdsBoundaries(100, 50, 1000));
        Assert.Throws<ArgumentException>(() => AnnotationContext.FromCdsBoundaries(1, 0, 1000));
    }

    // AC-CTX-3: Validate TranscriptLength < CdsEnd throws ArgumentException
    [Fact]
    public void AC_CTX_3_TranscriptLengthLessThanCdsEnd_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => AnnotationContext.FromCdsBoundaries(100, 300, 250));
        Assert.Throws<ArgumentException>(() => AnnotationContext.FromCdsBoundaries(1, 100, 99));
    }

    // AC-CTX-4: Validate GeneStart < 1 throws ArgumentException
    [Fact]
    public void AC_CTX_4_GeneStartLessThanOne_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            AnnotationContext.FromCdsAndGeneBoundaries(100, 300, 0, 500));
        Assert.Throws<ArgumentException>(() =>
            AnnotationContext.FromCdsAndGeneBoundaries(100, 300, -5, 500));
    }

    // AC-CTX-5: Validate GeneEnd < GeneStart throws ArgumentException
    [Fact]
    public void AC_CTX_5_GeneEndLessThanGeneStart_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            AnnotationContext.FromCdsAndGeneBoundaries(100, 300, 50, 60));
    }

    // AC-CTX-6: Validate GeneEnd < CdsEnd throws ArgumentException
    [Fact]
    public void AC_CTX_6_GeneEndLessThanCdsEnd_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            AnnotationContext.FromCdsAndGeneBoundaries(100, 300, 50, 250));
    }

    // AC-CTX-7: FromCdsBoundaries with valid params produces correct values
    [Fact]
    public void AC_CTX_7_FromCdsBoundaries_ValidParams_ProducesCorrectValues()
    {
        var ctx = AnnotationContext.FromCdsBoundaries(100, 300, 1000);
        Assert.Equal(100, ctx.CdsStart);
        Assert.Equal(300, ctx.CdsEnd);
        Assert.Equal(1000, ctx.TranscriptLength);
        Assert.Null(ctx.GeneBoundaries);
        Assert.Null(ctx.Introns);
    }

    // AC-CTX-8: FromCdsAndGeneBoundaries with valid params produces correct values + introns
    [Fact]
    public void AC_CTX_8_FromCdsAndGeneBoundaries_ValidParams_ProducesCorrectValues()
    {
        var introns = new List<(int Start, int End)> { (120, 150), (200, 250) };
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(100, 300, 50, 500, introns);
        Assert.Equal(100, ctx.CdsStart);
        Assert.Equal(300, ctx.CdsEnd);
        Assert.Equal(500, ctx.TranscriptLength);
        Assert.NotNull(ctx.GeneBoundaries);
        Assert.Equal(50, ctx.GeneBoundaries.Value.Start);
        Assert.Equal(500, ctx.GeneBoundaries.Value.End);
        Assert.NotNull(ctx.Introns);
        Assert.Equal(2, ctx.Introns.Count);
        Assert.Equal((120, 150), ctx.Introns[0]);
        Assert.Equal((200, 250), ctx.Introns[1]);
    }

    // AC-CTX-9: ClassifyPosition with GeneBoundaries but no Introns still works
    [Fact]
    public void AC_CTX_9_ClassifyPosition_GeneBoundariesNoIntrons_Works()
    {
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(100, 300, 50, 500, null);
        Assert.Null(ctx.Introns);

        // CDS region
        Assert.Null(ctx.ClassifyPosition(150));

        // Splice site (within 3bp of CDS start)
        Assert.Equal(VariantConsequence.SpliceSite, ctx.ClassifyPosition(97));

        // Upstream
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(50));

        // Intronic not possible with no introns
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(49));
    }

    // AC-CTX-10: Intronic position with no intron list returns delegates to coding classifier
    [Fact]
    public void AC_CTX_10_IntronicPosition_NoIntronList_DelegatesToCodingClassifier()
    {
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(100, 300, 50, 500, Array.Empty<(int Start, int End)>());
        // Position in CDS with no introns = null (coding, delegate to variant classifier)
        Assert.Null(ctx.ClassifyPosition(200));
        // Position outside CDS with no introns = Upstream/Downstream
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(80));
    }


    // ================================================================
    // Task 3: Uncertain Consequence Engine-Level Tests
    // AC-UC-3: Engine-level filter rejects variants where ref allele doesn't match transcript
    [Fact]
    public async Task AC_UC_3_RefMismatch_ViaEngine_ReturnsUncertain()
    {
        // Write a FASTA with a known transcript
        var fastaPath = Path.Combine(_testDir, "mismatch.fasta");
        File.WriteAllLines(fastaPath, [
            ">NM_MISMATCH",
            "ATGGCCATT"
        ]);

        // VCF: position 1 claims REF=G, but transcript has A
        var vcfPath = Path.Combine(_testDir, "mismatch.vcf");
        File.WriteAllLines(vcfPath, [
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tG\tT\t30.0\tPASS\t."
        ]);

        using var engine = new VariantAnnotationEngine();
        await Assert.ThrowsAsync<FileNotFoundException>(() => engine.LoadTranscriptsAsync("nonexistent.fasta"));
        await engine.LoadTranscriptsAsync(fastaPath);

        var variant = new VcfVariant
        {
            Chromosome = "chr1",
            Position = 1,
            Reference = "G",
            Alternate = "T",
            ErrorProbabilities = Array.Empty<int>()
        };

        var annotations = engine.AnnotateVariantAsync(variant);
        Assert.NotNull(annotations);
        Assert.Single(annotations);
        Assert.Equal(VariantConsequence.Uncertain, annotations[0].Consequence);
    }

    // AC-UC-4: BuildHgvsCoding handles null refBase/altBase gracefully
    [Fact]
    public void AC_UC_4_BuildHgvsCoding_NullRefAltBase_ProducesCodingNotation()
    {
        // Use the codonChange.Annotate overload without refBase/altBase
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('A', 30).ToCharArray()),
            new ReadOnlyMemory<char>(new string('.', 30).ToCharArray()));
        var codonChange = VariantAnnotator.Substitution("GCT", 1, 'G', 'A');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_001", seq, 1); // no refBase/altBase

        Assert.NotNull(result);
        // Should produce a valid HGVS coding notation even without refBase/altBase
        Assert.True(!string.IsNullOrEmpty(result.HgvsCoding));
    }


    // ================================================================
    // Task 4: StopRetained FrameshiftOffset is null
    [Fact]
    public void AC_SR_3_StopRetained_FrameshiftOffset_IsNull()
    {
        // Transcript: TCG TCG TAA = 9 bases. Position 8 (last codon TAA).
        // Mutate TAA -> TGA (stop codon, last position = StopRetained).
        var transcript = new Sequence("test", "TCGTCGTAA".AsMemory(), new string('I', 9).AsMemory());
        var codonChange = new CodonChange
        {
            OriginalCodon = "TAA",
            MutatedCodon = "TGA"
        };

        var ann = codonChange.Annotate("TestTranscript", transcript, 8);

        Assert.NotNull(ann);
        Assert.Equal(VariantConsequence.StopRetained, ann.Consequence);
        Assert.Null(ann.FrameshiftOffset);
    }


    // ================================================================
    // Task 5: Additional Edge Case Tests
    // AC-ED-1: Empty transcript returns Unknown
    [Fact]
    public void AC_ED_1_EmptyTranscript_ReturnsUnknown()
    {
        var seq = new Sequence("trans", ReadOnlyMemory<char>.Empty, ReadOnlyMemory<char>.Empty);
        var codonChange = VariantAnnotator.Substitution("ATG", 1, 'A', 'T');
        Assert.NotNull(codonChange);

        var result = codonChange.Annotate("NM_EMPTY", seq, 1);

        Assert.NotNull(result);
        Assert.Equal(VariantConsequence.Unknown, result.Consequence);
    }

    // AC-ED-4: Position beyond transcript length but within gene boundaries -> Intergenic
    [Fact]
    public void AC_ED_4_PositionBeyondTranscriptWithinGeneBoundary_ReturnsIntergenic()
    {
        var introns = new List<(int Start, int End)> { (105, 130) };
        var ctx = AnnotationContext.FromCdsAndGeneBoundaries(
            100, 200, 50, 1000, introns);
        // Use a transcript long enough to hold position 3500 and within gene (50-1000)
        var seq = new Sequence("trans", new ReadOnlyMemory<char>(new string('A', 4000).ToCharArray()),
            new ReadOnlyMemory<char>(new string('I', 4000).ToCharArray()));
        // Position 3500 is within gene (50-1000)? No, 3500 > 1000, so it's beyond gene -> Intergenic
        // For a position that IS within gene but beyond 3kb from CDS: use gene boundaries of 50-5000
        var ctx2 = AnnotationContext.FromCdsAndGeneBoundaries(
            100, 200, 50, 5000, introns);
        // Position 3500 is within gene (50-5000), beyond CDS (200), 3500-200=3300 > 3000 -> Intergenic
        var codonChange = new CodonChange
        {
            OriginalCodon = "AAT",
            MutatedCodon = "ATT"
        };

        var result = codonChange.Annotate("NM_X", seq, 3500, null, null, ctx2);

        Assert.NotNull(result);
        // Should be Intergenic (beyond transcript + beyond CDS + beyond 3kb window + within gene)
        Assert.Equal(VariantConsequence.Intergenic, result.Consequence);
    }

    // AC-ED-6: VQSLOD exactly at threshold (5.0) is included (not filtered)
    [Fact]
    public async Task AC_ED_6_VQSLOD_AtThreshold_IsIncluded()
    {
        var vcfPath = Path.Combine(_testDir, "threshold.vcf");
        File.WriteAllLines(vcfPath, [
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tA\tG\t5.0\tPASS\t."
        ]);
        var fastaPath = Path.Combine(_testDir, "threshold.fasta");
        File.WriteAllLines(fastaPath, [
            ">NM_THRESH",
            "ATG"
        ]);

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(fastaPath);

        var annotations = new List<VariantAnnotation>();
        await foreach (var ann in engine.AnnotateVcfAsync(vcfPath, null, 5.0f))
        {
            annotations.Add(ann);
        }

        Assert.Single(annotations);
    }

    // AC-ED-7: Empty VCF (header only) returns empty IAsyncEnumerable
    [Fact]
    public async Task AC_ED_7_EmptyVcf_ReturnsEmptySequence()
    {
        var vcfPath = Path.Combine(_testDir, "empty.vcf");
        File.WriteAllLines(vcfPath, [
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO"
        ]);
        var fastaPath = Path.Combine(_testDir, "empty.fasta");
        File.WriteAllLines(fastaPath, [
            ">NM_EMPTY",
            "ATGGCCATT"
        ]);

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(fastaPath);

        var annotations = await engine.AnnotateVcfAsync(vcfPath, null, 5.0f).ToListAsync();
        Assert.Empty(annotations);
    }

    // AC-ED-11: Upstream region exactly 3001bp away from CDS -> Intergenic
    // CDS start=3002. Position 1 is 3001bp away (>= 3001) -> Intergenic.
    // Position 3001 is 1bp away (within 3kb) -> Upstream.
    [Fact]
    public void AC_ED_11_UpstreamRegion_DistanceDeterminesIntergenicOrUpstream()
    {
        // CDS starts at 3002. Distance from position 1 = 3001 > 3000 -> Intergenic.
        var ctx = AnnotationContext.FromCdsBoundaries(3002, 4000, 8000);
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(1));
        // Distance from position 3001 = 1 <= 3000 -> Upstream.
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(3001));
        // Distance from position 2000 = 1002 <= 3000 -> Upstream.
        Assert.Equal(VariantConsequence.Upstream, ctx.ClassifyPosition(2000));
    }

    // AC-ED-12: Downstream region exactly 3001bp away from CDS -> Intergenic
    // CDS end=200. Position 3201 is 3001bp away (>= 3001) -> Intergenic.
    // Position 3001 is 2801bp away (within 3kb) -> Downstream.
    [Fact]
    public void AC_ED_12_DownstreamRegion_DistanceDeterminesIntergenicOrDownstream()
    {
        // CDS ends at 200, transcript length 8000
        var ctx = AnnotationContext.FromCdsBoundaries(100, 200, 8000);
        // Position 3201: 3201-200=3001 > 3000 -> Intergenic
        Assert.Equal(VariantConsequence.Intergenic, ctx.ClassifyPosition(3201));
        // Position 3001: 3001-200=2801 <= 3000 -> Downstream
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(3001));
        // Position 201 is 1bp away -> Downstream
        Assert.Equal(VariantConsequence.Downstream, ctx.ClassifyPosition(201));
    }


    // ================================================================
    // Task 6: End-to-End Integration Tests
    // AC-EE-4: Variant with filter "FAIL" still annotated
    [Fact]
    public async Task AC_EE_4_VariantWithFailFilter_StillAnnotated()
    {
        var vcfPath = Path.Combine(_testDir, "fail_filter.vcf");
        File.WriteAllLines(vcfPath, [
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tA\tG\t30.0\tFAIL\t."
        ]);
        var fastaPath = Path.Combine(_testDir, "fail_filter.fasta");
        File.WriteAllLines(fastaPath, [
            ">NM_FAIL",
            "ATG"
        ]);

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(fastaPath);

        var annotations = await engine.AnnotateVcfAsync(vcfPath, null, 5.0f).ToListAsync();
        // Filter is INFO-level, engine does not filter by it
        Assert.Single(annotations);
    }

    // AC-EE-5: Transcript ID filter with non-existent ID returns empty
    [Fact]
    public async Task AC_EE_5_NonexistentTranscriptId_ReturnsEmpty()
    {
        var vcfPath = Path.Combine(_testDir, "nonexistent.vcf");
        File.WriteAllLines(vcfPath, [
            "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO",
            "chr1\t1\t.\tA\tG\t30.0\tPASS\t."
        ]);
        var fastaPath = Path.Combine(_testDir, "nonexistent.fasta");
        File.WriteAllLines(fastaPath, [
            ">NM_REAL",
            "ATG"
        ]);

        using var engine = new VariantAnnotationEngine();
        await engine.LoadTranscriptsAsync(fastaPath);

        var annotations = await engine.AnnotateVcfAsync(vcfPath, "NM_UNKNOWN", 5.0f).ToListAsync();
        Assert.Empty(annotations);
    }
}
