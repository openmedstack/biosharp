# End-to-End Pipeline: Option D — Cohort Analysis

## Introduction

[Cohort analysis](cohort-variant-calling.md) extends [somatic calling](somatic-variant-detection.md) to multiple tumor samples simultaneously, enabling population 
genetics studies and cancer subtyping. Instead of comparing a single tumor against a single normal, you compare all 
tumor samples against shared [Bloom filter](bloom-filter.md) normal controls and aggregate findings across the entire cohort using 
the [Multi-Sample Graph](multi-sample-graph.md) infrastructure.

Each sample is processed independently through the somatic detection pipeline. Results are then aggregated at the 
cohort level: variants are grouped by genomic position, and each is classified by how many samples carry it.

Variants are classified into biologically meaningful categories:
- SingleSample: found in only one sample (a private, unique mutation)
- Subclonal: found in 20-50% of samples (suggesting a subclonal expansion)
- Present: found in 50% or more of samples (an event shared across the cohort)
- Oncogenic: found in 90% or more of samples (strongly suggesting a driver mutation)

Filter helper methods allow you to extract subsets of variants by frequency threshold or classification type. Summary 
statistics provide quick overviews of the cohort's mutational landscape.

Cohort analysis is essential for understanding tumor heterogeneity, identifying shared oncogenic pathways, and 
discovering recurrent mutations that suggest therapeutic vulnerability.

Cohort analysis steps:
1. Build De Bruijn graphs for all tumor samples
2. Build a shared Bloom filter from normal sample(s)
3. Run somatic detection on each tumor sample
4. Aggregate variants across samples
5. Classify by frequency (single, oncogenic, present, subclonal)
6. Filter and summarize results

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

var tumorNormalPairs = samples
     .Select(s => (s.Name, s.TumorGraph))
     .ToList();

var cohortVariants = await CohortVariantCaller.CallCohortVariantsAsync(
    tumorNormalPairs: tumorNormalPairs,
    normalFilter: normalBloomFilter,
    reference: referenceSequence,
    chromosome: "chr22",
    refStart: 0,
    minAlleleFraction: 0.20
);

// Filter by frequency
var rareVariants = CohortVariantCaller
     .FilterByFrequency(cohortVariants, 0.1)
     .Where(v => v.SampleCount == 1)
     .ToList();

// Filter by type
var oncogenicVariants = CohortVariantCaller
     .FilterByType(cohortVariants, CohortVariantType.Oncogenic)
     .ToList();

// Summary
var summary = CohortVariantCaller.GetSummary(cohortVariants, totalSamples: samples.Count);
Console.WriteLine(summary);
```
