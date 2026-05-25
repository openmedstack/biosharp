# `openmedstack.biosharp.benchmarks`

BenchmarkDotNet harness for BioSharp micro-benchmarks and head-to-head comparisons against external bioinformatics tools.

The head-to-head alignment / variant-calling / BCL benchmarks now use a **10-thread configuration where the underlying tool or BioSharp pipeline supports it**. The report merger can then combine separately-run benchmark CSV exports into one comparison report without requiring a single long full-suite run.

## Included benchmark groups

### Existing BioSharp-only groups
- `AlignmentBenchmarks`
- `BclBenchmarks`
- `DeBruijnBenchmarks`
- `PipelineBenchmarks`

### New comparison groups
- `SeederComparisonBenchmarks`
  - `ReferenceIndex` vs `FmIndexSeeder`
  - index construction and candidate-window lookup
- `FmIndexOperationBenchmarks`
  - backward search
  - exact seed finding
  - MEM seed finding
  - locate
- `AlignmentHeadToHeadBenchmarks`
  - BioSharp hash-map seeding + Smith-Waterman
  - BioSharp FM-index seeding + Smith-Waterman
  - BioSharp `VariantCallingPipeline` from FASTQ
  - `bwa mem`
  - `bwa-mem2`
- `VariantCallingHeadToHeadBenchmarks`
  - BioSharp BAM pipeline with hash-map seeder
  - BioSharp BAM pipeline with FM-index seeder
  - BioSharp BAM pipeline with graph SV detection enabled
  - `freebayes`
  - `samtools mpileup | bcftools call`
- `BclHeadToHeadBenchmarks`
  - BioSharp decode only
  - BioSharp decode + FASTQ write to memory
  - BioSharp decode + FASTQ write to disk
  - `bcl-convert`
  - `bcl2fastq`
- `FastqProcessingHeadToHeadBenchmarks`
  - BioSharp `FastQQualityReport`
  - BioSharp `AdapterTrimmer`
  - `fastqc`
  - `fastp`
  - `cutadapt`
- `CoverageAndDuplicateHeadToHeadBenchmarks`
  - BioSharp `CoverageCalculator`
  - BioSharp `DuplicateMarker`
  - `samtools depth`
  - `samtools markdup`
- `RepeatMaskingHeadToHeadBenchmarks`
  - BioSharp graph-based `RepeatMasker`
  - `RepeatMasker`
  - `trf`
- `SmithWatermanMicroBenchmarks`
  - scalar Smith-Waterman at multiple window sizes
  - banded vs non-banded runs

## Why this harness is fairer to BioSharp

BenchmarkDotNet performs warmup iterations before the measured iterations. That avoids penalizing BioSharp for first-run JIT startup costs.

The comparison benchmarks also try to keep the work comparable:

- BioSharp indices are usually built in `GlobalSetup` so lookup/alignment timings measure steady-state work.
- External tools are exercised as real subprocesses so their measurements include their real CLI startup/runtime behavior.
- Temporary FASTA / FASTQ / BAM inputs are shared across BioSharp and the external tools where practical.
- External-tool stdout is redirected away when serialization would otherwise dominate the benchmark.

## Build

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet build -c Release
```

## List all benchmarks

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --list flat
```

## Run a focused benchmark class

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*FmIndexOperationBenchmarks*'
dotnet run -c Release -- --filter '*SeederComparisonBenchmarks*'
dotnet run -c Release -- --filter '*AlignmentHeadToHeadBenchmarks*'
dotnet run -c Release -- --filter '*VariantCallingHeadToHeadBenchmarks*'
dotnet run -c Release -- --filter '*BclHeadToHeadBenchmarks*'
dotnet run -c Release -- --filter '*FastqProcessingHeadToHeadBenchmarks*'
dotnet run -c Release -- --filter '*CoverageAndDuplicateHeadToHeadBenchmarks*'
dotnet run -c Release -- --filter '*RepeatMaskingHeadToHeadBenchmarks*'
```

## Merge previously generated BenchmarkDotNet CSVs into one report

This is the recommended workflow for full lab benchmarking when a single warmup-heavy run would take too long.

1. Run the benchmark classes you care about individually.
2. Let BenchmarkDotNet write its normal `*-report.csv` files.
3. Merge them into one markdown + CSV comparison report.

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- merge-report
```

By default this reads:

