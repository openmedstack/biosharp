# Tool Comparison Review — `docs/tool_comparison.md`

**Reviewed by:** GitHub Copilot  
**Date:** 2026-05-25  
**Method:** Source-code inspection of `src/` + cross-reference with publicly available information about the compared tools  

---

## Overall Assessment

The document is a fair and well-structured marketing-oriented comparison. It is honest about BioSharp's limitations in the "Honesty and Limitations" section, which is commendable. However, there are a number of factual errors concerning the *competing tools* and at least one significant misrepresentation about BioSharp's own features. These are detailed section by section below.

---

## Section 1 — Illumina BCL Decoding

### ❌ Wrong comparison tool: `fasterq-dump`

The document compares BioSharp's BCL decoder against `fasterq-dump` (SRA-TOOLS). This is the **wrong tool entirely**.

`fasterq-dump` is a tool for downloading sequence archives from NCBI's Sequence Read Archive (SRA) and converting them from SRA binary format to FASTQ. It does not read Illumina BCL files from sequencing runs at all. Its feature column ("No (not applicable)") is therefore correct for almost every row — not because the tools are comparable but because they solve completely different problems.

The **correct comparison tools** for BCL decoding are:
- **Illumina BCL Convert** (the current Illumina-supported tool, replacing `bcl2fastq`)
- **bcl2fastq** (older, still widely used)
- **Picard IlluminaBasecallsToFastq** / **Picard ExtractIlluminaBarcodes**

> **Recommendation:** Replace the `fasterq-dump` column with `bcl2fastq`/`BCL Convert`. These tools do the same job, have overlapping features (sample sheet parsing, barcode demultiplexing, tile processing, FASTQ output), and represent the actual competitive landscape for labs choosing whether to adopt BioSharp for BCL processing.

### ⚠️ "First .NET library" claim

The document says *"BioSharp is the first .NET library to read Illumina BCL files natively."*  

Inspection of `BclReader.cs` reveals the following header comment:

```
/* The code in this file is migrated from the Java code in the Picard project
   (https://github.com/broadinstitute/picard). The code is released under MIT license. */
```

The reader is a port of Picard's Java BCL reader, not an original implementation. While porting to C# is non-trivial and the claim that no prior .NET BCL library existed may still be true, this origin should be acknowledged or the "natively" qualifier removed.

---

## Section 2 — Sequence Data I/O

### ❌ FASTX-Toolkit is C, not Perl

The narrative description states:

> "Unlike `FASTX-Toolkit` (which is a **Perl-based toolkit** for basic FASTA/FASTQ manipulation)..."

FASTX-Toolkit is written in **C and C++**, not Perl. This is a factual error.  
(Perl-based wrappers around bioinformatics tools exist, but FASTX-Toolkit itself is a compiled C binary suite.)

### ❌ `seqrepo` feature claims

**`seqrepo`** is a reference sequence storage and retrieval tool designed to store sequences (FASTA-format) by accession. It is NOT a VCF toolkit or Tabix toolkit. The following entries for `seqrepo` appear to be wrong:

| Feature | Claimed | Reality |
|---|---|---|
| VCF reader/writer | Yes | No — seqrepo doesn't process VCF files |
| BCF reader/writer | Partial | No |
| Tabix index reader/writer | Yes | No — seqrepo uses its own accession-based index |

These cells should read "No" or, where truly not applicable, "N/A".

### ❌ `seqtk` VCF claim

`seqtk` is a C utility for **FASTA/FASTQ** manipulation (subsampling, reverse complement, masking). It does not have a VCF reader/writer. The entry "VCF reader/writer: Yes" for seqtk is incorrect.

### ⚠️ FASTX-Toolkit BED/GFF claim

In the summary feature matrix, FASTX-Toolkit is listed as supporting "BED/GFF reader." FASTX-Toolkit is a FASTA/FASTQ-specific toolkit and does not read BED or GFF files.

---

## Section 4 — Read Alignment

### ❌ Needleman-Wunsch implementation is for protein alignment, not DNA reads

The document's alignment table states:

