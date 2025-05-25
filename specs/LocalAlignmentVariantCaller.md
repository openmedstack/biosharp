# Local Alignment-Based SNP/Indel Caller -- Specification

## Overview

This module implements a variant caller based on local sequence alignment. Given a genomic reference sequence and a set of read sequences (from short-read sequencing), it aligns each read locally to the reference using the Smith-Waterman algorithm and derives variant calls (SNPs, insertions, deletions) from the resulting alignments.

Output: VCF-compatible records with position, ref/alt alleles, quality scores, and filter status.

## Architecture

```
LocalAligner (Smith-Waterman)
    |
    v
AlignmentResult (aligned read, aligned ref, alignment string, score, coordinates)
    |
    v
VariantCaller (parse alignment -> call variants -> compute quality)
    |
    v
LocalVariantResult (chromosome, position, ref, alt, quality, filter)
```

## Components

### 1. Smith-Waterman Local Aligner

- **File:** `src/openmedstack.biosharp.calculations/Alignment/SmithWatermanAligner.cs`
- **Algorithm:** Smith-Waterman local alignment (not global Needleman-Wunsch)
- **Scoring:** Nucleotide-specific scoring matrix
  - Match score: +2
  - Mismatch penalty: -3
  - Gap open penalty: -5
  - Gap extend penalty: -2
  (These are standard for nucleotide alignment, e.g., EMBOSS water defaults)
- **Input:** Two DNA sequences (reference span + read sequence), optionally with quality string
- **Output:** `AlignmentResult` containing:
  - Aligned reference sequence (with gaps if ref had deletions)
  - Aligned read sequence (with gaps if read had insertions)
  - Alignment string: '|' for match, 'X' for mismatch, '-' for gap in read, ' ' for gap in ref
  - Reference start position (0-based genomic coordinate)
  - Alignment score
  - CIGAR-style operation list

**Key differences from existing Needleman-Wunsch:**
- SW is a *local* aligner — it finds the best matching subregion, not full-sequence alignment
- SW allows gaps at the start and end of the alignment (glocal / semi-global behavior)
- SW uses a different recurrence: `H[i,j] = max(0, H[i-1,j-1]+s, H[i-1,j]-d, H[i,j-1]-d)` — the `max(0,...)` prevents negative scores and allows the alignment to start anywhere
- Traceback starts from the highest-scoring cell anywhere in the matrix, not the bottom-right corner

### 2. Variant Caller

- **File:** `src/openmedstack.biosharp.calculations/Alignment/VariantCaller.cs`
- **Input:** Reference sequence, aligned read, read quality string, and mapping context
- **Output:** List of `LocalVariantResult` (variant calls)
- **Logic:** Parse the alignment to detect:
  - **SNP (substitution):** Reference and read disagree at a single position (aligned ref char != aligned read char, neither is '-')
  - **Insertion:** Gap in reference (aligned ref has '-', aligned read has a base)
  - **Deletion:** Gap in read (aligned read has '-', aligned ref has a base)
- **VCF Conventions:**
  - Positions are 1-based for output
  - Alleles are uppercased ACGT
  - For deletions: use the "leftmost base" reference context (1 base before the del)
  - For insertions: no ref base changed, so ref = the base after the del position (or leftmost anchor)
  - Multi-base events: merge consecutive SNPs/indels into MNPs or multi-base indels with proper VCF notation

### 3. Quality Scoring

- **Input:** Alignment score, per-base quality scores from the read, mapping position
- **Output:** Phred-scaled variant quality score (Q = -10 log10(error_prob))
- **Logic:**
  - For SNPs: Q = min(Phred(sum of base quals at the SNP position), 60)
  - For indels: Q = min(Phred(min base quals in gap region), 60)
  - Also apply a coverage/depth bonus: variants supported by multiple reads get higher confidence

### 4. VCF Record Generation

- **File:** `src/openmedstack.biosharp.calculations/Alignment/VcfRecordBuilder.cs`
- **Input:** `LocalVariantResult` list, reference name, and metadata
- **Output:** VCF-formatted string lines or `VcfVariant[]`
- **Fields populated:**
  - #CHROM, POS, ID (".")
  - REF, ALT
  - QUAL (Phred-scaled quality)
  - FILTER ("PASS" or low quality)
  - INFO (DP = depth, type of variant)

## Variant Classification Rules

1. **SNP/Substitution:** A single-nucleotide change. Output ALT = the alternate base.
2. **Insertion:** Nucleotide(s) present in the read but not in the reference at that position. Output REF = base before insertion (left anchor), ALT = REF + insertion bases.
3. **Deletion:** Nucleotide(s) present in the reference but not in the read (read has gap). Output:
   - REF = base before deletion + deleted bases (for VCF normalization)
   - ALT = base before deletion (i.e., the deleted bases are removed)
   - This is the "left-alignment" convention used by GATK and samtools
4. **Complex multi-base:** Consecutive mismatches and indels within the same alignment region should be considered for multi-base variant calling. Simple SNPs remain single-base.

## Edge Cases

1. **No alignment found:** If the read has no meaningful alignment (score below threshold), skip it.
2. **Multiple equivalent alignments:** Use the highest-scored alignment. If ties exist, prefer the one that starts earliest in the reference.
3. **Soft clipping:** Reads may have bases at the ends that don't align. These are treated as unmapped and not called as variants.
4. **Homopolymer indels:** Insertions/deletions in homopolymer runs are common sequencing errors. Quality filtering should be aggressive here.
5. **Near reference boundary:** If the alignment extends beyond the reference sequence, report the variant at the nearest valid coordinate.
6. **Duplicate variants:** Same variant from different reads of the same locus — aggregate depth, take max quality.

## Quality Thresholds

- Minimum alignment score to consider a read as mapped: 20
- Minimum variant quality (Phred) for PASS: 30
- Minimum read depth for calling: 2

## Integration with Existing Code

- `LocalVariantResult` is a new model type, stored in the Model project (or Calculations)
- `VcfVariant.Parse()` in the model can be reused for output format (or the caller writes the VCF format directly)
- The existing `Sequence` type is used for both reference and read sequences
- No changes required to the existing `VariantAnnotator` — this is a variant *caller*, not an *annotator*
