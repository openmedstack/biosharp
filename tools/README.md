# Benchmark tools layout

This repository now stages benchmark dependencies under:

- `tools/osx-arm64/`
- `tools/osx-x64/`
- `tools/linux-arm64/`
- `tools/linux-x64/`

Each platform folder is self-contained and may contain:

- `bin/` — stable executable entrypoints used by scripts and benchmarks
- `env/` — micromamba-managed open-source tools
- `micromamba/` and `mamba-root/` — micromamba bootstrap state
- `downloads/` — downloaded archives
- `opt/` — staged vendor tools or manually supplied installs

The `tools/` tree is only for external benchmark dependencies. The repository assumes `dotnet` is already installed system-wide for this C# solution, so no repo-local .NET SDK is staged here.

## Provisioning with the benchmark script

From the repository root:

```bash
SETUP_ONLY=1 ./benchmark-tutorial-comparison.sh
```

That prepares the repo-local toolchain for the current machine's OS/architecture and writes versions to `benchmark-results/tool-versions.txt`.

## `bcl2fastq`

`bcl2fastq` is vendor-distributed and is not auto-installed from the open-source micromamba environment.

To stage it into the repo-local `tools/<os-arch>/` tree, provide one of these environment variables before running the script:

```bash
BCL2FASTQ_PATH=/path/to/bcl2fastq-or-install-dir SETUP_ONLY=1 ./benchmark-tutorial-comparison.sh
BCL2FASTQ_TARBALL=/path/to/bcl2fastq.tar.gz SETUP_ONLY=1 ./benchmark-tutorial-comparison.sh
BCL2FASTQ_URL=https://example.invalid/bcl2fastq.tar.gz SETUP_ONLY=1 ./benchmark-tutorial-comparison.sh
```

If `bcl2fastq` is already installed globally, the script stages a repo-local wrapper in `tools/<os-arch>/bin/bcl2fastq`.

