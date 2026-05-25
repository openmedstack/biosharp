# Lessons Learned

## Illumina BCL run folder: per-tile vs. per-lane filter files

`bcl-convert` and `bcl2fastq` both require **per-tile** filter files named
`s_{lane}_{tile}.filter` inside `Data/Intensities/BaseCalls/L{lane:D3}/`.
A single per-lane file (`s_{lane}.filter`) is silently ignored by both tools:
the tools exit 0 but produce **zero FASTQ reads**, which can easily be mistaken
for a misconfigured sample sheet.

BioSharp's `IlluminaDataReader` checks for the per-tile name first and falls back to
the per-lane name, so it works with either convention. This fallback can mask the bug
when the BioSharp side passes but the external tool side produces no output.

**Fix**: always write per-tile filter files in synthetic benchmark setup code:

```csharp
for (var tileIndex = 0; tileIndex < TileCount; tileIndex++)
{
    var tile = FirstTile + tileIndex;
    WriteFilterFile(Path.Combine(baseCallsLaneDir, $"s_{lane}_{tile}.filter"), ClusterCount);
}
// BioSharp per-lane fallback
WriteFilterFile(Path.Combine(baseCallsLaneDir, $"s_{lane}.filter"), ClusterCount);
```

## Illumina bcl-convert SampleSheet format (v1 vs v2)

`bcl-convert` 4.x and `bcl2fastq` 2.x expect **incompatible SampleSheet formats** — do
not try to write a single file that satisfies both. Use separate sample sheets.

### bcl-convert 4.x — v2 format (minimal working example)

```
[Header]
FileFormatVersion,2

[BCLConvert_Settings]
Read1Cycles,75

[BCLConvert_Data]
Sample_ID
MySample
```

Key rules:
- `FileFormatVersion,2` switches the parser to v2 mode.
- `[BCLConvert_Settings]` must use `key,value` pairs. A bare cycle count in a `[Reads]`
  section is rejected with *"Reads entry must contain only key-value pair entries"*.
- Omit `SoftwareVersion` to avoid version-mismatch errors (installed version strings like
  `00.000.000.4.0.3` never match a simple `4.0.0`).
- Do **not** include a `[Data]` section alongside `[BCLConvert_Data]`.
- `[Reads]` section must be absent; cycle counts come from `Read1Cycles` or RunInfo.xml.

### bcl2fastq 2.x — IEM v1 format

```
[Header]
IEMFileVersion,4
Date,2026-01-01
Workflow,GenerateFASTQ
Application,FASTQ Only

[Reads]
75

[Data]
Sample_ID,Sample_Name
MySample,MySample
```

Key rules:
- `[Reads]` takes a **bare integer** per line — key-value syntax is not recognised.
- Only `[Data]` is parsed; any `[BCLConvert_*]` sections are silently ignored.

## RunInfo.xml: explicit tile list

`bcl-convert` can infer tile IDs from `FlowcellLayout` attributes but it is more
reliable to include an explicit `<TileSet>` element, especially for synthetic run
folders where no real instrument metadata exists:

```xml
<FlowcellLayout LaneCount="2" SurfaceCount="1" SwathCount="1" TileCount="2">
  <TileSet TileNamingConvention="FourDigit">
    <Tiles>
      <Tile>1_1101</Tile>
      <Tile>1_1102</Tile>
      <Tile>2_1101</Tile>
      <Tile>2_1102</Tile>
    </Tiles>
  </TileSet>
</FlowcellLayout>
```

## `set -o pipefail` + partial output before non-zero exit = concatenated fallback

In a `set -o pipefail` shell script, the pattern:

```bash
echo "tool: $(tool --version 2>&1 | head -n 1 || echo 'not available')"
```

has a subtle bug when `tool --version` **writes output to stderr/stdout and then
exits non-zero**:

1. `head -n 1` reads and prints the first line of output → that text is already
   in the command substitution buffer.
2. The pipe fails (because `tool` exited non-zero with `pipefail` active).
3. `|| echo 'not available'` fires and appends "not available" to the buffer.
4. The substitution receives both the real version string *and* "not available"
   joined by a newline, which multi-line-aware parsers concatenate into a single
   string like `"Version X.Y.Z not available"`.

