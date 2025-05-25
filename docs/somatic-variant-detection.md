# Somatic Variant Detection (Tumor-Normal)

## Introduction

In cancer genomics, the goal is to find mutations that are unique to the tumor (somatic variants) while filtering out 
the patient's inherited germline variants [see cohort analysis](cohort-variant-calling.md) for population-level extension. This is done by 
comparing a tumor sample against a matched normal sample (often blood or healthy tissue from the same patient).

BioSharp performs this comparison graphically: both tumor and normal samples are assembled into 
[De Bruijn graphs](de-bruijn-assembly.md). A [Bloom filter](bloom-filter.md) is built from the normal sample's k-mers, representing all k-mers 
present in the germline. Any [bubble](bubble-tip-detection.md) in the tumor graph whose alternative path is absent from the normal filter 
is classified as somatic — it is a mutation found in the tumor that is not present in the patient's normal genome.

Quality filters ensure only biologically relevant variants are reported: the tumor sample must have a minimum allele 
fraction (how many reads support the variant, e.g. 30%) and minimum coverage (e.g. 5x) to confirm the variant is real 
and not a sequencing error.

Each somatic variant record includes both tumor and normal coverage data, the allele fraction, confidence scores, and 
the quality metric. This is the core output of a somatic calling pipeline and is used to identify driver mutations, 
therapeutic targets, and tumor-specific biomarkers.

The process involves:
1. Building De Bruijn graphs from both tumor and normal reads
2. Creating a normal Bloom filter (k-mers in normal = germline)
3. Scanning tumor graph bubbles against the normal filter
4. Filtering by tumor allele fraction and coverage thresholds
5. Reporting variant position, type, coverage in both samples

```csharp
// Build a Bloom filter from normal-sample k-mers
var normalBloom = await MultiSampleGraph.BuildNormalFilter(k: 15, normalGraph);

// Detect somatic variants
var somaticVariants = await SomaticVariantDetector.DetectSomaticVariantsAsync(
    tumorGraph: tumorGraph,
    normalFilter: normalBloom,
    reference: referenceSequence,
    chromosome: "chr17",
    refStart: 0,
    minAlleleFraction: 0.30,    // minimum alt fraction in tumor (default 30%)
    minTumorCoverage: 5         // minimum tumor coverage (default 5x)
);

foreach (var variant in somaticVariants)
{
    Console.WriteLine($"Somatic: {variant.Position} {variant.Reference}>{variant.Alternate} " +
        $"Tumor={variant.TumorCoverage}x Normal={variant.NormalCoverage}x " +
        $"AF={variant.TumorAlleleFraction:F2} Q={variant.Quality} {variant.Confidence}");
}
```