- `BenchmarkDotNet.Artifacts/results/*-report.csv`

and writes:

- `BenchmarkDotNet.Artifacts/results/benchmark-comparison-report.md`
- `BenchmarkDotNet.Artifacts/results/benchmark-comparison-summary.csv`

You can override the paths:

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- merge-report \
  --results ./BenchmarkDotNet.Artifacts/results \
  --output ./BenchmarkDotNet.Artifacts/results/lab-comparison.md \
  --csv-output ./BenchmarkDotNet.Artifacts/results/lab-comparison.csv
```

## Run a single benchmark method

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*BioSharp_FmIndex_AlignReads*'
dotnet run -c Release -- --filter '*FreeBayes_VariantCalling*'
dotnet run -c Release -- --filter '*BwaMem_Subprocess*'
```

## Optional external tool setup

The external-tool benchmarks are discoverable even if the tools are not installed. To get meaningful head-to-head timing results, install the relevant tools first.

### macOS

```bash
brew install bwa
brew install samtools
brew install bcftools
brew install freebayes
brew install fastqc
brew install fastp
brew install cutadapt
brew install trf
```

`bwa-mem2`, `bcl-convert`, and `bcl2fastq` may need manual installation depending on your environment.
`RepeatMasker` is library/database-sensitive and may also need manual installation.

### Linux

```bash
sudo apt-get update
sudo apt-get install -y bwa samtools bcftools freebayes
```

For Linux lab runs, prefer the repository provisioning script from the repository root:

```bash
SETUP_ONLY=1 ./benchmark-tutorial-comparison.sh
```

It stages open-source tools under `tools/linux-x64/` for x64 Linux and writes a support note to `benchmark-results/tool-support-osx-arm64-linux-x64.md`.

## Output files

BenchmarkDotNet writes reports under:

- `BenchmarkDotNet.Artifacts/results/*.csv`
- `BenchmarkDotNet.Artifacts/results/*.html`
- `BenchmarkDotNet.Artifacts/results/*-github.md`

## Notes on external-tool benchmarks

- `bwa`, `bwa-mem2`, `freebayes`, `samtools`, `bcftools`, `bcl-convert`, and `bcl2fastq` are invoked as real subprocesses.
- `fastqc`, `fastp`, `cutadapt`, `RepeatMasker`, and `trf` are invoked as real subprocesses in their focused head-to-head classes.
- Head-to-head external benchmarks now **fail fast** when the required external tool is missing, instead of returning a bogus near-zero timing from a short-circuit branch.
- The BCL head-to-head benchmark additionally validates that the BioSharp run-folder conversion path and each available external converter path produce matching FASTQ file count / read count / total base count before benchmarking.
- The report merger probes tool availability too, so unavailable external-tool methods can be called out in the merged report instead of being mistaken for valid results.
- The new QC/trimming, coverage/depth, duplicate-marking, and repeat-masking benchmarks use synthetic inputs shared by BioSharp and external tools. Database-heavy comparisons such as GATK BQSR, SnpEff/VEP/ANNOVAR annotation, and Strelka/Manta workflows are documented as not credible microbenchmarks unless the user supplies real reference databases and workflow inputs.
- `VariantCallingHeadToHeadBenchmarks` prefers the repository's real sample inputs:
  - `data/small_test.fasta`
  - `data/small_test_sorted.bam`
- If those are unavailable, it falls back to synthetic data.

## Suggested runs for performance investigation

### FM-index vs hash-map seeding

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*SeederComparisonBenchmarks*'
```

### Alignment inner-loop cost

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*SmithWatermanMicroBenchmarks*'
dotnet run -c Release -- --filter '*AlignmentHeadToHeadBenchmarks*'
```

### Variant-calling pipeline cost

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*VariantCallingHeadToHeadBenchmarks*'
```

### BCL decoding throughput

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*BclHeadToHeadBenchmarks*'
```

### FASTQ QC and adapter trimming

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*FastqProcessingHeadToHeadBenchmarks*'
```

### Coverage, depth, and duplicate marking

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*CoverageAndDuplicateHeadToHeadBenchmarks*'
```

### Repeat masking

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- --filter '*RepeatMaskingHeadToHeadBenchmarks*'
```

### Merge the resulting class-level runs into one report

```bash
cd benchmarks/openmedstack.biosharp.benchmarks
dotnet run -c Release -- merge-report
```



