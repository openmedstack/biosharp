# CIGAR Generation

## Introduction

The CIGAR (Concise Idiosyncratic Mapped Alignment Report) string is the SAM/BAM standard notation that compactly 
describes how a read aligns to the reference genome. After [Smith-Waterman](smith-waterman.md) alignment produces an 
`AlignmentResult`, the [CigarBuilder](cigar-generation.md) converts it into a CIGAR string — a compact code that downstream variant 
callers and genome browsers parse to understand the alignment's structure.

Instead of storing the full aligned sequences, a CIGAR string encodes the alignment as a sequence of operation codes: 
matched bases, insertions, deletions, and the soft-clipped ends (which indicate unaligned bases at the read 
extremities).

Each CIGAR operation is a number followed by a single character:
- `M` — match or mismatch (aligned block)
- `I` — insertion in the read (bases in read but missing from reference)
- `D` — deletion in the read (bases in reference but missing from read)
- `N` — skipped region (typically introns in RNA-seq data)
- `S` — soft clip (bases present in read but not aligned; still in the sequence field)
- `H` — hard clip (bases clipped from the read, not in the sequence field)
- `=` — sequence match (explicitly matching bases)
- `X` — sequence mismatch (explicitly mismatching bases)

The CigarBuilder class converts an AlignmentResult into a CIGAR string by scanning the aligned reference and read 
sequences in a single pass, counting consecutive matches, insertions, deletions, and clips.

CIGAR strings are essential for downstream tools — variant callers parse them to identify where mismatches and indels 
occur, and genome browsers render them to visualize alignments. They are a compact, standardized way to describe 
alignment structure that is understood by virtually all genomic analysis tools.

`CigarBuilder` converts alignment results into SAM/BAM-compliant CIGAR strings:

```csharp
var cigar = CigarBuilder.BuildCigar(alignment);
// Example: "10M5I3M1D7M"

// Or from raw aligned strings:
var cigar2 = CigarBuilder.BuildCigar(
    alignedRef: "ACGTACGTAAACGTACGT",
    alignedRead: "ACGTACGT----ACGTACGT",
    leftClip: 0,
    rightClip: 3
);
// "10M3D10M3S"
```