> "Global alignment (Needleman-Wunsch): **Yes**"

Inspection of `NeedlemanWunsch/NeedlemanWunschAlgo.cs` reveals that this implementation uses a **BLOSUM62** substitution matrix — a protein amino acid substitution matrix used for protein sequence alignment. For NGS read-to-reference DNA alignment, a simple match/mismatch scoring scheme (as in the `SmithWatermanAligner`) is correct; BLOSUM62 is semantically inappropriate for DNA.

The NW implementation may be useful for protein sequence comparison within other bioinformatics applications, but it is misleading to present it as an equivalent to, or an alternative to, DNA read alignment or BWA's `--local`/`--global` modes. The table comparison against BWA-MEM and Bowtie2 in the context of NGS alignment should not list "Global alignment (Needleman-Wunsch): Yes" without this important caveat.

**The primary alignment algorithm** confirmed in source is `SmithWatermanAligner`, which is correctly described as semi-global (glocal) alignment.

### ⚠️ SIMD acceleration is partial, not full DP

The table entry "SIMD acceleration (`SequenceSimd`): Yes" is technically correct but potentially misleading. Inspection of `SequenceSimd.cs` and `SmithWatermanAligner.cs` shows that:

- `SequenceSimd` uses `Vector256<ushort>` (AVX2) and `Vector128<ushort>` (SSE2) for **mismatch counting** and **k-mer finding**.
- The **core DP matrix fill** in `SmithWatermanAligner` uses **scalar integer operations**. The inner loop does not use SIMD.

Contrast this with BWA-MEM2 (and tools like `ksw2`), which apply SIMD to the actual alignment DP. The document implies a direct comparison ("BWA: Yes (C), BioSharp: Yes") which is misleading in terms of the degree of SIMD optimization.

Additionally, the `ReverseComplementSimd` method in `SequenceSimd.cs` has this comment:

```csharp
// For now, use scalar but read-ahead using SIMD-width chunks
// A proper SIMD implementation requires byte-level packing/shuffling
// which is complex with char (2-byte); the scalar path is used here
```

So reverse complement is scalar despite appearing under an `IsHardwareAccelerated` guard. This is a known technical debt item, not a completed feature.

### ✅ Smith-Waterman glocal alignment: Correct

The core algorithm description — semi-global (glocal), affine gap penalties, banded mode (`bandWidth`), X-drop extension — is accurately described and confirmed by source.

### ⚠️ BWT is not used in the alignment pipeline

`BurrowsWheelerTransform.cs` exists but is a utility class (suffix array / BWT string transform). It does not form an FM-index and is not used by `SmithWatermanAligner` or `VariantCallingPipeline`. The document correctly never claims BWT-based alignment, but a reader comparing with BWA's BWT+FM-index approach should be aware that BioSharp has no BWT-based approximate matching index.

### ❌ Bowtie2 global alignment row

The table row "Global alignment (Needleman-Wunsch)" has `Bowtie2: Yes (local mode)`. This is contradictory: Bowtie2's `--local` mode is *semi-local* (soft-clipping), the opposite of global alignment. Bowtie2 also has `--end-to-end` mode (its default), which is a form of global alignment. The table entry is confusingly labelled.

---

## Section 5 — Variant Calling

### ✅ De Bruijn graph + bubble-based calling: Accurately described

`DeBruijn/`, `BubbleFinder.cs`, `BloomFilter.cs`, and `VariantCallingPipeline.cs` exist and are substantive. The two-stage description (SW alignment events + De Bruijn bubble finding) is confirmed by source.

### ⚠️ FreeBayes De Bruijn claim

The document says FreeBayes uses "hidden Markov model over haplotypes" as its algorithm. More accurately, FreeBayes constructs all plausible haplotypes from an observed read stack, then uses a Bayesian genotype likelihood model (not a hidden Markov model) to evaluate them. FreeBayes does not use a De Bruijn graph at all.

---

## Section 8 — Genotype Calling

### ❌ HomRef threshold is stated as `<0.2` but code uses `<0.4`

