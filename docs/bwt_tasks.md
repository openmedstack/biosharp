# BWT / FM-Index Implementation Tasks

## Overview

This file contains a detailed task list with acceptance criteria for implementing the six missing features identified in `bwt_iisues.md`. Each task is written as a self-contained specification that can be handed to an implementer or subagent.

## Task 1 — BWT Encode Public API (P0)

**Priority:** P0
**Component:** `BurrowsWheela/BurrowsWheelaTransform.cs`
**Tracking Issue:** "BWT Encode () public API"

### Acceptance Criteria

- Add `public static string Encode(string input)` to `BurrowsWheelaTransform`.
- The method delegates to the existing private `ComputeSuffixArray` and `FindLastChar`, or refactors them into the public method for clarity.
- Input must not be null; throw `ArgumentNullException`.
- Input must not be empty; throw `ArgumentException`.
- Output is a string of the same length as the input, being the BWT-encoded string.
- Given input S, calling `Encode(S)` then `Invert(result)` must equal the original S (round-trip verification).
- The method must handle sentinel characters (e.g. `$`) correctly. If the input does not contain a sentinel, warn or auto-append one (document the behavior).

### Implementation Notes

- Current code has `ComputeSuffixArray(string)` and `FindLastChar(string, int[])` as private static methods. The public API should call `ComputeSuffixArray` then `FindLastChar`.
- Consider adding an optional `char? sentinel` parameter; default to `$`.
- Document that for sequences without a sentinel character, behavior is undefined unless one is provided or appended.

### Tests

- `Encode_BasicRoundTrip` : Encode("ACGT$") then Invert must yield "ACGT$".
- `Encode_EmptyStringThrows` : Pass empty string; expect `ArgumentException`.
- `Encode_NullThrows` : Pass null; expect `ArgumentNullException`.
- `Encode_WithSentinel` : Verify round-trip for inputs with `$` sentinel.
- `Encode_NoSentinel` : Pass input without `$`; verify that no exception is thrown and that Invert still works (or document that sentinel is required).
- `Encode_RepeatedPattern` : Encode("ACGTACGT$") and verify round-trip.

---

## Task 2 — FM-Index with Backward Search (P0)

**Priority:** P0
**Component:** New file `BurrowsWheela/FmIndex.cs`

### Acceptance Criteria

