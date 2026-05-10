# BWT / FM-Index / Alignment Tasks

Specification and implementation plan for all missing features listed in `bwt_iisues.md`.
Each task includes: objective, design, implementation steps, and acceptance criteria.
Tasks are ordered by priority (P0 before P1 before P2) and dependency.

---

## t1. BWT Encode — Public API (P0)

**Objective.**
`BurrowsWheeraTransform.Encode()` is already implemented as a `public static` method.
What is missing are unit tests to verify its contract and edge cases.

**Design.**
The existing ` Encode(string input, char sentinel = DefaultSentinel)` method:
- Auto-appends `$` sentinel if not present (line 89-90 of `BurrowsWheelerTransform.cs`).
- Validates no sentinel already in input.
- Calls `ComputeSuffixArray` + `FindLastChar`.

**Acceptance criteria.**
- [ ] `Encode("ACGT")` returns `"ACGT$"`.
- [ ] `Encode("ACGTACGT")` returns `"T$AAACCCGGGT"`.
- [ ] `Encode(sequence)` returns a string of the same length as `sequence + "$"`.
- [ ] `Encode($sentinel, sentinel)` throws `InvalidOperationException`.
- [ ] `Encode("")` throws `ArgumentException` with message "Input must not be empty."
- [ ] `Encode(null)` throws `ArgumentNullException`.
- [ ] Decode round-trip: `Invert(Encode(seq))` equals `seq + "$"` for varied inputs ("A", "ACGT", "ACGTACGT", "AAAA", "ACGTACGTACGT", "NNNNNACGTNNN").
- [ ] No regression on existing `BurrowsWheelerTransformEncodeTests.cs` tests.

---

## t2. FM-Index — Backward Search (P0)

**Objective.**
Create a `FmIndex` class on top of the BWT that provides exact substring search via the
FM-index backward-search algorithm.

**Design.**
The class (`FmIndex.cs`) implements:
- **Index construction:** BWT via `BurrowsWheelerTransform.Encode`, C array, Occ (occurrence rank) table, LF mapping, and suffix array.
- **Backward search:** Starts from the full range `[0, n-1]` and narrows character-by-character from the last base of the pattern to the first. Each step computes `S' = C(c) + count(c, 0 .. S-1)` and `E' = C(c) + count(c, 0 .. E)`, where `count(c, i, j)` = `Occ(c, j) - Occ(c, i-1)`.
- **Position extraction:** After finding the final range `[S, E]`, map each row index in that range to an actual reference position via the suffix array.
- **Alphabet:** A, C, G, T, N (case-insensitive). Invalid characters throw `ArgumentException`.

**Algorithm reference (from Python trace):**
For `"ACGT"` in `"ACGTACGTACGT"`:
- Start: S=0, E=12 (n=13)
- 'T': S=10, E=12
- 'G': S=7, E=9
- 'C': S=4, E=6
- 'A': S=1, E=3
- Extract positions from SA[1], SA[2], SA[3] -> [8, 4, 0] -> sorted: [0, 4, 8]

**Acceptance criteria.**
- [ ] `FmIndex("ACGTACGTACGT").Search("ACGT")` returns `[0, 4, 8]`.
- [ ] `FmIndex("ACGTACGTACGT").Search("ACGTACGTACGT")` returns `[0]`.
- [ ] `FmIndex("ACGTACGTACGT").Search("XYZ")` returns empty `int[]`.
- [ ] `FmIndex("ACGTACGTACGT").Search("T")` returns `[3, 7, 11]`.
- [ ] `FmIndex("ACGTACGTACGT").Search("A")` returns `[0, 4, 8]`.
- [ ] `FmIndex("ACGTACGTACGT").Search("AA")` returns `[]` (pattern "AA" not in ref).
- [ ] `FmIndex("AAAAAA").Search("AAA")` returns `[0, 1, 2, 3]`.
- [ ] Empty pattern `Search("")` throws (or returns empty) -- edge case to define.
- [ ] Pattern longer than reference returns `[]`.
- [ ] Null pattern throws `ArgumentNullException`.
- [ ] Invalid chars (e.g., "XY") throw `ArgumentException`.
- [ ] `Search` with `out IList<int>` overload returns `true`/`false` correctly.
- [ ] `GetCArray()` returns `{'$':0, 'A':1, 'C':4, 'G':7, 'T':10}` for "ACGTACGTACGT".
- [ ] `GetOccTable()` returns correct rank tables.
- [ ] `GetSuffixArray()` returns the full SA (for debugging).
- [ ] `GetLF()` returns the LF mapping (for debugging).
- [ ] All tests in `FmIndexTests.cs`.

