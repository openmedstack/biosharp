# Burrows-Wheeler Transform, Reference Indexing, and Alignment

## Overview

This document describes how to use BioSharp for three related steps in alignment-based genomic analysis:

1. **Burrows-Wheera Transform (BWT)** -- compress a string and reconstruct it.
2. **Reference indexing** -- build a searchable index from a reference genome.
3. **Alignment** -- map a read to the reference using Smith-Waterman.

In a typical pipeline you load a reference genome from FASTA, align each read against it with
Smith-Waterman, and then call variants from the alignments. The BWT is included as a utility for
situations where you need to compress or rearrange sequences, but the current implementation does not
provide an FM-index or BWT-based search. For alignment against a large reference genome you must use
the Smith-Waterman aligner described in section 3 below.

---

## 1. Burrows-Wheera Transform

### What is the BWT?

The Burrows-Wheera Transform rearranges a string into runs of similar characters, making it highly
compressible while remaining perfectly reversible. In genomics the BWT is most often paired with an
FM-index (see Missing Features at the bottom of this page) to enable fast substring search without
scanning the entire reference.

### How BioSharp Implements It

BioSharp's `BurrowsWheeraTransform` class works as follows:

- **Encode** (via `ComputeSuffixArray` + `FindLastChar`):
  - Generate all suffixes of the input string.
  - Sort them lexicographically to produce a suffix array.
  - The last character of each sorted rotation becomes the BWT output.
- **Decode** (via `Invert`):
  - Sort the BWT string to build the corresponding F0 (sorted) column.
  - Build an `lShift` mapping: for each character, map its position in BWT to the corresponding
    position in the sorted column.
  - Walk the mapping from a fixed starting index (`x = 4`, hardcoded) — reconstructs the original
    string in reverse.

> **Note:** The decode method expects the original string to appear at index 4 in the sorted
rotations table. For general use you may want the start index to be configurable or inferred
automatically.

### Example Usage

The class is currently accessed through its static members. Here is a minimal example:

```csharp
using OpenMedStack.BioSharp.Calculations.BurrowsWheera;

// Encode
string input = "ACGTACGT$";
string bwt = /* encoded result - see code for how to call */;

// Decode
string decoded = BurrowsWheeraTransform.Invert(bwt);
Console.WriteLine(decoded);  // outputs: ACGTACGT$
```

To obtain the encoded BWT output you must call the internal pipeline (see Missing Features below for
the planned public API).

---

## 2. Indexing a Reference Genome

### Loading the Reference

To align reads against a reference genome you first load it from a FASTA file:

```csharp
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Model;

var faReader = new FastAReader();

// Load the reference genome (e.g. chromosome 22)
Sequence? reference = null;
await foreach (var seq in faReader.Read("data/Homo_sapiens.GRCh38.dna.chromosome.22.fa"))
{
    reference = seq;  // load the desired chromosome/contig
    break;
}

if (reference == null)
    throw new InvalidOperationException("No reference sequences found in FASTA.");

Console.WriteLine($"Loaded reference: {reference.Id} ({reference.Length} bp)");
```

The `FastAReader` supports:
- Plain `.fa` / `.fasta` files.
- Gzip-compressed `.fa.gz` / `.fasta.gz` files (call `ReadGz` instead of `Read`).
- Multi-sequence FASTA -- each sequence header line (`>`) produces one `Sequence` object.

Each `Sequence` wraps the nucleotide string, optional per-base quality scores, and a header.

### Building the BWT as an Index

```csharp
using OpenMedStack.BioSharp.Calculations.BurrowsWheera;

// Build the BWT of the reference (up to a practical max length for now)
string refStr = new string(reference.Sequence.Span);
string bwtEncoded = BurrowsWheeraTransform.Encode(refStr);

Console.WriteLine($"BWT size: {bwtEncoded.Length} chars");
```

The BWT is then the primary tool for FM-index construction and substring search.
(See the missing features section for the planned FM-index API.)

### Reference Metadata in SAM / BAM

When writing alignments, BioSharp includes a `ReferenceSequence` record per contig:

```csharp
using OpenMedStack.BioSharp.Io.Sam;

var refSeq = new ReferenceSequence(
    name: "chr22",
    length: (uint)reference.Length,
    genomeAssembly: "GRCh38",
    species: "Homo sapiens"
);
```

These objects are consumed by `SamWriter` and `BamWriter` when writing @SQ header lines.

---

