# Alignment-Based Variant Calling Pipeline

## Introduction

Variant calling is one of the most critical steps in a genomic analysis pipeline: it converts alignment mismatches 
(differences between sequenced reads and the reference genome) into formal variant records (SNPs, indels, structural 
variants). These variant records are then merged across reads and written to VCF format, flowing into downstream steps 
like [genotype calling](genotype-calling.md) for zygosity determination and [variant annotation](variant-annotation-quick-start.md) for biological interpretation. 
In a typical lab workflow, variant calling is the bridge between raw sequencing alignment data and the actionable 
variant list used for clinical or research analysis.

The pipeline follows a clear sequence: reads are aligned to a reference genome (after optional [repeat masking](e2e-option-f-repeat-masking.md) 
to eliminate ambiguous mapping regions), alignment results are parsed into atomic events (individual SNPs, insertions, 
deletions), consecutive similar events are grouped into multi-base variants (as required by VCF specifications), 
quality scores are computed, and finally calls from multiple reads are merged into a consensus. This merged output is 
the foundation for the downstream [variant annotation](variant-annotation-quick-start.md) step that assigns biological meaning to each call.

This approach contrasts with assembly-based variant calling, which builds [De Bruijn graphs](de-bruijn-assembly.md) directly from reads 
and detects [bubbles and tips](bubble-tip-detection.md) as graph features representing genetic variation. The alignment-based approach is 
simpler and faster, making it suitable for whole-genome and targeted resequencing analyses where a high-quality 
reference genome is available. For cases where no reference genome exists, the [de novo assembly](de-bruijn-assembly.md) workflow is the 
appropriate alternative.

If you are performing variant calling for the first time, this pipeline is the standard workflow: align reads to 
reference, extract variant calls using the principles documented in this page, merge them with [MergeVariants](./variant-calling-pipeline.md), 
determine [genotypes](genotype-calling.md) for each sample, and write the final VCF. For a complete end-to-end experience see 
[Option A](e2e-option-a-pipeline-orchestrator.md).

The core flow is:

**Alignment --> Event Parsing --> Variant Grouping --> Quality Scoring --> Output**

### Smith-Waterman Alignment per Read

Each read is aligned independently against the reference.
```csharp
var alignment = SmithWatermanAligner.Align(reference, read);
```

### Variant Calling

`VariantCaller.CallVariants()` parses the alignment into atomic events (SNPs, insertions, deletions), groups 
consecutive events of the same type, builds VCF-compliant alleles using the leftmost-anchor convention, and assigns 
Phred-scaled quality scores.

```csharp
var variants = VariantCaller.CallVariants(reference, alignment, minQuality: 30);

foreach (var v in variants)
{
    Console.WriteLine($"{v.Chromosome}:{v.Position} {v.Reference}>{v.Alternate}  Q={v.QuantitativeQuality}  DP={v.Depth}");
    Console.WriteLine($"  IsSubstitution={v.IsSubstitution}  IsInsertion={v.IsInsertion}  IsDeletion={v.IsDeletion}");
}
```

**Quality scoring** uses the alignment score divided by event count, with penalties for indels (-5) and homopolymer 
indels (-10). Scores are clamped to [10, 255].

**Homopolymer detection**: `VariantCaller.GetHomopolymerRun(refSeq, position)` returns the length of the run of 
identical bases at a given position. Homopolymer indels are penalized because they are common sequencing errors in 
platforms like Ion Torrent and Oxford Nanopore.

### Merging Variants from Multiple Reads

When multiple reads cover the same locus, `VariantCaller.MergeVariants()` consolidates them into a single variant per 
unique (chromosome, position, REF, ALT) key, taking the maximum quality and summing depth.

```csharp
var allVariants = new List<LocalVariantResult>();
// ... process reads, collect variants ...

var merged = VariantCaller.MergeVariants(allVariants.ToArray());
Console.WriteLine($"From {allVariants.Count} raw calls -> {merged.Length} merged variants");
```
