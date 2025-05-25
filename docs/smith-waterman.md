# Smith-Waterman Alignment

## Introduction

Sequence alignment is the foundational step in alignment-based variant calling: it maps short sequencing reads back to 
a reference genome to determine where each read originated. The Smith-Waterman algorithm performs semi-global (glocal) 
alignment, meaning it aligns every base of the read to the reference but allows the alignment to start and end at any 
position on the reference — no penalty for unaligned reference bases at the start or end.

This semi-global approach is essential for variant calling because reads come from fragmentary sequencing and can 
originate from anywhere within a target region. Reads often have soft-clipped ends (bases at the read extremities 
that don't align well to the reference), and global alignment would incorrectly penalize these unaligned ends.

The algorithm uses a dynamic programming matrix with affine gap penalties: opening a gap costs more than extending it, 
which biologically reflects the observation that a single indel event is far more likely than multiple independent gap 
events within a single read.

The output is an AlignmentResult containing the aligned reference and read sequences, a visual alignment guide (showing 
matches, mismatches, and gaps), the alignment score, the start position on the reference, and soft-clip lengths at both 
ends.

This alignment step is the foundation of the alignment-based variant calling pipeline — without accurate 
read-to-reference mapping, variant calling cannot proceed.

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

// Reference and read sequences
var reference = new Sequence("ref", "ACGTACGTACGTACGTACGTACGT".AsMemory(), "IIIIIIIIIIIIIIIIIIIIIIII".AsMemory());
var read       = new Sequence("read", "ACGTACGTAAACGTACGTACGT".AsMemory(), "IIIIIIIIIIIIIIIIIIIIIII".AsMemory());

var alignment = SmithWatermanAligner.Align(
    reference,
    read,
    matchScore: 2,
    mismatchPenalty: -3,
    gapOpenPenalty: -5,
    gapExtendPenalty: -2,
    minScore: 10
);

Console.WriteLine($"Score: {alignment!.Score}");
Console.WriteLine($"Ref:    {alignment.AlignedReference}");
Console.WriteLine($"Read:   {alignment.AlignedRead}");
Console.WriteLine($"Visual:{alignment.VisualString}");
Console.WriteLine($"Start: {alignment.ReferenceStartPosition}");
Console.WriteLine($"Left clip:   {alignment.LeftSoftClip}");
Console.WriteLine($"Right clip: {alignment.RightSoftClip}");
```

Visual alignment output looks like:

```
Ref:   ACGTACGTAAACGTACGTACGT
Read:  ACGTACGT-AAACGTACGTACGT
Visual: |||||||| |||||||||||||||
```

`|` means match, `X` means mismatch, ` ` means gap.

#### Return values

`AlignmentResult` (a C# record):

| Field | Description |
|---|---|
| `AlignedReference` | Reference sequence with `-` for insertions in the read |
| `AlignedRead` | Read sequence with `-` for deletions in the read |
| `VisualString` | Visual guide: `|`, `X`, ` ` |
| `Score` | Total alignment score |
| `ReferenceStartPosition` | 0-based start position on the reference |
| `LeftSoftClip` | Unaligned bases at 5' end of the read |
| `RightSoftClip` | Unaligned bases at 3' end of the read |
