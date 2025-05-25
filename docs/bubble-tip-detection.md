# Bubble and Tip Detection

## Introduction

In a De Bruijn graph, genetic variation manifests as structural features: [bubbles and tips](bubble-tip-detection.md). Detecting these 
features is how graph-based variant calling identifies SNPs and indels — the primary variant types used downstream by 
[variant calling](variant-calling-pipeline.md) and [genotype calling](genotype-calling.md) pipelines. This graph-based approach complements the alignment-based 
[VariantCaller pipeline](variant-calling-pipeline.md) and is also the foundation for [structural variant detection](structural-variant-detection.md), where larger bubbles 
and tips represent inversions, insertions, and deletions.

A **bubble** is a divergence point where the graph forks into two or more paths, then reconverges at a shared endpoint. 
Each path through a bubble represents a different allele — SNP alleles or alternative insertion/deletion sequences. 
By comparing coverage on each path, you can estimate the allele frequency and call the variant with confidence. 
For larger variants (inversions, translocations, large indels), [StructuralVariantDetector](structural-variant-detection.md) classifies these 
features using additional soft-clip and re-alignment analysis.

A **tip** is a dead-end branch — a path that does not reconverge back into the main graph. Short tips (less than 2k 
bases) are typically sequencing errors: single-read artifacts that should be pruned. Long tips (2k+ bases) may 
represent genuine insertions — sequences present in the sample but absent from the reference.

BubbleFinder and TipFinder traverse the graph efficiently, using node degree information to identify branch points and 
convergence points. Their output feeds directly into [StructuralVariantDetector](structural-variant-detection.md) for larger variant 
classification, [SomaticVariantDetector](somatic-variant-detection.md) for tumor-specific mutation identification, and ultimately into [genotype 
calling](genotype-calling.md) to determine zygosity from coverage ratios.

Detecting bubbles and tips is how graph-based calling discovers variants — these are the [De Bruijn graph's](de-bruijn-assembly.md) way 
of "saying" there is genetic variation at a given position. The classified features are then consumed by downstream 
variant detection and [annotation](variant-annotation-quick-start.md) steps.

```csharp
var bubbles = await BubbleFinder.FindBubbles(graph, k: 15);

foreach (var bubble in bubbles)
{
    Console.WriteLine($"Bubble at node {bubble.StartNode} -> {bubble.EndNode}");
    Console.WriteLine($"   {bubble.Paths.Length} paths:");
    foreach (var path in bubble.Paths)
    {
        Console.WriteLine($"     {path.Sequence} (coverage: {path.Coverage})");
    }
}
```

**Tips** are dead-end branches — paths that end without converging back into the main graph. Short tips (<= 2k) are 
typically sequencing artefacts; long tips (> 2k) represent potential insertions not present in the reference.

```csharp
var tips = await TipFinder.FindTips(graph, longTipThreshold: 40); // 2*k where k=20

foreach (var tip in tips)
{
    Console.WriteLine($"Tip: {tip.Sequence} ({tip.Sequence.Length}bp) " +
        $"[{(tip.IsLongTip ? "Potential insertion" : "Sequencing artefact")}]]");
}
```
