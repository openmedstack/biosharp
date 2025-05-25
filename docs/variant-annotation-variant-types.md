# Variant Annotation: Supported Variant Types

## Introduction

The annotation engine — described in the [quick start](variant-annotation-quick-start.md) — can interpret a range of [variant types](variant-annotation-variant-types.md) that appear 
in VCF files (produced by the [variant calling pipeline](variant-calling-pipeline.md)), translating each into a biological consequence and 
[HGVS notation](variant-annotation-hgvs.md). Each VCF variant type represents a different kind of DNA change, and each has specific annotation 
requirements.

Supported variant types and their descriptions:
- **Substitution (SNV)**: single nucleotide change, e.g., c.150A>T
- **Deletion**: 1-3 bp deletion within a codon, or spanning multiple codons
- **Insertion**: 1 or more base additions within or between codons
- **MultiCodonIndel**: indel spanning 2 or more codons (requires codon-aware calculation)
- **MNP (Multi-Nucleotide Polymorphism)**: multiple simultaneous substitutions at adjacent positions
- **Delins**: compound deletion-insertion event (bases removed then different bases inserted at the same position)

The engine handles the codon-aware calculations for each type, translating DNA changes into amino acid changes and 
determining whether a frameshift results from the mutation.

Different variant types require different code paths for annotation — especially indels that span codon boundaries, 
which need careful frame tracking to produce correct HGVS notation. Understanding which variant types your pipeline 
supports is essential because each type has specific annotation requirements.

```csharp
// Process each variant type from the VCF
await foreach (var ann in engine.AnnotateVcfAsync("variants.vcf"))
{
    switch (ann.VariantType)
    {
        case VariantType.Substitution:
            Console.WriteLine($"SNV: {ann.HgvsCoding}");
            break;
        case VariantType.Insertion:
            Console.WriteLine($"Ins: {ann.HgvsCoding}");
            break;
        case VariantType.Deletion:
            Console.WriteLine($"Del: {ann.HgvsCoding}");
            break;
        case VariantType.MultiCodonIndel:
            Console.WriteLine($"MC Indel: {ann.HgvsCoding}");
            break;
        case VariantType.MNP:
            Console.WriteLine($"MNP: {ann.HgvsCoding}");
            break;
        case VariantType.Delins:
            Console.WriteLine($"DelIns: {ann.HgvsCoding}");
            break;
    }
}
```