## 3. Aligning Reads with Smith-Waterman

### Overview

BioSharp provides a `SmithWatermanAligner` that performs semi-global ("glocal") alignment:
every base of the read must be placed (global on the read), but the alignment can start and end
anywhere on the reference (local on the reference -- no penalty for unaligned reference bases at
the start or end).

It uses **affine gap penalties**:
| Parameter | Default | Meaning |
|---|---|---|
| `matchScore` | `2` | Score for a matching base pair |
| `mismatchPenalty` | `-3` | Score for a mismatch |
| `gapOpenPenalty` | `-5` | Penalty for opening a new gap |
| `gapExtendPenalty` | `-2` | Penalty for extending an existing gap |
| `minScore` | `0` | Minimum alignment score to return a result |

The algorithm maintains three dynamic programming matrices (H, X, Y) to implement affine gap
costs efficiently.

### Aligning a Single Read

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

// Create a read sequence
var read = new Sequence(
    "read1",
    "ACGTACGTAAACGTACGTACGT".AsMemory(),
    "IIIIIIIIIIIIIIIIIIIIIIIII".AsMemory()
);

// Align read against the reference
var alignment = SmithWatermanAligner.Align(
    reference,
    read,
    matchScore: 2,
    mismatchPenalty: -3,
    gapOpenPenalty: -5,
    gapExtendPenalty: -2,
    minScore: 10
);

if (alignment != null)
{
    Console.WriteLine($"Score:     {alignment.Score}");
    Console.WriteLine($"Ref start: {alignment.ReferenceStartPosition} (0-based)");
    Console.WriteLine($"Left clip: {alignment.LeftSoftClip}");
    Console.WriteLine($"Right clip:{alignment.RightSoftClip}");
    Console.WriteLine($"Aligned ref: {alignment.AlignedReference}");
    Console.WriteLine($"Aligned read: {alignment.AlignedRead}");
    Console.WriteLine($"Visual:      {alignment.VisualString}");
    //
    // Visual output: | = match, X = mismatch, space = gap in either sequence
    //
}
else
{
    Console.WriteLine("No alignment met the minimum score threshold.");
}
```

### Visual Alignment Output

| Symbol | Meaning |
|---|---|
| `|` | Match (ref base = read base, case-insensitive) |
| `X` | Mismatch (ref base != read base) |
| ` ` (space) | Gap in either reference or read |

### Example Alignment

Given reference: `ACGTACGTACGTACGTACGTACGT` and read: `ACGTACGTAAACGTACGTACGT`

```
Ref:     ACGTACGTAAACGTACGTACGT
Read:    ACGTACGT-AAACGTACGTACGT
Visual:  |||||||| |||||||||||||||
```

### Aligning All Reads from a FASTQ File

```csharp
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Calculations.Alignment;

var fqReader = new FastQReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
var alignments = new List<AlignmentResult>();

await foreach (var read in fqReader.Read("data/reads.fastq"))
{
    var result = SmithWatermanAligner.Align(reference, read);
    if (result != null)
    {
        alignments.Add(result);
    }
}

Console.WriteLine($"Aligned {alignments.Count} of {fqReader.TotalReads} reads.");
```

---

## 4. From Alignments to Variant Calls (Bonus Workflow)

After aligning reads, you can extract variant calls using the `VariantCaller`:

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;

var allVariantCalls = new List<LocalVariantResult>();

await foreach (var read in fqReader.Read("data/reads.fastq"))
{
    var alignment = SmithWatermanAligner.Align(reference, read);
    if (alignment != null)
    {
        var variants = VariantCaller.CallVariants(reference, alignment, minQuality: 30);
        allVariantCalls.AddRange(variants);
    }
}

// Merge variants from all reads covering the same locus
var merged = VariantCaller.MergeVariants(allVariantCalls.ToArray());
foreach (var v in merged)
{
    Console.WriteLine($"{v.Chromosome}:{v.Position} {v.Reference}>{v.Alternate}  Q={v.QuantitativeQuality}  DP={v.Depth}");
}
```

---

## 5. Putting It All Together

Here is a complete end-to-end example that loads a reference, aligns each read, and reports results:

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Calculations.BurrowsWheera;
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Io.Sam;
using OpenMedStack.BioSharp.Model;

var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

// --- Step 1: Load reference from FASTA ---
var faReader = new FastAReader();
Sequence? reference = null;
await foreach (var seq in faReader.Read("data/Homo_sapiens.GRCh38.dna.chromosome.22.fa"))
{
    reference = seq;
    break;
}

