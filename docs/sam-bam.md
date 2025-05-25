# SAM / BAM

## Introduction

After sequencing reads are aligned to the reference genome (using [Smith-Waterman](smith-waterman.md)), the results must be recorded 
in SAM/BAM format with their mapping positions, CIGAR strings, and quality metrics. BAM files are compressed, indexed 
versions of SAM that allow random-access queries — you can request alignments from a specific chromosomal region 
without scanning the entire file. This is essential for genome browsers and targeted variant calling where you only 
need reads from a particular gene or locus.

The BAM index file (`.bai`) uses a hierarchical binning strategy to map genomic regions to compressed blocks in the 
file, enabling efficient random access. BioSharp's [BamReader](sam-bam.md) reads these files while 
[BamWriter](sam-bam.md) produces them, and [BamIndexCalculator](sam-bam.md) builds the index structure.

SAM tags are key-value metadata pairs attached to each aligned read. They include edit distance (NM), mismatching 
positions (MD), read group identity (RG), cell barcode for single-cell RNA-seq (CB), and unique molecular 
identifiers (UB). These tags provide additional context downstream tools use for quality assessment and variant 
filtering.

```csharp
using OpenMedStack.BioSharp.Io.Bam;

var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<BamReader>.Instance;
var reader = new BamReader("data/mapt.NA12156.altex.bam", logger);
var samDefinition = await reader.Read();

foreach (var section in samDefinition.AlignmentSections)
{
    if ((section.Flag & AlignmentSection.AlignmentFlag.SegmentUnmapped) == 0) // mapped
    {
        Console.WriteLine($"{section.QName}: pos={section.Position}, CIGAR={section.Cigar}, flag={section.Flag}");
    }
}
```

BAM index (`.bai`) files are supported for region-based queries via `BamIndexReader`:

```csharp
var indexReader = new BamIndexReader("data/mapt.NA12156.altex.bam.bai");
// Use for querying alignments in a specific genomic region
```

BAM writing is also supported via `BamWriter`, which accepts a `SamDefinition` to produce sorted/coordinate BAM output.

For BAM index construction from alignment coordinates:

```csharp
// Calculate hierarchical bins for a genomic region [beg, end)
var bins = BamIndexCalculator.Reg2Bins(beg: 10000, end: 15000);
// Or single-bin calculation
var bin = BamIndexCalculator.Reg2Bin(beg: 10000, end: 15000);
```

**SAM Format Fields**: SAM/BAM records carry comprehensive metadata through `AlignmentTag` key-value pairs:

| Tag | Type | Description |
|---|---|---|
| `NM` | `i` | Edit distance to reference |
| `MD` | `Z` | Mismatching positions post-alignment |
| `RG` | `Z` | Read group ID |
| `RG` | `Z` | Read group metadata (SM, PG, LB, etc.) |
| `CB` | `Z` | Cell barcode (single-cell RNA-seq) |
| `UB` | `Z` | Unique barcode / UMI |

**Read Group Metadata** is defined via `ReadGroup` and `FileMetadata` objects, and written through `SamWriter`:

```csharp
using OpenMedStack.BioSharp.Io.Sam;

var readGroup = new ReadGroup("RG001")
{
    Sample = "NA12156",
    Platform = "ILLUMINA",
    Library = "WGS_001",
    Program = "BioSharpAligner"
};

var fileMetadata = new FileMetadata(readGroup);
var samWriter = new SamWriter("output.sam", fileMetadata);
// samWriter.WriteSamDefinition(samDefinition);
```

For CIGAR operation encoding, the `CigarOp` enum maps to SAM-spec CIGAR characters: `M` (match), `I` (insertion), 
`D` (deletion), `N` (skip), `S` (soft clip), `H` (hard clip), `P` (pad), `=` (seq match), `X` (seq mismatch).

**BGZF Compression**: BAM, VCF, and FASTQ outputs use BGZF (Blocked GZIP) compression for random-access I/O. The 
`BgzfStream` class handles chunked gzip blocks suitable for genomic data.