**Fix**: capture to a variable first and use parameter expansion for the default:

```bash
_ver=$( { tool --version 2>&1 || true; } | head -n 1 )
echo "tool: ${_ver:-not available}"
```

The `|| true` inside the group command prevents the pipe from failing, so `head`
always closes normally and the substitution never sees the "not available" string.

## Docker build context exclusions can silently block staged benchmark tools

If a Dockerfile copies a staged binary from a path like `tools/linux-x64/...`, a broad `.dockerignore` rule such as `tools/` will exclude that file from the build context even when the `COPY` line looks correct.

When only a few staged binaries should be shipped, keep the broad ignore and add explicit allow rules for the required paths:

```text
tools/
!tools/
!tools/linux-x64/
!tools/linux-x64/bcl-convert
!tools/linux-x64/bcl2fastq
```

## BenchmarkDotNet for BioSharp vs external bioinformatics tools

### Do not measure `dotnet run` when the goal is steady runner performance

For CLI benchmark harnesses that generate a small runner project, build the runner once before the measured loop and execute the compiled DLL inside each timed iteration:

```bash
dotnet build runner.csproj -c Release
dotnet runner/bin/Release/net10.0/Runner.dll
```

Using `dotnet run` inside every measured iteration can dominate wall time with project/build host overhead even when the actual pipeline work is sub-second. Keep build/setup outside the measured loop unless startup/build cost is intentionally part of the benchmark contract.

### Avoid per-variant full-depth rescans in variant merge filters

When merge/filter logic needs total coverage for many variant candidates, do not scan every stored alignment for every variant. Build a per-position coverage array once after reads are loaded, then answer each variant's coverage lookup in O(1). On the BAM variant-calling fixture this changed merge/filter from effectively quadratic in variants × reads to linear in aligned bases plus variants.

### Current samtools mpileup no longer emits BCF/VCF for bcftools call

Recent samtools reports that `samtools mpileup` no longer generates BCF/VCF output. A retained SAMtools/BCFtools comparison can still include samtools by streaming BAM with `samtools view -u`, then using bcftools for the call-ready pileup:

```bash
samtools view -u input.bam | bcftools mpileup -Ou -f ref.fa - | bcftools call -m -v -Ov
```

### Never short-circuit a missing external tool benchmark to a constant return value

If an external-tool benchmark does this:

```csharp
if (!toolInstalled)
    return 0;
```

then BenchmarkDotNet will happily measure the short-circuit branch and report a meaningless near-zero runtime. That looks like a real result even though no external work happened.

For head-to-head benchmarks, a missing external tool should instead:

- fail fast with a clear exception, or
- be excluded from the run entirely by the caller.

Returning a constant sentinel is acceptable for metadata/reporting-only paths, but **not** for a benchmark method that users may compare directly against BioSharp timings.

### Apples-to-apples means matching the abstraction level, not just the source files

The original BCL benchmark compared:

- BioSharp raw `BclReader` over cycle files
- `bcl2fastq` over a run folder + `RunInfo.xml` + `SampleSheet.csv`

That is not a valid head-to-head comparison even if the underlying BCL bytes are the same. The comparable BioSharp path must use the same abstraction level as the external tool — in this case, **run-folder → FASTQ conversion** using `IlluminaDataReader` and `FastQWriter`.

### Synthetic benchmark inputs must be internally consistent

The synthetic BCL run folder must include all metadata and sidecar files needed by both implementations, and those files must agree with one another:

- `RunInfo.xml` cycle counts
- `SampleSheet.csv` `[Reads]` section
- presence/absence of index columns vs actual read structure
- filter files
- position files (`.locs` / `.clocs`)

Inconsistent synthetic metadata can cause the external tool to exit immediately, which again produces misleadingly tiny benchmark timings.

### BenchmarkDotNet CSV uses the benchmark *display name*, not the reflected method name

When a benchmark method has `Description = "..."`, the `*-report.csv` file uses that description in the `Method` column rather than the C# method identifier. A postprocessor that joins CSV rows back to reflection metadata must therefore match on **either**:

- reflected method name, or
- `BenchmarkAttribute.Description`

