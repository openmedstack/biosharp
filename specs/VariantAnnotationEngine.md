# Variant Annotation Engine Specification

## Overview

This module annotates genetic variants with their predicted biological consequences,
transforming HGVS notation between reference sequence types (coding DNA to protein level)
and classifying the functional impact of each variant.

## Input

1. **VcfVariant** -- a parsed VCF record (chromosome, position, ref allele, alt allele,
   filters, quality, additional INFO).
2. **Reference transcript sequences** -- FASTA entries keyed by transcript/RefSeq ID
   (e.g., `NM_004006`). Used as the ground truth for determining what happens at each
   position.

## Output

**VariantAnnotation** records containing:
- `Consequence` -- one of the `VariantConsequence` categories
- `HgvsProtein` -- HGVS protein-level notation (p. style), e.g. `p.A25V`
- `HgvsCoding` -- HGVS coding DNA notation (c. style), e.g. `c.76A>G`
- `AffectedGene` -- gene/transcript identifier
- `AffectedAminoAcid` -- original amino acid at the variant position
- `ResultingAminoAcid` -- amino acid after the variant (null for intronic/UTR)
- `CodonChange` -- nucleotide change within the codon (e.g. `GCT>GTT`)

## Consequence Categories

| Consequence       | Definition                                                         |
|-------------------|--------------------------------------------------------------------|
| Synonymous        | c. change but same amino acid -- no protein effect                 |
| Missense          | c. change results in different amino acid                          |
| Nonsense          | c. change creates a premature stop codon (`*`)                     |
| Frameshift        | c. indel not divisible by 3, shifts reading frame, produces `*`    |
| InframeIns        | c. insertion of n nucleotides, n % 3 == 0, adds amino acids        |
| InframeDel        | c. deletion of n nucleotides, n % 3 == 0, removes amino acids      |
| SpliceSite        | < 3 bp from exon-intron boundary                                   |
| Upstream          | Within 3kb upstream of start codon                                 |
| Downstream        | Within 3kb downstream of stop codon                                |
| Intergenic        | Not in any gene/transcript                                         |
| Intronic          | Within intronic region (not near splice site)                      |
| VariantInUtr      | In untranslated region (5' or 3' UTR)                             |
| StopRetained      | Nonsense in last codon -- technically functional (edge case)       |

## HGVS c. to p. Translation Rules

### Step 1: Locate the variant on the transcript

- Use the position from the VCF (chromosome:pos) against the reference transcript.
- For HGVS-style c. input, use the position directly.

### Step 2: Determine the codon

- Calculate codon position: `(c.pos - 1) / 3` (0-based codon index)
- Extract the full reference codon (3 nucleotides)

### Step 3: Determine the altered codon

- **Substitution**: Replace the base at the c.position offset within the codon
- **Deletion**: Remove the specified bases from the codon(s)
- **Insertion**: Insert new bases into the codon (or adjacent codon)
- **delins/delins**: Remove then insert within the codon

### Step 4: Translate to amino acid

- Use the standard genetic code (same as `ReadAminoAcids()`)
- Compare old amino acid vs new amino acid

### Step 5: Classify consequence

- Same AA -> Synonymous
- Different AA -> Missense
- New `*` -> Nonsense (unless last codon -> StopRetained)
- Length change and %3 != 0 -> Frameshift
- Length change and %3 == 0 -> InframeIns or InframeDel

### Step 6: Build HGVS protein notation

- Format: `p.[OriginalAA][OriginalPosition][NewAA]`
- For nonsense:  `p.Arg25*`
- For frameshift: `p.Arg25Valfs*10` (position of shift, new AA, frameshift indicator, position of new stop)
- For in-frame del: `p.Gly25del`
- For in-frame ins: `p.Gly25insVal`

## HGVS Position Conventions

- c. positions are 1-based, counting from start codon (A of ATG = position 1)
- p. positions are 1-based, counting from the first amino acid of the mature protein
- Deletions are inclusive: c.4375_4379del means positions 4375 through 4379 inclusive
- Insertions go between specified positions: c.4375_4376insX means "between 4375 and 4376"

## Edge Cases

1. **Variant at last codon producing stop**: StopRetained (not a true nonsense)
2. **Multiple alt alleles**: Each alt allele gets one VariantAnnotation
3. **Compounded variants**: delins treated as separate del then ins for consequence classification
4. **Out-of-bounds variant position**: Should throw an exception or return null annotation
5. **Non-coding sequence**: If transcript is non-coding (n. or r. style), set
   Consequence to Intronic or VariantInUtr
6. **Reference/alt allele mismatch**: If ref/alt from VCF doesn't match what's
   expected for the given position based on transcript, flag as Uncertain

## Quality Thresholds

- Minimum variant quality (VQSLOD or equivalent): 5.0 (Phred-scaled)
- Filter status: Only annotate variants passing filters (PASS) or if explicitly requested
  to annotate all variants regardless of filter.
