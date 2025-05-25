# DeBruijn SV Detection Pipeline — Enhancement Specification

## Overview

This spec covers three proposals for enhancing the DeBruijn graph-based structural variant detection pipeline in `biosharp`. Each addresses a specific gap identified in `annotations/debruijn_improvements.md`.

## Priority #1: Diploid Genotype Calling Over Bubbles

### Problem
The current pipeline treats all bubbles as heterozygous variants (alt vs ref). In diploid organisms, bubbles fall into:
- **Heterozygous**: alt coverage ≈ ref coverage / 2 (one allele present, one absent)
- **Homozygous alternate**: alt coverage ≈ ref coverage (both alleles present at full depth from two haplotypes)

### Requirements

#### AC-GC-1: `Genotype` Model
Given a bubble's ref coverage and alt coverage, the pipeline creates a `Genotype` object with:
- `Caller` = `"DeBruijn"`
- `Type` — an enum: `HomozygousRef` (0/0), `Heterozygous` (0/1), `HomozygousAlt` (1/1)
- `GQ` (genotype quality) — Phred-scaled from binomial CDF P-value

#### AC-GC-2: Zygosity Classification Logic
Given ref coverage R and alt coverage A:
- Ratio R/A where 0.4 ≤ A/R ≤ 0.6 → `Heterozygous` (0/1)
- R/A where A/R ≥ 0.8 → `HomozygousAlt` (1/1)
- A/R < 0.2 → suspicious, variant flagged or suppressed

#### AC-GC-3: Genotype Quality (GQ) Calculation
- Use binomial CDF: P(successes = A | n = R+A, p = 0.5)
- GQ = -10 * log10(P)
- GQ clamped to [0, 99]

#### AC-GC-4: `LocalVariantResult` Extension
- `LocalVariantResult` gains a `Genotype?` property
- GQ is exposed at the variant level

#### AC-GC-5: VCF FORMAT Integration
- VCF output includes FORMAT = `GT:GQ:DP` per sample
- Per-sample genotype values: `0/0`, `0/1`, `1/1`

### Edge Cases
- Coverage ratios at exact boundaries (0.4, 0.6, 0.8)
- Very low coverage (ref=1, alt=1)
- Extreme imbalance (ref=100, alt=1)

---

## Priority #2: Multi-Sample Joint Calling

### Problem
The pipeline processes one set of reads at a time. Clinical and research workflows need comparison between samples (tumor vs normal, cohort-level germline).

### Requirements

#### AC-MS-1: `MultiSampleGraph` Class
- Unions k-mers across multiple `DeBruijnGraph` instances
- K-mers shared across samples get edge weight bonuses
- Supports 2–100 samples

#### AC-MS-2: Bloom Filter Integration
- Use Bloom filters for cross-sample k-mer deduplication
- Probable false positive rate < 1%
- Memory-efficient compared to loading all k-mers

#### AC-MS-3: Somatic Calling (Tumor-Normal Pair)
- Variants with high coverage in tumor and zero coverage in normal → flagged as `Somatic`
- SV type: `Somatic` (new enum value on SvType)

#### AC-MS-4: Cohort Calling
- Common variants (present in ≥ 2 samples) get higher confidence scores
- Shared variant catalog across samples

#### AC-MS-5: VCF FORMAT Column Support
- VCF output with `FORMAT` column: `GT:DP:GQ`
- Per-sample genotype, depth, and genotype quality fields

### Edge Cases
- Sample with zero coverage in a region
- Variant present in all samples vs. unique to one
- Very large cohorts (100+ samples)

---

## Priority #3: Homopolymer and Repetitive Region Awareness

### Problem
Short-read DeBruijn graphs collapse in repetitive regions and homopolymers, producing false-positive indel calls that are assembly artifacts.

### Requirements

#### AC-RH-1: `BubbleConfidence` Enum
- Values: `High`, `Medium`, `Low`
- `Low` bubbles are flagged for optional downstream filtering rather than definitive SVs

#### AC-RH-2: `RepetitivenessAnalyzer` Helper Class
- Counts how many k-mers in a bubble path appear >N times in the overall graph
- Computes `Confidence = (refCoverage + altCoverage) / (repeatCount * k)`
- High values = confident variant; low values = likely artifact

#### AC-RH-3: Integration into `AnalyzeBubble`
- `Bubble` gains a `Confidence` field
- `BubbleFinder.AnalyzeBubble` scoring calls `RepetitivenessAnalyzer`

#### AC-RH-4: `repeats.json` Configuration
- Optional configuration file for known problematic k-mers
- K-mers listed in file are masked during graph building and bubble analysis

### Edge Cases
- Homopolymer stretches (AAAAA vs AAAAAA)
- Tandem repeats (TATATATATA vs TATATATA)
- Mixed repeat/unique regions

---

## Acceptance Test Plan

### Proposal #2 (Genotype Calling)
| ID | Scenario | Expected Result |
|----|----------|-----------------|
| GC-T1 | Heterozygous: ref=20x, alt=10x (ratio ~0.5) | Type = Heterozygous |
| GC-T2 | Homozygous alt: ref=10x, alt=10x (ratio ~1.0) | Type = HomozygousAlt |
| GC-T3 | Low coverage alt: ref=20x, alt=2x (ratio ~0.1) | Flagged as suspicious / suppressed |
| GC-T4 | GQ calculation: ref=20x, alt=10x | GQ ≥ 10 |
| GC-T5 | GQ at boundary: ref=10x, alt=8x | GQ computed correctly |
| GC-T6 | VCF format: genotype output includes GT:GQ:DP | GT value matches classification |

### Proposal #3 (Multi-Sample Joint Calling)
| ID | Scenario | Expected Result |
|----|----------|-----------------|
| MS-T1 | Tumor has unique bubble at chr1:5000, normal does not | Somatically-called variant |
| MS-T2 | 3 samples, 1 has novel indel, 2 are reference | Variant present in 1 sample only |
| MS-T3 | Variant in all 3 samples | High confidence, shared variant |
| MS-T4 | Bloom filter with 1M k-mers, 1% FPR | Correct containment checks |
| MS-T5 | Multi-sample VCF output | FORMAT column with per-sample GT:DP:GQ |
| MS-T6 | 100-sample cohort | Pipeline completes, variants reported |

### Proposal #1 (Repetitive Region Awareness)
| ID | Scenario | Expected Result |
|----|----------|-----------------|
| RH-T1 | Homopolymer: ref=`AAAAACCCCC`, alt=`AAAAACCCCCC` | Confidence = Low or Medium |
| RH-T2 | Tandem repeat: ref=`TATATATATA`, alt=`TATATATA` | Confidence = Low or Medium |
| RH-T3 | Unique sequence bubble | Confidence = High |
| RH-T4 | Repeat k-mers masked via repeats.json | Bubble not reported |
| RH-T5 | Scoring formula: (refCov+altCov)/(repeatCount*k) | Returns expected numeric value |
| RH-T6 | 50x bubble with 0 repeat k-mers | Confidence = High |
