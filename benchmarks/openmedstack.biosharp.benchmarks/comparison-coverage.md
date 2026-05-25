# Benchmark Comparison Coverage

This note tracks the credibility of the BenchmarkDotNet head-to-head comparisons against the capabilities described in `docs/tool_comparison.md`.

## Credible BenchmarkDotNet comparisons implemented

| `tool_comparison.md` area | Benchmark class | BioSharp side | External side | Credibility notes |
|---|---|---|---|---|
| Illumina BCL decoding | `BclHeadToHeadBenchmarks` | `BclReader` and run-folder conversion through `IlluminaDataReader` + `FastQWriter` | `bcl2fastq` | Shared synthetic run folder, sample sheet, cycles, cluster count, output FASTQ summary validation when `bcl2fastq` is present. |
| Read alignment | `AlignmentHeadToHeadBenchmarks` | Hash-map seeder + SW, FM-index seeder + SW, full FASTQ pipeline | `bwa mem`, `bwa-mem2` | Shared synthetic FASTA/FASTQ, BWA indices built in setup, mapped-read count validation. |
| Variant calling | `VariantCallingHeadToHeadBenchmarks` | BAM pipeline with hash-map seeder, FM-index seeder, graph SV enabled | `freebayes`, `samtools mpileup | bcftools call` | Shared BAM and reference. Uses repository real test BAM when available; otherwise synthetic BAM requires `bwa` + `samtools`. |
| Sequence seeding / FM-index | `SeederComparisonBenchmarks`, `FmIndexOperationBenchmarks` | `ReferenceIndex`, `FmIndexSeeder`, raw `FmIndex` operations | BioSharp internal comparison | No external CLI equivalent is cleanly separable from a full aligner, so this is an internal apples-to-apples algorithm benchmark. |
| Smith-Waterman inner loop | `SmithWatermanMicroBenchmarks` | `SmithWatermanAligner` | BioSharp internal comparison | External aligners do not expose their DP inner loops as stable CLI operations. |
| FASTQ quality control | `FastqProcessingHeadToHeadBenchmarks` | `FastQQualityReport` | `fastqc` | Shared synthetic FASTQ. External output includes report serialization, matching real CLI usage. |
| Adapter trimming / FASTQ processing | `FastqProcessingHeadToHeadBenchmarks` | `AdapterTrimmer` | `fastp`, `cutadapt` | Shared synthetic FASTQ with embedded TruSeq adapter sequence. |
| Coverage and depth | `CoverageAndDuplicateHeadToHeadBenchmarks` | `CoverageCalculator` | `samtools depth` | Shared synthetic alignments; external BAM is generated from the same records with `samtools`. |
| Duplicate marking | `CoverageAndDuplicateHeadToHeadBenchmarks` | `DuplicateMarker` | `samtools markdup` | Shared synthetic duplicate-prone alignments; external setup runs `view`, name sort, `fixmate`, coordinate sort, and `markdup`. |
| Repeat masking | `RepeatMaskingHeadToHeadBenchmarks` | BioSharp graph/k-mer `RepeatMasker` | `RepeatMasker`, `trf` | Shared repeat-rich synthetic FASTA. `RepeatMasker` also depends on external repeat libraries, so missing or unconfigured installs fail clearly. |

## Areas intentionally not forced into BenchmarkDotNet head-to-head jobs

| Area | External tools | Why not credible as a synthetic microbenchmark yet | Best current treatment |
|---|---|---|---|
| Base quality recalibration | GATK `BaseRecalibrator` + `ApplyBQSR` | Requires a realistic reference, BAM, known-sites VCF, and GATK resource configuration. Tiny synthetic reads do not exercise the covariate model honestly. | Documented limitation; add a lab benchmark only when known-sites fixtures are supplied. |
| Variant annotation | SnpEff, VEP, ANNOVAR | Requires genome-specific transcript databases and cache/download setup. Runtime mostly measures database availability and annotation corpus choice. | Keep BioSharp annotation benchmarks internal until a pinned annotation database fixture is added. |
| Somatic / tumor-normal calling | Strelka, Mutect2, FreeBayes tumor/normal workflows | Needs matched tumor/normal BAMs with realistic allele fractions and truth expectations. Graph-only synthetic examples would not be comparable to full callers. | Use future e2e fixtures or GIAB-style truth data before adding a head-to-head BenchmarkDotNet class. |
| Cohort calling | FreeBayes multisample, bcftools multisample, Strelka-style workflows | Needs multiple samples and a defined truth/aggregation model. BioSharp's cohort classification is not a one-to-one CLI feature in common tools. | Document as feature coverage, not raw speed comparison, until multi-sample fixtures exist. |
| Structural variant detection | Manta, Delly, Lumpy, Strelka SV | Requires split-read/discordant-pair evidence and caller-specific preprocessing. Tiny synthetic graph signals are misleading for mature SV callers. | BioSharp graph/SV path is included in the BAM pipeline benchmark; external SV caller benchmark should wait for an SV fixture. |
| Clinical reporting | SnpEff/VEP/ANNOVAR plus custom reporting | Common tools annotate; they do not produce the same clinical JSON report object. | Treat as BioSharp-only integration capability rather than a direct runtime comparison. |

## OS/architecture outcome

| OS/architecture | Succeeded | Known gaps |
|---|---|---|
| OSX-arm64 | Script stages/wraps common Homebrew tools for alignment, variant calling, FASTQ QC/trimming, coverage/depth, duplicate marking, and TRF when formulas are available. C# benchmarks fail with clear notes for absent tools. | `bcl2fastq` is vendor-only. `RepeatMasker` is not reliably available as a simple arm64 package with libraries. GATK/Picard/SnpEff/VEP-style comparisons need databases and are not auto-staged as credible microbenchmarks. |
| Linux-x64 | Script uses micromamba with conda-forge/bioconda to stage common open-source tools under `tools/linux-x64/`. C# benchmarks fail with clear notes for absent tools. | `bcl2fastq` remains vendor-only. RepeatMasker may install but still needs repeat-library configuration. Database-heavy tools can be installed but are not automatically benchmarked without pinned resources. |

## Failure policy

All external BenchmarkDotNet methods check tool availability and throw when a required executable is missing. This is intentional: a failed benchmark is more honest than a near-zero measurement of a short-circuit branch.