The comparison table states:
> "Thresholds: HomRef (<0.2)"

However, in `DeBruijn/Genotype.cs`, the actual thresholds are:

```csharp
if (AltRefRatio >= 0.8)    → HomozygousAlt
if (AltRefRatio >= 0.4)    → Heterozygous
if (AltRefRatio < 0.2)     → HomozygousRef
else (0.2 – 0.4)           → HomozygousRef (borderline, conservative default)
```

**In practice, everything with alt/ref < 0.4 maps to HomRef.** The stated threshold of `<0.2` only describes the clearly confident HomRef call; the 0.2–0.4 range is also stored as HomRef. The document's description is therefore incomplete and slightly misleading.

Similarly, `GenotypeCaller.cs` documents the suspicious threshold as `< 0.2` (for flagging artifacts), but the actual call for 0.2–0.4 is still HomRef, just without the suspicious flag. This asymmetry should be documented.

---

## Section 10 — Base Quality Recalibration

### ⚠️ GATK model description is imprecise

The table note for GATK says *"pair-HMM, covariate model."* GATK `BaseRecalibrator` does **not** use pair-HMM. Pair-HMM is used in `HaplotypeCaller` for haplotype likelihood estimation. `BaseRecalibrator` uses an empirical Bayesian model over covariates (read group, reported quality, cycle, and dinucleotide context). These are distinct tools with distinct algorithms.

### ✅ BioSharp BQSR implementation: Accurately described

`BaseQualityRecalibrator.cs` confirms: three covariates (reported Phred quality, read cycle, dinucleotide context), known variant exclusion, and in-memory application via `ApplyRecalibration`. The "first-order covariates only" limitation is correctly stated.

---

## Section 11 — Mapping Quality

### ⚠️ Two different MAPQ formulas in code

The document describes one formula: `60 × (1 − secondBest/best)`. The code has two methods:

1. `MapqCalculator.Calculate(int, int)` — uses the linear formula: `round(60 × (1 − second/best))`
2. `MapqCalculator.CalculateFromRatio(double, double)` — uses a Phred formula: `-10 × log10(second/best)`

These two formulas produce different results and are used in different contexts. The document does not acknowledge `CalculateFromRatio`. For full transparency, both methods and their intended call sites should be documented.

---

## Section 12 — Repeat Masking

### ✅ Correctly described and differentiated

The distinction between BioSharp's k-mer/graph-based approach and RepeatMasker's BLAST+RepBase approach is fair and accurate. Limitations are correctly noted.

---

## Section 13 — Variant Annotation

### ✅ Annotations generally accurate

`VariantAnnotationEngine.cs`, `SpliceSitePredictor.cs`, `ClinVarAnnotator.cs`, `DbSnpAnnotator.cs`, `PathogenicityAnnotator.cs` all confirmed present. The PWM splice site predictor confirmed with Shapiro & Senapathy 1987 log-odds matrix. HGVS generation confirmed.

---

## Section 14 — Clinical Reporting

### ✅ Accurately described

`Report/ClinicalReport.cs`, `ClinicalReportWriter.cs`, `VariantReport.cs` confirmed. JSON via `System.Text.Json` confirmed.

---

## Summary Feature Matrix — Multiple Errors

### ❌ Bloom filter attribution to GATK

Row: *"Bloom filter k-mer filtering"*, Column: PICARD/GATK → **"Yes (GATK)"**

GATK does **not** use a Bloom filter. GATK's `HaplotypeCaller` uses a pair-HMM for haplotype likelihood calculation; `BaseRecalibrator` uses covariate tables. Bloom filters appear in some genome assemblers (SPAdes, Minia) but not in GATK. This cell should read "No."

### ❌ Bloom filter attribution to FastP / SRA-TOOLS

Row: *"Bloom filter k-mer filtering"*, Column for FastP → **"Yes (SRA-TOOLS)"**

This appears to be a formatting or copy-paste error. FastP does not use Bloom filters, and "SRA-TOOLS" does not belong in the FastP column. This cell should read "No."

### ❌ VarScan2 programmatic API listed as Python