// --- Step 2: (Optional) Build BWT of the reference ---
if (reference != null)
{
    string refStr = new string(reference.GetData().Span);
    string bwt = BurrowsWheeraTransform.Encode(refStr);

    // Decode to verify reversibility
    string decoded = BurrowsWheeraTransform.Invert(bwt);
    Console.WriteLine($"BWT round-trip verified: {refStr.Length} == {decoded.Length}");
}

// --- Step 3: Align each FASTQ read ---
var fqReader = new FastQReader(logger);
var alignedCount = 0;
await foreach (var read in fqReader.Read("data/reads.fastq"))
{
    var alignment = SmithWatermanAligner.Align(reference, read);
    if (alignment != null)
    {
        alignedCount++;
        // Use alignment info (position, score, CIGAR) as needed
        Console.WriteLine($"{read.Id}: {alignment.ReferenceStartPosition}..{alignment.ReferenceStartPosition + alignment.AlignedReference.Length} score={alignment.Score}");
    }
}
Console.WriteLine($"Aligned {alignedCount} reads.");
```

---

## Missing Features and Gaps

The following functionality is **not yet implemented** in BioSharp. These are planned improvements:

| Missing Feature | Description | Suggested Specification |
|---|---|---|
| **BWT Encode (public API)** | `BurrowsWheeraTransform.Encode()` is not exposed as a public static method; only `Invert(string)` is public. | Add `public static string Encode(string input)` that computes the suffix array and returns the BWT string. |
| **FM-Index** | The BWT exists but no FM-index is built on top of it. An FM-index adds a cumulative count (C) array and occurrence table (O) to enable backward search -- the core of substring indexing. | Create `FmIndex` class: `FmIndex.Build(fromSequence)` loads BWT + computes rank/select tables. `FmIndex.Search(pattern)` returns all positions where `pattern` occurs in the reference. Should handle DNA alphabet (A,C,G,T,N) only. |
| **BWT-based Alignment** | Alignment currently uses Smith-Waterman only. There is no BWT/FM-index backed alignment for fast exact or approximate matching of reads. | Create `FmAligner` class: `FmAligner.Search(sequence, fmIndex, maxErrors)` performs seed-and-extend or banded alignment using FM-index backward search for the seed phase. |
| **Contig / Multi-ref support** | Each alignment uses a single `Sequence` as reference. No multi-contig reference index that maps contig names to `Sequence` objects. | Create `ReferenceIndex` class wrapping a `Dictionary<string, Sequence>` (contig name to sequence). Provide methods to load from a multi-contig FASTA file. `SmithWatermanAligner` should accept a `ReferenceIndex` and auto-select the appropriate contig. |
| **Semi-global end score detection** | The aligner currently finds the best cell in the last row of the DP matrix. The right soft-clip calculation formula (`refLen - refStart - pos + count('-')`) may not accurately count unaligned trailing ref bases -- it should simply be `refLen - (refStart + alignedRefLength)`. | Fix the right soft clip formula, or provide a clean property `UnalignedRefBasesAtEnd` on `AlignmentResult`. |
| **CIGAR generation from alignment** | No `CigarBuilder` implementation exists, even though its project is in the solution. | Implement `CigarBuilder.FromAlignment(AlignmentResult)` converting the aligned ref/read strings to CIGAR operations (M, I, D, S, X, `=`). |
| **BWT decode start index** | `Invert()` hardcodes `x = 4` (the index of the original string in the sorted rotations). This assumes a known sentinel character and fixed input size. | Make the start index a parameter: `Invert(string bwt, int originalIndex)` or detect it by finding the row where the BWT equals the sorted BWT (row 0 = sentinel position). |

### Priority Recommendations

Priority Order | Feature | Rationale
---|---|---
P0 | **BWT Encode (public API)** | Minimal code, completes the existing BWT class, enables downstream FM-index work.
P0 | **FM-Index** | Essential for large-genome alignment; transforms BWT from a curiosity into a practical tool.
P1 | **BWT-based Alignment** | Provides fast exact matching as a complement to Smith-Waterman's slower but more flexible alignment.
P1 | **CIGAR generation** | Required for SAM/BAM output compatibility.
P2 | **Multi-contig ReferenceIndex** | Needed for real genomes but not for single-contig use cases.
P2 | **Right soft-clip fix** | Bug fix for correctness.
