# End-to-End Pipeline: Option A — Using the Pipeline Orchestrator

## Introduction

The [VariantCallingPipeline](variant-calling-pipeline.md) is the highest-level API in BioSharp. It ties together every component of the 
analysis pipeline — loading reference and read data, [repeat masking](e2e-option-f-repeat-masking.md) for pre-processing, [alignment](e2e-option-b-individual-components.md), 
realignment for soft-clipped reads, [variant calling](variant-calling-pipeline.md) with [genotype calling](genotype-calling.md), graph-based [structural 
variant detection](structural-variant-detection.md) driven by [bubble and tip](bubble-tip-detection.md) analysis, and [VCF output](variant-annotation-quick-start.md) — into a single orchestrator 
that handles the entire workflow end-to-end.

The pipeline follows these steps: load the reference genome, configure alignment and calling parameters, input 
sequencing reads (BAM or FASTQ), perform alignment and variant calling for each read, optionally run graph-based 
structural variant detection, build the final variant results, and write VCF output.

Configuration options control every aspect of the pipeline: scoring parameters for alignment (match, mismatch, gap 
open, gap extend), quality thresholds for variant calling (minimum alignment score, minimum variant quality), and 
graph detection settings (k-mer size, graph window in base pairs, minimum coverage).

Metrics tracking provides visibility into each stage: how many reads were loaded, how many mapped, how many were 
aligned, how many variants were called, how many survived merging, and how many structural variants were discovered. 
This is essential for assessing run quality and troubleshooting pipeline performance.

This is the recommended approach for most use cases because it abstracts away the complexity of managing individual 
components while still providing full configurability for production analysis.

End-to-end steps:
1. Load reference sequence from FASTA file
2. Configure pipeline with scoring and quality parameters
3. Load reads from BAM or FASTQ
4. (Optional) Run graph-based SV detection on specific regions
5. Build final results with comprehensive metrics
6. Inspect results (variants, metrics, structural variants, genotypes)
7. Write output VCF file

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

// --- Step 1: Load the reference sequence ---
var faReader = new FastAReader();
Sequence? reference = null;
await foreach (var seq in faReader.Read("data/Homo_sapiens.GRCh38.dna.chromosome.22.fa"))
{
    if (seq.Id.Contains("chr22", StringComparison.OrdinalIgnoreCase))
     {
        reference = seq;
        break;
     }
}

// --- Step 2: Configure the pipeline ---
var pipeline = new VariantCallingPipeline(
    reference: reference!,
    chromosome: "chr22",
    options: new VariantCallingPipeline.PipelineOptions
     {
        MatchScore = 2,
        MismatchPenalty = -3,
        GapOpenPenalty = -5,
        GapExtendPenalty = -2,
        MinAlignmentScore = 10,
        MinVariantQuality = 30,
        EnableSoftClipRealignment = true,
        MinClipFraction = 0.20f,
        MinClipSize = 20,
        LocalWindowSize = 200,
        EnableGraphSvDetection = true,
        KmerSize = 15,
        GraphWindowBp = 500,
        MinGraphCoverage = 5
     }
);

// --- Step 3: Load reads (BAM or FASTQ) ---

// Option 1: BAM input
await pipeline.LoadBamAsync("data/mapt.NA12156.altex.bam");

// Option 2: FASTQ input
// await pipeline.LoadFastQAsync("data/ERR164409.fastq");

// --- Step 4: Run graph-based SV detection on specific regions ---
// (optional, requires loaded reads from step 3)
var svAnalysis = await pipeline.RunGraphAnalysisAsync(
    windowStart: 10000,
    windowEnd: 15000
);

// Or run full-genome graph analysis:
// var fullSv = await pipeline.RunFullGraphAnalysisAsync();

// --- Step 5: Build final results ---
var result = pipeline.BuildResult();

// --- Step 6: Inspect results ---
Console.WriteLine(result.Metrics);
// PipelineMetrics{reads=1500, mapped=1420, aligned=380, called=245, final=128, sv=3}

foreach (var variant in result.Variants)
{
    Console.WriteLine($"{variant.Chromosome}:{variant.Position} " +
         $"{variant.Reference}>{variant.Alternate} " +
         $"Q={variant.QuantitativeQuality} DP={variant.Depth}");

    if (variant.IsStructuralVariant)
     {
        Console.WriteLine($"  SV TYPE: {variant.SvType} " +
             $"END={variant.EndPosition} " +
             $"AssemblyInfo={variant.AssemblyInfo}");
     }

    if (variant.ClipInfo != null)
     {
        Console.WriteLine($"  Soft-clip: {variant.ClipInfo}");
     }

    if (variant.Genotype != null)
     {
        Console.WriteLine($"  Genotype: {variant.Genotype}");
     }
}

// --- Step 7: Write output VCF ---
await pipeline.WriteVcfAsync("output/variants.vcf", chromLength: 50818468L);

// (Optional: also write SAM for debugging)
// await pipeline.WriteSamAsync("output/aligned.sam");
```