Otherwise every described benchmark row will look "unknown" and parameter/category/tool metadata will be lost.

### Reflect `[Params]` properties to disambiguate merged runs

BenchmarkDotNet CSV exports include benchmark parameter columns (for example `ReadCount`) alongside the normal timing columns. When merging multiple class-level benchmark outputs into one report, those parameter values must be included in the rendered benchmark label, otherwise rows like:

- `BioSharp-HashMap-Seed+SW [ReadCount=100]`
- `BioSharp-HashMap-Seed+SW [ReadCount=500]`

collapse into one visually ambiguous benchmark name.

The most reliable approach is to reflect all public properties marked with `[Params]` on the benchmark type, then read those column values from the CSV row.

### Benchmark executables do not need trim analysis

The benchmark harness is an executable used only for measurement and reporting. It is not a shipping trimmed artifact. If the harness intentionally uses reflection (for example to merge CSV results back to benchmark metadata), trim analysis can create noisy IL2026/IL2070 warnings that add no value.

For this kind of tool, it is reasonable to override the shared defaults in the benchmark `.csproj`:

```xml
<EnableTrimAnalyzer>false</EnableTrimAnalyzer>
<IsTrimmable>false</IsTrimmable>
```

That keeps the real library projects strict while allowing the benchmark/reporting executable to use reflection freely.

### Stale staged tool wrappers can invalidate tool availability checks

When tools are staged under `tools/<os-arch>/bin`, do not trust existing symlinks blindly. A previous micromamba environment can leave wrappers pointing at a removed or relocated `env`, causing dyld errors such as missing `libtinfow.6.dylib` and making optional tools appear installed in version reports.

On macOS, refresh staged wrappers from Homebrew paths and remove stale optional wrappers when a tool is not available. Also resolve `dotnet` outside the staged bin directory to avoid self-referential symlinks.

### BenchmarkDotNet generated-project mode can fail while the benchmark project builds

With .NET SDK 10.0.300, the BioSharp benchmark project built successfully, but BenchmarkDotNet's generated project restore failed with `Invalid framework identifier ''`. For result collection, a pragmatic fallback is a benchmark-project command that invokes the same benchmark classes directly with explicit warmup and measured iterations, while documenting that methodology in the report.

### Batch FASTQ writes instead of flushing each record

When converting BCL or other high-throughput sequence data to FASTQ, avoid formatting each record through `StringBuilder` plus an intermediate string and avoid flushing compressed output after every read. Encode records into pooled byte buffers and flush once per batch or async sequence. On the BCL fixture, batching and pooled encoding roughly halved the in-memory decode+FASTQ row for 75-cycle reads.

### FASTQ fixtures must not include a UTF-8 BOM

Some .NET text writers can emit a UTF-8 byte-order mark. External FASTQ tools expect the first byte of a FASTQ file to be `@` and may reject a gzipped fixture whose decompressed content starts with BOM bytes before the header. Use `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` when generating synthetic FASTQ fixtures for tool comparisons.

### External-tool availability must be accounted for during report merging

If an external benchmark method returns quickly when a tool is not installed, the raw BenchmarkDotNet timing can look artificially excellent. A merger that combines BioSharp and external rows into one comparison report should independently re-check tool availability and annotate or exclude those rows from meaningful comparisons.

This avoids nonsense conclusions like "`bwa mem` is 1000× faster" when in reality `bwa` was missing and the benchmark method short-circuited immediately.

### External head-to-head benchmarks should return a comparable work metric, not an exit code

Returning a process exit code (`0`, `1`, etc.) from a benchmark method is not a meaningful benchmark result. It causes two problems:

1. it hides whether the tool really produced output, and
2. it makes the benchmark result incomparable to the BioSharp side.

For head-to-head benchmarks, the external method should instead write its real output to a temp file and summarize a comparable unit of work, for example:

- alignment benchmark → mapped SAM records
- variant calling benchmark → non-header VCF records
- BCL benchmark → compressed FASTQ bytes / read count / base count

That makes the return value itself useful as a lightweight sanity check that the tool actually did the intended work.

### Never reuse mutable pipelines across benchmark iterations

