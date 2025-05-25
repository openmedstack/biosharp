# End-to-End Pipeline: Option B — Using Individual Components

## Introduction

For maximum flexibility and control, you can use the individual [BioSharp](e2e-option-a-pipeline-orchestrator.md) components directly instead of the 
pipeline orchestrator. This gives you fine-grained control over each processing step and allows you to insert custom 
logic between pipeline stages. The individual components — including the [Smith-Waterman aligner](e2e-option-a-pipeline-orchestrator.md), 
[VariantCaller](variant-calling-pipeline.md), [ReadRealigner](e2e-option-a-pipeline-orchestrator.md), and [De Bruijn graph](de-bruijn-assembly.md) SV detection — can be assembled into a custom 
[variant calling pipeline](variant-calling-pipeline.md).

The manual pipeline follows the same logical flow as the orchestrator (align -> call -> merge -> SV detect -> write 
VCF) but lets you process reads one at a time and make per-read decisions. You can add custom filtering or quality 
control between any step.

The key advantage is the ReadRealigner component: it takes reads with large soft-clipped segments (bases at the read 
end that didn't align), re-aligns them to a local reference window using indel-optimized parameters (lower gap open and 
gap extend penalties), and extracts additional variants that were missed by the initial alignment pass. This addresses 
the well-known problem where indel misalignments create cascading false SNP calls around the true indel site.

This approach is ideal for custom pipelines, research projects with unique workflows, or when you need the lowest level 
of control over each processing step.

Manual pipeline steps:
1. Load reference sequence from FASTA
2. Load reads from FASTQ
3. For each read: perform Smith-Waterman alignment and variant calling
4. For reads with soft-clips: realign and extract additional variants
5. Merge all variants from all reads
6. (Optional) De Bruijn graph SV detection for deeper discovery
7. Merge graph-discovered SVs with alignment-discovered variants
8. Write final VCF output

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;

// --- Step 1: Load reference ---
var faReader = new FastAReader();
Sequence reference = null!;
await foreach (var seq in faReader.Read("data/reference.fa"))
{
    reference = seq;
}

// --- Step 2: Load reads ---
var fqReader = new FastQReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
var allVariants = new List<LocalVariantResult>();
var alignments = new List<AlignmentResult>();

await foreach (var read in fqReader.Read("data/reads.fastq"))
{
     // Align each read
    var alignment = SmithWatermanAligner.Align(
        reference, read,
        matchScore: 2,
        mismatchPenalty: -3,
        gapOpenPenalty: -5,
        gapExtendPenalty: -2,
        minScore: 10
     );

    if (alignment == null) continue;
    alignments.Add(alignment);

     // Call variants from primary alignment
    var variants = VariantCaller.CallVariants(reference, alignment, minQuality: 30);
    foreach (var v in variants)
     {
        v.Chromosome = "chr22";
        allVariants.Add(v);
     }

     // Realigned soft-clips for SV discovery
    if (alignment.LeftSoftClip > 0 || alignment.RightSoftClip > 0)
     {
        var realigner = new ReadRealigner
         {
            MinClipFraction = 0.20f,
            MinClipSize = 20,
            LocalWindowSize = 200
         };

        var readSeq = read.GetData().Span.ToString();
        var readQual = read.GetQuality().Span.ToString();

        var realignResults = realigner.Realign(alignment, reference, "chr22", readSeq);

        foreach (var realign in realignResults)
         {
            if (realign.WasRealigned && realign.Alignment != null)
             {
                var realignVariants = VariantCaller.CallVariants(reference, realign.Alignment!, 30);
                foreach (var rv in realignVariants)
                 {
                    rv.Chromosome = "chr22";
                    if (realign.IsStructuralVariant)
                     {
                        rv.IsStructuralVariant = true;
                        rv.SvType = realign.SvType;
                     }
                    allVariants.Add(rv);
                 }
             }
         }
     }
}

// --- Step 3: Merge variants from all reads ---
var mergedVariants = VariantCaller.MergeVariants(allVariants.ToArray());

Console.WriteLine($"Merged {allVariants.Count} raw calls into {mergedVariants.Length} variants");

// --- Step 4: De Bruijn graph SV detection (optional, for deeper SV discovery) ---
var graphReads = alignments
     .Where(a => a != null)
     .Select(a =>
     {
        var idx = alignments.IndexOf(a!);
        return new Sequence($"r{idx}",
            reads[idx].GetData(),
            reads[idx].GetQuality());
     })
     .ToList();

// Convert to async enumerable for graph construction
var asyncGraphReads = AsyncEnumerableExtensions.ToAsyncEnumerable(graphReads);
var graph = new DeBruijnGraph(k: 15, asyncGraphReads);

var svAnalysis = await StructuralVariantDetector.AnalyzeGraph(
    graph,
    new string(reference.GetData().Span),
     "chr22",
     0
);

// Merge graph-discovered SVs with alignment-discovered variants
foreach (var sv in svAnalysis.Variants)
{
    sv.Chromosome = "chr22";
    allVariants.Add(sv);
}

var finalVariants = VariantCaller.MergeVariants(allVariants.ToArray());

// --- Step 5: Write VCF ---
await VcfWriter.WriteAsync(
    stream: new FileStream("output/variants.vcf", FileMode.Create),
    variants: finalVariants,
    chromosome: "chr22",
    chromLength: 50818468
);
```