---

## t8. BWT Decode Start Index (P2)

**Objective.**
Fix the `Invert()` method so that the start index for LF-mapping iteration is correctly computed
rather than hardcoded to 4.

**Design.**
Currently `Invert` hardcodes the start position. The correct approach:
- The sentinel row in the sorted (F) column is at index `C[sentinel]` (since sentinel '$' is
  always the lexicographically smallest character, its row starts at `C['$'] = 0`).
- The LF mapping `LF[i] = C[bwt[i]] + rank_bwt[i][i-1]` correctly maps from any row to its
  predecessor in the original string.
- Start from row `C[sentinel]` (which is 0 when sentinel is the smallest char), iterate `n` times
  collecting characters, then locate and strip the sentinel.

**Acceptance criteria.**
- [ ] `Invert(Encode("ACGT"))` returns `"ACGT"`.
- [ ] `Invert(Encode("ACGTACGTACGT"))` returns `"ACGTACGTACGT"`.
- [ ] `Invert(Encode("AAAA"))` returns `"AAAA"`.
- [ ] `Invert(Encode("NNNTANNN"))` returns `"NNNTANNN"`.
- [ ] `Invert("")` returns `""`.
- [ ] `Invert(null)` throws `ArgumentNullException` or is gracefully handled.
- [ ] Round-trip: for every test in t1, `Invert(Encode(seq)) == seq + "$"`.

---

## t3. BWT-Based Alignment — FM-Aligner (P1)

**Objective.**
Create an `FmAligner` class that uses the FM-index backward search for fast exact matching,
plus approximate (fuzzy) matching with a configurable number of allowed errors via a
seed-and-extend or iterative backward-search strategy.

**Design.**
- **Exact match:** `FmAligner.Search(string query, FmIndex index, maxErrors=0)` delegates to `index.Search(query)`.
- **Approximate match (k mismatches):** Use a chimeric seed approach. For a pattern P and max errors k:
  1. Split P into k+1 seeds of roughly equal length (e.g., P[0..m/2], P[m/2..m]).
  2. For each seed, perform backward search on the FM-index.
  3. For each pair of seed matches that are consistent with being on the same ref position
     (i.e., they overlap or abut on the reference), combine them into a candidate local alignment.
  4. Validate each candidate by comparing the full pattern against the reference region,
     counting mismatches/gaps. If within `maxErrors`, record as a match.
  5. Alternatively, use iterative backward search: search the first `k+1`-mer, then for each
     match extend with a banded Smith-Waterman alignment.
- **Output:** `FmAlignResult` containing positions and error count.

**Class structure:**
```
FmAlignResult {
    string Query;
    FmAlignResult[] Subalignments;  // for chimeric seeds
    int NumErrors;
    IList<int> Positions;
    int BestScore;
}

FmAligner {
    FmAlignResult Search(string query, FmIndex index, int maxErrors);
    FmAlignResult ExactSearch(string query, FmIndex index);
    FmAlignResult ApproxSearch(string query, FmIndex index, int maxErrors);
}
```

**Acceptance criteria.**
- [ ] `FmAligner.ExactSearch("ACGT", fmIndex)` returns the same positions as `fmIndex.Search("ACGT")`.
- [ ] `FmAligner.Search("ACGT", fmIndex, maxErrors=0)` returns exact matches.
- [ ] `FmAligner.Search("ACGU", fmIndex, maxErrors=1)` returns positions where ACGT matches with 1
  mismatch (N is treated as wild-card or mismatch -- define semantics).
- [ ] `FmAligner.Search("ACGUACGT", fmIndex, maxErrors=1)` finds partial matches with at most 1 error.
- [ ] Chimeric seeds with maxErrors=2 correctly combine partial matches on the same ref position.
- [ ] Returns empty `FmAlignResult` if no matches found.
- [ ] Handles patterns longer than max allowed / ref length gracefully.
- [ ] All tests in `FmAlignerTests.cs`.

---

## t4. CIGAR Generation (P1)

**Objective.**
Implement a `CigarBuilder` that converts `AlignmentResult` into a CIGAR string, and
extend `AlignmentResult` with a `.Cigar` property.

