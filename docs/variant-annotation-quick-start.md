# Variant Annotation Quick Start

## Introduction

Variant annotation is the final analytical step in a genomics pipeline: it takes a list of genomic variants (produced 
by the [variant calling pipeline](variant-calling-pipeline.md) as VCF data) and interprets their biological significance by comparing them 
against known gene structures and transcript sequences. This step transforms [raw variant calls](e2e-option-a-pipeline-orchestrator.md) — chromosome 
position, reference allele, alternate allele — into human-readable descriptions of which gene is affected, what HGVS 
notation describes the change, and what biological consequence the mutation has.

The VariantAnnotationEngine works by loading transcript sequences (from FASTA files), defining gene structures (CDS 
boundaries, introns, gene start/end positions), and then evaluating each VCF variant against this annotation context 
to assign biological consequences and standard HGVS notation.

For each variant, the engine determines:
- Which gene is affected
- [What the HGVS notation is](variant-annotation-hgvs.md) (both DNA-level c. and protein-level p.)
- [What biological consequence it has](variant-annotation-variant-types.md) (missense, nonsense, frameshift, etc.) based on the supported 
- [variant types](variant-annotation-variant-types.md) the engine can handle
- For problematic cases, [ref mismatch detection](variant-annotation-ref-mismatch.md) flags variants where the VCF reference allele does not match the 
- transcript base, preventing incorrect annotations
- Variants outside protein-coding regions are classified using [non-coding annotation logic](variant-annotation-non-coding.md) (splice site, 
- intronic, upstream, downstream, intergenic, UTR)

The Quick Start example shows the minimum code required to run annotation. All results include HGVS notation and 
consequence classification. Quick setup involves creating an annotation context, initializing the engine, loading 
transcripts, and annotating a VCF file.

This step transforms raw variant calls into biologically meaningful information that researchers and clinicians can 
act upon.

```csharp
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Model;

// 1. Create annotation context with CDS boundaries
var ctx = AnnotationContext.FromCdsBoundaries(cdsStart: 100, cdsEnd: 1000, transcriptLength: 1500);

// 2. Initialize engine with context
var engine = new VariantAnnotationEngine(ctx);

// 3. Load transcripts from FASTA
await engine.LoadTranscriptsAsync("data/transcripts.fasta");

// 4. Annotate VCF file
await foreach (var ann in engine.AnnotateVcfAsync(
    vcfPath: "data/variants.vcf",
    transcriptId: "NM_001",        // optional; null to annotate all transcripts
    minQuality: 5.0f))             // min Phred quality to include
{
    Console.WriteLine($"{ann.HgvsCoding} -> {ann.Consequence}");
    Console.WriteLine($"  Protein: {ann.HgvsProtein}");
    Console.WriteLine($"  Codon: {ann.CodonChange}");
}
```
