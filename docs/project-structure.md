# Project Structure

## Introduction

Bioinformatics projects require a clear organizational structure to manage the various components involved in genomic 
data processing. The BioSharp project is organized into three main library projects, each responsible for a distinct 
layer of the bioinformatics pipeline. This structure mirrors the bioinformatics workflow: data loading (I/O), 
algorithmic analysis (calculations), and data representation (model).

The **model** project defines the core domain types — the biological data structures that represent sequences, 
variants, annotations, and HGVS notation. These are the building blocks used throughout the pipeline.

The **I/O** project handles reading and writing biological file formats. Genomic data comes in many standardized 
formats (FASTA, FASTQ, BAM, VCF, BCL), and this layer provides the readers and writers to convert between on-disk 
formats and in-memory objects.

The **calculations** project contains the computational core — alignment algorithms, De Bruijn graph operations, 
variant calling logic, genotype inference, and variant annotation. This is where the actual bioinformatics algorithms 
live.

Understanding this structure is the first step because every subsequent section of this documentation will reference 
these projects and their components. The I/O layer feeds data into the calculations layer, and the model layer ties 
everything together.

```
src/
  openmedstack.biosharp.model/           # Core domain models
    Sequence, BasePair, AminoAcid
    VariantConsequence, VariantAnnotation
    AnnotationContext, CodonChange
    HgvsDescription, SubstitutionDescription, DeletionDescription,
      InsertionDescription, DuplicationDescription, DeletionInsertionDescription
    ReferenceSequenceKind
    VcfVariant, VariantCallFile, VcfMetaReader
  openmedstack.biosharp.io/              # File I/O
    FastA/FastQ readers and writers
    SAM/BAM readers and writers (with index support)
    VCF readers and writers (BGZF-compression aware)
    Illumina BCL readers, barcode/quality parsers
  openmedstack.biosharp.calculations/    # Algorithms
    Alignment/                           # Smith-Waterman, Needleman-Wunsch,
                                        # CIGAR builder, VariantCaller
    DeBruijn/                            # De Bruijn graph, Bloom filter,
                                        # BubbleFinder, TipFinder,
                                        # StructuralVariantDetector,
                                        # RepeatMasker, GenotypeCaller,
                                        # SomaticVariantDetector,
                                        # CohortVariantCaller
    VariantAnnotator, VariantAnnotationEngine
tests/                                   # xUnit test suite
data/                                    # Reference data
  Homo_sapiens.GRCh38.dna.chromosome.22.fa.gz
  ERR164409.fastq.gz
  mapt.NA12156.altex.bam
  repeats.json
  illumina_run/                          # BCL sample data
```
