# Cohort Variant Calling

## Introduction

Cohort analysis extends [somatic calling](somatic-variant-detection.md) to multiple tumor samples simultaneously. Instead of comparing two 
samples (tumor vs. normal), you compare all tumor samples against a shared [Bloom filter](bloom-filter.md) from a normal sample and 
then aggregate findings across the cohort using the [Multi-Sample Graph](multi-sample-graph.md) infrastructure.

Each sample is processed independently through the somatic detection pipeline, then results are aggregated per genomic 
position. Variants are classified by frequency:
- **SingleSample**: found in only one sample (private mutation)
- **Subclonal**: found in 20-50% of samples (suggesting a subpopulation)
- **Present**: found in 50%+ of samples (likely a shared event)
- **Oncogenic**: found in 90%+ of samples (strongly suggesting a driver mutation)

This classifies findings into biologically meaningful categories: rare private mutations, subclonal expansions, shared 
germline or common somatic events, and recurrent oncogenic alterations.

Filtering by frequency and type allows researchers to focus on the most relevant variants. Summary statistics provide 
quick overviews of the cohort's mutational landscape.

Cohort analysis is essential for discovering both common and rare variants across a population, identifying which 
mutations are recurrent (and therefore potentially important) versus which are random individual occurrences.

The process involves:
1. Building graphs for all tumor samples
2. Building a shared Bloom filter from normal sample(s)
3. Running somatic detection on each tumor sample
4. Aggregating results across samples
5. Classifying by frequency (single, subclonal, present, oncogenic)
6. Filtering and summarizing results

```csharp
var tumorNormalPairs = new[]
{
    ("tumor_1", tumorGraph1),
    ("tumor_2", tumorGraph2),
    ("tumor_3", tumorGraph3),
};

var cohortVariants = await CohortVariantCaller.CallCohortVariantsAsync(
    tumorNormalPairs,
    normalFilter: normalBloom,
    reference: referenceSequence,
    chromosome: "chr22",
    refStart: 0,
    minAlleleFraction: 0.20
);

foreach (var cv in cohortVariants)
{
    Console.WriteLine($"{cv.Position}: {cv.Reference}>{cv.Alternate} " +
        $"in {cv.SampleCount}/{cv.TotalSamples} samples " +
        $"({cv.Type}) Q={cv.AverageQuality:F0} MAF={cv.AverageAlleleFraction:F2}");
    Console.WriteLine($"  Detected in: {string.Join(", ", cv.DetectedIn)}");
}
```

**Cohort variant classification** (based on fraction of samples carrying the variant):

| Classification | Condition |
|---|---|
| SingleSample | Present in 1 sample |
| Oncogenic | Present in >= 90% of samples |
| Present | Present in >= 50% of samples |
| Subclonal | Present in 20-50% of samples |

Filtering helpers:

```csharp
// By frequency
var highFreq = CohortVariantCaller.FilterByFrequency(cohortVariants, 0.5);

// By type
var oncogenicVariants = CohortVariantCaller.FilterByType(cohortVariants, CohortVariantType.Oncogenic);

// Summary statistics
var summary = CohortVariantCaller.GetSummary(cohortVariants, totalSamples: 50);
Console.WriteLine(summary);
// "CohortSummary: 142 variants, AvgQuality=32.5, AvgFreq=0.34. Single=89, Oncogenic=12, Present=23, Subclonal=18"
```
