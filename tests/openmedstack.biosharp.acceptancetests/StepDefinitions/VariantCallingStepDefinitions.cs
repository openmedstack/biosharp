using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.AcceptanceTests.StepDefinitions;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Calculations.Alignment;
using Calculations.DeBruijn;
using Reqnroll;
using Xunit;

[Binding]
public class VariantCallingStepDefinitions
{
    private readonly ScenarioContext _ctx;

    public VariantCallingStepDefinitions(ScenarioContext ctx) => _ctx = ctx;

    // ── VC-1: gVCF output ────────────────────────────────────────────────────

    [Given("I have a reference region of known length and a set of variants")]
    public void GivenReferenceRegionAndVariants()
    {
        const string refSeq = "ACGTACGTACGTACGTACGTACGT";
        var variants = new[]
        {
            new LocalVariantResult { Chromosome = "chr1", Position = 5, Reference = "A", Alternate = "G", QuantitativeQuality = 30, Depth = 10 },
            new LocalVariantResult { Chromosome = "chr1", Position = 15, Reference = "C", Alternate = "T", QuantitativeQuality = 40, Depth = 20 }
        };
        var depths = Enumerable.Repeat(10, refSeq.Length).ToArray();
        _ctx["gvcfRef"] = refSeq;
        _ctx["gvcfVariants"] = variants;
        _ctx["gvcfDepths"] = depths;
    }

    [When("I write gVCF output covering the region")]
    public async Task WhenWriteGvcf()
    {
        var refSeq = (string)_ctx["gvcfRef"];
        var variants = (LocalVariantResult[])_ctx["gvcfVariants"];
        var depths = (int[])_ctx["gvcfDepths"];
        var stream = new MemoryStream();
        await GvcfWriter.Write(stream, variants, refSeq.AsMemory(), "chr1", depths);
        stream.Position = 0;
        _ctx["gvcfContent"] = Encoding.UTF8.GetString(stream.ToArray());
    }

    [Then("the output should contain NON_REF symbolic allele records for reference blocks")]
    public void ThenGvcfHasNonRef()
    {
        var content = (string)_ctx["gvcfContent"];
        Assert.Contains("<NON_REF>", content);
    }

    [Then("the output should contain the END INFO field in reference block records")]
    public void ThenGvcfHasEndInfo()
    {
        var content = (string)_ctx["gvcfContent"];
        Assert.Contains("END=", content);
    }

    [Then("variant positions should be emitted as standard variant records")]
    public void ThenGvcfHasVariantRecords()
    {
        var content = (string)_ctx["gvcfContent"];
        var dataLines = content.Split('\n')
            .Where(l => !l.StartsWith('#') && !string.IsNullOrWhiteSpace(l))
            .ToList();
        Assert.NotEmpty(dataLines);
    }

    // ── VC-2: VCF Normalisation ───────────────────────────────────────────────