If a benchmark class stores a mutable pipeline object in a field and reuses it across benchmark invocations, timing can drift because state accumulates between iterations. In BioSharp, `VariantCallingPipeline` stores alignments and variants internally, so reusing one instance across multiple benchmark calls is incorrect.

Benchmark methods should create a **fresh pipeline instance per invocation** unless the type is explicitly designed to be reset and reused.

### Fixed relative repo paths are unreliable inside BenchmarkDotNet generated job directories

BenchmarkDotNet runs benchmarks from generated job output directories such as:

```text
.../bin/Release/net10.0/openmedstack.biosharp.benchmarks-Job-XYZ/bin/Release/net10.0/
```

A simple fixed relative path like `../../../../..` may no longer resolve to the repository root from there. For benchmark code that needs workspace test data, the safer pattern is:

- start from `AppContext.BaseDirectory` and `Directory.GetCurrentDirectory()`
- walk parent directories upward
- stop when a reliable workspace marker is found, e.g. `openmedstack-biosharp.sln` plus `data/`

That keeps the benchmark working both in the normal project directory and inside BenchmarkDotNet job directories.

### Warm JIT, but measure real CLI behavior separately

### Mounted volumes can create noisy executable-bit diffs

On the `/Volumes/public` workspace, many files appeared as mode `100755` even after `chmod 644`. This created huge, unrelated Git mode-change noise. For this repository on that volume, set repo-local Git config:

```bash
git config core.fileMode false
```

Then review content diffs and status instead of chasing permissions the filesystem will not preserve.

For BioSharp code paths, BenchmarkDotNet warmup iterations are enough to avoid unfairly charging .NET JIT startup to the measured result. That means BioSharp inner-loop timings (FM-index lookup, Smith-Waterman, pipeline stages) should be benchmarked **in-process** with pre-built inputs in `GlobalSetup`.

For external tools like `bwa`, `freebayes`, or `bcl2fastq`, the realistic cost includes process startup, native library loading, index loading, and command-line parsing. The cleanest comparison is therefore:

- BioSharp steady-state code measured in-process with BenchmarkDotNet warmup
- External tools exercised as real subprocesses inside a benchmark method

This does not make the raw numbers identical in interpretation, but it does make them useful for identifying where BioSharp is slower in the actual user workflow.

### Put shared inputs in `GlobalSetup`

To keep comparisons apples-to-apples, generate or load the shared reference / FASTQ / BAM inputs once in `GlobalSetup` and reuse them across both BioSharp and external-tool benchmark methods. This avoids measuring data generation costs instead of the algorithm under test.

Recommended pattern:

```csharp
[GlobalSetup]
public void Setup()
{
    _reference = LoadReference(...);
    _readsPath = WriteSharedFastq(...);
    _bamPath   = PrepareSharedBam(...);
    _fmSeeder  = new FmIndexSeeder(_reference);
    _hashIndex = new ReferenceIndex(_reference, options);
}
```

### Redirect external-tool stdout when serialization is not the point

When comparing algorithmic work (alignment, calling, decoding), writing huge SAM/VCF/FASTQ outputs to the terminal can dominate the measurement. Redirect stdout to `/dev/null` (or `NUL` on Windows) unless the write path itself is the thing being benchmarked.

Examples:

```bash
bwa mem ref.fa reads.fq > /dev/null 2>&1
freebayes -f ref.fa input.bam > /dev/null 2>&1
```

For BioSharp, this means either:

- return in-memory objects, or
- explicitly create a separate benchmark that includes disk write cost.

### External-tool benchmarks must degrade gracefully

Benchmark discovery should still work on machines without `bwa`, `freebayes`, or `bcl2fastq`. The harness should detect tool availability up front and make the benchmark method return quickly instead of crashing the full run.

This keeps the suite runnable everywhere while still enabling true head-to-head comparison on properly provisioned benchmarking machines.

### Use multiple benchmark layers

A useful performance harness should include:

1. **Micro-benchmarks** — e.g. `BackwardSearch`, `FindMemSeeds`, banded vs unbanded SW
2. **Component benchmarks** — e.g. `ReferenceIndex` vs `FmIndexSeeder`
3. **Pipeline benchmarks** — BioSharp pipeline from FASTQ/BAM
4. **External-tool head-to-head benchmarks** — `bwa`, `freebayes`, `bcl2fastq`

