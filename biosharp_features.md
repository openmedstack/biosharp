# BioSharp Variant Detection — Feature Proposals

## Current State Overview

BioSharp already implements **two complementary variant detection pipelines** plus a rich annotation layer:

### Alignment-Based Pipeline (Alignment-Driven)
- `SmithWatermanAligner` — Semi-global (glocal) alignment: reads aligned globally against a reference, with affine gap penalties. Returns aligned sequences, visual CIGAR strings, and reference positions.
- `VariantCaller` — Parses alignment events (SNP/insertion/deletion), groups consecutive events into multi-base variants via `VariantGroup`, builds VCF-compliant alleles using leftmost-anchor convention, calculates Phred-scaled quality scores from alignment score / event count, and merges variants across multiple reads.
- `VcfRecordBuilder` — Outputs VCF lines with CHROM/POS/REF/ALT/QUAL/FILTER/INFO, including an optional GT genotype field.

### De Bruijn Graph Pipeline (Assembly-Driven)
- `DeBruijnGraph` — Builds a k-mer overlap graph from reads, tracks node and edge coverage.
- `BubbleFinder` — Detects bubbles (divergent paths from branch nodes to convergence nodes), representing SNPs and indels.
- `TipFinder` — Detects dead-end tips; flags short tips as sequencing artifacts and long tips as potential insertions.
- `StructuralVariantDetector` — Orchestrates bubble and tip analysis, classifies bubble types (SNP / insertion / deletion) by comparing alt vs ref path lengths, computes quality from alt/ref coverage ratios, and outputs `LocalVariantResult` records compatible with the alignment pipeline.

### Annotation Layer
- `VariantAnnotator` — Takes VCF variants and classifies biological consequences: synonymous, missense, nonsense, frameshift, inframe indel, splice site, upstream, downstream, intergenic, intronic, UTR, and stop-retained.
- Full RNA codon table for translation.
- HGVS notation generation at c. (coding DNA) and p. (protein) levels via `HgvsDescription` hierarchy.
- Support for `SubstitutionDescription`, `DeletionDescription`, `InsertionDescription`, `DuplicationDescription` with `ReferenceSequenceKind` (LinearGenomic, CircularGenomic, Mitochondrial, CodingDna, NonCodingDna, Rna, Protein).

### I/O Ecosystem
- FastA/FastQ readers and writers with index support.
- BAM/BAM-index readers and writers (SAM/BAM parsing, CIGAR encoding).
- BCL readers (Illumina raw sequencer data).
- VCF readers and writers with BGZF compression support.
- `VariantCallFile` with metadata and entry storage.

---

## 3 Feature Proposals

---

### Proposal 1 — De Bruijn Completion: CNVs, Tandem Duplications, and Inversions

**Problem.** The De Bruijn SV detection has unresolved bugs (BubbleFinder convergence detection, TipFinder false positives, StructuralVariantDetector alt-path classification) and only detects simple SNPs and indels. No copy number variants, no tandem duplications, no inversions are detected.

**What to build:**

1. **Fix existing bugs** in `BubbleFinder` (convergence detection with cycle prevention), `TipFinder` (false-positive filtering on short tips), and `StructuralVariantDetector` (alt-path classification).
2. **Coverage-based CNV detection.** Use the existing `OutboundCoverage` tracking in `KmerNode`. Compute per-region coverage by sliding windows across graph nodes, normalize by mean coverage, and flag regions with significantly deviant coverage (>2x = duplication, <0.5x = deletion). This is the same approach used in SOAPdenovo2 and other de novo assemblers.
3. **Tandem duplication detection.** Detect a graph topology where a path branches, re-enters the same genomic region (overlapping k-mers), then re-diverges. Requires tracking node positions and detecting when a path revisits a previous genomic region.
4. **Inversion detection.** Detect when a path in the De Bruijn graph uses k-mers that, when reverse-complemented, match the reference but the forward orientation does not. If a subset of reads consistently follows the reverse-complement path, that is a candidate inversion.

**Impact.** Makes the assembly-based pipeline competitive with short-read SV callers (e.g. SVIM-asm, minigraph) and adds CNV detection, a core requirement for clinical and population genetics workflows.
**Recommended order:** 3rd (after bugs are fixed and Proposal 2 is implemented).

---

### Proposal 2 — Indel Realignment Pipeline

**Problem.** The current `SmithWatermanAligner` aligns reads once, and indels near homopolymers or low-complexity regions are notoriously error-prone. Reads with indels frequently misalign, creating false SNP calls around the true indel site. A 1 bp deletion in a homopolymer stretch can look like 3–4 SNPs.

**What to build:**

