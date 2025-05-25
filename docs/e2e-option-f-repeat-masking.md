# End-to-End Pipeline: Option F — Repeat Masking (Pre-Calling)

## Introduction

[Repeat masking](repeat-masking.md) is the pre-processing step that should be applied before [variant calling](variant-calling-pipeline.md) or 
[genome assembly](de-bruijn-assembly.md) to prevent repetitive DNA regions from causing false variant calls and assembly errors. It 
improves the accuracy of every downstream analysis by eliminating ambiguous mapping regions.

Repetitive DNA elements (transposons, tandem repeats, segmental duplications) are present at multiple locations in the 
genome. When a sequencing read originates from a repeat region, it can map equally well to multiple distinct genomic 
locations. This ambiguity leads to misaligned reads, which in turn produce spurious (false positive) variant calls.

BioSharp's RepeatMasker loads a repeat library (such as RepBase, a curated database of known repetitive elements) and 
scans a DNA sequence (or reference genome) for matches against this library. Matches are replaced with 'N' characters, 
marking those regions as ambiguous and unreliable for downstream variant calling or assembly.

A Bloom filter can also be built from the repeat library for fast k-mer pre-filtering. Instead of scanning the full 
library for every k-mer, the Bloom filter provides O(1) membership tests to quickly skip known repeat k-mers during 
graph construction or alignment.

Repeat masking steps:
1. Load repeat library from JSON file (repbase or custom format)
2. Apply masking to reference or assembly sequence (replaces repeat regions with 'N')
3. Optionally build Bloom filter from library for fast repeat k-mer filtering
4. Use masked sequence for downstream alignment and variant calling

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

var library = await RepeatMasker.LoadLibraryAsync("data/repeats.json");
var maskedSequence = await RepeatMasker.MaskRepeatsFromLibraryAsync(
    sequence: referenceData,
    libraryPath: "data/repeats.json"
);

foreach (var region in maskedSequence)
{
    Console.WriteLine($"Masked repeat: {region}");
}
```
