# Variant Annotation: Ref Mismatch Detection

## Introduction

Sometimes a variant's reference allele in the VCF file does not match the expected transcript base at that position. 
This [ref mismatch](variant-annotation-ref-mismatch.md) is a quality control check that prevents the [annotation engine](variant-annotation-quick-start.md) from silently producing 
incorrect [HGVS notation](variant-annotation-hgvs.md) when the input variant data and the transcript reference are inconsistent.

Such mismatches can occur for several reasons:
1. Reference genome version mismatch (e.g., VCF was generated from GRCh37 but annotation is against GRCh38)
2. Sequencing or alignment errors in the variant call
3. True biological differences between the sample and the reference genome

BioSharp handles this situation by tagging such variants as "Uncertain" rather than silently skipping them or producing 
incorrect annotations. This ensures that potentially problematic variants are flagged for manual review.

Ref mismatch detection is a quality control step that prevents the pipeline from silently producing incorrect HGVS 
notation when the input variant data and the transcript reference are inconsistent. This protects users from getting 
misleading annotation results.

Handling works as follows:
- If VCF reference allele does not match expected transcript base at the variant position -> mark as Uncertain
- Prevents incorrect HGVS notation generation
- Flags variants for manual review
- Maintains data integrity when reference versions don't match

```csharp
await foreach (var ann in engine.AnnotateVcfAsync("variants.vcf"))
{
    if (ann.IsRefMismatch)
    {
        Console.WriteLine($"WARNING: Ref mismatch at {ann.VariantPosition} " +
              $"in {ann.AffectedGene} (VCF ref: {ann.ReferenceAllele} vs " +
              $"expected: {ann.ExpectedBase})");
    }
    // Process non-mismatch annotations normally
}
```
