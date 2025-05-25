# Structural Variant Detection

## Introduction

While [SNPs and small indels](bubble-tip-detection.md) manifest as simple bubbles in a [De Bruijn graph](de-bruijn-assembly.md), larger structural variants 
(SVs) — including inversions, insertions, deletions, and translocations — produce more complex graph signatures. The 
[StructuralVariantDetector](structural-variant-detection.md) class orchestrates [bubble and tip](bubble-tip-detection.md) analysis and classifies each finding into a 
specific SV type, bridging the gap between raw graph features and actionable variant calls that feed into downstream 
[variant calling](variant-calling-pipeline.md) and [genotype calling](genotype-calling.md) pipelines.

Classification is based on comparing the alternative path's length to the reference path's length. If they are equal 
length but differ in sequence, and the alt sequence matches the reference's reverse complement, it indicates an 
inversion. If the alt path is significantly longer, it indicates an insertion. If shorter, a deletion.

Soft-clip analysis adds another dimension: reads with large soft-clipped segments can be re-aligned to alternative 
positions — a step also found in the [Option B pipeline](e2e-option-b-individual-components.md). If re-alignment places a read on a different chromosome, 
it indicates a translocation. If re-alignment reverses the strand orientation, it indicates an inversion.

LocalVariantResult objects carry SV metadata including SvType (Inversion, Insertion, Deletion, Translocation), end 
position, assembly info (the alternative sequence path), and BubbleConfidence scores. These results are merged into the 
final variant set and written to VCF, where they may later be annotated for biological meaning via 
[variant annotation](variant-annotation-quick-start.md). This bridges the gap between graph structure and actionable variant calls.

```csharp
var analysis = await StructuralVariantDetector.AnalyzeGraph(
    graph,
    referenceSequence: "ACGTACGTACGTACGTACGTACGTACGTACGT",
    chromosome: "chr22",
    refStart: 0
);

foreach (var variant in analysis.Variants)
{
    Console.WriteLine($"{variant.Chromosome}:{variant.Position} " +
        $"{variant.Reference}>{variant.Alternate} " +
        $"Q={variant.QuantitativeQuality} DP={variant.Depth}");

    if (variant.IsStructuralVariant)
    {
        Console.WriteLine($"  SV: {variant.SvType} (END={variant.EndPosition})");
        Console.WriteLine($"  Assembly: {variant.AssemblyInfo}");
    }
}
```

**Classification rules:**

| Condition | SV Type |
|---|---|
| Alt ref equal length, sequence differs, reverse complement matches ref | Inversion |
| Alt longer than ref by >= 5bp (or >= 10% of ref) | Insertion |
| Alt shorter than ref by >= 5bp (or >= 10% of ref) | Deletion |
| Read re-aligns in reverse orientation on soft-clip | Inversion (from ReadRealigner) |
| Read re-aligns on a different chromosome | Translocation |

`LocalVariantResult` carries SV metadata: `IsStructuralVariant`, `SvType`, `EndPosition`, `AssemblyInfo`, `ClipInfo`, 
`Genotype`, and `BubbleConfidence`.