    [Given("I have a deletion variant that is right-aligned in a homopolymer region")]
    public void GivenRightAlignedDeletion()
    {
        // Reference: AAAAAACGT, deletion removes last A before CGT
        // Right-aligned: pos=6, REF=AC, ALT=C (or similar)
        // Left-aligned should be at pos=1
        const string refSeq = "AAAAAACGT";
        // Right-aligned deletion: pos=6, REF="AC", ALT="A" (deletes one A at position 6, but should left-align)
        // Actually: deletion of an A in a run AAAAAA: pos=6 ref="AA" alt="A" is right-aligned
        // Left-aligned should be pos=1 ref="AA" alt="A"
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 6, // 1-based
            Reference = "AA",
            Alternate = "A",
            QuantitativeQuality = 30
        };
        _ctx["rightAlignedVariant"] = variant;
        _ctx["normRefSeq"] = refSeq;
    }

    [When("I normalize the variant with the reference sequence")]
    public void WhenNormalizeVariant()
    {
        var variant = (LocalVariantResult)_ctx["rightAlignedVariant"];
        var refSeq = (string)_ctx["normRefSeq"];
        var normalized = VcfNormalizer.Normalize(
            [variant],
            refSeq.AsSpan()).ToList();
        _ctx["normalizedVariants"] = normalized;
    }

    [Then("the normalized variant should be at the leftmost position")]
    public void ThenNormalizedAtLeftmost()
    {
        var normalized = (List<LocalVariantResult>)_ctx["normalizedVariants"];
        Assert.NotEmpty(normalized);
        // Left-aligned should be at position 1
        Assert.Equal(1, normalized[0].Position);
    }

    [Given("I have a multi-allelic variant with two alternate alleles")]
    public void GivenMultiAllelicVariant()
    {
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 100,
            Reference = "A",
            Alternate = "G",
            QuantitativeQuality = 30
        };
        variant.AddAltAllele("T"); // makes it multi-allelic
        _ctx["multiAllelicVariant"] = variant;
        _ctx["normRefSeq"] = $"ACGTACGT{new string('N', 200)}";
    }

    [When("I normalize the variant")]
    public void WhenNormalizeMultiAllelic()
    {
        var variant = (LocalVariantResult)_ctx["multiAllelicVariant"];
        var refSeq = (string)_ctx["normRefSeq"];
        var normalized = VcfNormalizer.Normalize(
            [variant],
            refSeq.AsSpan()).ToList();
        _ctx["normalizedVariants"] = normalized;
    }

    [Then("I should receive two separate biallelic variant records")]
    public void ThenTwoBiallelicRecords()
    {
        var normalized = (List<LocalVariantResult>)_ctx["normalizedVariants"];
        Assert.Equal(2, normalized.Count);
        Assert.All(normalized, v => Assert.DoesNotContain(",", v.Alternate));
    }

    [Given("I have an already-normalized variant")]
    public void GivenNormalizedVariant()
    {
        var variant = new LocalVariantResult
        {
            Chromosome = "chr1",
            Position = 1,
            Reference = "AT",
            Alternate = "A",
            QuantitativeQuality = 30
        };
        _ctx["alreadyNormalized"] = variant;
        _ctx["normRefSeq"] = "ATCGATCG";
    }

    [When("I normalize it twice")]
    public void WhenNormalizeTwice()
    {
        var variant = (LocalVariantResult)_ctx["alreadyNormalized"];
        var refSeq = (string)_ctx["normRefSeq"];
        var first = VcfNormalizer.Normalize([variant], refSeq.AsSpan()).ToList();
        var second = VcfNormalizer.Normalize(first, refSeq.AsSpan()).ToList();
        _ctx["firstNorm"] = first;
        _ctx["secondNorm"] = second;
    }

    [Then("both outputs should have the same position and alleles")]
    public void ThenIdempotentNormalization()
    {
        var first = (List<LocalVariantResult>)_ctx["firstNorm"];
        var second = (List<LocalVariantResult>)_ctx["secondNorm"];
        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Position, second[i].Position);
            Assert.Equal(first[i].Reference, second[i].Reference);
            Assert.Equal(first[i].Alternate, second[i].Alternate);
        }
    }

    // ── VC-3: CNV Calling ────────────────────────────────────────────────────

    [Given(@"I have a depth profile with a (\d+)-window region at (\d+) percent of baseline depth")]
    public void GivenDepthProfileWithLowRegion(int lowWindowCount, int percentBaseline)
    {
        const int windowSize = 100;
        // Use 4x the lowWindowCount as total windows so baseline windows dominate the median
        var totalWindows = Math.Max(lowWindowCount * 4, 200);
        const int baselineDepth = 30;
        var lowDepth = (int)(baselineDepth * percentBaseline / 100.0);
        var refLen = totalWindows * windowSize;
        var refSeq = new string('A', refLen);
        var depths = new int[refLen];

        // Low-depth region in the middle
        var startWindow = totalWindows / 4;
        for (var i = 0; i < refLen; i++)
        {
            var windowIdx = i / windowSize;
            depths[i] = windowIdx >= startWindow && windowIdx < startWindow + lowWindowCount
                ? lowDepth
                : baselineDepth;
        }

        _ctx["cnvRefSeq"] = refSeq;
        _ctx["cnvDepths"] = depths;
        _ctx["cnvChrom"] = "chr1";
    }

    [When("I run the copy number caller with a deletion threshold of (.+)")]
    public void WhenRunCopyNumberCallerDeletion(double deletionThreshold)
    {
        var refSeq = (string)_ctx["cnvRefSeq"];
        var depths = (int[])_ctx["cnvDepths"];
        var chrom = (string)_ctx["cnvChrom"];
        var caller = new CopyNumberCaller(windowSize: 100, deletionThreshold: deletionThreshold, duplicationThreshold: 1.5, minWindowsPerSegment: 2);
        var calls = caller.Call(refSeq.AsMemory(), depths, chrom).ToList();
        _ctx["cnvCalls"] = calls;
    }

    [Then("a DEL structural variant should be reported spanning the low-depth region")]
    public void ThenDelCallReported()
    {
        var calls = (List<LocalVariantResult>)_ctx["cnvCalls"];
        Assert.Contains(calls, v =>
            v is { IsStructuralVariant: true, SvType: SvType.Deletion });
    }

    [When("I run the copy number caller with a duplication threshold of (.+)")]
    public void WhenRunCopyNumberCallerDuplication(double duplicationThreshold)
    {
        var refSeq = (string)_ctx["cnvRefSeq"];
        var depths = (int[])_ctx["cnvDepths"];
        var chrom = (string)_ctx["cnvChrom"];
        var caller = new CopyNumberCaller(windowSize: 100, deletionThreshold: 0.6, duplicationThreshold: duplicationThreshold, minWindowsPerSegment: 2);
        var calls = caller.Call(refSeq.AsMemory(), depths, chrom).ToList();
        _ctx["cnvCalls"] = calls;
    }

    [Then("a DUP structural variant should be reported spanning the high-depth region")]
    public void ThenDupCallReported()
    {
        var calls = (List<LocalVariantResult>)_ctx["cnvCalls"];
        Assert.Contains(calls, v =>
            v is { IsStructuralVariant: true, SvType: SvType.CopyNumber });
    }

    // ── VC-4: Haplotype Phasing ───────────────────────────────────────────────

    [Given("I have two variants at adjacent positions and a read spanning both positions supporting both alt alleles")]
    public void GivenTwoVariantsOnSameRead()
    {
        var variants = new List<LocalVariantResult>
        {
            new() { Chromosome = "chr1", Position = 10, Reference = "A", Alternate = "G" },
            new() { Chromosome = "chr1", Position = 20, Reference = "C", Alternate = "T" }
        };
        var reads = new List<ReadSpan>
        {
            new ReadSpan("read1", start: 1, end: 50,
                (10, true),  // supports alt at pos 10
                (20, true))  // supports alt at pos 20
        };
        _ctx["phasingVariants"] = variants;
        _ctx["phasingReads"] = reads;
    }

    [When("I run haplotype phasing")]
    public void WhenRunHaplotypePhasing()
    {
        var variants = (List<LocalVariantResult>)_ctx["phasingVariants"];
        var reads = (List<ReadSpan>)_ctx["phasingReads"];
        var engine = new HaplotypePhasingEngine();
        var phased = engine.Phase(variants, reads);
        _ctx["phasedVariants"] = phased;
    }

    [Then("both variants should be phased with a shared phase set")]
    public void ThenBothVariantsPhased()
    {
        var phased = (PhasedVariant[])_ctx["phasedVariants"];
        Assert.Equal(2, phased.Length);
        Assert.All(phased, v => Assert.True(v.IsPhased, $"Variant at pos {v.Variant.Position} should be phased"));
        Assert.Equal(phased[0].PhaseSet, phased[1].PhaseSet);
    }

    [Then("the genotype strings should use the pipe separator")]
    public void ThenGenotypeStringsPipe()
    {
        var phased = (PhasedVariant[])_ctx["phasedVariants"];
        Assert.All(phased, v => Assert.Contains("|", v.GenotypeString));
    }

    [Given("I have two variants at distant positions with no read spanning both")]
    public void GivenTwoVariantsNoBridgingRead()
    {
        var variants = new List<LocalVariantResult>
        {
            new() { Chromosome = "chr1", Position = 10, Reference = "A", Alternate = "G" },
            new() { Chromosome = "chr1", Position = 5000, Reference = "C", Alternate = "T" }
        };
        var reads = new List<ReadSpan>
        {
            new ReadSpan("readA", start: 1, end: 50, (10, true)),   // only covers pos 10
            new ReadSpan("readB", start: 4980, end: 5050, (5000, true)) // only covers pos 5000
        };
        _ctx["phasingVariants"] = variants;
        _ctx["phasingReads"] = reads;
    }

    [Then("both variants should be unphased with slash separator in genotype")]
    public void ThenVariantsUnphased()
    {
        var phased = (PhasedVariant[])_ctx["phasedVariants"];
        Assert.All(phased, v => Assert.Contains("/", v.GenotypeString));
    }

    // ── VC-5: Population Frequency Annotation ────────────────────────────────

    [Given("I have a population VCF database with a known variant at frequency (.+)")]
    public void GivenPopulationVcfWithKnownVariant(double frequency)
    {
        var vcfContent = $"##fileformat=VCFv4.2\n#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO\nchr1\t100\trs1\tA\tG\t.\tPASS\tAF={frequency};AF_popmax={frequency};AN=1000;AC=50\n";
        _ctx["popVcfStream"] = new MemoryStream(Encoding.UTF8.GetBytes(vcfContent));
        _ctx["popFrequency"] = frequency;
    }

    [Given("I have a variant matching that entry")]
    public void GivenMatchingVariant()
    {
        _ctx["popVariant"] = new LocalVariantResult
        {
            Chromosome = "chr1", Position = 100, Reference = "A", Alternate = "G"
        };
    }

    [When("I annotate the variant with population frequencies")]
    public async Task WhenAnnotateWithPopFreq()
    {
        var variant = (LocalVariantResult)_ctx["popVariant"];
        var stream = (MemoryStream)_ctx["popVcfStream"];
        stream.Position = 0;
        var results = new List<PopulationFrequencyAnnotation>();
        await foreach (var ann in PopulationFrequencyAnnotator.Annotate([variant], stream, CancellationToken.None))
        {
            results.Add(ann);
        }

        _ctx["popFreqAnnotations"] = results;
    }

    [Then("the annotation should have an AF value of (.+)")]
    public void ThenAfValue(double expectedAf)
    {
        var annotations = (List<PopulationFrequencyAnnotation>)_ctx["popFreqAnnotations"];
        Assert.Single(annotations);
        Assert.Equal(expectedAf, annotations[0].GnomadAf, 3);
    }

    [Given("I have a population VCF database without a particular variant")]
    public void GivenPopulationVcfWithoutVariant()
    {
        var vcfContent = "##fileformat=VCFv4.2\n#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO\nchr1\t999\trs999\tC\tT\t.\tPASS\tAF=0.001;AN=1000;AC=1\n";
        _ctx["popVcfStream"] = new MemoryStream(Encoding.UTF8.GetBytes(vcfContent));
    }

    [Given("I have that variant")]
    public void GivenAbsentVariant()
    {
        _ctx["popVariant"] = new LocalVariantResult
        {
            Chromosome = "chr1", Position = 100, Reference = "A", Alternate = "G"
        };
    }
}
