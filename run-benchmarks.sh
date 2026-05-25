#!/bin/bash
set -euo pipefail

cd /app

is_mountpoint() {
    local target="$1"
    awk -v target="$target" '$5 == target { found = 1 } END { exit found ? 0 : 1 }' /proc/self/mountinfo
}

echo "==================================================================="
echo "BioSharp Linux Container Benchmark Suite"
echo "==================================================================="
echo "Date: $(date '+%Y-%m-%d %H:%M:%S')"
echo "Platform: $(uname -s)-$(uname -m)"
echo ""

if [[ ! -d /app/data ]] || [[ -z "$(find /app/data -mindepth 1 -maxdepth 1 2>/dev/null)" ]]; then
    echo "ERROR: benchmark input data is not available at /app/data" >&2
    echo "Mount the repository data directory when starting the container:" >&2
    echo "  podman run --rm -v \$(pwd)/benchmark-results:/app/benchmark-results -v \$(pwd)/reports:/app/reports -v \$(pwd)/data:/app/data:ro biosharp-benchmark" >&2
    exit 1
fi

if ! is_mountpoint /app/reports; then
    echo "ERROR: report output path /app/reports is not a mounted volume" >&2
    echo "Mount a host reports directory when starting the container:" >&2
    echo "  podman run --rm -v \$(pwd)/benchmark-results:/app/benchmark-results -v \$(pwd)/reports:/app/reports -v \$(pwd)/data:/app/data:ro biosharp-benchmark" >&2
    exit 1
fi

if [[ ! -w /app/reports ]]; then
    echo "ERROR: report output path /app/reports is not writable" >&2
    exit 1
fi

exit_code=0

run_section() {
    local label="$1"
    local filter="$2"
    local output="$3"

    echo ""
    echo "--- ${label} ---"
    if ! ./benchmarks/openmedstack.biosharp.benchmarks warm-report \
        --filter "$filter" \
        --warmups 1 \
        --iterations 3 \
        --output "$output" 2>&1; then
        echo "WARNING: ${label} completed with benchmark failures; results were still written to ${output}" >&2
        exit_code=1
    fi
}

# Record tool versions
echo "Recording tool versions..."
{
    echo "bwa: $(bwa 2>&1 | head -n 2 | tr '\n' ' ' | sed 's/[[:space:]]\+/ /g')"
    echo "bwa-mem2: $(bwa-mem2 version 2>&1 | head -n 1 || echo 'not available')"
    echo "samtools: $(samtools --version | head -n 1)"
    echo "bcftools: $(bcftools --version | head -n 1)"
    echo "sambamba: $(sambamba 2>&1 | head -n 1 || echo 'not available')"
    echo "freebayes: $(freebayes --version 2>&1 | head -n 1)"
    echo "bgzip: $(bgzip --version 2>&1 | head -n 1)"
    echo "tabix: $(tabix --version 2>&1 | head -n 1)"
    echo "seqtk: $(seqtk 2>&1 | head -n 1 || echo 'not available')"
    echo "fastqc: $(fastqc --version 2>&1 | head -n 1)"
    echo "fastp: $(fastp --version 2>&1 | head -n 1)"
    echo "cutadapt: $(cutadapt --version 2>&1 | head -n 1)"
    echo "trf: $(trf 2>&1 | head -n 1 || echo 'not available')"
    echo "picard: $(picard MarkDuplicates --version 2>&1 | head -n 1 || echo 'not available')"
    # Use a subshell with pipefail disabled so that a non-zero exit from bcl-convert/bcl2fastq
    # does not trigger the || fallback *after* head -n 1 already captured the version string,
    # which would produce "Version X.Y.Z not available" and cause the tool to be reported as
    # unavailable even when it is present.  Instead we capture the first line of output and
    # use shell parameter expansion to substitute 'not available' only when the capture is empty.
    _bcl_convert_ver=$( { bcl-convert --version 2>&1 || true; } | head -n 1 )
    echo "bcl-convert: ${_bcl_convert_ver:-not available}"
    _bcl2fastq_ver=$( { bcl2fastq --version 2>&1 || true; } | head -n 1 )
    echo "bcl2fastq: ${_bcl2fastq_ver:-not available}"
} > benchmark-results/tool-versions-linux.txt

cat benchmark-results/tool-versions-linux.txt
echo ""

# Run warmup iteration
echo "==================================================================="
echo "Running warmup iteration..."
echo "==================================================================="
./benchmarks/openmedstack.biosharp.benchmarks warm-report \
    --warmups 1 \
    --iterations 1 \
    --output benchmark-results/csharp-warmup-linux.csv 2>&1 || true

# Run all head-to-head benchmarks
echo ""
echo "==================================================================="
echo "Running full benchmark suite (3 iterations each)..."
echo "==================================================================="

run_section "Alignment Benchmarks" "AlignmentHeadToHeadBenchmarks" "benchmark-results/csharp-linux-alignment.csv"
run_section "Variant Calling Benchmarks" "VariantCallingHeadToHeadBenchmarks" "benchmark-results/csharp-linux-variant-calling.csv"
run_section "BCL Conversion Benchmarks" "BclHeadToHeadBenchmarks" "benchmark-results/csharp-linux-bcl.csv"
run_section "FASTQ Processing Benchmarks" "FastqProcessingHeadToHeadBenchmarks" "benchmark-results/csharp-linux-fastq.csv"
run_section "Coverage and Duplicate Marking Benchmarks" "CoverageAndDuplicateHeadToHeadBenchmarks" "benchmark-results/csharp-linux-coverage-dup.csv"
run_section "Repeat Masking Benchmarks" "RepeatMaskingHeadToHeadBenchmarks" "benchmark-results/csharp-linux-repeatmask.csv"

echo ""
echo "Generating markdown benchmark report..."
./benchmarks/openmedstack.biosharp.benchmarks linux-report \
    --results-dir benchmark-results \
    --tool-versions benchmark-results/tool-versions-linux.txt \
    --output /app/reports/benchmark-results-linux.md

echo ""
echo "==================================================================="
echo "Benchmark suite complete!"
echo "Results saved to benchmark-results/"
echo "Markdown report saved to /app/reports/benchmark-results-linux.md"
echo "==================================================================="

# List output files
ls -la benchmark-results/*.csv 2>/dev/null || true
ls -la /app/reports/*.md 2>/dev/null || true

exit "$exit_code"
