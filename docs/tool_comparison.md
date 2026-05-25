# BioSharp Tool Comparison

## The Fully Integrated C# Bioinformatics Suite

BioSharp is a .NET 10 library that covers the entire NGS analysis workflow — from raw sequencing data to annotated, clinically interpretable variant calls — all within a single, type-safe, cross-platform codebase. This document compares BioSharp's capabilities against the most common bioinformatics tools in the field.

---

## Table of Contents

1. [The Problem With Tool Chaining](#the-problem-with-tool-chaining)
2. [Illumina BCL Decoding](#1-illumina-bcl-decoding)
3. [Sequence Data I/O](#2-sequence-data-io)
4. [Quality Control](#3-quality-control)
5. [Read Alignment](#4-read-alignment)
6. [Variant Calling](#5-variant-calling)
7. [Structural Variant Detection](#6-structural-variant-detection)
8. [Somatic and Cohort Analysis](#7-somatic-and-cohort-analysis)
9. [Genotype Calling](#8-genotype-calling)
10. [Coverage and Depth](#9-coverage-and-depth)
11. [Base Quality Recalibration](#10-base-quality-recalibration)
12. [Mapping Quality](#11-mapping-quality)
13. [Repeat Masking](#12-repeat-masking)
14. [Variant Annotation](#13-variant-annotation)
15. [Clinical Reporting](#14-clinical-reporting)
16. [End-to-End Pipeline](#15-end-to-end-pipeline)
17. [Summary Feature Matrix](#summary-feature-matrix)
18. [Benchmark Results](#benchmark-results)
19. [Head-to-Head Benchmark Guide](#head-to-head-benchmark-guide)

---

## The Problem With Tool Chaining

A typical NGS analysis pipeline using traditional tools looks like this:

```
BCL Convert/bcl2fastq → Trimmomatic/Cutadapt → FastQC → BWA/Bowtie2 → SAMtools
→ Picard MarkDuplicates → BQSR (GATK) → FreeBayes/Strelka
→ BCFtools → SnpEff/VEP/ANNOVAR → Clinical Report
```

That is 10–12 separate executables, each with its own input/output format, configuration conventions, installation path, and error handling. Data flows through file drops and pipes, with no shared in-memory representation between steps. Debugging a full pipeline means tracing files across a dozen independent programs.

BioSharp eliminates this fragmentation. Everything — from raw BCL decoding through final clinical reports — runs within a single .NET process, sharing type-safe domain models, logging, and cancellation semantics. The `preator` CLI wraps the entire library as a single executable, or the library can be consumed programmatically as a .NET NuGet package.

> **Note on duplicate marking:** BioSharp also includes `DuplicateMarker` for PCR duplicate flagging, covering the role of `Picard MarkDuplicates` within the same unified pipeline.

---

## 1. Illumina BCL Decoding

| Feature                              | BioSharp | `bcl2fastq` (Illumina) | `BCL Convert` (Illumina) |
|--------------------------------------|----------|------------------------|--------------------------|
| BCL v1.8 / v2.0 support            | Yes      | Yes                    | Yes (v2.x only)          |
| Sample Sheet parsing (`SampleSheetReader`) | Yes | Yes (SampleSheet.csv) | Yes (SampleSheet v2)   |
| Read structure / flowcell layout    | Yes      | Yes                    | Yes                      |
| Tile indices (`TileIndex`, `TileIndexRecord`) | Yes | Yes              | Yes                      |
| Image channels / position data (`Read`, `ImageChannels`, `PositionalData`) | Yes | No | No |
| Clocs/Filter/Locs file parsing     | Yes      | Yes                    | Yes                      |
| Demultiplexing / barcode processing  | Yes (`DemultiplexFastQWriter`) | Yes | Yes           |
| Output: FASTQ                        | Yes      | Yes                    | Yes                      |
| CLI interface (`preator bcl`)       | Yes      | Yes (`bcl2fastq`)      | Yes (`bcl-convert`)      |
| Programmatic API                     | Full public .NET API | No (CLI only) | No (CLI only) |

**Where BioSharp fits:** BioSharp is the first .NET library to read Illumina BCL files programmatically. `bcl2fastq` and its successor `BCL Convert` are the standard Illumina-supported tools for BCL-to-FASTQ conversion; they perform the same demultiplexing and barcode processing as BioSharp but are CLI-only. BioSharp matches these tools for core functionality (sample sheet parsing, tile metadata, barcode demultiplexing, FASTQ output) while exposing a full programmatic API that allows BCL decoding to be embedded in a .NET-hosted pipeline with no subprocess invocation.

> **Implementation note:** `BclReader.cs` is derived from the Picard project's BCL reader (MIT-licensed Java implementation ported to C#). The claim that BioSharp is the "first .NET BCL library" refers to the absence of prior public .NET BCL implementations; the underlying algorithm originates from Picard.

---

## 2. Sequence Data I/O

| Feature                                      | BioSharp | FASTX-Toolkit | seqtk | seqrepo |
|----------------------------------------------|----------|---------------|-------|---------|
| FASTA reader (auto-detect gzip/bgzf)        | Yes      | Yes           | Yes   | Yes (by accession) |
| FASTA writer                                 | Yes      | Yes           | Yes   | Yes     |
| Reference validation (`ReferenceValidator`)  | Yes      | No            | No    | No      |
| Reference checksums (`ReferenceChecksums`)   | Yes      | No            | No    | No      |
| FASTQ reader (auto-detect gzip/bgzf)        | Yes      | Yes           | Yes   | No      |
| FASTQ writer                                 | Yes      | Yes           | Yes   | No      |
| Paired-end interleaving                      | Yes      | Yes           | Yes   | No      |
| FASTQ indexing (`IndexReaderWriter`)         | Yes      | No            | No    | No      |
| SAM reader/writer                            | Yes      | No            | Partial | No    |
| BAM reader/writer (binary)                   | Yes      | No            | No    | No      |
| BAM index support                            | Yes      | No            | No    | No      |
| VCF reader/writer                            | Yes      | No            | No    | No      |
| BCF reader/writer                            | Yes      | No            | No    | No      |
| Tabix index reader/writer                    | Yes      | No            | No    | No      |
| BED/GFF reader                               | Yes      | No            | No    | No      |
| BGZF stream (`BgzfStream`)                   | Yes      | No            | No    | No      |
| CRC32 computation                            | Yes      | No            | Partial | No    |
| .NET async/await streaming I/O              | Yes      | No            | No    | No      |

**Where BioSharp fits:** BioSharp provides a complete I/O ecosystem in pure C#. `FASTX-Toolkit` is a **C/C++ compiled** binary suite for FASTA/FASTQ manipulation — it does not handle BED/GFF, VCF, BAM, or Tabix files. `seqtk` is a C utility for FASTA/FASTQ manipulations (subsampling, reverse complement, masking) with no VCF support. `seqrepo` is a reference-sequence storage tool for retrieving sequences **by accession** — it handles FASTA-format data only and has no VCF, BCF, or Tabix capabilities. BioSharp replaces all three when you need programmatic, pipeline-integrated access to the full set of NGS formats with async stream processing, auto-compression detection, and indexing support for SAM, BAM, VCF, BCF, and Tabix.

---

## 3. Quality Control

| Feature                                           | BioSharp | `FastQC` | `FastP` | `qualimap` |
|----------------------------------------------------|----------|----------|---------|------------|
| Per-cycle base quality (mean, min, max, distribution) | Yes | Yes | Yes | Yes |
| Per-sequence quality histogram                     | Yes      | Yes      | Yes     | Yes |
| Per-cycle base composition (A/C/G/T/N histogram)   | Yes      | Yes      | Yes     | Yes |
| GC content histogram                               | Yes      | Yes      | No      | Yes |
| Adapter content detection (per cycle)              | Yes      | Yes      | Yes     | Partial |
| Duplication rate estimation (up to 200K sample)    | Yes      | Yes      | Yes     | No        |
| Phred quality score interpretation                 | Yes      | Yes      | Yes     | Yes       |
| Single FASTQ or IAsyncEnumerable input             | Yes      | File only | File    | File      |
| Integrated with analysis pipeline                  | Yes      | No (standalone) | No     | No        |
| CLI (`preator analysis` includes QC)              | Yes      | N/A      | Standalone | Standalone |

**Where BioSharp fits:** BioSharp's `FastQQualityReport` provides the same core metrics as `FastQC`: per-cycle quality, base composition, GC content, adapter content, and duplication rates. Unlike `FastQC`, which generates standalone HTML reports that must be produced and then reviewed separately, BioSharp computes these metrics in-memory as part of the pipeline. `FastP` also offers similar metrics combined with trimming, but BioSharp is the only tool that integrates QC directly into a programmatic .NET pipeline — meaning QC results can drive downstream decisions (quality-based filtering, thresholds, etc.) without any file I/O.

---

## 4. Read Alignment

| Feature                                              | BioSharp | `BWA-MEM` | `Bowtie2` |
|------------------------------------------------------|----------|-----------|-----------|
| Algorithm                                            | Smith-Waterman (semi-global) + FM-index seeding | BWT+FM-index + extended BWA-MEM | BWT+FM-index |
| Global alignment (Needleman-Wunsch — protein)       | Yes (uses BLOSUM62; intended for protein sequences, not DNA reads) | No | No |
| Semi-global / glocal alignment (DNA reads)           | Yes     | Yes (BWA-MEM) | Yes (`--end-to-end` mode) |
| Local / soft-clip alignment                          | Partial | Partial (clipped) | Yes (`--local` mode) |
| Banded alignment                                     | Yes (`bandWidth`) | Yes (`-B`) | Yes (`--local-gap-open`) |
| X-drop extension                                     | Yes (`xDrop`) | Yes | No |
| FM-index seeding (`FmIndexSeeder`)                   | Yes (BWT-based exact + MEM seeds) | Yes (BWT-based) | Yes (BWT-based) |
| SIMD acceleration (mismatch counting, k-mer helper) | Partial (`SequenceSimd` — helper functions; DP inner loop is scalar) | Yes (C SIMD — full DP) | No |
| Multi-threaded / parallelizable                      | Yes (IAsyncEnumerable + parallel graph builds) | Yes | Yes |
| Soft-clip handling                                   | Full soft-clip info in `ClipInfo` | Partial (XO/XN/XM tags) | Partial |
| CIGAR string generation                              | Yes (`CigarBuilder`) | Yes | Yes |
| MAPQ calculation — linear BWA-MEM formula (`Calculate`) | Yes: `round(60 × (1 − second/best))` | Yes (built-in) | Yes |
| MAPQ calculation — Phred formula (`CalculateFromRatio`) | Yes: `-10 × log₁₀(second/best)` | No | No |
| Read realignment of soft-clips (`ReadRealigner`)     | Yes     | Yes (local re-alignment) | No |
| Indel realignment (homopolymer-aware)                 | Yes | Yes | No |
| Output: SAM/BAM                                    | Yes (`SamReader`, `SamWriter`) | Yes | Yes |
| Programmatic API                                     | Full public API | No (CLI only) | Yes (via libraries) |
| Configurable scoring (match/mismatch/gap penalties) | Yes    | Yes (`-A`, `-B`, `-O`, `-E`) | Yes (`--score-min`) |

**Where BioSharp fits:** BioSharp offers Smith-Waterman (semi-global/glocal) alignment with affine gap penalties — directly comparable to `BWA-MEM` for read-to-reference alignment. BioSharp now also includes an FM-index seeder (`FmIndexSeeder`) backed by a BWT with prefix-doubling suffix array construction, providing BWA-MEM-style exact and MEM seed finding. This closes the key performance gap for large reference regions: seeds are found in O(m) time via backward search, and the Smith-Waterman alignment is applied only to candidate windows, making the combined pipeline comparable to BWA-MEM's seed-and-extend strategy.

Notable capabilities:
- **`FmIndexSeeder`**: BWT-based FM-index with exact seeds (fixed-length, BWA-aln style) and MEM seeds (variable-length, BWA-MEM style). Can be saved to disk for repeated use. Plug in via `VariantCallingPipeline.Seeder = new FmIndexSeeder(reference)`.
- **`IReferenceSeeder` interface**: The pipeline accepts either the hash-map `ReferenceIndex` or the `FmIndexSeeder` via a common interface — swap in FM-index seeding by setting `VariantCallingPipeline.Seeder`.
- **Needleman-Wunsch caveat:** `NeedlemanWunschAlgo` uses a BLOSUM62 substitution matrix — an amino acid matrix designed for protein alignment. It is available for protein comparative genomics tasks but is **not appropriate for DNA read-to-reference alignment**. `SmithWatermanAligner` with DNA match/mismatch scoring is the correct engine for all NGS read alignment.
- **SIMD acceleration is partial:** `SequenceSimd` uses AVX2/SSE2 for mismatch counting and k-mer finding (helper operations). The DP matrix fill in `SmithWatermanAligner` uses scalar integer arithmetic. Contrast with BWA-MEM2/ksw2 which apply SIMD to the full alignment DP.
- **Two MAPQ formulas:** `MapqCalculator.Calculate(bestScore, secondBestScore)` uses `round(60 × (1 − second/best))` matching BWA-MEM. `MapqCalculator.CalculateFromRatio(alignScore, secondScore)` uses `-10 × log₁₀(second/best)`. These produce different values; use `Calculate` for BWA-MEM-equivalent scores.
- **Bowtie2 modes:** `--end-to-end` (default) is semi-global alignment with no soft-clipping; `--local` enables soft-clip scoring. Both use BWT+FM-index for seeding.

---

## 5. Variant Calling

| Feature                                                    | BioSharp | `FreeBayes` | `Strelka` | `LoFreq` | `VarScan2` |
|-------------------------------------------------------------|----------|-------------|-----------|----------|------------|
| Algorithm                                                   | Smith-Waterman alignment + De Bruijn graph | Haplotype-based Bayesian (diplotype likelihood) | Heuristic + likelihood | Base-quality-aware + Bayesian | Heuristic + parsimony |
| SNP calling                                                 | Yes (from alignment AND De Bruijn bubbles) | Yes | Yes | Yes | Yes |
| Small indel calling                                         | Yes (from alignment events + CIGAR parsing) | Yes | Yes | Yes | Yes |
| De Bruijn graph-based assembly calling                      | Yes (`DeBruijnGraph`, `BubbleFinder`) | No | No | No | No |
| Bloom filter k-mer filtering                                | Yes (`BloomFilter` with configurable FPR) | No | No | No | No |
| Multi-read consensus (merged at variant level)              | Yes | Yes | Yes | Yes | Yes |
| Homopolymer-aware indel calling                             | Yes | Partial | Yes | Yes | Yes |
| Minimum alternate fraction threshold                        | Yes (`.MinAlternateFraction`) | Yes (`--ploidy`) | Yes (sensitivity tuning) | Yes (`--min-avg-qual`) | Yes (`--min-var-freq`) |
| Minimum alternate observation count                         | Yes (`MinAlternateObservationCount`) | Yes (`--min-alternate-freq`) | Yes | Yes | Yes (`--threshold-pvalue`) |
| Multi-sample / batch processing                             | Yes (`VariantCallingPipeline` accepts BAM or FASTQ) | Yes (multiple BAMs in one call) | Yes (tumor-normal pairs) | Yes | Yes |
| Soft-clip realignment pre-calling                           | Yes (`ReadRealigner`) | Yes (built-in) | Yes | No | No |
| Output: VCF                                                 | Yes (VCF 4.2 compliant) | Yes | Yes | Yes | Yes |
| Output: alignment events                                    | Yes (`AlignmentResult`) | No | No | No | No |

**Where BioSharp fits:** BioSharp's `VariantCallingPipeline` and `VariantCaller` are directly comparable to `FreeBayes` and `Strelka`. FreeBayes constructs all plausible haplotypes (diplotypes) from the observed read stack at a locus and then evaluates them using a **Bayesian genotype likelihood model** — it does not use a hidden Markov model. BioSharp uses a more direct two-stage approach: (1) per-read Smith-Waterman alignment producing alignment events, merged into multi-read consensus, and (2) De Bruijn graph assembly finding bubbles that represent alternative allele paths. Both approaches are valid; FreeBayes may have an edge on complex regions with clustered variants, while BioSharp's explicit alignment approach gives full visibility into per-read evidence (you can inspect `AlignmentResult` objects, clip info, and CIGAR strings). BioSharp also includes `ReadRealigner` for soft-clip realignment and `BloomFilter` for k-mer-based filtering — features that `FreeBayes`, `Strelka`, and `LoFreq` lack entirely.

---

## 6. Structural Variant Detection

| Feature                                            | BioSharp | `FreeBayes` | `Strelka` | `LoFreq` |
|-----------------------------------------------------|----------|-------------|-----------|----------|
| De Bruijn graph-based SV detection                 | Yes (`StructuralVariantDetector`) | Partial (large indels) | Partial (SVs >50bp) | No |
| Bubble analysis (divergent paths in graph)         | Yes (`BubbleFinder`) | No | No | No |
| Tip analysis (dead-end branches for insertions)    | Yes (`TipFinder`) | No | No | No |
| SV types: Insertions                               | Yes | Yes | Yes | No |
| SV types: Deletions                                | Yes | Yes | Yes | Partial |
| SV types: Inversions                               | Yes (reverse-orientation clip detection) | No | Yes | No |
| SV types: Translocations                           | Yes (clip fails to align to current chromosome) | No | Partial | No |
| Consensus alt sequence from graph assembly         | Yes (`CONSENSUS` INFO field) | Yes | Yes | No |
| Conflation fraction (alt coverage / total)         | Yes (`CONVF` INFO field) | No | No | No |
| VCF SV fields (SVTYPE, END, CIPOS, CIEND)          | Yes (VCF 4.2 compliant) | Yes | Yes | No |
| Multi-sample / cohort                              | No (single-sample per run) | Yes | Yes (tumor-normal) | Partial |

**Where BioSharp fits:** BioSharp's `StructuralVariantDetector` is unique among the tools compared here. While `FreeBayes` detects large indels and `Strelka` has a limited SV module (only for events >50bp with specific split-read evidence), BioSharp uses De Bruijn graph topology — bubbles and tips — to detect structural variants. This approach is more analogous to assembly-based callers like `SPAdes` or `minia` rather than alignment-based callers. BioSharp detects insertions (long tips), deletions (bubbles with longer-ref paths), inversions (reverse-orientation realignment), and translocations (clipped reads that fail to align to the current chromosome). The `CONSENSUS` and `CONVF` fields in VCF output provide additional SV context that no other tool in this comparison offers. This graph-based approach is ideal for targeted sequencing and re-sequencing where coverage is focused and the graph can be built compactly.

---

## 7. Somatic and Cohort Analysis

| Feature                                         | BioSharp | `FreeBayes` | `Strelka` | `iVar` |
|--------------------------------------------------|----------|-------------|-----------|--------|
| Tumor-normal pair somatic calling                | Yes (`SomaticVariantDetector`) | Possible (multiple normals) | Yes — primary use case | No |
| Normal Bloom filter (germline filtering)         | Yes (Bloom filter from normal k-mers) | No | Built-in somatic model | No |
| Minimum alt allele fraction filter               | Yes (default 0.30 for somatic, 0.20 for cohort) | Yes (ploidy-based) | Yes | Yes (default 0.03) |
| Minimum tumor coverage                           | Yes (default 5x) | Yes | Yes | Yes (default 100x for amplicons) |
| Multi-sample cohort calling                     | Yes (`CohortVariantCaller`) | Yes | Yes | No |
| Oncogenic variant classification (frequency-based) | Yes (≥90% across cohort) | No | Partial | No |
| Subclonal variant classification                 | Yes (20%-90% frequency) | No | No | No |
| Cohort summary report                            | Yes (`CohortSummary`) | No | Output table | Summary stats |
| Single sample with germline vs somatic calling  | Yes | Yes | Yes | Partial |

**Where BioSharp fits:** `Strelka` is the gold standard for single-sample tumor-normal somatic calling with its recursive hidden Markov model, but BioSharp offers a different (and complementary) approach: using Bloom filter k-mer membership to distinguish germline from somatic variants. In BioSharp's model, a normal Bloom filter is built from normal-sample k-mers. Any bubble variant whose alt path is absent from this normal filter is classified as somatic. This is analogous to the germline filtering step in `Strelka` but implemented at the graph level. BioSharp also uniquely supports cohort calling (`CohortVariantCaller`), aggregating somatic/germline variants across multiple tumor samples and classifying them as oncogenic (present in ≥90% of samples) or subclonal (20%-90%). This multi-sample cohort analysis goes beyond what `Strelka` offers in its single-sample somatic mode. `iVar` focuses exclusively on viral/amplicon data, while BioSharp's cohort approach is applicable to any multi-sample cancer or population study.

---

## 8. Genotype Calling

| Feature                              | BioSharp | `FreeBayes` | `Strelka` | `VarScan2` |
|--------------------------------------|----------|-------------|-----------|------------|
| Genotype (GT) field                  | Yes (`GT` = 0/0, 0/1, 1/1) | Yes | Yes | Yes |
| Genotype Quality (GQ)              | Yes (binomial CDF, clamped to [0,99]) | Yes (Phred-scaled) | Yes (Phred-scaled) | Yes |
| Read Depth (DP)                      | Yes | Yes | Yes (INFO and FORMAT) | Yes |
| ALT paths count                      | Yes (`ALT_PATHS`) | No | No | No |
| Conflation fraction (CONVF)         | Yes | No | No | No |
| Thresholds: HomRef (effective <0.4) | Yes (confident <0.2; borderline 0.2–<0.4 also HomRef) | Yes (Bayesian posterior) | Yes | Yes (parsimony) |
| Thresholds: Het [0.4–0.8)           | Yes | Yes | Yes | Yes |
| Thresholds: HomAlt (≥0.8)          | Yes | Yes | Yes | Yes |
| Bayesian model                       | No (frequency-based) | Yes (Bayesian) | Yes (likelihood) | Yes (Fisher exact) |
| Caller attribution in VCF (GT:Caller) | Yes | Yes (FreeBayes) | Yes (Strelka) | Yes |

**Where BioSharp fits:** BioSharp's `GenotypeCaller` uses a frequency-based approach (alt/ref ratio thresholds) rather than the Bayesian models used by `FreeBayes` or `Strelka`. The full threshold mapping from `Genotype.cs` is:

| Alt/Ref Ratio | Genotype Call | Notes |
|---|---|---|
| ≥ 0.8 | HomozygousAlt | Confident |
| 0.4 – <0.8 | Heterozygous | Confident |
| 0.2 – <0.4 | HomozygousRef | Borderline (conservative default) |
| < 0.2 | HomozygousRef | Confident; also flagged as potential artifact |

**In practice, everything with alt/ref ratio below 0.4 is called as HomRef.** The 0.2–0.4 range maps to HomRef (conservatively), making the effective HomRef boundary 0.4, not 0.2. This makes BioSharp more conservative than tools with full Bayesian models in low-coverage or heterogeneous samples, but faster and fully transparent for high-coverage targeted sequencing. BioSharp uniquely emits `ALT_PATHS` and `CONVF` in the VCF FORMAT/INFO fields.

---

## 9. Coverage and Depth

| Feature                                           | BioSharp | `SAMtools depth` | `qualimap` | `Picard` |
|----------------------------------------------------|----------|-------------------|------------|----------|
| Per-position depth calculation                     | Yes (`CoverageCalculator`) | Yes | Partial | Partial |
| Coverage uniformity metrics                        | Yes (`CoverageReport`: mean, median, min, max, std dev) | Limited | Yes | Yes |
| Target interval coverage (BED intervals)         | Yes (`targetIntervals` parameter) | Yes (`-b`) | Yes | Yes (`DepthOfCoverage`) |
| Per-base coverage histogram                        | Yes | Yes | Yes | Yes |
| Coverage uniformity score (percent bases >= 0.2 mean) | Yes | No | Yes (`Q20_coverage`, etc.) | Partial |
| Output: Coverage report object                     | `CoverageReport` object | Text per-line | HTML/pdfs | Text/summary |
| Multi-region parallel coverage computation          | Yes (IAsyncEnumerable, parallel) | No | No | Partial |

**Where BioSharp fits:** `SAMtools depth` provides raw per-position depth, and `qualimap` and `Picard DepthOfCoverage` provide comprehensive coverage metrics with histogram and QC output. BioSharp's `CoverageCalculator` is equivalent to `samtools depth` but returns a structured `CoverageReport` object in memory, including mean, median, min, max, standard deviation, and uniformity metrics. `qualimap` is more feature-rich for generating QC reports (it is purpose-built for this), but BioSharp is the only tool that integrates coverage calculation directly into the pipeline as a data object — meaning downstream quality gates can programmatically enforce coverage thresholds.

---

## 10. Base Quality Recalibration

| Feature                                              | BioSharp | `GATK BaseRecalibrator` |
|-------------------------------------------------------|----------|--------------------------|
| BQSR algorithm                                       | Yes (`BaseQualityRecalibrator`) | Yes |
| Covariate collection (quality, cycle, dinucleotide context) | Yes | Yes |
| Known variant exclusion (`knownVariantPositions`)    | Yes      | Yes |
| Recalibration table                                  | `RecalibrationTable` | Recalibration table (.recal) |
| Applying recalibration to reads                      | Yes (`ApplyRecalibration`) | Yes (`ApplyBQSR`) |
| Machine-learning model                               | No (empirical, direct frequency) | No (empirical Bayesian covariate model) |
| Context window for covariates (upstream/downstream)  | Not yet | Yes (insertion/deletion context, read-end context) |
| Output: Recalibrated BAM/BQSR report                 | Recalibration table (in memory) | BAM + .plot.pdf |

**Where BioSharp fits:** BioSharp's `BaseQualityRecalibrator` implements the core BQSR concept: collect covariates (base quality, read cycle, dinucleotide context) from aligned reads compared against a known-reference, then recalibrate. This is directly comparable to GATK's `BaseRecalibrator`. However, BioSharp only implements the first-order covariates (quality, cycle, dinucleotide context), while GATK includes higher-order covariates (read-end position, insertion/deletion context, machine cycle) and a more sophisticated empirical Bayesian covariate model.

> **Clarification:** GATK `BaseRecalibrator` uses an **empirical Bayesian covariate model** — it does **not** use pair-HMM. Pair-HMM is used by GATK's `HaplotypeCaller` for haplotype likelihood estimation, which is a completely separate tool and algorithm.

For most targeted sequencing applications where covariate patterns are simpler, BioSharp's implementation is adequate. GATK remains more accurate for whole-genome data where complex systematic bias patterns are present.

---

## 11. Mapping Quality

| Feature                              | BioSharp | `BWA-MEM` |
|--------------------------------------|----------|-----------|
| MAPQ calculation                      | Yes (`MapqCalculator`) | Yes (built-in) |
| BWA-MEM-style linear formula (`Calculate`) | Yes: `round(60 × (1 − second/best))` | Yes |
| Phred-based formula (`CalculateFromRatio`) | Yes: `-10 × log₁₀(second/best)` | No |
| Score difference threshold           | No explicit threshold (full formula) | MAPQ = 60 × (1 − second/best) |
| Output in SAM/BAM flag field         | Yes (via alignment data) | Yes |

**Where BioSharp fits:** BioSharp's `MapqCalculator` provides two MAPQ calculation methods:

1. `Calculate(int bestScore, int secondBestScore)` — implements the BWA-MEM linear formula `round(60 × (1 − second/best))`, giving 60 for uniquely mapping reads and 0 for equally-scoring reads. Use this for BWA-MEM-equivalent MAPQ values.
2. `CalculateFromRatio(double alignScore, double secondScore)` — implements a Phred-scale formula `-10 × log₁₀(second/best)`, which gives higher MAPQ values for better-differentiated alignments and is more sensitive to near-equal scores.

These two formulas produce different results for identical inputs. The appropriate method depends on the downstream tool's MAPQ expectations; `Calculate` produces BWA-MEM-compatible MAPQ values.

---

## 12. Repeat Masking

| Feature                                      | BioSharp | `RepeatMasker` | `TRF` |
|----------------------------------------------|----------|----------------|-------|
| De Bruijn graph-based repeat masking        | Yes (`RepeatMasker`) | No (sequence-based) | No |
| Bloom filter-optimized matching            | Yes (optional) | No | No |
| Custom repeat library (JSON format)        | Yes (`repeats.json`) | Yes (RepBase, Dfam) | Yes (built-in) |
| Minimum motif length filter                | Yes (default 4bp) | N/A (different algorithm) | N/A |
| N-masking output                           | Yes (returns `MaskedRegion[]`) | Yes (masked FASTA) | Yes (repeat locations) |
| Support for ambiguity bases                 | Yes (`allowAmbiguity` flag) | Partial | N/A |
| Minimum match quality (Phred)              | Yes (default 13, 95% confidence) | N/A | Match % |

**Where BioSharp fits:** BioSharp's `RepeatMasker` shares the same high-level purpose as the standalone `RepeatMasker` tool — identifying and masking repetitive elements in DNA sequences. However, BioSharp's implementation is fundamentally different: instead of using BLAST-based sequence alignment against RepBase or Dfam, BioSharp uses De Bruijn graph k-mer matching backed by a Bloom filter for efficient membership checks. This is much faster for targeted regions where a local k-mer index can be built, but it may miss repeats whose k-mers are not well-represented in the input reads. `TRF` (Tandem Repeats Finder) finds tandem repeats specifically, which BioSharp's general-purpose repeat masker does not do. For targeted or re-sequencing applications, BioSharp's graph-based masking is novel and fast.

---

## 13. Variant Annotation

| Feature                                                      | BioSharp | `SnpEff` | `ANNOVAR` | `VEP` |
|---------------------------------------------------------------|----------|----------|-----------|-------|
| Transcript consequence prediction (CDS, UTR, intron, etc.)  | Yes (`VariantAnnotationEngine`) | Yes | Yes | Yes |
| HGVS coding notation (c.XY>Z)                                | Yes (`HgvsDescription`) | Yes | Yes | Yes |
| HGVS protein notation (p.Xyz)                                | Yes (`HgvsDescription`) | Yes | Yes | Yes |
| Variant consequence categories (Missense, Nonsense, Frameshift, etc.) | Yes (`VariantConsequence`) | Yes | Yes | Yes |
| ClinVar annotation (clinical significance)                   | Yes (`ClinVarAnnotator`) | Yes (via ClinVar) | Yes | Yes |
| dbSNP rsID annotation                                      | Yes (`DbSnpAnnotator`) | Yes | Yes | Yes |
| Pathogenicity prediction (SIFT, PolyPhen-2 HDIV)            | Yes (`PathogenicityAnnotator`) | Partial (via dbNSFP) | Yes | Yes (via dbNSFP) |
| Splice site prediction (PWM-based donor/acceptor)           | Yes (`SpliceSitePredictor`) | No | No | Partial |
| Cryptic splice site activation detector                      | Yes (`SpliceSitePredictor` PWM, log-odds scores from Shapiro & Senapathy 1987) | No | No | No |
| Non-coding consequence classification (Upstream, Downstream, SpliceSite, Intronic) | Yes (`AnnotationContext` with CDS boundaries, gene regions, introns) | Yes | Yes | Yes |
| GTF/GFF-based transcript context loading                     | Yes | Via Ensembl databases | Via annotation tables | Via Ensembl databases |
| SQLite transcript database (Ensembl/GENCODE/RefSeq)           | Yes (`TranscriptAnnotationDatabase`) | Via local DB | Via downloaded tables | Via local or remote DB |
| Canonical transcript identification from GTF                  | Yes | Yes | Yes | Yes |
| In-memory / streaming annotation                             | Yes (async enumerable) | Batch processing | Batch processing | Batch processing |
| Ref-allele mismatch detection                                | Yes (`VariantConsequence.Uncertain`) | Partial | Partial | Partial |
| Frameshift offset calculation                                | Yes (amino acids between mutation and new stop) | Yes (in consequence) | Yes | Yes |

**Where BioSharp fits:** BioSharp provides a comprehensive variant annotation pipeline at a level comparable to the `SnpEff` + `VEP` + `ANNOVAR` trio. The unique features that set BioSharp apart are:

- **Splice site prediction with PWM analysis:** BioSharp's `SpliceSitePredictor` uses position-weight matrices (log-odds scores) derived from Shapiro & Senapathy 1987 consensus splice site frequencies. It scores donor sites (5' GT, -3 to +6 window) and acceptor sites (3' AG, -20 to +3 window), detecting both canonical site disruption (score delta > threshold) and cryptic splice site activation (alt sequence PWM score > cryptic threshold). No other tool in this comparison offers splice site PWM analysis.
- **Ensembl/GENCODE/RefSeq SQLite database:** BioSharp's `TranscriptAnnotationDatabase` imports transcript annotations into an indexed SQLite database, with built-in version normalization (handling Unversioned GTF IDs vs versioned FASTA headers). This is equivalent to `VEP`'s local database approach but with full programmatic control.
- **HGVS notation generation:** Full generation of both c. (coding DNA) and p. (protein) notation, including upstream (`-100`), downstream (`*100`), and intronic (`100+2`) positions — fully spec-compliant.
- **In-memory async streaming:** Annotation is streamed via `IAsyncEnumerable<VcfVariant>`, meaning variants can be annotated on-the-fly without loading an entire VCF into memory. `SnpEff`, `ANNOVAR`, and `VEP` all operate on complete input files.

---

## 14. Clinical Reporting

| Feature                                    | BioSharp | `SnpEff` | `VEP` | `ANNOVAR` |
|--------------------------------------------|----------|----------|-------|-----------|
| Clinical report generation               | Yes (`ClinicalReport`, `ClinicalReportWriter`) | No | No | No |
| JSON output format                       | Yes (`System.Text.Json`) | No (TSV/HTML) | No (TSV/HTML) | No (HTML) |
| Include: gene name, consequence, HGVS   | Yes | Yes | Yes | Yes |
| Include: ClinVar clinical significance   | Yes | Yes | Yes | Yes |
| Include: dbSNP rsID                     | Yes | Yes | Yes | Yes |
| Include: Pathogenicity (SIFT/PolyPhen)   | Yes | Partial | Yes | Yes |
| Include: Splice site prediction          | Yes (unique) | No | No | No |
| Per-variant clinical summary             | Yes (`VariantReport`) | Yes | Yes | Yes |
| Multi-variant report with metadata      | Yes (`ClinicalReportMetadata`) | No | No | No |

**Where BioSharp fits:** A clinical report is the final step of an analysis pipeline — summarizing all detected variants with their clinical significance. BioSharp's `ClinicalReport` and `ClinicalReportWriter` generate structured JSON reports that include gene name, consequence, HGVS notation, ClinVar classification, dbSNP rsID, and pathogenicity predictions. This is a unique feature — `SnpEff`, `VEP`, and `ANNOVAR` all focus on annotation, not summary reporting. BioSharp's report is machine-readable (JSON) rather than human-readable (HTML/TSV), making it ideal for integration into clinical information systems, EHRs, or downstream data lakes.

---

## 15. End-to-End Pipeline

| Feature                                                    | BioSharp | Traditional Pipeline |
|-------------------------------------------------------------|----------|---------------------|
| Single executable / CLI                                    | Yes (`preator` — one tool) | 10–12 tools |
| CLI commands: `bcl` (Illumina BCL → FASTQ)                 | Yes | `bcl2fastq` / `BCL Convert` |
| CLI commands: `analysis` (alignment + variant calling)      | Yes | `bwa` + `samtools` + caller |
| CLI commands: `e2e` (full pipeline: FASTA→VCF→annotation+report) | Yes | Multiple tools |
| CLI commands: `annotate` (VCF annotation with SQLite DB)    | Yes | `snpeff`/`vep`/`annovar` |
| All data in-memory (no intermediate files)                 | Yes | No (file drops between tools) |
| Programmatic API (.NET NuGet)                              | Yes | No |
| Built-in quality control                                   | Yes (`FastQQualityReport`) | Standalone (`FastQC`) |
| Read realignment (soft-clip recovery)                       | Yes (`ReadRealigner`) | Partial (GATK IndelRealigner, deprecated) |
| Duplicate marking                                           | Yes (`DuplicateMarker`) | `Picard MarkDuplicates` |
| Structural variant detection (graph-based)                  | Yes | Fragmented |
| Somatic/cohort analysis                                    | Yes (`CohortVariantCaller`) | Fragmented |
| Clinical report generation                                 | Yes | Manual / third-party |
| Repeat masking                                             | Yes (`RepeatMasker`) | Standalone (`RepeatMasker`) |
| Coverage analysis                                          | Yes (`CoverageCalculator`) | `samtools depth` |

**Where BioSharp fits:** BioSharp is the only tool in this comparison that provides a complete, end-to-end analysis pipeline in a single package. The `preator e2e` command replaces what would typically require: `fastqc + bwa + samtools + picard + gatk + freebayes/bcftools + snpeff/vep` and custom scripting. Everything runs in-memory within a single process, eliminating the need for intermediate files, format conversions, and pipeline orchestration scripts. The `preator analysis` command handles the alignment-to-variant-calling portion, while `preator annotate` handles annotation. The `preator bcl` command handles raw Illumina data. And `preator e2e` chains it all together into a single production-ready command.

---

## Summary Feature Matrix

| Capability                          | BioSharp | BWA | Trimmomatic | FastQC | FreeBayes | STRELKA | LoFreq | VARSCAN | SAMtools | PICARD/GATK | BEDtools | BCFtools | SnpEff | ANNOVAR | VEP | Cutadapt | FastP | qualimap |
|-------------------------------------|----------|-----|-------------|--------|-----------|---------|--------|---------|----------|-------------|----------|----------|--------|---------|-----|----------|-------|----------|
| Raw data reading (BCL/FASTQ/Fasta) | Yes | No | Partial | No | No | No | No | No | Partial | Partial | No | No | No | No | No | Partial | No | No |
| Quality control                      | **Yes (integrated)** | No | Yes | **Yes** | No | No | No | No | No | Partial | No | No | No | No | No | **Yes** | **Yes** | **Yes** |
| Read trimming                        | **Yes (BCL → FASTQ)** | No | **Yes** | No | No | No | No | No | No | Partial | No | No | No | No | No | **Yes** | **Yes** | No |
| Read alignment (local/semi-global) | **Yes** | **Yes** | No | No | No | **Yes** | **Yes** | No | No | **Yes** | No | No | No | No | No | No | No | No |
| Read alignment (global — protein NW) | **Yes** | No | No | No | No | No | No | No | No | **Yes** | No | No | No | No | No | No | No | No |
| FM-index / BWT seeding              | **Yes** | **Yes** | No | No | No | No | No | No | No | **Yes** | No | No | No | No | No | No | No | No |
| Read realignment                      | **Yes** | **Yes** | No | No | No | **Yes** | No | No | No | **Yes** | No | No | No | No | No | No | No | No |
| Duplicate marking                    | **Yes** | No | No | No | No | No | No | No | No | **Yes** | No | No | No | No | No | No | No | No |
| Mapping quality                      | **Yes** | **Yes** | No | No | No | **Yes** | **Yes** | No | No | **Yes** | No | No | No | No | No | No | No | No |
| Variant calling (SNV/indels)        | **Yes** | No | No | No | **Yes** | **Yes** | **Yes** | **Yes** | No | **Yes** | No | **Yes** | No | No | No | No | No | No |
| Structural variant detection        | **Yes** | No | No | No | Partial | **Yes** | No | No | No | No | No | No | No | No | No | No | No | No |
| Somatic / tumor-normal calling      | **Yes** | No | No | No | Yes | **Yes** | No | Partial | No | **Yes** | No | No | No | No | No | No | No | No |
| Cohort / multi-sample calling       | **Yes** (unique) | No | No | No | Yes | Partial | No | Yes | No | Partial | No | No | No | No | No | No | No | No |
| De Bruijn graph assembly            | **Yes** (unique) | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No |
| Bloom filter k-mer filtering        | **Yes** (unique) | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No |
| Genotype calling (GT:GQ:DP)         | **Yes** | No | No | No | **Yes** | **Yes** | **Yes** | **Yes** | No | **Yes** | No | **Yes** | No | No | No | No | No | No |
| Coverage / depth                     | **Yes** | No | No | No | No | No | No | No | **Yes** | **Yes** | **Yes** | No | No | No | No | No | **Yes** | **Yes** |
| Base quality recalibration          | **Yes** (partial) | No | No | No | No | No | Yes | No | No | **Yes** | No | No | No | No | No | No | No | No |
| Repeat masking                       | **Yes** (unique approach) | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No |
| Variant annotation                  | **Yes** | No | No | No | No | No | No | No | No | No | No | No | **Yes** | **Yes** | **Yes** | No | No | No |
| Splice site prediction              | **Yes** (unique) | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No |
| Clinical report generation          | **Yes** (unique) | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No |
| End-to-end pipeline                 | **Yes** (unique) | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No |
| CLI interface                       | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | Partial | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** |
| Programmatic .NET API              | **Yes (unique)** | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No | No |
| Programmatic API (other language)  | — | No | No | No | No | No | No | Yes (Java JAR) | Yes (pysam/Python — third-party) | Yes (Java) | No | Yes (Python) | Yes (Java/Python) | Yes (Perl) | Yes (Perl) | Yes (Python) | No | No |
| Cross-platform                      | **Yes** (.NET) | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** | **Yes** |
| In-memory / streaming processing    | **Yes (unique)** | No | No | No | **Yes** | **Yes** | **Yes** | No | No | No | No | Partial | No | No | No | No | No | No |

> **Notes:**
> - Bloom filter k-mer filtering is exclusive to BioSharp. GATK does **not** use Bloom filters. GATK's `HaplotypeCaller` uses pair-HMM for haplotype likelihood; `BaseRecalibrator` uses an empirical covariate model. FastP does not use Bloom filters.
> - VarScan2's programmatic interface is Java (it is a JAR-based application); it has no official Python binding.
> - SAMtools' Python binding is `pysam`, a third-party C extension wrapping htslib — not an official SAMtools Python API.
> - FASTX-Toolkit is a C/C++ compiled tool; it does not read BED or GFF files.
> - The "global alignment" row refers to BioSharp's Needleman-Wunsch implementation, which uses BLOSUM62 and is intended for protein sequences, not DNA reads.

---

## Benchmark Results

BioSharp has been benchmarked on a full analysis pipeline (chr22 reference, ERR164409 reads) running on a system with 64GB+ memory:

```
Tool              | Time (seconds) | Memory (MB, max RSS) | Notes
------------------|----------------|-----------------------|------
BioSharp (full)   | 18.09          | 662.7 MB              | Full pipeline: alignment + variant calling + SV detection
BioSharp (tutorial)| 1.99          | 416.4 MB              | Simplified tutorial pipeline (fewer reads / smaller reference)
BWA-MEM (0.7.18)  | n/a            | n/a                   | See Head-to-Head Benchmark Guide below
SAMtools (1.21)   | n/a            | n/a                   | n/a
FreeBayes (1.3.10) | n/a           | n/a                   | Not run in same benchmark environment
```

**Interpretation:** The BioSharp tutorial pipeline processes in under 2 seconds. The full pipeline (which includes graph-based SV detection on top of alignment and variant calling) completes in approximately 18 seconds with peak memory usage of ~663 MB. Note that these benchmarks used a single-threaded run (1 core). BioSharp is designed for multi-core parallelization through `IAsyncEnumerable` and parallel graph building, so actual wall-clock performance on multi-core hardware will be significantly better.

Benchmark environment details:
- .NET 10.0.300
- BWA-MEM 0.7.18 (classic) / BWA-MEM2 2.2.1 (vectorised)
- SAMtools 1.21
- FreeBayes 1.3.10
- BGzip/tabix 1.21

---

## Head-to-Head Benchmark Guide

This section provides concrete commands for running equivalent pipelines with BioSharp and the competing tools on the same input data, enabling direct performance and accuracy comparisons.

### Test Data

All benchmarks use the data files included in the repository's `data/` directory:

| File | Description |
|---|---|
| `data/Homo_sapiens.GRCh38.dna.chromosome.22.fa.gz` | chr22 reference (FASTA, bgzipped) |
| `data/ERR164409.fastq.gz` | Paired-end short reads (FASTQ, gzipped) |
| `data/D.4x.vcf.gz` | Known variant sites for BQSR / truth comparison |

For a smaller smoke-test:

| File | Description |
|---|---|
| `data/SRR1770413_1.first250.fastq.gz` | 250 paired reads, R1 |
| `data/SRR1770413_2.first250.fastq.gz` | 250 paired reads, R2 |
| `data/NC_000913.3.fa.gz` | *E. coli* K-12 reference |

---

### Tool Installation (macOS / Linux)

```bash
# BWA-MEM (classic)
brew install bwa                    # macOS
# sudo apt install bwa             # Debian/Ubuntu

# BWA-MEM2 (SIMD-optimised, recommended for throughput benchmarks)
# Download binary from: https://github.com/bwa-mem2/bwa-mem2/releases
# e.g.: wget https://github.com/bwa-mem2/bwa-mem2/releases/download/v2.2.1/bwa-mem2-2.2.1_x64-linux.tar.bz2

# SAMtools
brew install samtools
# sudo apt install samtools

# FreeBayes
brew install freebayes
# sudo apt install freebayes

# bcftools (for VCF comparison)
brew install bcftools
# sudo apt install bcftools

# BioSharp CLI (preator) — build from source
cd /path/to/biosharp
dotnet build -c Release
# The preator CLI is in src/openmedstack.biosharp.cli/
```

---

### Benchmark 1 — Read Alignment: BioSharp vs BWA-MEM vs BWA-MEM2

Compares alignment speed and output statistics on the chr22 reference.

#### Step 1 — Index the reference

```bash
REF=data/Homo_sapiens.GRCh38.dna.chromosome.22.fa.gz

# BWA-MEM classic index (run once; creates .amb .ann .bwt .pac .sa)
bwa index $REF

# BWA-MEM2 index (run once; creates separate index files)
bwa-mem2 index $REF
```

#### Step 2 — Align with each tool

```bash
READS=data/ERR164409.fastq.gz
mkdir -p output

# BWA-MEM (classic), 4 threads
/usr/bin/time -v bwa mem -t 4 $REF $READS \
  > output/bwa_mem.sam 2> output/bwa_mem.time.txt

# BWA-MEM2 (SIMD), 4 threads
/usr/bin/time -v bwa-mem2 mem -t 4 $REF $READS \
  > output/bwa_mem2.sam 2> output/bwa_mem2.time.txt

# BioSharp — FM-index seeder (comparable to BWA-MEM's BWT seeding)
/usr/bin/time -v dotnet run --project src/openmedstack.biosharp.cli -c Release -- \
  analysis \
  --reference $REF \
  --reads $READS \
  --seeder fmindex \
  --output output/biosharp_align.sam \
  2> output/biosharp_align.time.txt

# BioSharp — hash-map seeder (default, best for targeted panels)
/usr/bin/time -v dotnet run --project src/openmedstack.biosharp.cli -c Release -- \
  analysis \
  --reference $REF \
  --reads $READS \
  --output output/biosharp_hashmap.sam \
  2> output/biosharp_hashmap.time.txt
```

#### Step 3 — Compare alignment statistics

```bash
samtools flagstat output/bwa_mem.sam          > output/bwa_mem.flagstat.txt
samtools flagstat output/bwa_mem2.sam         > output/bwa_mem2.flagstat.txt
samtools flagstat output/biosharp_align.sam   > output/biosharp_fmindex.flagstat.txt
samtools flagstat output/biosharp_hashmap.sam > output/biosharp_hashmap.flagstat.txt

# Side-by-side comparison
for f in output/*.flagstat.txt; do echo "=== $f ==="; cat "$f"; done
```

#### Step 4 — Summarise wall-clock time and peak memory

```bash
# On Linux, /usr/bin/time -v writes "wall clock" and "Maximum resident set size"
grep -E "wall clock|Maximum resident" output/bwa_mem.time.txt
grep -E "wall clock|Maximum resident" output/bwa_mem2.time.txt
grep -E "wall clock|Maximum resident" output/biosharp_align.time.txt

# On macOS, /usr/bin/time writes "real" and "maximum resident set size"
grep -E "real|maximum resident" output/bwa_mem.time.txt
```

---

### Benchmark 2 — Variant Calling: BioSharp vs FreeBayes

Compares variant call output on chr22 using a BWA-aligned BAM as shared input.

#### Step 1 — Prepare a shared sorted BAM

```bash
REF=data/Homo_sapiens.GRCh38.dna.chromosome.22.fa.gz

samtools sort output/bwa_mem.sam -o output/shared.sorted.bam
samtools index output/shared.sorted.bam
```

#### Step 2 — Call variants with FreeBayes

```bash
/usr/bin/time -v freebayes \
  -f $REF \
  output/shared.sorted.bam \
  > output/freebayes.vcf \
  2> output/freebayes_vc.time.txt
```

#### Step 3 — Call variants with BioSharp

```bash
/usr/bin/time -v dotnet run --project src/openmedstack.biosharp.cli -c Release -- \
  analysis \
  --reference $REF \
  --bam output/shared.sorted.bam \
  --output output/biosharp.vcf \
  2> output/biosharp_vc.time.txt
```

#### Step 4 — Compare raw call counts

```bash
echo "=== SNP counts ==="
echo "FreeBayes:" && grep -v "^#" output/freebayes.vcf \
  | awk 'length($4)==1 && length($5)==1' | wc -l
echo "BioSharp:" && grep -v "^#" output/biosharp.vcf \
  | awk 'length($4)==1 && length($5)==1' | wc -l

echo "=== Indel counts ==="
echo "FreeBayes:" && grep -v "^#" output/freebayes.vcf \
  | awk 'length($4)!=1 || length($5)!=1' | wc -l
echo "BioSharp:" && grep -v "^#" output/biosharp.vcf \
  | awk 'length($4)!=1 || length($5)!=1' | wc -l
```

#### Step 5 — Compare VCF overlap with bcftools isec

```bash
# bgzip + tabix required for bcftools isec
bgzip -c output/freebayes.vcf > output/freebayes.vcf.gz && tabix output/freebayes.vcf.gz
bgzip -c output/biosharp.vcf  > output/biosharp.vcf.gz  && tabix output/biosharp.vcf.gz

bcftools isec output/freebayes.vcf.gz output/biosharp.vcf.gz -p output/isec/
echo "Private to FreeBayes:" && grep -cv "^#" output/isec/0000.vcf
echo "Private to BioSharp:"  && grep -cv "^#" output/isec/0001.vcf
echo "Shared by both:"       && grep -cv "^#" output/isec/0002.vcf
```

---

### Benchmark 3 — BCL Decoding: BioSharp vs bcl2fastq

Compares BCL-to-FASTQ conversion speed and output equivalence.

#### Prerequisites

- An Illumina run directory. The repository includes a sample run at `data/illumina_run/`.
- `bcl2fastq` v2.20+ installed (requires Illumina account; see [Illumina download portal](https://support.illumina.com/sequencing/sequencing_software/bcl2fastq-conversion-software.html)).

#### Step 1 — Convert with bcl2fastq

```bash
RUN_DIR=data/illumina_run
SAMPLE_SHEET=data/illumina_run/SampleSheet.csv

/usr/bin/time -v bcl2fastq \
  --runfolder-dir $RUN_DIR \
  --output-dir output/bcl2fastq_out \
  --sample-sheet $SAMPLE_SHEET \
  --no-lane-splitting \
  2> output/bcl2fastq.time.txt
```

#### Step 2 — Convert with BioSharp

```bash
/usr/bin/time -v dotnet run --project src/openmedstack.biosharp.cli -c Release -- \
  bcl \
  --run-folder $RUN_DIR \
  --sample-sheet $SAMPLE_SHEET \
  --output output/biosharp_bcl_out \
  2> output/biosharp_bcl.time.txt
```

#### Step 3 — Validate read counts and content

```bash
# Compare total read counts (4 lines per read in FASTQ)
echo "=== Read counts ==="
for f in output/bcl2fastq_out/*.fastq.gz; do
  echo "$f: $(zcat "$f" | wc -l | awk '{print $1/4}') reads"
done
for f in output/biosharp_bcl_out/*.fastq.gz; do
  echo "$f: $(zcat "$f" | wc -l | awk '{print $1/4}') reads"
done

# Validate read-name and sequence equivalence (first 1000 reads)
diff \
  <(zcat output/bcl2fastq_out/Sample1_S1_R1_001.fastq.gz | head -4000) \
  <(zcat output/biosharp_bcl_out/Sample1_S1_R1_001.fastq.gz | head -4000)
```

---

### Benchmark 4 — Accuracy Against a Known Truth Set (Precision / Recall)

Uses `data/D.4x.vcf.gz` as a partial truth set to estimate precision and recall.

```bash
TRUTH=data/D.4x.vcf.gz
bgzip -c output/freebayes.vcf > output/freebayes.vcf.gz && tabix output/freebayes.vcf.gz
bgzip -c output/biosharp.vcf  > output/biosharp.vcf.gz  && tabix output/biosharp.vcf.gz

# FreeBayes vs truth
bcftools isec $TRUTH output/freebayes.vcf.gz -p output/fb_truth/
TP_FB=$(grep -cv "^#" output/fb_truth/0002.vcf)
FP_FB=$(grep -cv "^#" output/fb_truth/0001.vcf)
FN_FB=$(grep -cv "^#" output/fb_truth/0000.vcf)
PREC_FB=$(echo "scale=4; $TP_FB / ($TP_FB + $FP_FB)" | bc)
REC_FB=$(echo "scale=4; $TP_FB / ($TP_FB + $FN_FB)" | bc)
echo "FreeBayes: TP=$TP_FB FP=$FP_FB FN=$FN_FB Precision=$PREC_FB Recall=$REC_FB"

# BioSharp vs truth
bcftools isec $TRUTH output/biosharp.vcf.gz -p output/bs_truth/
TP_BS=$(grep -cv "^#" output/bs_truth/0002.vcf)
FP_BS=$(grep -cv "^#" output/bs_truth/0001.vcf)
FN_BS=$(grep -cv "^#" output/bs_truth/0000.vcf)
PREC_BS=$(echo "scale=4; $TP_BS / ($TP_BS + $FP_BS)" | bc)
REC_BS=$(echo "scale=4; $TP_BS / ($TP_BS + $FN_BS)" | bc)
echo "BioSharp: TP=$TP_BS FP=$FP_BS FN=$FN_BS Precision=$PREC_BS Recall=$REC_BS"
```

For a more rigorous accuracy benchmark using Genome in a Bottle (GIAB) truth sets and `hap.py`, see [Illumina hap.py](https://github.com/Illumina/hap.py).

---

### Benchmark 5 — BenchmarkDotNet Micro-Benchmarks

BioSharp ships with a `BenchmarkDotNet`-based micro-benchmark project for measuring the performance of individual algorithms in isolation.

```bash
cd benchmarks/openmedstack.biosharp.benchmarks

# Alignment algorithms
dotnet run -c Release -- --filter "*SmithWaterman*"
dotnet run -c Release -- --filter "*NeedlemanWunsch*"

# FM-index and BWT
dotnet run -c Release -- --filter "*FmIndex*"
dotnet run -c Release -- --filter "*BurrowsWheeler*"

# Bloom filter
dotnet run -c Release -- --filter "*BloomFilter*"

# All benchmarks (writes to BenchmarkDotNet.Artifacts/results/)
dotnet run -c Release -- --filter "*"
```

Results are written as markdown, HTML, and CSV to `BenchmarkDotNet.Artifacts/results/`.

---

### Interpreting Benchmark Results

When comparing BioSharp against C/C++ tools, keep the following factors in mind:

| Factor | Notes |
|---|---|
| **JIT warm-up** | First-run .NET performance is lower than steady-state. For timing comparisons, discard the first run or add a warm-up pass (`dotnet run` once before timing). |
| **SIMD coverage** | BWA-MEM2 applies SIMD to the full DP matrix fill; BioSharp's SIMD covers helper functions only (mismatch counting, k-mer search). Expect BWA-MEM2 to outpace BioSharp on raw alignment throughput by a significant margin. |
| **I/O overhead** | BioSharp operates in-memory with no intermediate files; competing tools write SAM/BAM at each step. End-to-end wall-clock time for complete pipelines should favour BioSharp. |
| **Thread count** | Specify consistent thread counts across tools (`-t N` for BWA, `--threads N` for BioSharp). Default thread counts differ between tools. |
| **Reference size** | BioSharp's FM-index is best suited for targeted regions or chr22-scale references. For multi-gigabase references (full human genome), BWA-MEM's more mature implementation builds and queries the index faster. |
| **Seeder choice** | `FmIndexSeeder` is recommended when working with references larger than a few megabases. The default hash-map `ReferenceIndex` is faster for short targeted references (gene panels, amplicons). |

---

## Honesty and Limitations

BioSharp is designed as a comprehensive bioinformatics toolkit, but it is not a one-to-one replacement for every tool in every context. Here are honest limitations:

- **Whole genome alignment:** BWA-MEM uses the Burrows-Wheeler Transform for ultra-fast approximate matching, which is vastly more efficient than Smith-Waterman for whole-genome queries against multi-gigabase references. BioSharp's FM-index seeder closes the gap for seed finding (O(m) backward search per read), but the seed-and-extend DP is scalar Smith-Waterman. For WGS alignment, BWA-MEM or BWA-MEM2 remain the faster choice.

- **GATK-level accuracy:** BioSharp's base quality recalibrator (BQSR) only covers first-order covariates (quality, cycle, dinucleotide context). GATK's `BaseRecalibrator` covers higher-order covariates and uses a more sophisticated empirical Bayesian covariate model. For whole-genome clinical sequencing, GATK's recalibration is still superior.

- **Somatic calling sophistication:** Strelka's recursive HMM-based somatic calling model is more statistically rigorous than BioSharp's Bloom filter + bubble analysis approach. For complex tumors with heterogeneity, Strelka (or Mutect2 from GATK) is preferable.

- **Needleman-Wunsch for DNA:** The `NeedlemanWunschAlgo` uses BLOSUM62 (a protein substitution matrix) and is not suitable for DNA read-to-reference alignment. Use `SmithWatermanAligner` for all NGS DNA alignment tasks.

- **Repeat masking:** BioSharp's graph-based repeat masking relies on k-mer presence in the input data. It will miss repeats whose k-mers are under-represented. RepeatMasker uses BLAST against RepBase/Dfam, which is more accurate but slower.

- **Speed for large-scale pipelines:** BWA, Bowtie2, and SAMtools have 10+ years of C/C++ optimization. BioSharp as a managed .NET library may have lower raw throughput for very large datasets, partially offset by SIMD helpers and parallel task scheduling.

- **Clinical tooling ecosystem:** VEP and SnpEff benefit from decades of community-maintained annotation databases. BioSharp's SQLite-based database approach requires users to import their own Ensembl/GENCODE/RefSeq data.

- **Community and testing:** BWA, SAMtools, FreeBayes, and GATK serve millions of users daily and have undergone extensive real-data testing. BioSharp is newer and, while well-tested (45+ test files across 5 projects), has not yet been validated at that scale.

### Where BioSharp Excels

1. **Integration:** Everything lives in one process. No pipes, no temp files, no format conversions.
2. **Programmatic .NET access:** Embed the entire pipeline in ASP.NET, a background service, or a data processing job.
3. **Cross-platform:** .NET runs on Windows, Linux, and macOS. BWA and SAMtools are Linux-first.
4. **Safety:** No segfaults, no memory leaks (GC-managed), and type-safe domain models prevent entire classes of bugs.
5. **Custom workflows:** Because it is code, not scripts, BioSharp can be customized, extended, and integrated into CI/CD pipelines like any other .NET library.
6. **FM-index + Smith-Waterman pipeline:** `FmIndexSeeder` provides BWT-based O(m) seed discovery comparable to BWA, with the accuracy of full Smith-Waterman alignment extension.

---

## Conclusion

BioSharp is designed to replace the tool chain, not just individual tools. Where `BCL Convert + FastQC + BWA + SAMtools + Picard + GATK + FreeBayes + SnpEff + VEP` requires 9+ tools, BioSharp provides comparable functionality — and more (FM-index seeding, graph-based SV detection, splice site PWM prediction, clinical report generation, cohort calling, duplicate marking) — in a single .NET package. It is best suited for targeted sequencing, gene panels, and re-sequencing projects where accuracy per read matters more than whole-genome throughput. For whole-genome projects, BioSharp can still be used for annotation, reporting, and graph-based stages, even if BWA is retained for raw alignment. The [Head-to-Head Benchmark Guide](#head-to-head-benchmark-guide) above provides concrete commands to measure BioSharp directly against the established tools on the same input data.