1. **`LocalRealigner`.** A new component that:
    - Scans all `LocalVariantResult` records for indels.
    - For each indel, collects all reads within a configurable window (e.g. +/- 30bp).
    - Extracts the reference window and re-aligns each read to it using `SmithWatermanAligner` with adjusted gap penalties optimised for indels (lower gap open penalty, lower gap extend penalty).
    - Re-classifies alignment events from the realigned reads.
2. **`VariantReclassifier`.** Takes the realigned reads and re-generates `LocalVariantResult` records. Merges realigned variants with existing ones, resolving the classic case where five reads each show two SNPs near a position, but realignment reveals they all share the same 1 bp deletion.
3. **Homopolymer awareness.** Special handling when the anchor region contains runs of the same base (A>3, T>3, etc.), where homopolymer-induced alignment errors are most likely. Down-weight SNP quality in such regions; up-weight indel calls supported by multiple reads.
4. **VCF INFO field enrichment.** Add realignment quality metrics: `REALN` (count of reads realigned), `HOM` flag (variant inside a homopolymer region), improved `QD` (variant quality divided by depth).

**Impact.** Directly addresses the #1 source of false-positive SNP calls in short-read variant calling. Comparable to GATK's legacy IndelRealigner and the local assembly approach of HaplotypeCaller.
**Recommended order:** 1st (highest ROI, best-contained scope).

---

### Proposal 3 — Full Genotype Calling + Multi-Sample VCF Pipeline

**Problem.** The current pipeline is single-sample and depth-only. `VariantCaller.MergeVariants()` only increments depth and takes max quality. There is no genotype inference, no allele frequency, no population statistics, and no multi-sample VCF generation. The output VCF is incomplete for most downstream analyses.

**What to build:**

1. **Bayesian Genotype Caller.** Takes per-variant, per-sample read evidence (base qualities, mapping qualities, alignment positions) and computes genotype likelihoods P(D | G) for G in {0/0, 0/1, 1/1}. Uses a binomial model with base quality as the error probability (matching GATK's approach). Outputs the most-likely genotype and a PL (phred-scaled likelihood) array per sample.
2. **Allele Balance Calculator.** For each heterozygous call, computes the observed allele balance (alt bases / total bases). Flags variants with extreme imbalance as potential false positives or contamination.
3. **VCF FORMAT field enrichment.** Add standard fields:
    - `GT` — Genotype (0/0, 0/1, 1/1)
    - `AD` — Allele depths (ref_count, alt_count_1, alt_count_2, ...)
    - `DP` — Total depth
    - `GQ` — Genotype quality
    - `AB` — Allele balance
4. **`MultiSampleVcfBuilder`.** Takes variant calls from multiple samples (each processed through either the alignment or De Bruijn pipeline), harmonises variant positions and alleles (using leftmost-anchor normalisation), and produces a valid multi-sample VCF with sample columns.
5. **Population-level annotations.** Calculates allele frequency (AF) across samples, computes genotype quality statistics, and flags variant categories (segregating, monomorphic, rare).

**Impact.** Completes the variant calling pipeline end-to-end: reads --> alignment or assembly --> variant calls --> genotypes --> multi-sample VCF. Transforms BioSharp from a "variant detector" into a complete "variant caller" suitable for population genetics or clinical sample analysis.
**Recommended order:** 2nd (essential for real-world usage; follows Proposal 1 after it is done, or can be parallelised independently).

---

## Recommended Implementation Order

| Priority | Proposal | Rationale |
|----------|----------|-----------|
| 1 | Proposal 2 — Indel Realignment | Closes the most impactful gap (false-positive SNPs near indels). Contained scope; reuses `SmithWatermanAligner` and `VariantCaller` directly. |
| 2 | Proposal 3 — Genotype Calling + Multi-Sample VCF | Completes the pipeline end-to-end. Without genotypes, the VCF output is incomplete. Parallelisable with Proposal 1. |
| 3 | Proposal 1 — De Bruijn CNVs & Complex SVs | Important for the assembly-based pipeline, but the existing bugs should be resolved first. Larger scope; medium-term. |

---

## Dependencies on Existing Code

| New Component | Reuses / Depends On |
|---------------|----------------------|
| `LocalRealigner` | `SmithWatermanAligner`, `VariantCaller`, `AlignmentEvent`, `LocalVariantResult` |
| `VariantReclassifier` | `LocalRealigner`, `VariantCaller.MergeVariants`, `VcfRecordBuilder` |
| Genotype Caller | `LocalVariantResult`, VCF FORMAT field conventions |
| Allele Balance Calculator | `LocalVariantResult`, `AlignmentEvent` |
| `MultiSampleVcfBuilder` | VCF file format, `VcfVariant`, `VariantCallFile`, harmonisation logic |
| CNV / Duplication / Inversion detectors | `DeBruijnGraph`, `KmerNode`, `BubbleFinder`, `TipFinder` |