Row: *"Programmatic .NET API"*, Column VarScan variants → **"Yes (Python)"**

VarScan2 is a **Java** application. It does not have an official Python API. (pysam and other Python wrappers exist for SAMtools, but VarScan2 has no equivalent official Python binding.) This cell should read "Yes (Java)" or "No (CLI only)."

### ⚠️ SAMtools Python API cited as official

Row: *"Programmatic .NET API"*, Column SAMtools → **"Yes (Python)"**

The Python binding for SAMtools is **pysam**, a third-party C extension that wraps htslib. SAMtools itself does not include a Python API. The document implies an official Python API that does not exist.

---

## Benchmark Section

### ⚠️ BWA version "1.23.1" appears to be a version number error

The benchmark environment lists `BWA 1.23.1`. BWA-MEM (classic) version numbering is in the 0.7.x series (e.g., 0.7.17, 0.7.18). BWA-MEM2 is in the 2.x series. Neither has a 1.23.x release. This looks like a transposition from the SAMtools version number.

### ⚠️ No direct comparison with competing tools

The benchmark section presents BioSharp timing data in isolation. It explicitly states that competing tools were "not run in the same benchmark environment." This makes the benchmarking section anecdotal rather than comparative. The document should either:
1. Remove competing tool rows from the benchmark table (they contain only `n/a`), or
2. Run an actual head-to-head benchmark with comparable inputs.

---

## Problem With Tool Chaining — Minor Observation

The document lists the traditional pipeline as:

> `BCL → Trimmomatic/Cutadapt → FastQC → BWA/Bowtie2 → SAMtools → Picard MarkDuplicates → BQSR (GATK) → FreeBayes/Strelka → BCFtools → SnpEff/VEP/ANNOVAR → Clinical Report`

That is a reasonable framing, though:
- Picard's duplicate marking is notably absent from BioSharp's feature comparison tables, yet `DuplicateMarker.cs` exists in source. This capability should be mentioned.
- "12+ separate executables" is cited, but the example chain lists approximately 10-11 distinct tools, making "12+" a minor overcount.

---

## Summary of Issues by Severity

| # | Issue | Severity |
|---|---|---|
| 1 | BCL comparison tool should be `bcl2fastq`/`BCL Convert`, not `fasterq-dump` | **High** |
| 2 | FASTX-Toolkit is C, not Perl | **Medium** |
| 3 | `seqrepo` falsely listed with VCF/Tabix capabilities | **Medium** |
| 4 | `seqtk` falsely listed with VCF reader/writer | **Medium** |
| 5 | NW implementation uses BLOSUM62 (protein) — misleading as DNA read aligner | **Medium** |
| 6 | HomRef genotype threshold documented as `<0.2` but code classifies `<0.4` as HomRef | **Medium** |
| 7 | Bloom filter falsely attributed to GATK in summary matrix | **Medium** |
| 8 | Bloom filter / SRA-TOOLS misattribution in FastP column | **Medium** |
| 9 | VarScan2 listed as having a Python API (it's Java) | **Medium** |
| 10 | SIMD acceleration is partial (helper functions only, not the DP inner loop) | **Low** |
| 11 | MapqCalculator has two different formulas; only one documented | **Low** |
| 12 | GATK BQSR incorrectly described as using pair-HMM | **Low** |
| 13 | Bowtie2 "global alignment: Yes (local mode)" — contradictory | **Low** |
| 14 | BWA version "1.23.1" appears to be a typo | **Low** |
| 15 | BclReader.cs is a port from Picard Java; "first .NET library" claim is ambiguous | **Low** |
| 16 | `DuplicateMarker.cs` exists but is not mentioned in comparison tables | **Low** |
| 17 | FreeBayes uses Bayesian likelihood model, not HMM | **Low** |
| 18 | Benchmark does not compare BioSharp against other tools directly | **Low** |
| 19 | FASTX-Toolkit listed as supporting BED/GFF in summary matrix — incorrect | **Low** |
| 20 | `CalculateFromRatio` Phred-based MAPQ overload not documented | **Informational** |

