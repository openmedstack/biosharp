# End-to-End Pipeline: Option C — Somatic Calling (Tumor-Normal Pair)

## Introduction

[Cancer genomics](somatic-variant-detection.md) requires distinguishing tumor-specific (somatic) mutations from inherited (germline) variations. 
BioSharp performs this comparison by building [De Bruijn graphs](de-bruijn-assembly.md) for both the tumor and normal samples, then using 
a [Bloom filter](bloom-filter.md) approach to identify tumor-specific k-mers.

The normal sample's k-mers are added to a Bloom filter, representing all k-mers present in the germline (the patient's 
inherited variation). A [bubble](bubble-tip-detection.md) in the tumor graph whose alternative path is absent from the normal filter is 
classified as somatic — it represents a mutation found in the tumor that is not present in the patient's normal genome. 
This tumor-normal comparison workflow is the foundation for [cohort analysis](cohort-variant-calling.md) when extended to multiple samples.

Quality filters ensure biological rigor: the tumor sample must exceed both a minimum allele fraction threshold (30% by 
default, meaning at least 30% of reads must show the mutation) and a minimum coverage threshold (5x by default, meaning 
at least 5 reads must support the variant).

Each somatic variant record includes detailed information: position, reference and alternate alleles, tumor and normal 
coverage data, allele fraction, confidence scores, and quality metrics. This is the core output for downstream clinical 
interpretation or research analysis.

Somatic calling steps:
1. Build separate De Bruijn graphs for tumor and normal samples
2. Add both graphs to MultiSampleGraph
3. Build normal Bloom filter from normal sample k-mers
4. Run somatic detection on tumor graph against normal filter
5. Filter by allele fraction (30%) and coverage (5x) thresholds
6. Output somatic variants with tumor/normal coverage, allele fraction, confidence, and quality

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Build De Bruijn graphs for tumor and normal samples
var tumorGraph = new DeBruijnGraph(15, tumorReadsAsync);
var normalGraph = new DeBruijnGraph(15, normalReadsAsync);

// Build multi-sample graph to manage both samples
var multiGraph = new MultiSampleGraph(15);
multiGraph.AddSample("tumor", tumorGraph);
multiGraph.AddSample("normal", normalGraph);

// Create a normal Bloom filter (k-mers present in normal = germline, filter these out)
var normalFilter = await MultiSampleGraph.BuildNormalFilter(multiGraph, "normal");

// Detect somatic variants -- tumor-specific mutations
var somaticVariants = await SomaticVariantDetector.DetectSomaticVariantsAsync(
    tumorGraph: tumorGraph,
    normalFilter: normalFilter,
    reference: referenceSequence,
    chromosome: "chr17",
    refStart: 0,
    minAlleleFraction: 0.30,   // tumor must have >= 30% alt reads
    minTumorCoverage: 5        // tumor must have >= 5x coverage
);

foreach (var v in somaticVariants)
{
    Console.WriteLine($"Somatic variant: {v.Position} " +
         $"{v.Reference}>{v.Alternate} " +
         $"Tumor={v.TumorCoverage}x Normal={v.NormalCoverage}x " +
         $"AlleleFraction={v.TumorAlleleFraction:F2} " +
         $"Confidence={v.Confidence} Q={v.Quality}");
}
```