- Create a class `FmIndex` in namespace `OpenMedStack.BioSharp.Calculations.BurrowsWheeler`.
- `FmIndex.Build(string sequence, char? sentinel = '$')` constructs:
    - The BWT string (using Task 1's `Encode`).
    - The C array: `Dictionary<char, int>` mapping each character to the number of characters in the BWT that are lexicographically smaller.
    - The Occ table: A 2-D structure where `Occ(char c, int position)` returns the count of `c` in `BWT[0..position]` (inclusive).
    - Support DNA alphabet: A, C, G, T, N (case-insensitive). Any other character is invalid.
- `FmIndex.Search(string pattern)` performs backward search and returns `IEnumerable<int>` of 0-based starting positions where `pattern` occurs in the original (unencoded) sequence.
- The search must be correct: every returned position must be a valid occurrence of `pattern` in the original sequence.
- `Search("")` returns all valid starting positions (0 through sequence length - pattern length).
- `Search(pattern)` with a pattern longer than the sequence returns an empty enumerable.
- Construction time and memory should be reasonable for sequences up to at least 10,000 bases within a few seconds and under 50 MB.
- The index is immutable after construction (no mutations allowed post-build).

### Implementation Notes

- BWT construction already exists on `BurrowsWheelaTransform`. Reuse it.
- The C array: sorted unique chars from BWT, each mapped to cumulative count of smaller chars.
- The Occ table: Use a compact representation (e.g., `int[charCount, BWT.Length]` or a dictionary of lists to save space). Since DNA alphabet is small (A, C, G, T, N = 5), a 2D array `int[5, length]` is fine.
- Map characters: A→0, C→1, G→2, T→3, N→4 (or use a `Dictionary<char, int>` lookup).
- Backward search algorithm:
    1. Initialize `rowStart = 0`, `rowEnd = BWT.Length - 1`.
    2. Process pattern from last character to first:
        a. For character `c`, look up `count = Occ(c, rowEnd) - Occ(c, rowStart - 1)`.
        b. If `count == 0`, the pattern does not occur; return empty.
        c. Compute new `rowStart = C[c] + Occ(c, rowStart - 1)` and `rowEnd = C[c] + count - 1`.
    3. The result is positions `rowStart` through `rowEnd` in the BWT, which correspond to rotation starting positions.
- Positions are in terms of the original (unrotated) string; handle sentinel position specially (sentinel should not be returned as a valid match).

### Tests

- `Build_VerifyBWT` : Build on "ACGT$" and verify BWT equals known correct output.
- `Search_SimpleMatch` : Build on "ACGTACGT$", search "ACGT" → should return [0, 4].
- `Search_NoMatch` : Build on "ACGT$", search "XYZ" → should return empty.
- `Search_EmptyPattern` : Build on "ACGT$", search "" → should return [0, 1, 2, 3, 4].
- `Search_PatternLongerThanSeq` : Should return empty.
- `Search_SentinelNotReturned` : Ensure sentinel position itself is excluded from search results.
- `Search_CaseInsensitive` : Build on "ACGT", search "acgt" works.
- `Search_RepeatedSubstring` : Build on "AAAA$", search "AA" → return [0, 1, 2].
- `Search_LargeSequence` : Build on 10,000 random DNA bases; search a 20-mer that appears; verify correctness.
- `Occ_Correctness` : Verify Occ table independently by counting occurrences in BWT substring.
- `CArray_Correctness` : Verify C array sums match BWT length.

---

## Task 3 — BWT / FM-Index-Based Alignment (P1)

**Priority:** P1
**Component:** New file `BurrowsWheela/FmAligner.cs`

### Acceptance Criteria

- Create a class `FmAligner` in namespace `OpenMedStack.BioSharp.Calculations.BurrowsWheeler`.
- `FmAligner.Search(FmIndex fmIndex, string read, int maxErrors = 0, int? seedLength = null)` searches for the read in the reference using the FM-index.
- If `maxErrors == 0` (exact matching), uses FM-index backward search directly.
- If `maxErrors > 0`, uses seed-and-extend or multi-seed approach:
    - Split the read into overlapping seeds of length `seedLength` (default: 20).
    - Search each seed independently via FM-index.
    - For each valid seed hit, attempt to extend using Smith-Waterman banded alignment around the seed hit within a window of ~2*maxErrors.
    - Return the best alignment(s) that are within `maxErrors` mismatches/indels of the read.
- Returns `IEnumerable<FmAlignResult>` containing:
    - `ReferencePosition` (0-based start on the reference).
    - `SeedHits` : List of seed positions found.
    - `ErrorCount` : Total mismatches + indels in the best alignment.
    - `Score` : Alignment score.
    - `AlignedRead` : The aligned read string (with '-' for gaps).
    - `AlignedReference` : The aligned reference substring.
- If no valid alignment is found (all seed hits fail extension or exceed maxErrors), return empty enumerable.
- When multiple alignments have the same best score, return all of them (up to a configurable `maxResults` parameter, default 100 to avoid memory issues).
- `FmAlignResult` must be a `record` for immutability and value equality.
- Performance: For reads up to 300bp against a reference-indexed region of up to 10,000bp with up to 5 errors, search should complete in under 5 seconds.

### Implementation Notes

- Seed length: Default to 20bp. For exact search, a single seed spanning the full read works fine.
- For approximate search, use overlapping seeds with a step of `seedLength/2`.
- Extension: Use a banded Smith-Waterman variant centered on the seed hit. The band width should be proportional to `maxErrors`.
- For the Smith-Waterman, reuse the existing `SmithWatermanAligner` logic (port into a reusable static method if needed).
- Sort results by score descending.

### Tests

- `SearchExact_SimpleMatch` : Build FM-index on "ACGTACGTACGTACGT", search "ACGTACGT", maxErrors=0 → expect match at position 0.
- `SearchExact_NoMatch` : Search for a sequence not in reference → expect empty.
- `SearchApproximate_SingleMismatch` : Reference="ACGTACGT", read="ACCTACGT", maxErrors=1 → expect match with 1 mismatch.
- `SearchApproximate_SingleInsertion` : Reference="ACGTACGT", read="ACGTACATGT", maxErrors=2 → expect match with 1 insertion.
- `SearchApproximate_SingleDeletion` : Reference="ACGTACGTACGT", read="ACGTACGTGT", maxErrors=2 → expect match with 1 deletion.
- `SearchApproximate_MaxErrorsExceeded` : Read with 3 errors vs maxErrors=1 → expect empty.
- `SearchMultipleHits` : Reference with repeated pattern, read matches multiple positions → expect all returned.
- `SearchWithNoSeedHits` : Random junk read against reference → expect empty.

---

## Task 4 — CIGAR Generation from AlignmentResult (P1)

**Priority:** P1
**Component:** `Alignment/CigarBuilder.cs` (already exists but reviewed for gaps)

### Acceptance Criteria (Current CIGAR builder review)

The existing `CigarBuilder` class already has:
- `BuildCigar(AlignmentResult, int trimPrefix=0)` → string
- `BuildCigar(string alignedRef, string alignedRead, int leftClip=0, int rightClip=0)` → string
- `BuildCigarOps(AlignmentResult, int trimPrefix)` → (int, char)[]
- `BuildCigarOps(string, string, int, int, int)` → (int, char)[]
- `FormatCigar(List<CigarOpCount>)` → string

Verify the following are correct and complete:

1. CIGAR is produced matching SAM/BAM spec:
    - Operations: M (match/mismatch), I (insertion in read), D (deletion in read), S (soft clip), N (skip), = (exact match), X (mismatch), H (hard clip), P (padding).
    - Operations M, I, D, S, N, =, X must be generated.
    - = and X are not currently produced — add support if needed.
2. `BuildCigar` merges consecutive operations correctly (e.g., "AAA" → "3M").
3. `BuildCigar` with `trimPrefix` correctly skips leading characters in the alignment strings.
4. `BuildCigarOps` merges operations into (count, op) tuples correctly.
5. Edge cases: empty alignment, alignment with only clips, alignment with only gaps.

### Gap: Add = and X operators

- Add `GenerateExactCigar()` variant that uses `=` for exact matches and `X` for mismatches instead of `M` for both. This is preferred for SAM spec compliance when exact reference is known.
- Configuration option: `bool useExactOps` — when true, emit `=`/`X`; when false, emit `M` for all matches (default: false for backward compatibility).

### Gap: Add CIGAR string parsing (optional, nice-to-have)

- `ParseCigar(string)` → `CigarOp[]` (each with count and type).
- Validates that counts are > 0 and operations are valid SAM CIGAR ops.
- Throws `FormatException` for invalid CIGAR strings.

### Tests

- `BuildCigar_SimpleMatch` : ref="ACGT", read="ACGT", leftClip=0, rightClip=0 → "4M".
- `BuildCigar_WithSoftClip` : leftClip=3, rightClip=5, match=10 → "3S10M5S".
- `BuildCigar_WithInsertion` : ref="ACGT", read="ACGGT" → "3M1I1M".
- `BuildCigar_WithDeletion` : ref="ACGAT", read="ACGT" → "3M1D1M".
- `BuildCigar_EmptyAlignment` : empty strings → "*".
- `BuildCigar_TrimPrefix` : trimPrefix=2, ref="XXACGT", read="XYACGT" → "3M1M" after trim → "4M".
- `BuildCigarOps_Merge` : Verify ops merge into (count,op) with no consecutive duplicates.
- `GenerateExactCigar_ExactMatch` : "ACGT" vs "ACGT" with exact ops → "4=".
- `GenerateExactCigar_Mixed` : "ACGT" vs "ACGA" → "3=1X".
- `ParseCigar_Valid` : "3M1I2M" → parse returns CIGAR ops list of length 3.
- `ParseCigar_InvalidOps` : "3Z" → FormatException.

---

## Task 5 — Multi-Contig ReferenceIndex (P2)

**Priority:** P2
**Component:** New file `Alignment/ReferenceIndex.cs`

### Acceptance Criteria

- Create a class `ReferenceIndex` in namespace `OpenMedStack.BioSharp.Calculations.Alignment`.
- `ReferenceIndex` wraps a `Dictionary<string, Sequence>` (contig name → Sequence).
- `ReferenceIndex.LoadFromFasta(string filePath)` asynchronously reads a multi-contig FASTA file and populates the index.
    - Supports plain `.fa` / `.fasta` and gzipped `.fa.gz` / `.fasta.gz`.
    - Each `>` header line creates one entry with its `Id` as the key.
- `ReferenceIndex.Add(string contigName, Sequence sequence)` adds a single contig.
- `ReferenceIndex.Remove(string contigName)` removes a contig if it exists.
- `ReferenceIndex.this[string contigName]` → `Sequence?` returns null when key is missing.
- `ReferenceIndex.ContigNames` → `IReadOnlyCollection<string>` of all contig names.
- `ReferenceIndex.TotalBases` → total bases across all contigs.
- `SmithWatermanAligner` should gain an overload:
    - `Align(ReferenceIndex referenceIndex, Sequence read, ...)` that:
        1. Uses an FM-index-based heuristic to find the best contig for the read (e.g., seed-and-count which contig has the most exact seed matches).
        2. Falls back to Smith-Waterman against all contigs if FM-index is not available or heuristic is inconclusive.
        3. Returns `AlignmentResult` with a `ContigName` property so the caller knows which contig was aligned.
- `AlignmentResult` should be extended with a `ContigName` property (nullable, set only when alignment is against a multi-contig reference).

### Implementation Notes

- Use the existing `FastAReader` for FASTA loading.
- For the heuristic contig selection, use the FM-index (Task 2) to count exact seed matches per contig. The contig with the most matches wins. If no contig has any matches, fall back to trying all contigs with Smith-Waterman.
- Thread safety: ReferenceIndex is NOT thread-safe. If multi-threaded access is needed, add a lock or use `ConcurrentDictionary` internally.

### Tests

- `LoadFromFasta_SingleContig` : Load a single-contig FASTA → index has 1 contig.
- `LoadFromFasta_MultiContig` : Load 5-contig FASTA → index has 5 contigs with correct names.
- `LoadFromFasta_Gzipped` : Load a `.fa.gz` file → same number of contigs as plain.
- `Add_And_Contain` : Add contig "chr1"; `index["chr1"]` is not null; `index["chr2"]` is null.
- `Remove_Contig` : Remove "chr1"; index has 0 contigs after removal if it was the only one.
- `TotalBases` : Sum of all contig lengths matches expected.
- `Align_MultiContig_SelectsCorrectContig` : Read derived from "chr2" → aligns to chr2.
- `Align_MultiContig_FallsBackToAllContigs` : When no contig has seed hits, Smith-Waterman runs against all.
- `AlignmentResult_ContigName_Set` : Multi-contig alignment has `ContigName` populated.

---

## Task 6 — Fix Right Soft-Clip Calculation (P2)

**Priority:** P2
**Component:** `Alignment/SmithWatermanAligner.cs`

### Acceptance Criteria

- Current code at line 221 computes:
    `refLen - refStart - pos + alnRef.Count(c => c == '-')`
- This is incorrect. The right soft clip represents the number of reference bases AFTER the alignment end.
- Fix to:
    `refLen - (refStart + alignedRefLength)`
    where `alignedRefLength = pos` is the length of the aligned reference string (trimmed of gaps? No, `pos` is the length of the aligned strings including gaps).
- Wait — `pos` includes gaps in the reference. Right soft clip should count reference bases AFTER the alignment. Since `refStart` is the reference position where alignment starts, and the alignment spans `pos` columns, the last reference base consumed is `refStart + alignedRefBasesConsumed - 1`. The number of ref bases after the alignment is `refLen - (refStart + alignedRefBasesConsumed)`.
- `alignedRefBasesConsumed` = count of non-gap characters in the aligned reference = `pos - alnRef.Count(c => c == '-')`.
- So: `RightSoftClip = refLen - refStart - (pos - alnRef.Count(c => c == '-'))`.

### New Property on AlignmentResult

- Add `int UnalignedRefBasesAtStart` → equals `refStart` (alias for clarity).
- Add `int UnalignedRefBasesAtEnd` → same as `RightSoftClip` (alias for clarity).

### Tests

- `RightSoftClip_SimpleAlign` : ref="ACGTACGTACGT" (len 12), read aligns from position 2 spanning 6 ref bases → right clip = 12 - 2 - 6 = 4.
- `RightSoftClip_NoClip` : Read aligns to end of reference → right clip = 0.
- `RightSoftClip_AllClipped` : Read aligns only at very start → right clip = refLen - alignedBases.
- `UnalignedRefBasesAtStart_EqualsRefStart` : Verify alias property equals refStart.
- `UnalignedRefBasesAtEnd_EqualsRightSoftClip` : Verify alias property equals right soft clip.

---

## Task Dependencies

```
Task 1 (BWT Encode) ──┐
                        ├──► Task 2 (FM-Index) ──┬──► Task 3 (FM-Aligner)
                        └──────────────────────────┘
                        
Task 1 ──► Task 2 (for FM-index-based alignment in Task 3)

Task 4 (CIGAR) ── independent (uses existing AlignmentResult)

Task 5 (ReferenceIndex) ──► Task 3 optional (FM-index per contig for heuristic)

Task 6 (Soft-clip fix) ── independent bug fix
```

## Recommended Implementation Order

1. **Task 6** (soft-clip fix) — Quick correctness fix, no dependencies.
2. **Task 1** (BWT Encode public API) — Prerequisite for FM-index.
3. **Task 2** (FM-Index) — Core data structure, prerequisite for Task 3.
4. **Task 4** (CIGAR) — Independent, needed for SAM/BAM output.
5. **Task 3** (FM-Aligner) — Builds on FM-index; most complex.
6. **Task 5** (ReferenceIndex) — Multi-contig support; independent except for potential FM-index per-contig optimization.

## General Requirements

- All new code follows C# 13 conventions, uses `span`/`Memory<T>` where possible for efficiency.
- All new classes are in the `OpenMedStack.BioSharp.Calculations` namespace.
- All new classes have XML documentation comments (`///` style).
- All public APIs have `<summary>`, `<param>`, and `<returns>` tags.
- Null-safety: use `[NotNullWhen(true)]`, `[MaybeNullWhen(false)]`, and nullable reference types.
- Tests use xUnit v3 (consistent with existing test project).
- Test names follow the `MethodName_Scenario_ExpectedBehavior` convention.
- No breaking changes to existing public APIs unless explicitly stated.
