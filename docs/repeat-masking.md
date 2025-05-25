# Repeat Masking

## Introduction

Genomic repeats — repetitive DNA sequences present at multiple locations in the genome — are a major source of false 
positives in variant calling. When a read originates from a repeat region, it could map equally well to multiple 
locations, leading to misaligned reads and spurious variant calls.

Repeat masking prevents this ambiguity by identifying repeat regions and marking them as ambiguous. BioSharp's 
RepeatMasker scans a DNA sequence against a repeat library (such as RepBase or a custom JSON file), and replaces 
repeat sequences with 'N' characters. 'N' acts as a wildcard — it tells downstream [variant calling](variant-calling-pipeline.md) and 
[assembly](de-bruijn-assembly.md) tools that this region is unreliable and should not be trusted.

Two approaches are supported: loading the library first then masking (gives you detailed information about each masked 
region), or a convenience method that loads and masks in one call.

Additionally, [Bloom filters](bloom-filter.md) can be built from repeat libraries for fast pre-filtering — instead of scanning the 
full library for every k-mer, you build a Bloom filter once and use it to quickly skip repeats during assembly or 
variant calling.

Masking repeats is a pre-processing step that improves the reliability of all downstream variant calls, similar to how 
quality trimming improves alignment accuracy. Before calling variants or assembling genomes, masking repeats (as shown 
in [Option F](e2e-option-f-repeat-masking.md)) is the recommended practice.

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Load a repeat library (e.g. RepBase or custom JSON)
var library = await RepeatMasker.LoadLibraryAsync("data/repeats.json");

// Mask repeats in a sequence
var maskedRegions = await RepeatMasker.MaskRepeatsAsync(
    sequence: "ACGTACGTNNNNNNNNACGTACGT",
    library: library,
    allowAmbiguity: true
);

foreach (var region in maskedRegions)
{
    Console.WriteLine($"Masked [{region.Start}-{region.End}] " +
        $"({region.Repeat.Name}, type={region.RepeatType}) " +
        $"len={region.Length}");
}

// Convenience: load library and mask in one call
var allMasked = await RepeatMasker.MaskRepeatsFromLibraryAsync(
    sequence: largeSequence,
    libraryPath: "data/repeats.json"
);
```

You can also create a Bloom filter from the library for fast pre-filtering:

```csharp
var repeatBloom = RepeatMasker.CreateBloomFilterFromLibrary(library, k: 15, fpr: 0.01);
```
