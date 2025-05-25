# I/O Ecosystem

## Introduction

Before any bioinformatics analysis can begin, genomic data must be loaded into the system. BioSharp supports all the 
major sequencing and variant file formats used in modern genomics labs — from raw sequencer output through to variant 
call files.

The I/O layer uses async enumerable patterns (`IAsyncEnumerable<T>`) to process large genomic files without loading 
entire files into memory. This is critical for genome-scale datasets that can span gigabytes, allowing streaming 
processing that scales to whole-genome datasets.

Every downstream analysis — alignment ([Smith-Waterman](smith-waterman.md), [Needleman-Wunsch](needleman-wunsch.md)), variant calling ([variant 
calling pipeline](variant-calling-pipeline.md)), De Bruijn graph assembly ([de Bruijn assembly](de-bruijn-assembly.md)), and annotation 
([annotation quick start](variant-annotation-quick-start.md)) — depends on correctly reading input files. This ecosystem covers all the formats and 
how to use the readers and writers to move data in and out of the pipeline.

Think of this layer as the gateway between the lab bench and the computational analysis — whether your data comes from 
raw Illumina sequencer output (BCL files, processed via [Preator](e2e-option-f-repeat-masking.md)), or from downstream formats like FASTQ, BAM, or 
VCF, this documentation tells you how to bring it into BioSharp for processing. The I/O layer supports both reading and 
writing, completing the read-process-write cycle of every analytical workflow.

BioSharp provides streaming readers and writers for FASTA, FASTQ, SAM/BAM, VCF, and Illumina BCL raw data files. All 
I/O is streaming-based via `IAsyncEnumerable<T>` for memory-efficient processing of large genomic datasets.

```csharp
// Example: streaming FASTQ reads into the pipeline
var reader = new FastQReader(logger);
await foreach (var read in reader.Read("reads.fastq"))
{
    // Process each read through alignment, variant calling, etc.
    var seq = read.GetData().Span.ToString();
    var qual = read.GetQuality().Span.ToString();
    // ... feed into SmithWaterman, VariantCaller, etc.
}
```