This layered design makes it much easier to localize regressions. If the pipeline is slower but the micro-benchmarks are flat, the problem is likely orchestration or I/O rather than the algorithm itself.

### Cross-compile .NET for x86_64 containers on ARM64 hosts

When building container images for x86_64 (linux/amd64) on ARM64 hosts (Apple Silicon), avoid running `dotnet build` inside QEMU emulation. Instead:

1. **Cross-compile locally**: `dotnet publish -r linux-x64 --self-contained true -o ./publish`
2. **Copy pre-built binaries** to a minimal runtime-only Dockerfile
3. **Run with platform flag**: `podman run --platform linux/amd64`

This avoids BenchmarkDotNet/.NET 10 SDK compatibility issues under QEMU and dramatically reduces build time.

**Minimal runtime Dockerfile pattern**:
```dockerfile
FROM --platform=linux/amd64 mcr.microsoft.com/dotnet/runtime-deps:10.0-noble
WORKDIR /app
COPY publish/ ./benchmarks/
COPY tools/linux-x64/bcl-convert /usr/local/bin/bcl-convert
RUN chmod +x /usr/local/bin/bcl-convert
ENTRYPOINT ["/app/benchmarks/myapp"]
```

### BenchmarkDotNet 0.15.x has .NET 10 compatibility issues

BenchmarkDotNet 0.15.8 can fail with `CustomAttributeFormatException` when running under .NET 10 due to reflection issues with `[Benchmark(Description = "...")]` attributes. When in-process mode fails:

- Consider using a simple timing harness instead of BenchmarkDotNet for manual comparisons
- Or upgrade to a newer BenchmarkDotNet version when available

### bcl-convert requires a full Illumina run folder structure

Unlike direct BCL file decoding, `bcl-convert` requires a complete Illumina run folder including:
- `RunInfo.xml`
- `SampleSheet.csv`
- Proper directory structure with Data/Intensities/BaseCalls

Testing bcl-convert against raw BCL files alone will fail. For valid comparisons, either create synthetic run folder metadata or use a real Illumina run directory.

### Network volumes cause slow container builds

Building Docker/Podman images from network volumes (e.g., `/Volumes/public/`) can be extremely slow due to context transfer overhead. For better performance:

1. Copy source files to local disk first
2. Exclude large files (bin/, obj/, *.db) with tar or rsync
3. Build from the local copy

```bash
tar -cf - --exclude='bin' --exclude='obj' src benchmarks | tar -xf - -C ~/local-build/
cd ~/local-build && podman build -f Dockerfile .
```

## FM-index / BWT alignment

### Locate formula requires modulo (n+1)
The standard BWT LF-mapping property is `SA[LF(i)] = SA[i] - 1 (mod n+1)`. Therefore, when
walking back k LF-steps from row i to a sampled row j:
```
SA[i] = (SA[j] + k) mod (n+1)
```
Forgetting the modulo causes off-by-one or out-of-range positions whenever `SA[j] + k ≥ n+1`
(which happens near the end of the suffix array where suffixes wrap around the sentinel).

### Sampled Occ table — avoid overwriting partition 0
When writing the "total count" sample after the loop, always write to slot
`len / sampleRate + 1` (never `len / sampleRate`). For references shorter than one sample
interval, `len / sampleRate == 0`, which would overwrite the correctly zeroed slot 0.

### Static CharToBase lookup must cover the full byte range
The initial counting sort in prefix-doubling SA construction must support any `byte` value
(0–255), so the count array must be sized 257 (for offset-trick `cnt[rank[i]+1]`). Sizing it
to `AlphabetSize+1` (7) causes out-of-bounds on test data with character codes > 5.

### Seeder output compatibility
`FmIndexSeeder` returns the same `ReferenceIndex.CandidateWindow[]` type as `ReferenceIndex`.
Both implement `IReferenceSeeder`. The `VariantCallingPipeline.Seeder` property allows
swapping seeders without changing the call site.