**Design.**
A CIGAR string encodes the alignment using operations:
| CIGAR | Meaning | Consumes ref? | Consumes query? |
|-------|---------|--------------|-----------------|
| M     | Alignment match/mismatch | Yes | Yes |
| =     | Sequence match | Yes | Yes |
| X     | Sequence mismatch | Yes | Yes |
| I     | Insertion in read (del in ref) | No | Yes |
| D     | Deletion in read (ins in ref) | Yes | No |
| N     | Skipped region (intron in RNA-seq) | Yes | No |
| S     | Soft clip | No | Yes |
| H     | Hard clip | No | No |
| P     | Padding | Yes | No |

The `AlignmentResult` already has `AlignedReference`, `AlignedRead`, and `VisualString`.

Implementation:
1. Iterate through aligned ref and read side-by-side.
2. Current CIGAR op is determined by the visual string character:
   - `|` => match (either `M` or `=`)
   - `X` => mismatch (`X`)
   - ` ` => gap in ref or read (` D` or `I` respectively)
3. If any leading bases from read are not consumed in alignment, add `S` (soft clip) with
   count = `LeftSoftClip`.
4. If any trailing bases from read are not consumed in alignment, add `S` (soft clip) with
   count = `RightSoftClip`.
5. Run-length encode consecutive same operations (e.g., `"||||"` becomes `"4M"`).

**Acceptance criteria.**
- [ ] `CigarBuilder.FromAlignment(alignment)` returns a valid CIGAR string.
- [ ] Exact match alignment produces a CIGAR like `"10M"` (no clips, no gaps, no mismatches).
- [ ] Alignment with soft clips produces `"3S7M"`.
- [ ] Alignment with a 3-bp insertion produces `"10M3I"`.
- [ ] Alignment with a 2-bp deletion produces `"10M2D"`.
- [ ] Alignment with soft clips and gaps: `"3S6M1I3M2S"`.
- [ ] CIGAR round-trips: given an alignment, building and parsing the CIGAR reconstructs the original
     aligned ref/read strings.
- [ ] CIGAR builder adds `=` for matches and `X` for mismatches (not just `M`).
- [ ] Empty alignment (empty aligned strings) returns empty string.
- [ ] All tests in `CigarBuilderTests.cs`.

---

## t5. ReferenceIndex — Multi-Contig Support (P2)

**Objective.**
Create a `ReferenceIndex` class that holds multiple contigs (chromosomes/sequences), each
indexed with an FM-index for fast lookup.

**Design.**
```
ReferenceIndex {
    // Dictionary of contig name -> FMIndex
    Dictionary<string, FmIndex> Contigs { get; }

    // Load from a FASTA file (supports multi-contig)
    static ReferenceIndex LoadFromFasta(string path)

    // Load from already-loaded sequences
    static ReferenceIndex FromSequences(Sequence[] sequences)

    // Search pattern, returns (contigName, position) for each match
    Dictionary<string, FmAlignResult> Search(string pattern);

    // Search with max errors
    Dictionary<string, FmAlignResult> Search(string pattern, int maxErrors);

    // Get all contig names
    IList<string> ContigNames { get; }

    // Get total size of all contigs
    long TotalLength { get; }
}
```

**Acceptance criteria.**
- [ ] `ReferenceIndex.FromSequences([ref1, ref2])` correctly creates FM indices for each contig.
- [ ] `ReferenceIndex.LoadFromFasta(multiFastaPath)` loads all contigs from a multi-FASTA file.
- [ ] `index.Search("ACGT")` returns matches for every contig (keyed by contig name).
- [ ] Single-contig index works the same as `FmIndex` alone (backward-compatible path).
- [ ] `Contigs["chr22"]` returns the FM index for chr22 (or null if not found).
- [ ] Loading an empty FASTA produces a valid but empty `ReferenceIndex`.
- [ ] Loading a FASTA with one contig works.
- [ ] `TotalLength` is sum of all contig lengths.
- [ ] All tests in `ReferenceIndexTests.cs`.

---

## t6. Right Soft-Clip Fix (P2)

**Objective.**
Fix the right soft-clip calculation in `SmithWatermanAligner.Align()`. The current formula
(`refLen - refStart - alignedRefBasesConsumed`) may not accurately count unaligned trailing
reference bases.

**Current code (line 263):**
```csharp
var alignedRefBasesConsumed = pos - refGapCount;
var rightSoftClip = refLen - refStart - alignedRefBasesConsumed;
```

**Problem.**
`pos` is the number of alignment columns (after tracing backward).
`refGapCount` is number of gaps in reference (insertions in read).
So `alignedRefBasesConsumed` is correct: total reference bases in the alignment.

