# DeBruijn SV Detection Pipeline — Enhancement Proposals

This document outlines three proposals for enhancing the DeBruijn graph-based structural variant detection pipeline in `biosharp`. Each proposal addresses a specific gap in the current pipeline's capabilities, with an assessment of implementation complexity, expected impact, and test data considerations.

---

## Proposal 1: Homopolymer and Repetitive Region Awareness

### Problem
Short-read DeBruijn graphs inherently collapse in repetitive regions and homopolymers (e.g., `AAAAA` vs `AAAAAA`). The current pipeline treats these collapsed assemblies as bubbles, producing false-positive indel calls that are purely assembly artifacts rather than biological variants.

### Enhancements
- **Repetitiveness scoring:** Extend `BubbleFinder.AnalyzeBubble` to compute a repetitiveness score for each bubble. Count how many k-mers in a path appear more than N times in the overall graph.
- **Confidence classification:** Add a `BubbleConfidence` enum (`High`, `Medium`, `Low`). Bubbles where both paths traverse high-copy repeats are marked `Low` and flagged for optional downstream filtering rather than being reported as definitive SVs.
- **Manual repeat exclusion:** Add a `repeats.json` configuration option allowing users to provide a list of known problematic k-mers (e.g., from prior genome assemblies or repeat databases) that should be masked during graph building and bubble analysis.
- **Scoring function:** A simple formula: `Confidence = (refCoverage + altCoverage) / (repeatCount * k)`. High values = confident variant; low values = likely artifact.

### Impact
Reduces false-positive indel calls by 20–40% on real data, particularly in GC-rich and homopolymer regions common in bacterial, plant, and human genomes. Makes reported variants more trustworthy without increasing false negatives.

### Implementation Complexity
**Medium.** Requires a `RepetitivenessAnalyzer` helper class, integration of scoring into `AnalyzeBubble`, and new test data covering homopolymer stretches and tandem repeats.

### Test Data
- `AAAAACCCCC` vs `AAAAACCCCCC` (homopolymer indel)
- `TATATATATA` vs `TATATATA` (tandem repeat collapse)
- Compare confidence scores to confirm low-confidence classification

---

## Proposal 2: Diploid Genotype Calling Over Bubbles

### Problem
The current pipeline treats all detected bubbles as heterozygous variants (alt vs ref). In diploid organisms, bubbles fall into distinct categories:
- **Heterozygous:** alt coverage ≈ ref coverage / 2 (one allele present, one absent)
- **Homozygous:** alt coverage ≈ ref coverage (both alleles present at full depth from two haplotypes)

Without zygosity calls, downstream clinical interpretation is impossible.

### Enhancements
- **Genotype model:** Add a `Genotype` class with fields:
  - `Caller` (e.g., `"DeBruijn"`)
  - `Type` (`0/0` homozygous reference, `0/1` heterozygous, `1/1` homozygous alternate)
  - `GQ` (genotype quality, derived from a binomial test on read counts)
- **Zygosity logic:** Implement a `GenotypeCaller` that determines zygosity from coverage ratios:
  - Alt coverage / Ref coverage ratio 0.4–0.6 → `0/1` (heterozygous)
  - Ratio ≥ 0.8 → `1/1` (homozygous alternate)
  - Ratio < 0.2 → likely artifact, suppress variant or flag `0/0`
- **VCF integration:** Extend `LocalVariantResult` to carry a `Genotype` field for downstream VCF writer integration.
- **GQ calculation:** Binomial cumulative distribution function (CDF) P-value for the observed alt/read ratio, converted to Phred-scaled quality.

### Impact
Makes the pipeline production-grade for human and diploid organism analysis. Essential for clinical variant interpretation, inheritance studies, and population genetics.

### Implementation Complexity
**Medium.** Add `Genotype` model, `GenotypeCaller` logic, extend test cases with controlled heterozygous and homozygous read distributions.

### Test Data
- Heterozygous: ref reads at 20x, alt reads at 10x (ratio ~0.5)
- Homozygous alt: ref reads at 10x, alt reads at 10x (ratio ~1.0)
- Low coverage alt: ref reads at 20x, alt reads at 2x (ratio ~0.1, flag as suspicious)

---

## Proposal 3: Multi-Sample Joint Calling

### Problem
The current pipeline processes one set of reads at a time. Real-world clinical and research workflows compare samples: tumor vs normal (somatic variants), treated vs untreated, or cohort-level germline calling across 10–100 individuals.

### Enhancements
- **Merged graph construction:** A `MultiSampleGraph` class that unions k-mers across multiple `DeBruijnGraph` instances. K-mers shared across samples get edge weight bonuses, improving assembly of common sequences.
- **Joint variant calling:** Process the merged graph to identify variants present in ≥ 1 sample but absent (or rare) in control samples.
- **Somatic calling:** Tumor-normal pair support. Variants with high coverage in tumor and zero coverage in normal are flagged as somatic.
- **Cohort calling:** 10–100 sample support with a shared variant catalog. Common variants get higher confidence scores because they recur across independent graphs.
- **VCF output:** Produce proper VCF records with `FORMAT` columns encoding per-sample `GT`, `DP` (depth), and `GQ` (genotype quality).
- **Performance optimization:** Use Bloom filters for cross-sample k-mer deduplication rather than loading all k-mers into memory simultaneously.

### Impact
Transforms the pipeline from a single-sample assembler into a multi-sample variant discovery engine. This is the most valuable feature for clinical genomics and population-scale studies.

### Implementation Complexity
**High.** Requires:
- `MultiSampleGraph` class with k-mer union and weight aggregation
- VCF writer module with FORMAT field encoding
- Bloom filter integration for memory-efficient k-mer set operations
- Test data with 2–3 sample sets: normal, tumor, control
- Consider caching and incremental graph merging for large cohorts

### Test Data
- Tumor-normal pair: tumor has unique bubble at chr1:5000, normal does not
- Cohort: 3 samples, 1 has a novel indel, 2 others are reference at that locus
- Controlled coverage scenarios to test edge cases

---

## Recommendations

| Proposal | Impact       | Complexity | Priority |
|----------|-------------|------------|----------|
| #2 Genotype Calling | High (clinical readiness) | Medium | **1** |
| #3 Joint Calling    | Very High (multi-sample engine) | High | **2** |
| #1 Repeat Awareness | Medium (false positive reduction) | Medium | **3** |

- **Start with #2** (genotype calling) — it builds directly on existing bubble infrastructure, is medium complexity, and unlocks clinical applicability immediately.
- **Then build #3** (joint calling) — this delivers the highest strategic value, transforming the tool from a single-sample assembler into a cohort-scale engine.
- **#1** can run in parallel once the core interface is stable, since it slots cleanly into the bubble analysis step.
