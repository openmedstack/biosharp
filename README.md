# OpenMedStack BioSharp

A C# (.NET 10) library for bioinformatics calculations covering sequence alignment, variant calling, de novo assembly via De Bruijn graphs, repeat masking, and variant annotation with HGVS notation.

Target framework: **.NET 10.0** | Version: **1.1.0**

```
dotnet build openmedstack-biosharp.sln
```

---

## Table of Contents

1. [Project Structure](#project-structure)
2. [Getting Started](#getting-started)
3. [I/O Ecosystem](#io-ecosystem)
    - [FASTA / FASTQ](#fasta--fastq)
    - [SAM / BAM](#sam--bam)
    - [VCF](#vcf)
    - [Illumina BCL](#illumina-bcl)
        - [Preator CLI (BCL-to-FASTQ)](#preator-cli-bcl-to-fastq)
        - [Programmatic Preator Usage](#programmatic-preator-usage)
4. [Sequence Alignment](#sequence-alignment)
    - [Smith-Waterman (Semi-Global / Glocal)](#smith-waterman)
    - [Needleman-Wunsch (Global)](#needleman-wunsch)
    - [CIGAR Generation](#cigar-generation)
5. [Alignment-Based Variant Calling Pipeline](#alignment-based-variant-calling-pipeline)
6. [De Bruijn Graph Pipeline](#de-bruijn-graph-pipeline)
    - [De Bruijn Graph Assembly](#de-bruijn-graph-assembly)
    - [Bloom Filter for k-mer Membership](#bloom-filter)
    - [Bubble and Tip Detection](#bubble-and-tip-detection)
    - [Structural Variant Detection](#structural-variant-detection)
7. [Repeat Masking](#repeat-masking)
8. [Somatic and Cohort Variant Calling](#somatic-and-cohort-variant-calling)
9. [Genotype Calling](#genotype-calling)
10. [Variant Annotation Engine](#variant-annotation-engine)
11. [End-to-End Analysis Pipeline](#end-to-end-analysis-pipeline)
12. [Reference Data](#reference-data)
13. [API Reference Summary](#api-reference-summary)

---

## Project Structure

```
src/
  openmedstack.biosharp.model/          # Core domain models
    Sequence, BasePair, AminoAcid
    VariantConsequence, VariantAnnotation
    AnnotationContext, CodonChange
    HgvsDescription, SubstitutionDescription, DeletionDescription,
      InsertionDescription, DuplicationDescription, DeletionInsertionDescription
    ReferenceSequenceKind
    VcfVariant, VariantCallFile, VcfMetaReader
  openmedstack.biosharp.io/             # File I/O
    FastA/FastQ readers and writers
    SAM/BAM readers and writers (with index support)
    VCF readers and writers (BGZF-compression aware)
    Illumina BCL readers, barcode/quality parsers
  openmedstack.biosharp.calculations/   # Algorithms
    Alignment/                          # Smith-Waterman, Needleman-Wunsch,
                                       # CIGAR builder, VariantCaller
    DeBruijn/                           # De Bruijn graph, Bloom filter,
                                       # BubbleFinder, TipFinder,
                                       # StructuralVariantDetector,
                                       # RepeatMasker, GenotypeCaller,
                                       # SomaticVariantDetector,
                                       # CohortVariantCaller
    VariantAnnotator, VariantAnnotationEngine
tests/                                  # xUnit test suite
data/                                   # Reference data
  Homo_sapiens.GRCh38.dna.chromosome.22.fa.gz
  ERR164409.fastq.gz
  mapt.NA12156.altex.bam
  repeats.json
  illumina_run/                         # BCL sample data
```

---

## Getting Started

Add a project reference in your `.csproj`:

```xml
<ProjectReference Include="src/openmedstack.biosharp.model/openmedstack.biosharp.model.csproj" />
<ProjectReference Include="src/openmedstack.biosharp.io/openmedstack.biosharp.io.csproj" />
<ProjectReference Include="src/openmedstack.biosharp.calculations/openmedstack.biosharp.calculations.csproj" />
```

Or reference the solution directly:

```bash
dotnet build openmedstack-biosharp.sln
```

All namespaces are under `OpenMedStack.BioSharp`. The three top-level namespaces are:

- `OpenMedStack.BioSharp.Model` -- domain models
- `OpenMedStack.BioSharp.Io` -- file I/O
- `OpenMedStack.BioSharp.Calculations` -- algorithms (alignment, De Bruijn graphs, variant calling, annotation)

---

## I/O Ecosystem

BioSharp provides streaming readers and writers for FASTA, FASTQ, SAM/BAM, VCF, and Illumina BCL raw data files. All I/O is streaming-based via `IAsyncEnumerable<T>` for memory-efficient processing of large genomic datasets.

### FASTA / FASTQ

FASTA and FASTQ files are read into `Sequence` objects via `FastAReader` and `FastQReader`. Reading returns an `IAsyncEnumerable<Sequence>` -- each `Sequence` carries an ID, nucleotide data, quality scores, and a header.

```csharp
using OpenMedStack.BioSharp.Io.FastA;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;

// Read FASTA
var faReader = new FastAReader();
await foreach (var seq in faReader.Read("Homo_sapiens.GRCh38.dna.chromosome.22.fa"))
{
    Console.WriteLine($"{seq.Id}: {seq.Length} bp");
}

// Read FASTQ
var fqReader = new FastQReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
await foreach (var read in fqReader.Read("ERR164409.fastq"))
{
    var bases = read.GetData().Span.ToString();
    var quals = read.GetQuality().Span.ToString();
    Console.WriteLine($"{read.Id}: {bases.Length}bp");
}
```

`Sequence` supports slicing:

```csharp
var subseq = sequence.Slice(100, 500); // 500 bases from position 100 (0-based)
```

Writers are also available for creating FASTA/FASTQ files programmatically.

### SAM / BAM

BAM files (binary SAM) are read via `BamReader`, which returns a `SamDefinition` containing `AlignmentSection[]` records. Each section holds the read name (`QName`), sequence, quality string, CIGAR string, mapping quality, flag bits, reference start position, and optional program/read group metadata.

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

For CIGAR operation encoding, the `CigarOp` enum maps to SAM-spec CIGAR characters: `M` (match), `I` (insertion), `D` (deletion), `N` (skip), `S` (soft clip), `H` (hard clip), `P` (pad), `=` (seq match), `X` (seq mismatch).

**BGZF Compression**: BAM, VCF, and FASTQ outputs use BGZF (Blocked GZIP) compression for random-access I/O. The `BgzfStream` class handles chunked gzip blocks suitable for genomic data.

### VCF

VCF 4.2 files are parsed by `VcfFileReader` with a `VcfMetaReader` for header metadata. The reader returns an `IAsyncEnumerable<VcfVariant>` for streaming access to variant records. Each `VcfVariant` has `Chromosome`, `Position`, `Reference`, `Alternate`, `QualitativeQuality`, `ErrorProbabilities` (array of per-allele Phred-scaled scores), `Filter`, and `Info` fields.

```csharp
using OpenMedStack.BioSharp.Io.Vcf;

var metaReader = new VcfMetaReader();
var vcfReader = new VcfFileReader(metaReader);
var reader = await vcfReader.Read("data/D.vcf");

await foreach (var variant in reader)
{
    Console.WriteLine($"{variant.Chromosome}:{variant.Position} {variant.Reference}>{variant.Alternate}");
}
```

Writing VCF output is handled by `VcfWriter`, which produces VCF 4.2-compliant files with auto-generated INFO headers (DP, SVTYPE, END, CIPOS, CIEND, etc.), FILTER definitions (PASS, LOW_QUAL, etc.), and optional per-sample FORMAT fields (GT:GQ:DP). For multi-sample VCFs, sample names and genotype arrays are passed to the writer.

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;

// Write a single-sample VCF
await VcfWriter.WriteAsync(
    stream: new FileStream("output.vcf", FileMode.Create),
    variants: mergedVariants,
    chromosome: "chr22",
    chromLength: 50818468
);

// Write with per-sample genotypes
await VcfWriter.WriteAsync(
    stream: fs,
    variants: mergedVariants,
    chromosome: "chr17",
    chromLength: 83257441,
    sampleNames: new[] { "sample1", "sample2" },
    genotypes: new string?[][] {
        new string?[] { "0/1", "42", "85" },   // sample1: GT:GQ:DP
        new string?[] { "0/0", "55", "90" },   // sample2: GT:GQ:DP
     }
);
```

**VCF Metadata**: The `VcfMetaReader` parses VCF headers, and `ContigFieldFormat`, `InformationFieldFormat`, `FilterFieldFormat`, `ContigFieldFormat`, `IndividualFormatFieldFormat`, and `AlternativeAlleleFieldFormat` define the schema for each VCF section. The `IVariantMetaInformation` interface and `KeyValueMetaInformation` class support custom VCF header extensions.

**VCF Record Building**: `VcfRecordBuilder` constructs individual VCF lines from `LocalVariantResult` objects. It supports the `BuildWithGenotype()` variants that embed GT:GQ:DP FORMAT fields directly, as well as structural variant fields (SVTYPE, END, CIPOS, CIEND, HOMLEN, HOMSEQ, ALT_PATHS).

**VariantCallFile**: The `VariantCallFile` class provides structured storage for variant calls with associated metadata, and `VariantsFieldFormat` / `AlternativeAlleleKind` and `HgvsVariant` support HGVS-integrated variant tracking.

### Illumina BCL
BioSharp reads raw Illumina BCL data (`*.bcl.bgzf` -- BGZF-compressed BCL files) into `Run` objects. This includes demultiplexing by barcode, flowcell layout parsing, and per-tile, per-lane read decomposition. The resulting `Reads` objects contain per-read base data and quality scores.

```csharp
using OpenMedStack.BioSharp.Io.Bcl;

var sampleReader = new SampleReader("data/illumina_run");
var run = sampleReader.Read();

foreach (var tile in run.TileSets.Tiles)
{
    foreach (var read in tile.Reads)
     {
        var bases = read.Data.Data.ToString();
        var qualities = read.Data.BaseQualities.ToString();
     }
}
```

#### Preator CLI (BCL-to-FASTQ)

The **Preator** CLI tool (`openmedstack.preator`) wraps the BCL reading pipeline into a command-line tool for converting raw Illumina BCL data into FASTQ files with quality trimming and index generation.

```bash
preator --input /path/to/bcl/run --output ./fastq_out --lanes * --readstructure R1
```

| Option | Description |
|---|---|
| `--input, -i` *(required)* | Path to BCL run directory (must contain RunInfo.xml, Config, InterOp) |
| `--output, -o` *(optional)* | Output FASTQ folder (defaults to `Unaligned/<run_id>`) |
| `--lanes, -l` *(optional)* | Comma-separated lanes (e.g. `1,2`) or `*` for all lanes |
| `--readstructure, -r` *(optional)* | Read structure string (e.g. "R1", "R1:76,R2:76"). Inferred from RunInfo if omitted |

Preator processes lanes in parallel (`AsParallel`/`SelectManyParallel`), producing:
- Gzip-compressed FASTQ files: `<instrument>_<number>_L<Lane>.fastq.gz`
- Indexed FASTQ files: `<instrument>_<number>_L<Lane>.fastqi.gz`

Quality trimming is applied via `DefaultQualityTrimmer` to each read before writing.

#### Programmatic Preator Usage

```csharp
using OpenMedStack.BioSharp.Io.Bcl;
using OpenMedStack.BioSharp.Io.FastQ;
using Microsoft.Extensions.Logging.Abstractions;

var inputDir = new DirectoryInfo("/path/to/bcl/run");
var reader = new IlluminaDataReader(inputDir, NullLoggerFactory.Instance);
var runInfo = reader.RunInfo();

// Process all lanes in parallel
var lanes = reader.GetAllLanes(); // returns int[]
foreach (var lane in lanes)
{
    var output = new FastQWriter(
        logger,
        new FileStream(outputPath, FileMode.Create),
        new GZipStream(indexStream, CompressionLevel.Fastest),
        CompressionLevel.Fastest,
        trimmer: DefaultQualityTrimmer.Instance,
        writeIndex: true
    );
    
    await foreach (var clusterData in reader.ReadClusterData(lane, ct))
    {
        var readBclData = clusterData.ReadBclData(DefaultQualityTrimmer.Instance);
        await output.Write(readBclData, ct);
    }
}
```

## Sequence Alignment

### Smith-Waterman

The `SmithWatermanAligner` performs **semi-global (glocal) alignment**: reads are aligned globally (every read base must be placed), while reference start/end positions are free (leading/trailing reference bases may be skipped without penalty). This is ideal for variant calling because reads can come from anywhere along a reference region.

The algorithm uses **affine gap penalties**: gap opening costs `gapOpenPenalty` and each subsequent gap base costs `gapExtendPenalty`. Default parameters are tuned for DNA: match +2, mismatch -3, gap open -5, gap extend -2.

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

// Reference and read sequences
var reference = new Sequence("ref", "ACGTACGTACGTACGTACGTACGT".AsMemory(), "IIIIIIIIIIIIIIIIIIIIIIII".AsMemory());
var read      = new Sequence("read", "ACGTACGTAAACGTACGTACGT".AsMemory(), "IIIIIIIIIIIIIIIIIIIIIII".AsMemory());

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
Console.WriteLine($"Ref:   {alignment.AlignedReference}");
Console.WriteLine($"Read:  {alignment.AlignedRead}");
Console.WriteLine($"Visual:{alignment.VisualString}");
Console.WriteLine($"Start: {alignment.ReferenceStartPosition}");
Console.WriteLine($"Left clip:  {alignment.LeftSoftClip}");
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

### Needleman-Wunsch

The `NeedlemanWunschAlgo` performs **global alignment** using the BLOSUM62 scoring matrix for protein sequences. This is useful for comparing full-length sequences where every position should be aligned.

```csharp
using OpenMedStack.BioSharp.Calculations.NeedlemanWunsch;

// Load BLOSUM62 (built-in) and align protein sequences
// The Align method returns an AlignmentResult compatible with the same interface
```

### CIGAR Generation

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

---

## Alignment-Based Variant Calling Pipeline

The alignment-based pipeline converts raw read alignments into variant calls. The core flow is:

**Alignment --> Event Parsing --> Variant Grouping --> Quality Scoring --> Output**

### Smith-Waterman Alignment per Read

Each read is aligned independently against the reference.

```csharp
var alignment = SmithWatermanAligner.Align(reference, read);
```

### Variant Calling

`VariantCaller.CallVariants()` parses the alignment into atomic events (SNPs, insertions, deletions), groups consecutive events of the same type, builds VCF-compliant alleles using the leftmost-anchor convention, and assigns Phred-scaled quality scores.

```csharp
var variants = VariantCaller.CallVariants(reference, alignment, minQuality: 30);

foreach (var v in variants)
{
    Console.WriteLine($"{v.Chromosome}:{v.Position} {v.Reference}>{v.Alternate}  Q={v.QuantitativeQuality}  DP={v.Depth}");
    Console.WriteLine($"  IsSubstitution={v.IsSubstitution}  IsInsertion={v.IsInsertion}  IsDeletion={v.IsDeletion}");
}
```

**Quality scoring** uses the alignment score divided by event count, with penalties for indels (-5) and homopolymer indels (-10). Scores are clamped to [10, 255].

**Homopolymer detection**: `VariantCaller.GetHomopolymerRun(refSeq, position)` returns the length of the run of identical bases at a given position. Homopolymer indels are penalized because they are common sequencing errors in platforms like Ion Torrent and Oxford Nanopore.

### Merging Variants from Multiple Reads

When multiple reads cover the same locus, `VariantCaller.MergeVariants()` consolidates them into a single variant per unique (chromosome, position, REF, ALT) key, taking the maximum quality and summing depth.

```csharp
var allVariants = new List<LocalVariantResult>();
// ... process reads, collect variants ...

var merged = VariantCaller.MergeVariants(allVariants.ToArray());
Console.WriteLine($"From {allVariants.Count} raw calls -> {merged.Length} merged variants");
```

---

## De Bruijn Graph Pipeline

The De Bruijn graph approach builds an overlapping-kmer graph from reads and traverses it to find divergent paths (bubbles = SNPs/small indels) and dead-end tips (insertions). This is the standard approach in short-read *de novo* assembly and structural variant detection.

### De Bruijn Graph Assembly

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Create reads: (name, sequence, quality)
var refs = AsyncEnumerableExtensions.ToAsyncEnumerable(
    new[]
    {
        new Sequence("r1", "ACGTACGTAGCTACGTACGTACGT".AsMemory(), "III".AsMemory()),
        new Sequence("r2", "ACGTACGTAGCTAGGTACGTACGT".AsMemory(), "III".AsMemory()),
        new Sequence("r3", "ACGTACGTAGCT".AsMemory(), "III".AsMemory()),
        new Sequence("r4", "TACGTACGTAGCTACGTACGT".AsMemory(), "III".AsMemory()),
    }
);

// Build a k=10 De Bruijn graph from reads
var graph = new DeBruijnGraph(k: 10, refs);

// Assemble -- walks the graph from start nodes following highest-coverage edges
await foreach (var seq in graph.Assemble())
{
    Console.WriteLine($"Assembled: {seq}");
}

// Or inspect the graph structure directly
var nodes = await graph.GetGraph();
foreach (var node in nodes.Values)
{
    Console.WriteLine($"{node.InDegree}->{node.Id}->{node.OutDegree} [{string.Join(", ", node.OutboundEdges)}]");
}
```

**Graph construction**: Each k-mer in each read creates a node. An edge from node A to node B exists when a k-mer's first (k-1) bases match node A's last (k-1) bases and node B. Edge weights equal coverage (how many times that transition appears in the data).

**Low-coverage filtering**: `graph.FilterLowCoverageEdges()` prunes edges below median coverage / 2 by default, removing spurious branches caused by sequencing errors.

### Bloom Filter

A Bloom filter provides ultra-fast probabilistic membership tests for k-mers using sub-linear memory. BioSharp implements a standard two-hash Bloom filter (FNV-1a hashes, Rook's method).

```csharp
// Build a Bloom filter with expected 100,000 k-mers and 0.1% false-positive rate
var filter = new BloomFilter(expectedInsertions: 100_000, targetFpr: 0.001);

// Add k-mers
var kmer = "ACGTTACG";
filter.Add(kmer);
filter.Add(new[] { "ACGTTACG", "CGTTACGT", "GTTACGTA" });

// Query
bool present = filter.Contains("ACGTTACG");     // true (definitely)
bool absent  = filter.Contains("ZZZ");           // false (definitely)
bool maybe   = filter.Contains("RANDOMBASE");    // possibly true (false positive)

// Bloom filter metadata
Console.WriteLine($"Bits: {filter.TotalBits}");
Console.WriteLine($"Hashes: {filter.NumHashFunctions}");
Console.WriteLine($"Estimated FPR: {filter.EstimatedFprValue:F6}");
```

**Set operations** for multi-sample analysis:

```csharp
// Union: a k-mer is in the result if it is in ANY sample
var allSamplesFilter = BloomFilter.Union(new[] { sample1Filter, sample2Filter, sample3Filter });

// Intersection: a k-mer is in the result only if it is in ALL samples (common k-mers)
var commonKmers = BloomFilter.Intersection(new[] { sample1Filter, sample2Filter, sample3Filter });
```

### Bubble and Tip Detection

**Bubbles** are divergent paths between a shared start (branch node, out-degree >= 2) and a shared end (convergence node, reachable from 2+ divergent successors). Bubbles represent SNPs and small indels -- each path through the bubble follows a different allele.

```csharp
var bubbles = await BubbleFinder.FindBubbles(graph, k: 15);

foreach (var bubble in bubbles)
{
    Console.WriteLine($"Bubble at node {bubble.StartNode} -> {bubble.EndNode}");
    Console.WriteLine($"  {bubble.Paths.Length} paths:");
    foreach (var path in bubble.Paths)
    {
        Console.WriteLine($"    {path.Sequence} (coverage: {path.Coverage})");
    }
}
```

**Tips** are dead-end branches -- paths that end without converging back into the main graph. Short tips (<= 2k) are typically sequencing artefacts; long tips (> 2k) represent potential insertions not present in the reference.

```csharp
var tips = await TipFinder.FindTips(graph, longTipThreshold: 40); // 2*k where k=20

foreach (var tip in tips)
{
    Console.WriteLine($"Tip: {tip.Sequence} ({tip.Sequence.Length}bp) " +
        $"[{(tip.IsLongTip ? "Potential insertion" : "Sequencing artefact")}]");
}
```

### Structural Variant Detection

`StructuralVariantDetector` orchestrates bubble and tip analysis, classifying bubbles by comparing alt vs ref path lengths and tips by their length. It outputs `LocalVariantResult` records compatible with the alignment-based variant caller.

```csharp
var analysis = await StructuralVariantDetector.AnalyzeGraph(
    graph,
    referenceSequence: "ACGTACGTACGTACGTACGTACGTACGTACGT",
    chromosome: "chr22",
    refStart: 0
);

foreach (var variant in analysis.Variants)
{
    Console.WriteLine($"{variant.Chromosome}:{variant.Position} " +
        $"{variant.Reference}>{variant.Alternate} " +
        $"Q={variant.QuantitativeQuality} DP={variant.Depth}");

    if (variant.IsStructuralVariant)
    {
        Console.WriteLine($"  SV: {variant.SvType} (END={variant.EndPosition})");
        Console.WriteLine($"  Assembly: {variant.AssemblyInfo}");
    }
}
```

**Classification rules**:

| Condition | SV Type |
|---|---|
| Alt ref equal length, sequence differs, reverse complement matches ref | Inversion |
| Alt longer than ref by >= 5bp (or >= 10% of ref) | Insertion |
| Alt shorter than ref by >= 5bp (or >= 10% of ref) | Deletion |
| Read re-aligns in reverse orientation on soft-clip | Inversion (from ReadRealigner) |
| Read re-aligns on a different chromosome | Translocation |

`LocalVariantResult` carries SV metadata: `IsStructuralVariant`, `SvType`, `EndPosition`, `AssemblyInfo`, `ClipInfo`, `Genotype`, and `BubbleConfidence`.

---

## Repeat Masking

`RepeatMasker` scans DNA sequences for matches against a repeat library, masking identified repeats with 'N' characters. This prevents repeats from interfering with variant calling and assembly.

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Load a repeat library (e.g. RepBase or custom JSON)
var library = await RepeatMasker.LoadLibraryAsync("data/repeats.json");

// Mask repeats in a sequence
var maskedRegions = await RepeatMasker.MaskRepeatsAsync(
    sequence: "ACGTACGTNNNNNNNNACGTACGT",
    library: library,
    allowAmbiguity: true
);

foreach (var region in maskedRegions)
{
    Console.WriteLine($"Masked [{region.Start}-{region.End}] " +
        $"({region.Repeat.Name}, type={region.RepeatType}) " +
        $"len={region.Length}");
}

// Convenience: load library and mask in one call
var allMasked = await RepeatMasker.MaskRepeatsFromLibraryAsync(
    sequence: largeSequence,
    libraryPath: "data/repeats.json"
);
```

You can also create a Bloom filter from the library for fast pre-filtering:

```csharp
var repeatBloom = RepeatMasker.CreateBloomFilterFromLibrary(library, k: 15, fpr: 0.01);
```

---

## Somatic and Cohort Variant Calling

### Somatic Variant Detection (Tumor-Normal)

`somaticVariantDetector` identifies tumor-specific variants by comparing a tumor De Bruijn graph against a Bloom filter built from normal-sample k-mers. Bubbles whose alt path is absent from the normal filter are classified as somatic.

```csharp
// Build a Bloom filter from normal-sample k-mers
var normalBloom = await MultiSampleGraph.BuildNormalFilter(k: 15, normalGraph);

// Detect somatic variants
var somaticVariants = await SomaticVariantDetector.DetectSomaticVariantsAsync(
    tumorGraph: tumorGraph,
    normalFilter: normalBloom,
    reference: referenceSequence,
    chromosome: "chr17",
    refStart: 0,
    minAlleleFraction: 0.30,   // minimum alt fraction in tumor (default 30%)
    minTumorCoverage: 5        // minimum tumor coverage (default 5x)
);

foreach (var variant in somaticVariants)
{
    Console.WriteLine($"Somatic: {variant.Position} {variant.Reference}>{variant.Alternate} " +
        $"Tumor={variant.TumorCoverage}x Normal={variant.NormalCoverage}x " +
        $"AF={variant.TumorAlleleFraction:F2} Q={variant.Quality} {variant.Confidence}");
}
```

### Cohort Variant Calling

Cohort analysis processes multiple tumor samples against a shared normal Bloom filter and aggregates findings across samples.

```csharp
var tumorNormalPairs = new[]
{
    ("tumor_1", tumorGraph1),
    ("tumor_2", tumorGraph2),
    ("tumor_3", tumorGraph3),
};

var cohortVariants = await CohortVariantCaller.CallCohortVariantsAsync(
    tumorNormalPairs,
    normalFilter: normalBloom,
    reference: referenceSequence,
    chromosome: "chr22",
    refStart: 0,
    minAlleleFraction: 0.20
);

foreach (var cv in cohortVariants)
{
    Console.WriteLine($"{cv.Position}: {cv.Reference}>{cv.Alternate} " +
        $"in {cv.SampleCount}/{cv.TotalSamples} samples " +
        $"({cv.Type}) Q={cv.AverageQuality:F0} MAF={cv.AverageAlleleFraction:F2}");
    Console.WriteLine($"  Detected in: {string.Join(", ", cv.DetectedIn)}");
}
```

**Cohort variant classification** (based on fraction of samples carrying the variant):

| Classification | Condition |
|---|---|
| SingleSample | Present in 1 sample |
| Oncogenic | Present in >= 90% of samples |
| Present | Present in >= 50% of samples |
| Subclonal | Present in 20-50% of samples |

Filtering helpers:

```csharp
// By frequency
var highFreq = CohortVariantCaller.FilterByFrequency(cohortVariants, 0.5);

// By type
var oncogenicVariants = CohortVariantCaller.FilterByType(cohortVariants, CohortVariantType.Oncogenic);

// Summary statistics
var summary = CohortVariantCaller.GetSummary(cohortVariants, totalSamples: 50);
Console.WriteLine(summary);
// "CohortSummary: 142 variants, AvgQuality=32.5, AvgFreq=0.34. Single=89, Oncogenic=12, Present=23, Subclonal=18"
```

### Multi-Sample Graph Management

`MultiSampleGraph` manages multiple De Bruijn graphs, one per sample, with utilities for k-mer comparison:

```csharp
var multiGraph = new MultiSampleGraph(k: 15);
multiGraph.AddSample("tumor", tumorGraph);
multiGraph.AddSample("normal", normalGraph);

// Build a union Bloom filter of all samples
var allKmerFilter = await multiGraph.GetUnionFilterAsync();

// Find k-mers unique to tumor (not in normal)
var tumorUnique = await multiGraph.GetUniqueKmersInSampleAsync("tumor");

// Find shared k-mers
var shared = await multiGraph.GetSharedKmersAsync(new[] { "tumor", "normal" });
```

---

## Genotype Calling

The genotype caller determines zygosity (0/0, 0/1, 1/1) from ref and alt coverage ratios using a binomial model. Quality scores come from the two-sided binomial CDF (p=0.5, testing against the heterozygous null hypothesis).

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Call genotype from coverage
var genotype = GenotypeCaller.Call(refCoverage: 45, altCoverage: 52);

Console.WriteLine($"Genotype: {genotype.ToVcfGenotype()}");  // "0/1" (heterozygous)
Console.WriteLine($"GQ: {genotype.GQ}");                       // Phred-scaled quality
Console.WriteLine($"Alt/Ref ratio: {genotype.AltRefRatio:F3}"); // ~1.156

// Extend a variant result with genotype info
var variant = new LocalVariantResult
{
    Chromosome = "chr22",
    Position = 50297290,
    Reference = "C",
    Alternate = "T",
    Depth = 97,
    QuantitativeQuality = 42,
};

variant = variant.WithGenotype(refCoverage: 45, altCoverage: 52);
// variant.Genotype now holds the full genotype call
```

**Zygosity thresholds**:

| Alt/Ref Ratio | Genotype |
|---|---|
| < 0.20 | Homozygous Ref (0/0) |
| 0.20 - 0.40 | Homozygous Ref (conservative; borderline) |
| 0.40 - 0.80 | Heterozygous (0/1) |
| >= 0.80 | Homozygous Alt (1/1) |

**Genotype Quality (GQ)**: Computed from the two-sided binomial CDF. Under the heterozygous null hypothesis (p=0.5), the probability of observing the given alt/ref ratio or more extreme is computed, then Phred-scaled: GQ = -10 * log10(p-value), clamped to [0, 99].

---

## Variant Annotation Engine

The `VariantAnnotationEngine` takes a VCF file and transcript sequences (FASTA), then annotates each variant with its predicted biological consequence and HGVS notation.

### Quick Start

```csharp
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Model;

// 1. Create annotation context with CDS boundaries
var ctx = AnnotationContext.FromCdsBoundaries(cdsStart: 100, cdsEnd: 1000, transcriptLength: 1500);

// 2. Initialize engine with context
var engine = new VariantAnnotationEngine(ctx);

// 3. Load transcripts from FASTA
await engine.LoadTranscriptsAsync("data/transcripts.fasta");

// 4. Annotate VCF file
await foreach (var ann in engine.AnnotateVcfAsync(
    vcfPath: "data/variants.vcf",
    transcriptId: "NM_001",       // optional; null to annotate all transcripts
    minQuality: 5.0f))            // min Phred quality to include
{
    Console.WriteLine($"{ann.HgvsCoding} -> {ann.Consequence}");
    Console.WriteLine($"  Protein: {ann.HgvsProtein}");
    Console.WriteLine($"  Codon: {ann.CodonChange}");
}
```

### Non-Coding Annotation

When an `AnnotationContext` is configured with gene boundaries and intron coordinates, the engine classifies variants in non-coding regions:

```csharp
// Define introns and gene boundaries
var introns = new List<(int Start, int End)>
{
    (105, 130),   // Intron 1
    (200, 250)    // Intron 2
};

var ctx = AnnotationContext.FromCdsAndGeneBoundaries(
    cdsStart: 100,
    cdsEnd: 200,
    geneStart: 50,
    geneEnd: 500,
    introns: introns
);

var engine = new VariantAnnotationEngine(ctx);
await engine.LoadTranscriptsAsync("transcripts.fasta");

await foreach (var ann in engine.AnnotateVcfAsync("variants.vcf"))
{
    // ann.Consequence will be one of:
    // SpliceSite, Upstream, Downstream, Intronic, VariantInUtr, Intergenic
}
```

**Classification logic** (with AnnotationContext):

| Position | Consequence |
|---|---|
| Outside gene boundaries | Intergenic |
| Within 3bp of CDS edge | SpliceSite |
| Within CDS | (coding consequence: Missense, Nonsense, etc.) |
| Within intron | Intronic |
| Within 3kb upstream of CDS | Upstream |
| Within 3kb downstream of CDS | Downstream |
| Within transcript but outside CDS, >3kb from edges | VariantInUtr |

Without gene/intron annotation, UTR and upstream/downstream classification still works at the transcript level (3kb window).

### HGVS Notation

The annotator generates both c. (coding DNA) and p. (protein) HGVS notation:

| Consequence | c. notation | p. notation |
|---|---|---|
| Synonymous | c.150A>G | p.Ala50= |
| Missense | c.76A>T | p.Arg25Val |
| Nonsense | c.200G>T | p.Trp67* |
| Frameshift | c.99_101del | p.Pro34fs* |
| InframeDel | c.100_102del | p.Ala34del |
| InframeIns | c.100_102insGCT | p.Ala34insAla |
| SpliceSite | c.103+1G>A | (no protein) |
| Upstream | c.50_79del | (upstream) |
| Intergenic | (non-coding) | (no protein change) |

### Supported Variant Types

| VCF Type | Description |
|---|---|
| Substitution (SNV) | Single-base change, e.g. c.150A>T |
| Deletion | 1-3 bp deletion within a codon, or multi-codon |
| Insertion | 1+ base insertion within or between codons |
| MultiCodonIndel | Indel spanning 2+ codons |
| MNP | Multi-nucleotide polymorphism |
| Delins | Compound deletion-insertion event |

### Ref Mismatch Detection

If the VCF reference allele does not match the expected transcript base at the variant position, the variant is annotated as `Uncertain` rather than silently skipping or producing incorrect results.

---

## End-to-End Analysis Pipeline

This section shows a complete workflow: **input data --> alignment --> variant calling --> De Bruijn SV detection --> VCF output**, using the `VariantCallingPipeline` or the individual components.

### Option A: Using the Pipeline Orchestrator

The `VariantCallingPipeline` ties everything together. It reads BAM or FASTQ, performs per-read alignment, calls variants, runs soft-clip realignment for SV discovery, and can optionally build De Bruijn graphs for graph-based SV detection.

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;

// --- Step 1: Load the reference sequence ---
var faReader = new FastAReader();
Sequence? reference = null;
await foreach (var seq in faReader.Read("data/Homo_sapiens.GRCh38.dna.chromosome.22.fa"))
{
    if (seq.Id.Contains("chr22", StringComparison.OrdinalIgnoreCase))
    {
        reference = seq;
        break;
    }
}

// --- Step 2: Configure the pipeline ---
var pipeline = new VariantCallingPipeline(
    reference: reference!,
    chromosome: "chr22",
    options: new VariantCallingPipeline.PipelineOptions
    {
        MatchScore = 2,
        MismatchPenalty = -3,
        GapOpenPenalty = -5,
        GapExtendPenalty = -2,
        MinAlignmentScore = 10,
        MinVariantQuality = 30,
        EnableSoftClipRealignment = true,
        MinClipFraction = 0.20f,
        MinClipSize = 20,
        LocalWindowSize = 200,
        EnableGraphSvDetection = true,
        KmerSize = 15,
        GraphWindowBp = 500,
        MinGraphCoverage = 5
    }
);

// --- Step 3: Load reads (BAM or FASTQ) ---

// Option 1: BAM input
await pipeline.LoadBamAsync("data/mapt.NA12156.altex.bam");

// Option 2: FASTQ input
// await pipeline.LoadFastQAsync("data/ERR164409.fastq");

// --- Step 4: Run graph-based SV detection on specific regions ---
// (optional, requires loaded reads from step 3)
var svAnalysis = await pipeline.RunGraphAnalysisAsync(
    windowStart: 10000,
    windowEnd: 15000
);

// Or run full-genome graph analysis:
// var fullSv = await pipeline.RunFullGraphAnalysisAsync();

// --- Step 5: Build final results ---
var result = pipeline.BuildResult();

// --- Step 6: Inspect results ---
Console.WriteLine(result.Metrics);
// PipelineMetrics{reads=1500, mapped=1420, aligned=380, called=245, final=128, sv=3}

foreach (var variant in result.Variants)
{
    Console.WriteLine($"{variant.Chromosome}:{variant.Position} " +
        $"{variant.Reference}>{variant.Alternate} " +
        $"Q={variant.QuantitativeQuality} DP={variant.Depth}");

    if (variant.IsStructuralVariant)
    {
        Console.WriteLine($"  SV TYPE: {variant.SvType} " +
            $"END={variant.EndPosition} " +
            $"AssemblyInfo={variant.AssemblyInfo}");
    }

    if (variant.ClipInfo != null)
    {
        Console.WriteLine($"  Soft-clip: {variant.ClipInfo}");
    }

    if (variant.Genotype != null)
    {
        Console.WriteLine($"  Genotype: {variant.Genotype}");
    }
}

// --- Step 7: Write output VCF ---
await pipeline.WriteVcfAsync("output/variants.vcf", chromLength: 50818468L);

// (Optional: also write SAM for debugging)
// await pipeline.WriteSamAsync("output/aligned.sam");
```

### Option B: Using Individual Components (Manual Pipeline)

For more control, use the individual components:

```csharp
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Io.FastQ;
using OpenMedStack.BioSharp.Model;

// --- Step 1: Load reference ---
var faReader = new FastAReader();
Sequence reference = null!;
await foreach (var seq in faReader.Read("data/reference.fa"))
{
    reference = seq;
}

// --- Step 2: Load reads ---
var fqReader = new FastQReader(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
var allVariants = new List<LocalVariantResult>();
var alignments = new List<AlignmentResult>();

await foreach (var read in fqReader.Read("data/reads.fastq"))
{
    // Align each read
    var alignment = SmithWatermanAligner.Align(
        reference, read,
        matchScore: 2,
        mismatchPenalty: -3,
        gapOpenPenalty: -5,
        gapExtendPenalty: -2,
        minScore: 10
    );

    if (alignment == null) continue;
    alignments.Add(alignment);

    // Call variants from primary alignment
    var variants = VariantCaller.CallVariants(reference, alignment, minQuality: 30);
    foreach (var v in variants)
    {
        v.Chromosome = "chr22";
        allVariants.Add(v);
    }

    // Realigned soft-clips for SV discovery
    if (alignment.LeftSoftClip > 0 || alignment.RightSoftClip > 0)
    {
        var realigner = new ReadRealigner
        {
            MinClipFraction = 0.20f,
            MinClipSize = 20,
            LocalWindowSize = 200
        };

        var readSeq = read.GetData().Span.ToString();
        var readQual = read.GetQuality().Span.ToString();

        var realignResults = realigner.Realign(alignment, reference, "chr22", readSeq);

        foreach (var realign in realignResults)
        {
            if (realign.WasRealigned && realign.Alignment != null)
            {
                var realignVariants = VariantCaller.CallVariants(reference, realign.Alignment!, 30);
                foreach (var rv in realignVariants)
                {
                    rv.Chromosome = "chr22";
                    if (realign.IsStructuralVariant)
                    {
                        rv.IsStructuralVariant = true;
                        rv.SvType = realign.SvType;
                    }
                    allVariants.Add(rv);
                }
            }
        }
    }
}

// --- Step 3: Merge variants from all reads ---
var mergedVariants = VariantCaller.MergeVariants(allVariants.ToArray());

Console.WriteLine($"Merged {allVariants.Count} raw calls into {mergedVariants.Length} variants");

// --- Step 4: De Bruijn graph SV detection (optional, for deeper SV discovery) ---
var graphReads = alignments
    .Where(a => a != null)
    .Select(a =>
    {
        var idx = alignments.IndexOf(a!);
        return new Sequence($"r{idx}",
            reads[idx].GetData(),
            reads[idx].GetQuality());
    })
    .ToList();

// Convert to async enumerable for graph construction
var asyncGraphReads = AsyncEnumerableExtensions.ToAsyncEnumerable(graphReads);
var graph = new DeBruijnGraph(k: 15, asyncGraphReads);

var svAnalysis = await StructuralVariantDetector.AnalyzeGraph(
    graph,
    new string(reference.GetData().Span),
    "chr22",
    0
);

// Merge graph-discovered SVs with alignment-discovered variants
foreach (var sv in svAnalysis.Variants)
{
    sv.Chromosome = "chr22";
    allVariants.Add(sv);
}

var finalVariants = VariantCaller.MergeVariants(allVariants.ToArray());

// --- Step 5: Write VCF ---
await VcfWriter.WriteAsync(
    stream: new FileStream("output/variants.vcf", FileMode.Create),
    variants: finalVariants,
    chromosome: "chr22",
    chromLength: 50818468
);
```

### Option C: Somatic Calling (Tumor-Normal Pair)

For somatic variant detection (e.g., cancer genomics):

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

// Build De Bruijn graphs for tumor and normal samples
var tumorGraph = new DeBruijnGraph(15, tumorReadsAsync);
var normalGraph = new DeBruijnGraph(15, normalReadsAsync);

// Build multi-sample graph to manage both samples
var multiGraph = new MultiSampleGraph(15);
multiGraph.AddSample("tumor", tumorGraph);
multiGraph.AddSample("normal", normalGraph);

// Create a normal Bloom filter (k-mers present in normal = germline, filter these out)
var normalFilter = await MultiSampleGraph.BuildNormalFilter(multiGraph, "normal");

// Detect somatic variants -- tumor-specific mutations
var somaticVariants = await SomaticVariantDetector.DetectSomaticVariantsAsync(
    tumorGraph: tumorGraph,
    normalFilter: normalFilter,
    reference: referenceSequence,
    chromosome: "chr17",
    refStart: 0,
    minAlleleFraction: 0.30,  // tumor must have >= 30% alt reads
    minTumorCoverage: 5       // tumor must have >= 5x coverage
);

foreach (var v in somaticVariants)
{
    Console.WriteLine($"Somatic variant: {v.Position} " +
        $"{v.Reference}>{v.Alternate} " +
        $"Tumor={v.TumorCoverage}x Normal={v.NormalCoverage}x " +
        $"AlleleFraction={v.TumorAlleleFraction:F2} " +
        $"Confidence={v.Confidence} Q={v.Quality}");
}
```

### Option D: Cohort Analysis

For population-level analysis across multiple samples:

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

var tumorNormalPairs = samples
    .Select(s => (s.Name, s.TumorGraph))
    .ToList();

var cohortVariants = await CohortVariantCaller.CallCohortVariantsAsync(
    tumorNormalPairs: tumorNormalPairs,
    normalFilter: normalBloomFilter,
    reference: referenceSequence,
    chromosome: "chr22",
    refStart: 0,
    minAlleleFraction: 0.20
);

// Filter by frequency
var rareVariants = CohortVariantCaller
    .FilterByFrequency(cohortVariants, 0.1)
    .Where(v => v.SampleCount == 1)
    .ToList();

// Filter by type
var oncogenicVariants = CohortVariantCaller
    .FilterByType(cohortVariants, CohortVariantType.Oncogenic)
    .ToList();

// Summary
var summary = CohortVariantCaller.GetSummary(cohortVariants, totalSamples: samples.Count);
Console.WriteLine(summary);
```

### Option E: Variant Annotation (Post-Calling)

After calling variants, annotate them against known transcripts:

```csharp
using OpenMedStack.BioSharp.Calculations;
using OpenMedStack.BioSharp.Model;

// 1. Load reference transcripts
var engine = new VariantAnnotationEngine();
await engine.LoadTranscriptsAsync("data/transcripts.fa");

// 2. Annotate variants from VCF
await foreach (var ann in engine.AnnotateVcfAsync(
    vcfPath: "output/variants.vcf",
    transcriptId: null,       // all transcripts
    minQuality: 5.0f))
{
    Console.WriteLine($"Transcript: {ann.AffectedGene}");
    Console.WriteLine($"  HGVS c.: {ann.HgvsCoding}");
    Console.WriteLine($"  HGVS p.: {ann.HgvsProtein}");
    Console.WriteLine($"  Consequence: {ann.Consequence}");
    if (ann.AffectedAminoAcid != null)
        Console.WriteLine($"  Amino acid: {ann.AffectedAminoAcid} -> {ann.ResultingAminoAcid}");
    if (ann.FrameshiftOffset.HasValue)
        Console.WriteLine($"  Frameshift offset: {ann.FrameshiftOffset} aa to stop");
    Console.WriteLine();
}
```

### Option F: Repeat Masking (Pre-Calling)

Mask repeats before variant calling to reduce false positives:

```csharp
using OpenMedStack.BioSharp.Calculations.DeBruijn;

var library = await RepeatMasker.LoadLibraryAsync("data/repeats.json");
var maskedSequence = await RepeatMasker.MaskRepeatsFromLibraryAsync(
    sequence: referenceData,
    libraryPath: "data/repeats.json"
);

foreach (var region in maskedSequence)
{
    Console.WriteLine($"Masked repeat: {region}");
}
```

---

## Reference Data

Sample data files are included in the `data/` directory:

| File | Description |
|---|---|
| `Homo_sapiens.GRCh38.dna.chromosome.1.fa.gz` | Human chr1 (GRCh38, gzip-compressed) |
| `Homo_sapiens.GRCh38.dna.chromosome.22.fa.gz` | Human chr22 (GRCh38, gzip-compressed) |
| `chr1.fa.gz` | Alternative chr1 FASTA, compressed |
| `ERR164409.fastq.gz` | Illumina reads, SRA sample, compressed |
| `D.4x.vcf.gz` | Variant calls, compressed |
| `mapt.NA12156.altex.bam` |_mapped BAM file for the NA12156 (CEU) sample |
| `mapt.NA12156.altex.bam.bai` | BAM index for region queries |
| `repeats.json` | Repeat library for `RepeatMasker` |
| `illumina_run/` | Illumina BCL run data for testing |
| `sampledata/` | Sample Illumina run metadata (RunInfo.xml, Config, InterOp) |

To decompress gzip files if needed (they are referenced internally with streaming support):

```bash
gunzip data/*.gz
```

---

## API Reference Summary

### Algorithm Classes

| Class | Namespace | Purpose |
|---|---|---|
| `SmithWatermanAligner` | `Calculations.Alignment` | Semi-global read alignment, returns `AlignmentResult` |
| `NeedlemanWunschAlgo` | `Calculations.NeedlemanWunsch` | Global protein alignment, BLOSUM62 |
| `CigarBuilder` | `Calculations.Alignment` | Alignment --> SAM/BAM CIGAR strings |
| `VariantCaller` | `Calculations.Alignment` | Parse alignment events --> `LocalVariantResult[]` |
| `VariantCallingPipeline` | `Calculations.Alignment` | Orchestrator: BAM/FASTQ --> alignment --> variants --> VCF |
| `ReadRealigner` | `Calculations.Alignment` | Soft-clip realignment for SV discovery |
| `DeBruijnGraph` | `Calculations.DeBruijn` | Build, assemble, filter De Bruijn graphs from reads |
| `BloomFilter` | `Calculations.DeBruijn` | Probabilistic k-mer membership testing |
| `BubbleFinder` | `Calculations.DeBruijn` | Divergent paths --> bubbles (SNPs, small indels) |
| `TipFinder` | `Calculations.DeBruijn` | Dead-end tips --> potential insertions |
| `StructuralVariantDetector` | `Calculations.DeBruijn` | Orchestrator: bubbles + tips --> `LocalVariantResult[]` |
| `GenotypeCaller` | `Calculations.DeBruijn` | Coverage --> zygosity (0/0, 0/1, 1/1) + GQ |
| `SomaticVariantDetector` | `Calculations.DeBruijn` | Tumor-normal comparison --> somatic variants |
| `CohortVariantCaller` | `Calculations.DeBruijn` | Multi-sample cohort analysis with frequency classification |
| `MultiSampleGraph` | `Calculations.DeBruijn` | Multi-sample graph management, k-mer queries |
| `RepeatMasker` | `Calculations.DeBruijn` | Repeat detection and masking |

### I/O Classes

| Class | Namespace | Input/Output |
|---|---|---|
| `FastAReader` | `Io.FastA` | Read --> `IAsyncEnumerable<Sequence>` |
| `FastAWriter` | `Io.FastA` | `Sequence[]` --> FASTA file |
| `FastQReader` | `Io.FastQ` | Read --> `IAsyncEnumerable<Sequence>` |
| `FastQWriter` | `Io.FastQ` | `Sequence[]` --> FASTQ file |
| `BamReader` | `Io.Bam` | BAM --> `SamDefinition` |
| `BamWriter` | `Io.Bam` | SAM data --> BAM file |
| `BamIndexReader` | `Io.Bam` | BAM index --> `BamIndex` |
| `VcfFileReader` | `Io.Vcf` | VCF --> `IAsyncEnumerable<VcfVariant>` |
| `VcfWriter` | `Calculations.Alignment` | `LocalVariantResult[]` --> VCF file |
| `BclReader` / `SampleReader` | `Io.Bcl` | BCL --> `Run` with tiles and reads |

### Model Classes

| Class | Namespace | Purpose |
|---|---|---|
| `Sequence` | `Model` | DNA/RNA sequence with quality scores |
| `AlignmentResult` | `Calculations.Alignment` | Alignment output |
| `LocalVariantResult` | `Calculations.Alignment` | Variant call with optional SV, genotype, clip info |
| `VariantAnnotation` | `Model` | Annotated variant with HGVS notation |
| `AnnotationContext` | `Model` | Gene/CDS/intron boundaries for non-coding annotation |
| `VariantConsequence` | `Model` | Enum: Synonymous, Missense, Nonsense, Frameshift, etc. |
| `Genotype` | `Calculations.DeBruijn` | Genotype call: 0/0, 0/1, 1/1 with GQ |
| `BloomFilter` | `Calculations.DeBruijn` | Probabilistic set for k-mers |
| `SomaticVariant` | `Calculations.DeBruijn` | Tumor-specific variant |
| `CohortVariant` | `Calculations.DeBruijn` | Variant across cohort samples with frequency |
| `RepeatElement` | `Calculations.DeBruijn` | Repeat from library |
| `MaskedRegion` | `Calculations.DeBruijn` | Masked repeat region |
