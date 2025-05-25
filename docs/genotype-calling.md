# Genotype Calling

## Introduction

After a variant is called (a position where a sample differs from the reference), the next step is determining the 
genotype: is the sample homozygous for the reference (0/0), heterozygous (0/1), or homozygous for the alternate (1/1)? 
In the context of a [variant calling pipeline](variant-calling-pipeline.md), genotype calling is the intermediate step between raw variant 
detection and [variant annotation](variant-annotation-quick-start.md) that assigns biological meaning.

BioSharp uses a binomial model to calculate genotype likelihoods from sequencing read evidence. If refCoverage reads 
show the reference allele and altCoverage reads show the alternate allele, the ratio determines the predicted zygosity:
- Alt/Ref ratio < 0.20: homozygous reference (very few alt reads, likely sequencing errors)
- Alt/Ref ratio 0.20-0.40: homozygous reference (conservative borderline)
- Alt/Ref ratio 0.40-0.80: heterozygous (balanced alt/ref, expected for a 0/1 genotype)
- Alt/Ref ratio >= 0.80: homozygous alternate (mostly alt reads, expected for a 1/1 genotype)

A confidence score (Genotype Quality, GQ) is computed using the two-sided binomial CDF under the heterozygous null 
hypothesis (p=0.5). The probability of observing the given allele ratio or more extreme is calculated, then 
Phred-scaled: GQ = -10 * log10(p-value), clamped to [0, 99].

These genotype calls are essential for VCF output (the GT field) and for downstream analyses like population genetics, 
pedigree analysis, and cancer subtyping.

The process involves:
1. Counting reference and alternate allele reads at the variant position
2. Computing alt/ref ratio
3. Classifying zygosity using threshold-based rules
4. Computing GQ from binomial CDF
5. Attaching genotype to variant result

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Call genotype from coverage
var genotype = GenotypeCaller.Call(refCoverage: 45, altCoverage: 52);

Console.WriteLine($"Genotype: {genotype.ToVcfGenotype()}");    // "0/1" (heterozygous)
Console.WriteLine($"GQ: {genotype.GQ}");                        // Phred-scaled quality
Console.WriteLine($"Alt/Ref ratio: {genotype.AltRefRatio:F3}"); // ~1.156

// Extend a variant result with genotype info
var variant = new LocalVariantResult
{
    Chromosome = "chr22",
    Position = 50297290,
    Reference = "C",
    Alternate = "T",
    Depth = 97,
    QuantitativeQuality = 42,
};

variant = variant.WithGenotype(refCoverage: 45, altCoverage: 52);
// variant.Genotype now holds the full genotype call
```

**Zygosity thresholds:**

| Alt/Ref Ratio | Genotype |
|---|---|
| < 0.20 | Homozygous Ref (0/0) |
| 0.20 - 0.40 | Homozygous Ref (conservative; borderline) |
| 0.40 - 0.80 | Heterozygous (0/1) |
| >= 0.80 | Homozygous Alt (1/1) |

**Genotype Quality (GQ)**: Computed from the two-sided binomial CDF. Under the heterozygous null hypothesis (p=0.5), 
the probability of observing the given alt/ref ratio or more extreme is computed, then Phred-scaled: 
GQ = -10 * log10(p-value), clamped to [0, 99].
