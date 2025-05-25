# End-to-End Pipeline: Option E — Variant Annotation (Post-Calling)

## Introduction

After [variants have been called](variant-calling-pipeline.md) and written to a VCF file (via the [pipeline orchestrator](e2e-option-a-pipeline-orchestrator.md) or individual 
components), the [annotation engine](variant-annotation-quick-start.md) interprets each variant's biological context. This is the bridge between 
computational discovery and biological understanding — it translates genomic coordinates and allele data into 
descriptions of what the mutation means for gene function, including [HGVS notation](variant-annotation-hgvs.md) and [non-coding effects](variant-annotation-non-coding.md).

The engine loads transcript sequences from FASTA files (representing known genes), then evaluates each VCF variant 
against these transcripts to determine:
- Which gene is affected by the variant
- The HGVS notation at both DNA and protein level
- The biological consequence: missense, nonsense, frameshift, splice site, etc.
- For frameshifts: how many amino acids the frame is shifted until reaching a stop codon

This step transforms raw variant calls (chromosome, position, ref, alt) into biologically meaningful information that 
researchers and clinicians can act upon. It is the final analytical step in a typical variant calling pipeline.

Variant annotation steps:
1. Load reference transcripts from FASTA file
2. Initialize VariantAnnotationEngine
3. Annotate VCF file (variants are evaluated against each transcript)
4. Get affected gene, HGVS c. and p. notation, and consequence
5. Get amino acid changes and frameshift information for applicable variants

```csharp
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Model;

// 1. Load reference transcripts
var engine = new VariantAnnotationEngine();
await engine.LoadTranscriptsAsync("data/transcripts.fa");

// 2. Annotate variants from VCF
await foreach (var ann in engine.AnnotateVcfAsync(
    vcfPath: "output/variants.vcf",
    transcriptId: null,        // all transcripts
    minQuality: 5.0f))
{
    Console.WriteLine($"Transcript: {ann.AffectedGene}");
    Console.WriteLine($"  HGVS c.: {ann.HgvsCoding}");
    Console.WriteLine($"  HGVS p.: {ann.HgvsProtein}");
    Console.WriteLine($"  Consequence: {ann.Consequence}");
    if (ann.AffectedAminoAcid != null)
        Console.WriteLine($"  Amino acid: {ann.AffectedAminoAcid} -> {ann.ResultingAminoAcid}");
    if (ann.FrameshiftOffset.HasValue)
        Console.WriteLine($"  Frameshift offset: {ann.FrameshiftOffset} aa to stop");
    Console.WriteLine();
}
```
