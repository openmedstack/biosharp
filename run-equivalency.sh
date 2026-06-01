#!/bin/bash
# BioSharp Tool Equivalency Test Runner
# Runs ToolEquivalency acceptance tests inside the biosharp-equivalency Docker image.
# External tools (bwa, fastp, fastqc, cutadapt, freebayes, samtools/bcftools) are
# required — tests for unavailable tools are skipped automatically.
#
# The AfterTestRun hook in ToolEquivalencyHooks.cs writes the equivalency markdown
# report to BIOSHARP_EQUIV_REPORT_PATH (default: /app/reports/equivalency-report.md).

set -euo pipefail

cd /app

# ─────────────────────────────────────────────────────────────────────────────
# Banner
# ─────────────────────────────────────────────────────────────────────────────
echo "==================================================================="
echo "  BioSharp Tool Equivalency Test Suite"
echo "==================================================================="
echo "  Date     : $(date '+%Y-%m-%d %H:%M:%S UTC')"
echo "  Platform : $(uname -s)-$(uname -m)"
echo "  .NET     : $(dotnet --version 2>/dev/null || echo 'unknown')"
echo "==================================================================="
echo ""

# ─────────────────────────────────────────────────────────────────────────────
# Validate report output directory
# ─────────────────────────────────────────────────────────────────────────────
REPORT_DIR=$(dirname "${BIOSHARP_EQUIV_REPORT_PATH:-/app/reports/equivalency-report.md}")
if [[ ! -d "$REPORT_DIR" ]]; then
    echo "ERROR: report output directory '$REPORT_DIR' does not exist." >&2
    echo "Mount a host directory to /app/reports when starting the container:" >&2
    echo "  podman run --rm -v \$(pwd)/reports:/app/reports biosharp-equivalency" >&2
    exit 1
fi

if [[ ! -w "$REPORT_DIR" ]]; then
    echo "ERROR: report output directory '$REPORT_DIR' is not writable." >&2
    exit 1
fi

# ─────────────────────────────────────────────────────────────────────────────
# Record external tool versions for provenance
# ─────────────────────────────────────────────────────────────────────────────
VERSIONS_FILE="${REPORT_DIR}/equivalency-tool-versions.txt"
echo "Recording tool versions to ${VERSIONS_FILE} ..."
{
    echo "# BioSharp Equivalency — External Tool Versions"
    echo "# Generated: $(date -u '+%Y-%m-%d %H:%M:%S UTC')"
    echo ""
    printf "bwa:        %s\n"  "$(bwa 2>&1 | head -n 2 | grep -oP 'Version:\s*\K[^\s]+' || echo 'not found')"
    printf "bwa-mem2:   %s\n"  "$(bwa-mem2 version 2>&1 | head -n 1 || echo 'not found')"
    printf "samtools:   %s\n"  "$(samtools --version 2>&1 | head -n 1 || echo 'not found')"
    printf "bcftools:   %s\n"  "$(bcftools --version 2>&1 | head -n 1 || echo 'not found')"
    printf "freebayes:  %s\n"  "$(freebayes --version 2>&1 | head -n 1 || echo 'not found')"
    printf "fastp:      %s\n"  "$(fastp --version 2>&1 | head -n 1 || echo 'not found')"
    printf "fastqc:      %s\n"  "$(fastqc --version 2>&1 | head -n 1 || echo 'not found')"
    printf "cutadapt:    %s\n"  "$(cutadapt --version 2>&1 | head -n 1 || echo 'not found')"
    printf "bcl-convert:  %s\n" "$(bcl-convert --version 2>&1 | head -n 1 || echo 'not found')"
    printf "bcl2fastq:    %s\n" "$(bcl2fastq --version 2>&1 | head -n 1 || echo 'not found')"
    printf "trimmomatic:  %s\n" "$(trimmomatic -version 2>&1 | head -n 1 || echo 'not found')"
    printf "snpeff:       %s\n" "$(snpeff -version 2>&1 | head -n 1 || echo 'not found')"
} > "$VERSIONS_FILE"
cat "$VERSIONS_FILE"
echo ""

# ─────────────────────────────────────────────────────────────────────────────
# Run the equivalency acceptance tests
#
# We invoke the xUnit.v3 in-process runner directly rather than through
# `dotnet test` because xUnit.v3 3.x uses a first-party in-process runner
# protocol that is not reliably triggered by `dotnet test --no-build` in
# container environments.  Running the DLL directly with `dotnet <dll>`
# is the supported and reliable path.
# ─────────────────────────────────────────────────────────────────────────────
TEST_DLL=/src/tests/openmedstack.biosharp.acceptancetests/bin/Release/net10.0/openmedstack.biosharp.acceptancetests.dll
TEST_RESULTS_DIR="${REPORT_DIR}/test-results"
mkdir -p "$TEST_RESULTS_DIR"

echo "Running ToolEquivalency acceptance tests..."
echo "(Tests for unavailable tools will be skipped automatically)"
echo ""

# Use exit code capture so the script can report partial success without aborting
set +e
dotnet "$TEST_DLL" \
    -trait "Category=Equivalency" \
    -trx "${TEST_RESULTS_DIR}/equivalency-results.trx" \
    -xml "${TEST_RESULTS_DIR}/equivalency-results.xml" \
    2>&1 | tee "${REPORT_DIR}/test-run.log"
TEST_EXIT_CODE=${PIPESTATUS[0]}
set -e

echo ""
echo "==================================================================="
if [[ $TEST_EXIT_CODE -eq 0 ]]; then
    echo "  Result: ALL EQUIVALENCY TESTS PASSED"
else
    echo "  Result: SOME EQUIVALENCY TESTS FAILED (exit code: $TEST_EXIT_CODE)"
    echo "  See ${REPORT_DIR}/test-run.log for details."
fi
echo "==================================================================="
echo ""

# ─────────────────────────────────────────────────────────────────────────────
# Report summary
# ─────────────────────────────────────────────────────────────────────────────
REPORT_PATH="${BIOSHARP_EQUIV_REPORT_PATH:-/app/reports/equivalency-report.md}"

if [[ -f "$REPORT_PATH" ]]; then
    echo "Equivalency report written to: ${REPORT_PATH}"
    echo ""
    # Print the summary table from the report
    awk '/## Summary/{found=1} found{print} /^---/{if(found)exit}' "$REPORT_PATH" | head -20
else
    echo "WARNING: equivalency report was not generated at ${REPORT_PATH}." >&2
    echo "The ToolEquivalencyHooks AfterTestRun hook may not have executed." >&2
fi

exit $TEST_EXIT_CODE
