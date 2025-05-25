# Variant Annotation: HGVS Notation

## Introduction

The [Human Genome Variation Society (HGVS)](variant-annotation-hgvs.md) nomenclature is the international standard for describing genetic 
variants in a machine-readable, unambiguous format. BioSharp's annotation engine generates both c. (coding DNA) and p. 
(protein) HGVS notation automatically for each variant in the context of the broader [variant annotation](variant-annotation-quick-start.md) workflow.

c. notation describes the change at the mRNA/DNA level. For example, c.150A>G means "at nucleotide position 150, the 
base Adenine is changed to Guanine." c.99_101del means "nucleotides 99 through 101 are deleted." c.100_102insGCT means 
"the sequence GCT is inserted at position 100."

p. notation describes the protein-level consequence. p.Ala50= means "alanine at position 50 is unchanged (synonymous)." 
p.Arg25Val means "arginine at position 25 is changed to valine (missense)." p.Trp67* means "tryptophan at position 67 
is changed to a stop codon (nonsense)."

Different consequence types have distinct HGVS format patterns:
- Synonymous: p.Xxx50= (the protein is unchanged at this codon)
- Missense: p.Arg25Val (amino acid substitution)
- Nonsense: p.Trp67* (premature stop codon introduced)
- Frameshift: p.Pro34fs* (reading frame shifted, followed by a stop)
- Inframe deletion: p.Ala34del (amino acid removed in-frame)
- Inframe insertion: p.Ala34insAla (amino acid inserted in-frame)

Generating this notation requires careful codon-aware calculation to get the correct nucleotide positions and amino 
acid changes. The engine handles all the translation math automatically.

```csharp
// Generate HGVS notation for each annotated variant
await foreach (var ann in engine.AnnotateVcfAsync("variants.vcf"))
{
    Console.WriteLine($"c. {ann.HgvsCoding}  ->  p. {ann.HgvsProtein}");
    // Example output: c.76A>T  ->  p.Arg25Val
}
```

HGVS format reference:
- Synonymous: c.150A>G -> p.Ala50=
- Missense: c.76A>T -> p.Arg25Val
- Nonsense: c.200G>T -> p.Trp67*
- Frameshift: c.99_101del -> p.Pro34fs*
- InframeDel: c.100_102del -> p.Ala34del
- InframeIns: c.100_102insGCT -> p.Ala34insAla
- SpliceSite: c.103+1G>A -> (no protein change; occurs in intron)
