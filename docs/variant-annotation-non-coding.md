# Variant Annotation: Non-Coding Regions

## Introduction

Not all variants fall within protein-coding regions. Many variants occur in introns, upstream/downstream regions, 
splice sites, or intergenic spaces. These non-coding variants — which can represent [splice site mutations](variant-annotation-hgvs.md) that 
disrupt mRNA splicing or [upstream mutations](variant-annotation-non-coding.md) that alter gene expression levels — still have significant 
biological effects but require different classification logic than coding variants described in the [quick start](variant-annotation-quick-start.md) 
guide.

When AnnotationContext is configured with gene boundaries and intron coordinates, the engine classifies non-coding 
variants based on their position relative to the gene:
- Outside gene boundaries: Intergenic (likely the least impactful)
- Within 3bp of CDS edge: SpliceSite (can disrupt splicing)
- Within CDS: coding consequence (missense, nonsense, frameshift)
- Within intron: Intronic (may affect splicing or gene regulation)
- Within 3kb upstream of CDS: Upstream (affects promoter or regulatory elements)
- Within 3kb downstream of CDS: Downstream (affects 3' UTR or regulatory elements)
- Within transcript but outside CDS, >3kb from edges: VariantInUtr (UTR regulatory region)

Without intron annotation, UTR and upstream/downstream classification still works at the transcript level using a 3kb 
window from CDS edges, which is sufficient for many analyses.

The process involves:
1. Defining intron coordinates and gene boundaries
2. Creating AnnotationContext with gene/CDS boundaries and introns
3. Loading transcript sequences
4. Annotating VCF file
5. Results include non-coding consequence classifications

```csharp
// Define introns and gene boundaries
var introns = new List<(int Start, int End)>
{
     (105, 130),    // Intron 1
     (200, 250)     // Intron 2
};

var ctx = AnnotationContext.FromCdsAndGeneBoundaries(
    cdsStart: 100,
    cdsEnd: 200,
    geneStart: 50,
    geneEnd: 500,
    introns: introns
);

var engine = new VariantAnnotationEngine(ctx);
await engine.LoadTranscriptsAsync("transcripts.fasta");

await foreach (var ann in engine.AnnotateVcfAsync("variants.vcf"))
{
     // ann.Consequence will be one of:
     // SpliceSite, Upstream, Downstream, Intronic, VariantInUtr, Intergenic
}
```

**Classification logic** (with AnnotationContext):

| Position | Consequence |
|---|---|
| Outside gene boundaries | Intergenic |
| Within 3bp of CDS edge | SpliceSite |
| Within CDS | (coding consequence: Missense, Nonsense, etc.) |
| Within intron | Intronic |
| Within 3kb upstream of CDS | Upstream |
| Within 3kb downstream of CDS | Downstream |
| Within transcript but outside CDS, >3kb from edges | VariantInUtr |
