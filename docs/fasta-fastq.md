# FASTA / FASTQ

## Introduction

FASTA and FASTQ are the two most fundamental file formats in genomics, serving as the entry point for virtually every 
bioinformatics analysis workflow. FASTA stores reference or consensus sequences as plain text nucleotide strings — this 
is how you load the reference genome (e.g., GRCh38) that all downstream alignments reference. FASTQ adds per-base 
quality scores, which are essential for assessing read reliability and filtering low-quality bases before alignment.

The FastAReader and FastQReader load these files into `Sequence` objects, which wrap nucleotide data and quality scores 
for easy manipulation. In a standard pipeline, FASTA provides the reference genome while FASTQ holds the sequencing 
reads that will be aligned back to it — these readers are the first step in every alignment-based variant calling 
workflow.

The Sequence class also supports slicing, allowing you to extract sub-sequences for targeted analysis of specific 
genomic regions (e.g., a single gene locus).

Writers are also available for creating FASTA and FASTQ files programmatically when you need to save processed or 
modified sequences back to disk.

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
