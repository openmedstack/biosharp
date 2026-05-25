# Benchmarks

The main BenchmarkDotNet harness lives in:

- `benchmarks/openmedstack.biosharp.benchmarks/`

It now includes both BioSharp-only micro-benchmarks and head-to-head comparison benchmarks against external tools.

The head-to-head classes use a **10-thread lab configuration where supported** (for example BioSharp pipeline parallelism and `bwa mem -t 10`).

## Quick start

```bash
dotnet run --project benchmarks/openmedstack.biosharp.benchmarks/openmedstack.biosharp.benchmarks.csproj -c Release -- --list flat
```

If you want repo-local copies of the external comparison tools, run `SETUP_ONLY=1 ./benchmark-tutorial-comparison.sh` first. That stages the native bioinformatics CLIs under `tools/<os-arch>/bin/`, and the benchmark harness will automatically prefer those binaries over the ambient PATH. `dotnet` remains a system prerequisite and is not staged under `tools/`.

## Main comparison groups

- `SeederComparisonBenchmarks`
  - `ReferenceIndex` vs `FmIndexSeeder`
- `FmIndexOperationBenchmarks`
  - backward search, exact seeds, MEM seeds, locate
- `AlignmentHeadToHeadBenchmarks`
  - BioSharp vs `bwa mem` / `bwa-mem2`
- `VariantCallingHeadToHeadBenchmarks`
  - BioSharp vs `freebayes` / `samtools mpileup | bcftools call`
- `BclHeadToHeadBenchmarks`
  - BioSharp vs `bcl2fastq`
- `SmithWatermanMicroBenchmarks`
  - scalar vs banded SW cost at different window sizes

## Existing BioSharp-only groups

- `AlignmentBenchmarks`
- `BclBenchmarks`
- `DeBruijnBenchmarks`
- `PipelineBenchmarks`

## Focused runs

```bash
dotnet run --project benchmarks/openmedstack.biosharp.benchmarks/openmedstack.biosharp.benchmarks.csproj -c Release -- --filter '*SeederComparisonBenchmarks*'
dotnet run --project benchmarks/openmedstack.biosharp.benchmarks/openmedstack.biosharp.benchmarks.csproj -c Release -- --filter '*AlignmentHeadToHeadBenchmarks*'
dotnet run --project benchmarks/openmedstack.biosharp.benchmarks/openmedstack.biosharp.benchmarks.csproj -c Release -- --filter '*VariantCallingHeadToHeadBenchmarks*'
dotnet run --project benchmarks/openmedstack.biosharp.benchmarks/openmedstack.biosharp.benchmarks.csproj -c Release -- --filter '*BclHeadToHeadBenchmarks*'
```

## Merge separate benchmark runs into one comparison report

```bash
dotnet run --project benchmarks/openmedstack.biosharp.benchmarks/openmedstack.biosharp.benchmarks.csproj -c Release -- merge-report
```

This reads BenchmarkDotNet CSV exports under `BenchmarkDotNet.Artifacts/results/` and writes:

- `benchmark-comparison-report.md`
- `benchmark-comparison-summary.csv`

See `benchmarks/openmedstack.biosharp.benchmarks/README.md` for detailed setup notes, external tool requirements, and interpretation guidance.

Note: the head-to-head external-tool benchmarks are intentionally fail-fast now. If a required tool such as `bwa`, `freebayes`, or `bcl2fastq` is missing, the benchmark throws instead of returning a misleading near-zero timing.

