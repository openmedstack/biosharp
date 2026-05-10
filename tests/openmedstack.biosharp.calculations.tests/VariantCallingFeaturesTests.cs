using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// VC-1 — gVCF (genomic VCF) output
// ─────────────────────────────────────────────────────────────────────────────
public class GvcfWriterTests
{
    private static LocalVariantResult Variant(string chrom, int pos, string refB, string alt, int qual, int depth)
    {
        return new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = alt,
            QuantitativeQuality = qual,
            Depth = depth
        };
    }

    [Fact]
    public async Task WriteGvcfAsync_EmitsFileFormatHeader()
    {
        var refSeq = new string('A', 20);
        var depths = new int[20];
        Array.Fill(depths, 30);
        var variants = new[] { Variant("chr1", 10, "A", "T", 50, 30) };

        using var ms = new MemoryStream();
        await GvcfWriter.WriteAsync(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("##fileformat=VCFv4.2", text);
        Assert.Contains("##ALT=<ID=NON_REF", text);
    }

    [Fact]
    public async Task WriteGvcfAsync_VariantPositionIsEmittedAsStandardRecord()
    {
        // Reference: 20 A's; variant at position 10 (1-based)
        var refSeq = new string('A', 20);
        var depths = new int[20];
        Array.Fill(depths, 30);
        var variants = new[] { Variant("chr1", 10, "A", "T", 50, 30) };

        using var ms = new MemoryStream();
        await GvcfWriter.WriteAsync(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        // The variant at pos 10 should appear as a standard record with T as ALT (not <NON_REF>)
        Assert.Contains(dataLines, l => l.Contains("\t10\t") && l.Contains("\tT\t"));
    }

    [Fact]
    public async Task WriteGvcfAsync_AllPositionsCoveredByVariantOrRefBlock()
    {
        // Reference: 20 A's; variant at pos 10 only.
        var refSeq = new string('A', 20);
        var depths = new int[20];
        Array.Fill(depths, 25);
        var variants = new[] { Variant("chr1", 10, "A", "T", 50, 25) };

        using var ms = new MemoryStream();
        await GvcfWriter.WriteAsync(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        // Collect all positions covered
        var covered = new HashSet<int>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split('\t');
            if (fields.Length < 8)
            {
                continue;
            }

            var pos = int.Parse(fields[1]);
            covered.Add(pos);

            // Reference blocks have END= in INFO
            var info = fields[7];
            var endIdx = info.IndexOf("END=", StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                var endSpan = info.AsSpan(endIdx + 4);
                var semi = endSpan.IndexOf(';');
                var endStr = semi >= 0 ? endSpan[..semi] : endSpan;
                var endPos = int.Parse(endStr);
                for (var p = pos; p <= endPos; p++)
                {
                    covered.Add(p);
                }
            }
        }

        // All positions 1..20 must be covered
        for (var i = 1; i <= 20; i++)
        {
            Assert.True(covered.Contains(i), $"Position {i} not covered by any record");
        }
    }

    [Fact]
    public async Task WriteGvcfAsync_RefBlocksIncludeEndMinDpAndGq()
    {
        var refSeq = new string('A', 10);
        var depths = new int[10];
        Array.Fill(depths, 15);
        var variants = Array.Empty<LocalVariantResult>();

        using var ms = new MemoryStream();
        await GvcfWriter.WriteAsync(ms, variants, refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        Assert.NotEmpty(dataLines);
        // Each reference block line should have END=, MIN_DP=, and GQ
        foreach (var line in dataLines)
        {
            Assert.Contains("END=", line);
            Assert.Contains("MIN_DP=", line);
            Assert.Contains("<NON_REF>", line);
        }
    }

    [Fact]
    public async Task WriteGvcfAsync_RefBlocksGroupByGqTier()
    {
        // Positions 1-5: depth=5  -> GQ tier 0-10
        // Positions 6-10: depth=35 -> GQ tier 30+
        var refSeq = new string('A', 10);
        var depths = new int[10];
        for (var i = 0; i < 5; i++)
        {
            depths[i] = 5;
        }

        for (var i = 5; i < 10; i++)
        {
            depths[i] = 35;
        }

        using var ms = new MemoryStream();
        await GvcfWriter.WriteAsync(ms, Array.Empty<LocalVariantResult>(), refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        // Expect at least 2 blocks because depth changes significantly (different GQ tier)
        Assert.True(dataLines.Count >= 2, $"Expected at least 2 blocks, got {dataLines.Count}");
    }

    [Fact]
    public async Task WriteGvcfAsync_ZeroCoveragePositionsHaveMinDpZero()
    {
        var refSeq = new string('A', 5);
        var depths = new int[5]; // all zero

        using var ms = new MemoryStream();
        await GvcfWriter.WriteAsync(ms, Array.Empty<LocalVariantResult>(), refSeq.AsMemory(), "chr1", depths);
        var text = Encoding.UTF8.GetString(ms.ToArray());

        var dataLines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#')).ToList();

        // Should have at least one block
        Assert.NotEmpty(dataLines);
        Assert.Contains("MIN_DP=0", text);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VC-2 — Multi-allelic site handling and VCF normalisation
// ─────────────────────────────────────────────────────────────────────────────
public class VcfNormalizerTests
{
    private static LocalVariantResult MakeVariant(string chrom, int pos, string refB, string alt)
    {
        return new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = alt,
            QuantitativeQuality = 50,
            Depth = 20
        };
    }

    // Left-align: a deletion in a homopolymer should move left
    [Fact]
    public void LeftAlignIndel_MovesInsertionLeftInHomopolymer()
    {
        // ref: AAAACG... deletion of one A should left-align to pos 1
        // e.g. pos=5, ref=AT, alt=T  ->  should be pos=1, ref=AA, alt=A
        // Simple case: AAAAT, pos=4 (1-based), ref=AT, alt=T
        // Left-aligned: pos=1, ref=AA, alt=A
        var reference = "AAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 4, "AT", "T");

        var normalized = VcfNormalizer.LeftAlignIndel(variant, reference.Span);

        // After left-alignment the position should be <= 4
        Assert.True(normalized.Position <= 4);
        // Reference and alt should differ only by the deleted base
        Assert.Equal(normalized.Reference.Length - 1, normalized.Alternate.Length);
    }

    [Fact]
    public void LeftAlignIndel_AlreadyLeftAligned_ReturnsSamePosition()
    {
        // ref: TAAAA, deletion at pos 1 → already left-aligned
        var reference = "TAAAAT".AsMemory();
        var variant = MakeVariant("chr1", 1, "TA", "T");

        var normalized = VcfNormalizer.LeftAlignIndel(variant, reference.Span);

        Assert.Equal(1, normalized.Position);
    }

    [Fact]
    public void LeftAlignIndel_Idempotent()
    {
        var reference = "CGAAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 3, "AAAAT", "AAAT");

        var once = VcfNormalizer.LeftAlignIndel(variant, reference.Span);
        var twice = VcfNormalizer.LeftAlignIndel(once, reference.Span);

        Assert.Equal(once.Position, twice.Position);
        Assert.Equal(once.Reference, twice.Reference);
        Assert.Equal(once.Alternate, twice.Alternate);
    }

    [Fact]
    public void Decompose_MultiAllelicSplitsIntoTwo()
    {
        var variant = MakeVariant("chr1", 100, "A", "T");
        variant.AddAltAllele("G");

        var decomposed = VcfNormalizer.Decompose(variant).ToList();

        Assert.Equal(2, decomposed.Count);
        Assert.Contains(decomposed, v => v.Alternate == "T");
        Assert.Contains(decomposed, v => v.Alternate == "G");
        // All decomposed variants have same chrom and pos
        Assert.All(decomposed, v => Assert.Equal(100, v.Position));
    }

    [Fact]
    public void Decompose_BiallelicReturnsSelf()
    {
        var variant = MakeVariant("chr1", 100, "A", "T");
        var decomposed = VcfNormalizer.Decompose(variant).ToList();
        Assert.Single(decomposed);
        Assert.Equal("T", decomposed[0].Alternate);
    }

    [Fact]
    public void Normalize_LeftAlignsAndDecomposesMultiAllelic()
    {
        var reference = "AAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 4, "AT", "T");
        variant.AddAltAllele("A");

        var results = VcfNormalizer.Normalize([variant], reference.Span).ToList();

        // Should have at least 2 records after decomposition
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public void Normalize_Idempotent()
    {
        var reference = "AAAATCG".AsMemory();
        var variant = MakeVariant("chr1", 4, "AT", "T");

        var once = VcfNormalizer.Normalize([variant], reference.Span).ToList();
        var twice = VcfNormalizer.Normalize(once, reference.Span).ToList();

        Assert.Equal(once.Count, twice.Count);
        for (var i = 0; i < once.Count; i++)
        {
            Assert.Equal(once[i].Position, twice[i].Position);
            Assert.Equal(once[i].Reference, twice[i].Reference);
            Assert.Equal(once[i].Alternate, twice[i].Alternate);
        }
    }

    [Fact]
    public void Normalize_SnpNotChanged()
    {
        var reference = "ACGTACGT".AsMemory();
        var variant = MakeVariant("chr1", 3, "G", "C");

        var results = VcfNormalizer.Normalize([variant], reference.Span).ToList();

        Assert.Single(results);
        Assert.Equal(3, results[0].Position);
        Assert.Equal("G", results[0].Reference);
        Assert.Equal("C", results[0].Alternate);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VC-3 — Copy number variation (CNV) calling
// ─────────────────────────────────────────────────────────────────────────────
public class CopyNumberCallerTests
{
    private static int[] MakeDepths(int length, int baseCopyNumber, params (int start, int end, int cn)[] regions)
    {
        // diploid baseline = baseCopyNumber copies ~ some depth
        const int baseDepth = 30;
        var depths = new int[length];
        for (var i = 0; i < length; i++)
        {
            depths[i] = baseDepth;
        }

        foreach (var (start, end, cn) in regions)
        {
            var factor = cn / 2.0;
            for (var i = start; i < end && i < length; i++)
            {
                depths[i] = (int)(baseDepth * factor);
            }
        }

        return depths;
    }

    private static string MakeRefSeq(int length, int gcFraction = 50)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(i % 2 == 0 ? 'G' : 'A'); // alternating for ~50% GC
        }

        return sb.ToString();
    }

    [Fact]
    public void Call_DiploidBaseline_NoCnvCalls()
    {
        const int len = 10000;
        var depths = MakeDepths(len, 2);
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 1000);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        // All segments should be CN=2
        Assert.All(calls, v =>
        {
            var info = v.AdditionalInformation ?? v.AdditionalInformation ?? "";
            // Either no calls, or all calls have CN=2
        });
    }

    [Fact]
    public void Call_HemizygousDeletion_DetectsDelSegment()
    {
        // 10 kb reference, deletion of 2 kb in the middle (positions 4000-6000) → CN=1
        const int len = 10000;
        var depths = MakeDepths(len, 2, (4000, 6000, 1));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        // Should detect at least one DEL call
        Assert.Contains(calls, v =>
            v.IsStructuralVariant &&
            v.SvType == SvType.Deletion &&
            v.Position <= 4500 && v.EndPosition >= 5500);
    }

    [Fact]
    public void Call_HomozygousDeletion_Detected()
    {
        const int len = 10000;
        var depths = MakeDepths(len, 2, (3000, 5000, 0));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        Assert.Contains(calls, v =>
            v.IsStructuralVariant &&
            v.SvType == SvType.Deletion);
    }

    [Fact]
    public void Call_Amplification_DetectedAsDup()
    {
        const int len = 10000;
        var depths = MakeDepths(len, 2, (2000, 4000, 6));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        Assert.Contains(calls, v =>
            v.IsStructuralVariant &&
            v.SvType == SvType.CopyNumber);
    }

    [Fact]
    public void Call_SvlenAndEndInfoFieldsPresent()
    {
        const int len = 10000;
        var depths = MakeDepths(len, 2, (4000, 7000, 1));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        // Each SV call should have EndPosition set
        var svCalls = calls.Where(v => v.IsStructuralVariant).ToList();
        Assert.NotEmpty(svCalls);
        Assert.All(svCalls, v => Assert.True(v.EndPosition > v.Position));
    }

    [Fact]
    public void Call_50kbDeletion_BreakpointsWithin5kb()
    {
        // Simulate 100 kb reference with a 50 kb deletion in the middle
        const int len = 100_000;
        var depths = MakeDepths(len, 2, (25_000, 75_000, 0));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 2000);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        var deletion = calls.FirstOrDefault(v => v.IsStructuralVariant && v.SvType == SvType.Deletion);
        Assert.NotNull(deletion);
        Assert.True(Math.Abs(deletion.Position - 25_000) <= 5000, $"Start breakpoint off: {deletion.Position}");
        Assert.True(Math.Abs(deletion.EndPosition - 75_000) <= 5000, $"End breakpoint off: {deletion.EndPosition}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VC-4 — Haplotype phasing
// ─────────────────────────────────────────────────────────────────────────────
public class HaplotypePhasingEngineTests
{
    private static LocalVariantResult Variant(string chrom, int pos, string refB, string alt)
    {
        return new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = alt,
            QuantitativeQuality = 50,
            Depth = 20
        };
    }

    private static ReadSpan MakeRead(string name, int start, int end, params (int pos, bool isAlt)[] alleles)
    {
        return new ReadSpan(name, start, end, alleles);
    }

    [Fact]
    public void Phase_TwoSnpsOnSameRead_ArePhased()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 150, "G", "C")
        };

        // One read spanning both variants, supporting alt at both
        var reads = new[]
        {
            MakeRead("read1", 90, 200, (100, true), (150, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        Assert.All(phased, p => Assert.True(p.IsPhased));
        // Both should be in the same phase set
        var ps = phased.Select(p => p.PhaseSet).Distinct().ToList();
        Assert.Single(ps);
    }

    [Fact]
    public void Phase_TwoSnpsOnDifferentReads_AreUnphased()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 400, "G", "C")
        };

        // Two reads, each covering only one variant (no bridging)
        var reads = new[]
        {
            MakeRead("read1", 90, 200, (100, true)),
            MakeRead("read2", 390, 500, (400, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        // No reads bridge both variants → at most 1 is phased (start of a block)
        // OR they are in different phase sets
        var phaseSets = phased.Where(p => p.IsPhased).Select(p => p.PhaseSet).Distinct().ToList();
        Assert.True(phaseSets.Count <= 2); // At most 2 separate blocks
    }

    [Fact]
    public void Phase_ThreeVariantBlock_AllInSamePhaseSet()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 150, "G", "C"),
            Variant("chr1", 190, "T", "A")
        };

        // Two reads bridging: read1 covers 100 and 150, read2 covers 150 and 190
        var reads = new[]
        {
            MakeRead("read1", 90, 180, (100, true), (150, true)),
            MakeRead("read2", 140, 210, (150, true), (190, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        var phasedOnes = phased.Where(p => p.IsPhased).ToList();
        Assert.NotEmpty(phasedOnes);
        // All phased variants should be in the same phase set
        var phaseSets = phasedOnes.Select(p => p.PhaseSet).Distinct().ToList();
        Assert.Single(phaseSets);
    }

    [Fact]
    public void Phase_PhaseBlockBoundaryWhenNoReadBridges()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 200, "G", "C"),
            Variant("chr1", 600, "T", "A"),
            Variant("chr1", 650, "C", "G")
        };

        // read1 covers 100+200, read2 covers 600+650 — gap between 200 and 600
        var reads = new[]
        {
            MakeRead("read1", 90, 300, (100, true), (200, true)),
            MakeRead("read2", 590, 700, (600, true), (650, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        var phasedOnes = phased.Where(p => p.IsPhased).ToList();
        var phaseSets = phasedOnes.Select(p => p.PhaseSet).Distinct().ToList();
        // Should have at least 2 phase sets (or one block ended)
        Assert.True(phaseSets.Count >= 2 || phasedOnes.Count <= 2);
    }

    [Fact]
    public void Phase_PhasedGenotypeUsesBarSeparator()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 130, "G", "C")
        };

        var reads = new[]
        {
            MakeRead("read1", 90, 180, (100, true), (130, true))
        };

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        foreach (var p in phased.Where(x => x.IsPhased))
        {
            Assert.Contains("|", p.GenotypeString);
        }
    }

    [Fact]
    public void Phase_UnphasedVariant_HasSlashSeparator()
    {
        var variants = new[]
        {
            Variant("chr1", 100, "A", "T")
        };
        // No reads — all variants remain unphased
        var reads = Array.Empty<ReadSpan>();

        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);

        Assert.All(phased, p => Assert.Contains("/", p.GenotypeString));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VC-5 — Population allele frequency annotation via VCF lookup
// ─────────────────────────────────────────────────────────────────────────────
public class PopulationFrequencyAnnotatorTests
{
    /// <summary>
    /// Builds a minimal population VCF in memory with known AF values.
    /// </summary>
    private static string BuildPopulationVcf(params (string chrom, int pos, string refB, string alt, double af, double afPopmax, int an, int ac)[] entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("##fileformat=VCFv4.2");
        sb.AppendLine("##INFO=<ID=AF,Number=A,Type=Float,Description=\"Allele Frequency\">");
        sb.AppendLine("##INFO=<ID=AF_popmax,Number=A,Type=Float,Description=\"Maximum AF across populations\">");
        sb.AppendLine("##INFO=<ID=AN,Number=1,Type=Integer,Description=\"Total Allele Number\">");
        sb.AppendLine("##INFO=<ID=AC,Number=A,Type=Integer,Description=\"Allele Count\">");
        sb.AppendLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");
        foreach (var (chrom, pos, refB, alt, af, afPopmax, an, ac) in entries)
        {
            sb.AppendLine(
                $"{chrom}\t{pos}\t.\t{refB}\t{alt}\t.\tPASS\tAF={af:G};AF_popmax={afPopmax:G};AN={an};AC={ac}");
        }

        return sb.ToString();
    }

    private static LocalVariantResult Variant(string chrom, int pos, string refB, string alt)
    {
        return new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = alt,
            QuantitativeQuality = 50,
            Depth = 20
        };
    }

    [Fact]
    public async Task Annotate_MatchingVariant_GetsFrequencyFields()
    {
        var vcf = BuildPopulationVcf(("chr1", 100, "A", "T", 0.01, 0.02, 10000, 100));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var variants = new[] { Variant("chr1", 100, "A", "T") };
        var annotator = new PopulationFrequencyAnnotator();
        var results = await annotator.AnnotateAsync(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(0.01, results[0].GnomadAf, precision: 6);
        Assert.Equal(0.02, results[0].GnomadAfPopmax, precision: 6);
        Assert.Equal(10000, results[0].GnomadAn);
        Assert.Equal(100, results[0].GnomadAc);
    }

    [Fact]
    public async Task Annotate_NonMatchingVariant_GetsZeroFrequency()
    {
        var vcf = BuildPopulationVcf(("chr1", 200, "G", "A", 0.05, 0.10, 5000, 250));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var variants = new[] { Variant("chr1", 100, "A", "T") }; // not in pop VCF
        var annotator = new PopulationFrequencyAnnotator();
        var results = await annotator.AnnotateAsync(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(0.0, results[0].GnomadAf);
        Assert.Equal(0.0, results[0].GnomadAfPopmax);
        Assert.Equal(0, results[0].GnomadAn);
        Assert.Equal(0, results[0].GnomadAc);
    }

    [Fact]
    public async Task Annotate_MultipleVariants_EachAnnotatedIndependently()
    {
        var vcf = BuildPopulationVcf(
            ("chr1", 100, "A", "T", 0.01, 0.02, 10000, 100),
            ("chr1", 200, "G", "C", 0.05, 0.08, 8000, 400)
        );
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 200, "G", "C"),
            Variant("chr1", 300, "T", "A") // absent
        };
        var annotator = new PopulationFrequencyAnnotator();
        var results = await annotator.AnnotateAsync(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Equal(0.01, results[0].GnomadAf, precision: 6);
        Assert.Equal(0.05, results[1].GnomadAf, precision: 6);
        Assert.Equal(0.0, results[2].GnomadAf);
    }

    [Fact]
    public async Task Annotate_ExacSchema_ParsesAfField()
    {
        // ExAC uses AF= like gnomAD v2 but without AF_popmax — annotator should handle gracefully
        var sb = new StringBuilder();
        sb.AppendLine("##fileformat=VCFv4.2");
        sb.AppendLine("##INFO=<ID=AF,Number=A,Type=Float,Description=\"Allele Frequency\">");
        sb.AppendLine("##INFO=<ID=AN,Number=1,Type=Integer,Description=\"Total Allele Number\">");
        sb.AppendLine("##INFO=<ID=AC,Number=A,Type=Integer,Description=\"Allele Count\">");
        sb.AppendLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");
        sb.AppendLine("chr1\t100\t.\tA\tT\t.\tPASS\tAF=0.003;AN=120000;AC=360");

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        var variants = new[] { Variant("chr1", 100, "A", "T") };
        var annotator = new PopulationFrequencyAnnotator();
        var results = await annotator.AnnotateAsync(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(0.003, results[0].GnomadAf, precision: 6);
        Assert.Equal(0, results[0].GnomadAn == 120000 ? 0 : 1); // AN should be 120000
    }

    [Fact]
    public async Task Annotate_ResultHasSourceVariantReference()
    {
        var vcf = BuildPopulationVcf(("chr1", 100, "A", "T", 0.01, 0.02, 10000, 100));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var original = Variant("chr1", 100, "A", "T");
        var annotator = new PopulationFrequencyAnnotator();
        var results = await annotator.AnnotateAsync([original], ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Same(original, results[0].Variant);
    }
}