### BWT Transform → FM-index connection
`BurrowsWheelerTransform.Transform()` was historically O(n² log n) due to naive string sort.
Delegating to `FmIndex.BuildSuffixArray()` (prefix doubling, O(n log n)) reduces construction
time by orders of magnitude for references > ~1 000 bp.

## Ensembl / GENCODE Transcript ID Version Mismatch

**Context**: Ensembl GTF files store unversioned transcript IDs (`ENST00000511072`) while
the corresponding cDNA FASTA files use versioned headers (`>ENST00000511072.5 cdna …`).
GENCODE GTF files, on the other hand, carry versioned IDs (`ENST00000832824.1`) that match
the versioned FASTA headers.

**Lesson**: When building a lookup dictionary from FASTA sequences and then matching them
against GTF transcript IDs, you must normalize both sides to a common form.  The safest
approach is to **strip the `.N` version suffix from `ENS*` identifiers** everywhere: in
`NormalizeSequenceId` (applied to FASTA keys) and when extracting `transcript_id` from the
GTF (applied to both Ensembl and GENCODE).  This way both sources → `ENST00000511072`.

```csharp
// Strip .N version from Ensembl/GENCODE IDs (e.g. ENST00000511072.5 → ENST00000511072)
private static string StripEnsemblVersion(string id)
{
    if (id.Length > 3 && id.StartsWith("ENS", StringComparison.OrdinalIgnoreCase))
    {
        var dotIdx = id.LastIndexOf('.');
        if (dotIdx > 0 && dotIdx < id.Length - 1)
        {
            var suffix = id.AsSpan(dotIdx + 1);
            if (suffix.IndexOfAnyExceptInRange('0', '9') < 0)
                return id[..dotIdx];
        }
    }
    return id;
}
```

Apply in `NormalizeSequenceId` (for FASTA) AND when reading `transcript_id` from the GTF.

---

## GTF Duplicate Attribute Keys (e.g. `tag`)

**Context**: GTF format allows the same key to appear multiple times on one line:
`tag "gencode_basic"; tag "Ensembl_canonical";`

**Lesson**: A simple `Dictionary<string, string>` will silently drop all but the last value
for repeated keys.  The `IsEnsemblCanonical` heuristic checks `tag.Contains("Ensembl_canonical")`,
so if `tag "Ensembl_canonical"` is overwritten by a subsequent `tag "foo"` the canonical flag
is missed.

**Fix**: When a key already exists in the attributes dictionary, **concatenate with a comma**
rather than overwrite:

```csharp
if (attributes.TryGetValue(key, out var existing))
    attributes[key] = existing + "," + value;
else
    attributes[key] = value;
```

This makes all `.Contains(...)` checks on the combined value work correctly.

---

## HGVS Parsing — Reference vs Version Separator

**Context**: `HgvsVariant.Parse` must extract the reference sequence accession number and its
optional version from strings like `NM_004006.2:c.4375C>T` and also handle gene-name
references without a version like `BRCA1:c.100A>G`.

**Lesson**: The dot (`.`) in a full HGVS string is used twice:
1. Between the accession number and its version: `NM_004006.2`
2. At the start of the description type prefix: `c.` / `g.` / `n.`

Using `IndexOf('.')` to find the version dot will grab the *first* dot in the string — which
is wrong when the reference has no version (the first dot will be inside the description).

**Fix pattern**: Always find the colon first, then check whether the dot appears *before* the colon:

```csharp
var colon   = input.IndexOf(':');
var dot     = input.IndexOf('.');
var hasVersionDot = dot >= 0 && dot < colon;
var reference = hasVersionDot ? input[..dot] : input[..colon];
```

---

## HGVS Position Notation — More than just integers

HGVS positions are not always a simple integer. The full syntax is:

| Notation      | Meaning                                             |
|---------------|-----------------------------------------------------|
| `100`         | Coding position 100                                 |
| `-100`        | 100 bases *before* the coding start (5' UTR)        |
| `*100`        | 100 bases *after* the stop codon (3' UTR)           |
| `100+2`       | 2 bases into the intron after coding position 100   |
| `100-1`       | 1 base before coding position 100 (intronic)        |

**Regex pattern** covering all of the above: `[-*]?\d+(?:[+-]\d+)?`

Use this pattern inside all HGVS description regexes to be spec-compliant.

---

## Single-Base Deletion Format

HGVS uses `c.4375del` for a single-nucleotide deletion — no range is written. Multi-base
deletions use a range: `c.4375_4379del`.

The `DeletionRegex` must therefore make the `_to` range optional. If `to` is absent, set
`to = from` for a well-formed model:

```csharp
var toValue = string.IsNullOrEmpty(match.Groups["to"].Value) ? fromValue : match.Groups["to"].Value;
```

---

## Insertion — Always Between *Adjacent* Positions

Per HGVS nomenclature, an insertion is placed *between* two consecutive positions:
`c.4375_4376insACCT`. Using the same position twice (`c.4375_4375ins...`) is invalid.

**Fix in `BuildHgvsCoding`**:

```csharp
// Wrong:  $"c.{cPos}_{cPos}ins{insSeq}"
// Correct:
return $"c.{cPos}_{cPos + 1}ins{insSeq}";
```

---

## Non-Coding Variants — Use Standard Uncertain-Position Notation

Custom suffixes like `spl`, `spl_dis`, `crypt` are not HGVS notation and will fail parsing.
Use the standard uncertain-position marker `?` for all non-coding or uncertain variants:

| Consequence      | Generated notation |
|------------------|--------------------|
| Upstream         | `c.-{pos}?`        |
| Downstream       | `c.*{pos}?`        |
| Intergenic       | `n.{pos}?`         |
| All others       | `c.{pos}?`         |

These all match the `UncertainPositionRegex` after updating it to use the extended position
pattern `[-*]?\d+(?:[+-]\d+)?`.

---

## `UncertainPositionDescription.Position` Should Be a String

Because HGVS positions can contain `-`, `*`, and `+/-` offsets, storing `Position` as `int`
is wrong. Change the record property to `string Position` so complex positions are preserved
without information loss.

---

## Tool Comparison Documentation — Common Pitfalls

When writing bioinformatics tool comparisons, watch for these common errors:

1. **Compare the right tools.** `fasterq-dump` (SRA-TOOLS) downloads SRA archives — it does NOT decode Illumina BCL files. The correct BCL comparison tools are `bcl2fastq` and Illumina's `BCL Convert`.

2. **FASTX-Toolkit is C, not Perl.** It is a compiled C/C++ binary suite.

3. **`seqrepo` is a reference sequence repository** — it fetches sequences by accession and does not process VCF or Tabix files.

4. **Genotype threshold documentation must match code.** In `Genotype.cs`, anything with alt/ref ratio < 0.4 is classified as HomRef (the 0.2–0.4 borderline range defaults to HomRef). Documenting `HomRef (<0.2)` omits the 0.2–0.4 conservative default.

5. **GATK does not use a Bloom filter.** GATK HaplotypeCaller uses pair-HMM for haplotype likelihood estimation. Bloom filters are found in genome assemblers (SPAdes, Minia).

6. **SIMD in .NET Intrinsics is often partial.** `Vector256<T>` SIMD in helper functions (mismatch counting, k-mer scan) does not imply that the core DP inner loop is SIMD-accelerated.

7. **NeedlemanWunsch + BLOSUM62 = protein aligner.** If the NW implementation uses BLOSUM62, it is designed for protein alignment, not DNA read alignment.

---

## Do NOT Use the `Async` Suffix on Async Method Names

Methods that return `Task` or `Task<T>` (or `ValueTask` / `ValueTask<T>`) should **not** have
an `Async` suffix appended to their name. The suffix adds noise without adding information —
callers already know the method is asynchronous from its return type and the `await` keyword.

**Rule**: Only keep the `Async` suffix when it is *required* by a
framework or external contract, specifically:
1. The method is an **overload** of a non-async version with the same name (so the suffix
   disambiguates the two).
2. The method is an **explicit implementation** (or required override) of an interface or base
   class member whose name already carries the `Async` suffix.

In all other cases, drop the suffix:

```csharp
// Wrong
public Task<Alignment> AlignAsync(string query, CancellationToken ct) { ... }

// Correct
public Task<Alignment> Align(string query, CancellationToken ct) { ... }
```

## In-Memory Pipeline Stages — Avoiding File Round-Trips

**Context**: The `e2e` command needed to run variant calling followed by annotation
without writing VCF to disk and reading it back.

**Lesson**: When connecting pipeline stages that were originally designed around files,
the cleanest approach is:
1. Keep the file-writing step intact (for auditing / downstream consumers).
2. Also expose an in-memory path by extracting a method that accepts
   `IAsyncEnumerable<T>` (or `IReadOnlyList<T>`) of the domain objects.
3. Convert between the upstream domain type (`LocalVariantResult`) and the downstream
   domain type (`VcfVariant`) with a small private helper that yields from an
   `IAsyncEnumerable` — this preserves backpressure and avoids materialising the
   full list again.

**Pattern**: extract a `RunWithVariants(IAsyncEnumerable<VcfVariant>, ...)` overload
on the command class. Use a private `ToVcfVariants(IReadOnlyList<LocalVariantResult>, ...)`
async iterator for the conversion, using `[EnumeratorCancellation]` and `await Task.CompletedTask`
to satisfy the compiler for synchronous yields inside an async iterator.

**Access modifiers**: Helper methods called from a sibling command must be `internal`
(not `private`). Change `private static` to `internal static` for `ProcessFasta` /
`ProcessFastq` in `AnalysisCommand` when `E2ECommand` needs to reuse them.

**Disposable resources**: When wrapping an EF Core `DbContext` for use across a pipeline
stage, define an `IAnnotationDatabaseHandle : IAsyncDisposable` interface and a private
implementation that holds the context so callers can `await using` it safely.

---

## Cross-Architecture Binary Compatibility in Containers

**Context**: Staging a Linux binary like `bcl-convert` for use in a container benchmark.

**Lesson**: Pre-compiled Linux binaries are architecture-specific. An x86-64 ELF binary
(like `tools/linux-x64/bcl-convert`) cannot run on an ARM64 Linux container — even if
both are "Linux". On Apple Silicon Macs with Podman/Docker, the VM runs ARM64 Linux.

**Check binary architecture** before staging:
```bash
file tools/linux-x64/bcl-convert
# bcl-convert: ELF 64-bit LSB executable, x86-64, version 1 (GNU/Linux)
```

**Implications**:
- Benchmarks requiring x86-64 tools must run on x86-64 Linux (physical or emulated)
- Stage separate binaries per architecture (`tools/linux-x64/`, `tools/linux-arm64/`)
- Document architecture requirements in benchmark README and results

**Workarounds**:
1. Run benchmarks on an x86-64 Linux machine
2. Obtain ARM64 builds from vendors (some vendors like Illumina provide ARM64 builds)
3. Use Rosetta 2 emulation (limited and slower)

## Per-tool setup failure isolation in WarmBenchmarkReportRunner

`WarmBenchmarkReportRunner.RunType()` invokes `[GlobalSetup]` once per parameter set.
If the setup method throws, the runner catches the exception and marks **every** benchmark
method in that class as `Failed` — not just the one that caused the problem.

This means a single broken validation step (e.g. BioSharp producing incorrect read counts)
silently kills unrelated benchmarks (bcl-convert, bcl2fastq, throughput probe) in the same
class, making it impossible to tell which tool actually failed.

**Pattern to prevent this**: mirror the existing external-tool isolation pattern for
*every* validating path inside `[GlobalSetup]`:

1. Add a `private string? _<tool>SetupError;` field per tool.
2. Wrap each validation in try/catch; store the exception message in the field on failure.
3. At the top of every benchmark method that belongs to that tool, throw
   `InvalidOperationException(_<tool>SetupError)` when the field is non-null.

```csharp
// Setup — safe for each tool independently
try { ValidateBioSharpPath().GetAwaiter().GetResult(); }
catch (Exception ex) { _bioSharpSetupError = ex.Message; }

// Benchmark method — fail only this row
[Benchmark]
public async Task<int> BioSharp_Decode()
{
    if (_bioSharpSetupError != null)
        throw new InvalidOperationException(_bioSharpSetupError);
    // ...
}
```

**Result**: each benchmark row in the CSV gets either `Measured` or `Failed` with its own
specific error message, and unrelated tools continue running.