`refStart` is the reference position in the original sequence where alignment starts.
So `refLen - refStart - alignedRefBasesConsumed` = unaligned trailing reference bases.

**Verification.**
Trace: ref="ACGTACGTACGT" (len=12), refStart=0, alignment=10 bases consumed.
Right soft clip should be `12 - 0 - 10 = 2`. That matches.

However, the issue description suggests this formula may not work in all edge cases (e.g.,
when alignment spans the full reference, or when the alignment starts mid-reference).

**Acceptance criteria.**
- [ ] Full-match alignment: exact copy of a reference region produces right soft clip = 0.
- [ ] Alignment starting at position 5 of a 100-bp ref consumes 10 ref bases: right soft clip = 85.
- [ ] Alignment at end of reference (ends at last base): right soft clip = 0.
- [ ] Alignment that starts and ends at reference boundaries: right soft clip = 0, left soft clip = 0.
- [ ] Verify formula: `rightSoftClip = refLen - (refStart + alignedRefBasesConsumed)`.
- [ ] Verify formula: `leftSoftClip = refStart`.
- [ ] Edge case: refLen == 0 returns null.
- [ ] Edge case: readLen == 0 returns null.
- [ ] All edge case tests in `SmithWatermanAlignerTests.cs`.

---

## t7. Task List Ordering and Dependencies

Priority and dependency graph:

```
P0: [t1 BWT Encode tests]          -> no dependencies
P0: [t2 FM-Index backward search]   -> depends on t1 (Encode must work)
P2: [t8 BWT decode start index]     -> depends on t1 (Invert must work, tested alongside t1)
P1: [t4 CIGAR generation]           -> depends on t6 (alignment correctness)
P1: [t3 FmAligner]                  -> depends on t2 (FM-index search)
P2: [t5 ReferenceIndex]             -> depends on t2, t4 (FM-index + CIGAR for results)
P2: [t6 Right soft-clip fix]        -> no dependencies (standalone alignment fix)
```

Recommended implementation order:
1. t1 -- BWT Encode tests (quick, enables t2)
2. t8 -- BWT decode start index + tests (quick, enables t2 round-trip)
3. t2 -- FM-Index backward search + tests (core new feature)
4. t6 -- Right soft-clip fix (alignment correctness)
5. t3 -- FmAligner for approximate matching
6. t4 -- CIGAR generation
7. t5 -- ReferenceIndex for multi-contig

---

## Files to Create

| Task | Test file | Source file |
|------|-----------|-------------|
| t1 | (existing: `BurrowsWheelerTransformEncodeTests.cs` -- add tests) | (none) |
| t2 | `FmIndexTests.cs` | `FmIndex.cs` (already exists, refine/add to existing) |
| t3 | `FmAlignerTests.cs` | `FmAligner.cs` (new) |
| t4 | `CigarBuilderTests.cs` | `CigarBuilder.cs` (new) |
| t5 | `ReferenceIndexTests.cs` (exists but may need expansion) `ReferenceIndex.cs`(new) |
| t6 | `SmithWatermanAlignerTests.cs` (add regression tests to existing) | `SmithWaterman aligner.cs` (modify existing) |
| t7 | (tracking -- this file) | `bwt_tasks.md` (this file) |
| t8 | (add to `BurrowsWheelerTransformEncodeTests.cs`) | `BurrowsWheelerTransform.cs` (modify) |

## Acceptance Summary

| ID | Priority | Feature | Deliverable | Tests |
|----|----------|--------|------------|-------|
| t1 | P0 | BWT Encode public API | Verified + tested | Encode round-trip + edge cases |
| t2 | P0 | FM-Index backward search | `FmIndex` class | `FmIndexTests.cs` |
| t3 | P1 | BWT-based alignment | `FmAligner` class | `FmAlignerTests.cs` |
| t4 | P1 | CIGAR generation | `CigarBuilder` class | `CigarBuilderTests.cs` |
| t5 | P2 | Multi-contig ReferenceIndex | `ReferenceIndex` class | `ReferenceIndexTests.cs` |
| t6 | P2 | Right soft-clip fix | `SmithWatermanAligner.cs` updated | `SmithWatermanAlignerTests.cs` |
| t7 | - | Task tracking | `bwt_tasks.md` (this file) | - |
| t8 | P2 | BWT decode start index | `BurrowsWheelerTransform.cs` updated | `BurrowsWheelerTransformEncodeTests.cs` |
