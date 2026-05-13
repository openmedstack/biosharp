# Task List

## Standing Rules

- **Never use the `Async` suffix** on async method names (methods returning `Task`, `Task<T>`,
  `ValueTask`, or `ValueTask<T>`). Exception: keep the suffix only when it disambiguates an
  overload of a non-async method, or when implementing/overriding an interface/base-class
  member that already has the suffix.

## Completed Tasks

### 3. Refactor `e2e` command: read FASTA/FASTQ, annotate variants in memory — DONE

Changed the `e2e` command from BCL file input to FASTA/FASTQ input (like `analysis`),
and added in-memory annotation (like `annotate`) without a VCF file round-trip.

Key changes:
- `E2EOptions`: replaced BCL fields (InputPath, ReadStructure, Lanes) with FASTA/FASTQ
  fields and annotation fields (TranscriptDatabasePath, TranscriptId, MinQuality,
  MaxReads, MinAlternateObservationCount, MinAlternateFraction).
- `E2ECommand`: reads FASTA/FASTQ via `AnalysisCommand.ProcessFasta/ProcessFastq`,
  writes VCF/TSV/summary to disk, then converts `LocalVariantResult[]` to
  `VcfVariant` via an in-memory `IAsyncEnumerable<VcfVariant>` (no file I/O),
  and calls `AnnotateCommand.RunWithVariants` directly.
- `AnnotateCommand`: extracted `RunWithVariants(IAsyncEnumerable<VcfVariant>, ...)`,
  `CreateAnnotationEngine(...)` (returning `IAnnotationDatabaseHandle`), and refactored
  `WriteSummary` to take individual parameters. Added `IAnnotationDatabaseHandle`
  interface for clean disposal of the EF Core context.
- `AnalysisCommand`: made `ProcessFastq` and `ProcessFasta` `internal` so E2ECommand
  can call them.
- `Program.cs`: updated `CreateE2ECommand` to register FASTA/FASTQ + annotation options
  with a validator requiring at least one of --fastq or --fasta.
- Test updated: `E2ECommandOptionTests` now uses `--fastq` + `--database` instead of
  the removed `--input` BCL option.

## Completed Tasks

### 2. Remove `Async` suffix from all async method names — DONE

Renamed all user-defined async methods (returning `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`,
or `IAsyncEnumerable`) to drop the `Async` suffix throughout `src/`, `tests/`, and benchmark
files. Exceptions kept with `Async` suffix (legitimate overload disambiguation):
- `GetCrc32Async` / `GetCrc32AndCopyAsync` in `CRC32.cs` — sync overloads with identical
  parameter signatures already exist (`GetCrc32(Stream)` / `GetCrc32AndCopy(Stream, Stream?)`).
- `GetCrc32Async` / `GetCrc32AndCopyAsync` in `CRC32.cs` — sync overloads exist.
- `FirstAsync<T>(this IAsyncEnumerable<T>)` — standard .NET 10 name; `First` already exists on
  `IEnumerable<T>`.
- Framework-required names kept: `DisposeAsync`, `ReadAsync`, `WriteAsync`, `FlushAsync`,
  `InitializeAsync`, `InvokeAsync` (System.CommandLine), xUnit lifecycle methods, etc.

### 1. Fix HgvsVariant/HgvsDescription parsing per HGVS nomenclature — DONE

**Status**: In progress

**Issues found:**

1. **`HgvsVariant.Parse`** - Bug: `dot = input.IndexOf('.')` finds first dot in string, 
   which is in description (e.g., the `.` in `c.100A>G`) when no version is present.
   For `BRCA1:c.100A>G`: reference parsed as "BRCA1:c" instead of "BRCA1"
   Fix: only treat dot as version separator if it appears BEFORE the colon.

2. **`HgvsDescription` SubstitutionRegex** — position `\d+` too restrictive:
   - Need `[-*]?\d+(?:[+-]\d+)?` to handle `c.-100?` (upstream), `c.*100?` (downstream), `c.100+1A>G` (intronic)

3. **`HgvsDescription` DeletionRegex** — requires range `from_to`, but single-base deletion is `c.100del`
   Fix: make `_to` part optional: `(?:_(?<to>...))?`

4. **`HgvsDescription` UncertainPositionRegex** — same as above, position too restrictive
   Fix: use `[-*]?\d+(?:[+-]\d+)?` for position pattern

5. **`UncertainPositionDescription`** — stores `int Position`, must change to `string Position`
   since positions can be like `*100`, `-5`, `100+1`

6. **`VariantAnnotator.BuildHgvsCoding`** — insertion produces `c.{pos}_{pos}ins{seq}` 
   (same position twice), HGVS requires adjacent positions: `c.{pos}_{pos+1}ins{seq}`

7. **`VariantAnnotationEngine.BuildNonCodingHgvs`** — generates non-parseable HGVS:
   - `c.{pos}spl`, `c.{pos}spl_dis`, `c.{pos}crypt` — not HGVS notation
   - `c.{pos}+?` — intronic uncertain, not matched by any current regex
   - `c.-{pos}` — upstream, no `?` suffix, needs extended position regex
   - `c.*{pos}` — downstream, no `?` suffix, needs extended position regex  
   - `n.{pos}` — intergenic, no `?` suffix
   Fix: standardize all to `c.{pos}?` or add `?` suffix for parseability

**Files to modify:**
- `src/openmedstack.biosharp.model/HgvsVariant.cs`
- `src/openmedstack.biosharp.model/HgvsDescription.cs`
- `src/openmedstack.biosharp.model/UncertainPositionDescription.cs`
- `src/openmedstack.biosharp.calculations/VariantAnnotator.cs`
- `src/openmedstack.biosharp.calculations/VariantAnnotationEngine.cs`


