@Equivalency
Feature: Tool Equivalency Acceptance Tests
  As a bioinformatics developer
  I want BioSharp to produce results equivalent to established external tools
  So that clinical results can be independently validated and trusted

  # ─────────────────────────────────────────────────────────────────────────────
  # Alignment: BioSharp (HashMap seeder) vs bwa mem
  # Tests that the mapped-read count from BioSharp's hash-map k-mer seeder
  # is statistically equivalent to bwa mem on the same synthetic input.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresBwa @RequiresSamtools
  Scenario Outline: BioSharp HashMap alignment produces equivalent mapped-read count to bwa mem
    Given a synthetic FASTA reference of <ReferenceSize> bases seeded with random 42
    And <ReadCount> synthetic FASTQ reads of <ReadLength> bp derived from the reference with 10 percent SNP rate
    When BioSharp aligns the reads using the HashMap seeder with seed size 11
    And bwa mem aligns the same reads to the same reference
    Then the BioSharp mapped count should be within <TolerancePct> percent of the bwa mem mapped count

    Examples:
      | ReferenceSize | ReadCount | ReadLength | TolerancePct |
      | 10000         | 100       | 75         | 10           |
      | 50000         | 200       | 150        | 10           |
      | 10000         | 300       | 100        | 10           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Alignment: BioSharp (FM-index seeder) vs bwa mem
  # Tests that BioSharp's Burrows-Wheeler FM-index seeder produces the same
  # alignment rate as bwa mem, since both use BWT-based seeding.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresBwa @RequiresSamtools
  Scenario Outline: BioSharp FmIndex alignment produces equivalent mapped-read count to bwa mem
    Given a synthetic FASTA reference of <ReferenceSize> bases seeded with random 42
    And <ReadCount> synthetic FASTQ reads of <ReadLength> bp derived from the reference with 10 percent SNP rate
    When BioSharp aligns the reads using the FmIndex seeder
    And bwa mem aligns the same reads to the same reference
    Then the BioSharp mapped count should be within <TolerancePct> percent of the bwa mem mapped count

    Examples:
      | ReferenceSize | ReadCount | ReadLength | TolerancePct |
      | 10000         | 100       | 75         | 10           |
      | 50000         | 200       | 150        | 10           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Alignment: BioSharp (HashMap seeder) vs bwa-mem2
  # bwa-mem2 uses SIMD-accelerated DP — testing equivalency confirms BioSharp's
  # seeding is functionally correct regardless of implementation speed.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresBwaMem2
  Scenario Outline: BioSharp HashMap alignment produces equivalent mapped-read count to bwa-mem2
    Given a synthetic FASTA reference of <ReferenceSize> bases seeded with random 42
    And <ReadCount> synthetic FASTQ reads of <ReadLength> bp derived from the reference with 10 percent SNP rate
    When BioSharp aligns the reads using the HashMap seeder with seed size 11
    And bwa-mem2 aligns the same reads to the same reference
    Then the BioSharp mapped count should be within <TolerancePct> percent of the bwa-mem2 mapped count

    Examples:
      | ReferenceSize | ReadCount | ReadLength | TolerancePct |
      | 10000         | 100       | 75         | 10           |
      | 50000         | 200       | 150        | 10           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Adapter trimming: BioSharp vs fastp — surviving read count
  # Verifies that the number of reads passing the adapter trim + minimum-length
  # filter is the same as fastp produces on the identical FASTQ.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresFastp
  Scenario Outline: BioSharp adapter trimming produces equivalent surviving read count to fastp
    Given <ReadCount> FASTQ reads of <ReadLength> bp with <AdapterFraction> percent carrying adapter <Adapter>
    When BioSharp trims adapter <Adapter> with minimum length <MinLength> and max mismatches 2
    And fastp trims the reads with adapter <Adapter> and minimum length <MinLength>
    Then the BioSharp surviving read count should be within <TolerancePct> percent of the fastp surviving count

    Examples:
      | ReadCount | ReadLength | AdapterFraction | Adapter                               | MinLength | TolerancePct |
      | 1000      | 100        | 30              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 20        | 10           |
      | 1000      | 100        | 60              | "CTGTCTCTTATACACATCT"               | 20        | 10           |
      | 500       | 150        | 50              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 50        | 10           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Adapter trimming: BioSharp vs fastp — bases removed
  # Adapter trimming should remove a similar number of bases as fastp.
  # A wider tolerance (15 %) accounts for minor differences in how partial
  # adapter overlaps are scored.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresFastp
  Scenario Outline: BioSharp bases removed by adapter trimming is within tolerance of fastp
    Given <ReadCount> FASTQ reads of <ReadLength> bp with <AdapterFraction> percent carrying adapter <Adapter>
    When BioSharp trims adapter <Adapter> with minimum length <MinLength> and max mismatches 2
    And fastp trims the reads with adapter <Adapter> and minimum length <MinLength>
    Then the BioSharp bases removed should be within <TolerancePct> percent of the fastp bases removed

    Examples:
      | ReadCount | ReadLength | AdapterFraction | Adapter                               | MinLength | TolerancePct |
      | 1000      | 100        | 30              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 20        | 15           |
      | 1000      | 100        | 60              | "CTGTCTCTTATACACATCT"               | 20        | 15           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Adapter trimming: BioSharp vs cutadapt — surviving read count
  # cutadapt is the canonical Python adapter trimmer; equivalency here
  # validates that BioSharp's C# implementation is compatible.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresCutadapt
  Scenario Outline: BioSharp adapter trimming produces equivalent surviving read count to cutadapt
    Given <ReadCount> FASTQ reads of <ReadLength> bp with <AdapterFraction> percent carrying adapter <Adapter>
    When BioSharp trims adapter <Adapter> with minimum length <MinLength> and max mismatches 2
    And cutadapt trims the reads with adapter <Adapter> and minimum length <MinLength>
    Then the BioSharp surviving read count should be within <TolerancePct> percent of the cutadapt surviving count

    Examples:
      | ReadCount | ReadLength | AdapterFraction | Adapter                               | MinLength | TolerancePct |
      | 1000      | 100        | 30              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 20        | 10           |
      | 1000      | 100        | 60              | "CTGTCTCTTATACACATCT"               | 20        | 10           |
      | 500       | 150        | 50              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 50        | 10           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Adapter trimming: BioSharp vs cutadapt — bases removed
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresCutadapt
  Scenario Outline: BioSharp bases removed by adapter trimming is within tolerance of cutadapt
    Given <ReadCount> FASTQ reads of <ReadLength> bp with <AdapterFraction> percent carrying adapter <Adapter>
    When BioSharp trims adapter <Adapter> with minimum length <MinLength> and max mismatches 2
    And cutadapt trims the reads with adapter <Adapter> and minimum length <MinLength>
    Then the BioSharp bases removed should be within <TolerancePct> percent of the cutadapt bases removed

    Examples:
      | ReadCount | ReadLength | AdapterFraction | Adapter                               | MinLength | TolerancePct |
      | 1000      | 100        | 30              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 20        | 15           |
      | 1000      | 100        | 60              | "CTGTCTCTTATACACATCT"               | 20        | 15           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Adapter trimming: BioSharp vs Trimmomatic — surviving read count
  # Trimmomatic is the canonical Java adapter trimmer widely used in NGS pipelines.
  # Equivalency here validates that BioSharp's C# implementation is compatible.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresTrimmomatic
  Scenario Outline: BioSharp adapter trimming produces equivalent surviving read count to trimmomatic
    Given <ReadCount> FASTQ reads of <ReadLength> bp with <AdapterFraction> percent carrying adapter <Adapter>
    When BioSharp trims adapter <Adapter> with minimum length <MinLength> and max mismatches 2
    And trimmomatic trims the reads with adapter <Adapter> and minimum length <MinLength>
    Then the BioSharp surviving read count should be within <TolerancePct> percent of the trimmomatic surviving count

    Examples:
      | ReadCount | ReadLength | AdapterFraction | Adapter                               | MinLength | TolerancePct |
      | 1000      | 100        | 30              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 20        | 15           |
      | 1000      | 100        | 60              | "CTGTCTCTTATACACATCT"               | 20        | 15           |
      | 500       | 150        | 50              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 50        | 15           |

  # ─────────────────────────────────────────────────────────────────────────────
  # Adapter trimming: BioSharp vs Trimmomatic — bases removed
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresTrimmomatic
  Scenario Outline: BioSharp bases removed by adapter trimming is within tolerance of trimmomatic
    Given <ReadCount> FASTQ reads of <ReadLength> bp with <AdapterFraction> percent carrying adapter <Adapter>
    When BioSharp trims adapter <Adapter> with minimum length <MinLength> and max mismatches 2
    And trimmomatic trims the reads with adapter <Adapter> and minimum length <MinLength>
    Then the BioSharp bases removed should be within <TolerancePct> percent of the trimmomatic bases removed

    Examples:
      | ReadCount | ReadLength | AdapterFraction | Adapter                               | MinLength | TolerancePct |
      | 1000      | 100        | 30              | "AGATCGGAAGAGCACACGTCTGAACTCCAGTCA" | 20        | 20           |
      | 1000      | 100        | 60              | "CTGTCTCTTATACACATCT"               | 20        | 20           |
  # FastQC is the standard tool for read-level quality assessment.
  # The total read count must match exactly — there is no tolerance.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresFastqc
  Scenario Outline: BioSharp total read count matches fastqc exactly
    Given <ReadCount> uniform-quality FASTQ reads of <ReadLength> bp with quality score <QualScore>
    When BioSharp computes the FastQ quality report
    And fastqc analyses the same FASTQ reads
    Then the BioSharp total read count should match the fastqc total read count exactly

    Examples:
      | ReadCount | ReadLength | QualScore |
      | 1000      | 75         | 30        |
      | 500       | 150        | 20        |
      | 200       | 100        | 40        |

  # ─────────────────────────────────────────────────────────────────────────────
  # QC metrics: BioSharp vs FastQC — mean per-base quality
  # The average per-base quality computed by BioSharp should be within a small
  # tolerance of FastQC, as both should report the same Phred scores.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresFastqc
  Scenario Outline: BioSharp mean per-base quality is within tolerance of fastqc
    Given <ReadCount> uniform-quality FASTQ reads of <ReadLength> bp with quality score <QualScore>
    When BioSharp computes the FastQ quality report
    And fastqc analyses the same FASTQ reads
    Then the BioSharp mean per-base quality should be within <TolerancePct> percent of the fastqc mean quality

    Examples:
      | ReadCount | ReadLength | QualScore | TolerancePct |
      | 1000      | 75         | 30        | 5            |
      | 1000      | 150        | 20        | 5            |

  # ─────────────────────────────────────────────────────────────────────────────
  # Variant calling: BioSharp vs freebayes — SNP position overlap
  # Both callers process the same sorted BAM (produced via bwa mem + samtools sort).
  # Equivalency is measured as Jaccard overlap of called SNP positions.
  # A minimum overlap of 50 % is required — lower thresholds would not give
  # meaningful clinical confidence.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresBwa @RequiresSamtools @RequiresFreebayes
  Scenario Outline: BioSharp SNP calls overlap with freebayes calls
    Given a synthetic FASTA reference of <ReferenceSize> bases with <SnpCount> planted SNPs seeded with random 42
    And <ReadCount> FASTQ reads of <ReadLength> bp covering those SNP positions
    When a sorted BAM is produced by aligning the reads with bwa mem and sorting with samtools
    And BioSharp calls variants from that BAM using the HashMap seeder
    And freebayes calls variants from the same BAM
    Then the BioSharp SNP positions should overlap freebayes SNP positions by at least <MinOverlapPct> percent

    Examples:
      | ReferenceSize | SnpCount | ReadCount | ReadLength | MinOverlapPct |
      | 10000         | 5        | 500       | 150        | 50            |
      | 10000         | 10       | 1000      | 150        | 50            |

  # ─────────────────────────────────────────────────────────────────────────────
  # Variant calling: BioSharp vs samtools mpileup + bcftools call
  # samtools mpileup + bcftools call is the lightest-weight pileup-based
  # variant caller in wide clinical use.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresBwa @RequiresSamtools @RequiresBcftools
  Scenario Outline: BioSharp SNP calls overlap with samtools mpileup bcftools call
    Given a synthetic FASTA reference of <ReferenceSize> bases with <SnpCount> planted SNPs seeded with random 42
    And <ReadCount> FASTQ reads of <ReadLength> bp covering those SNP positions
    When a sorted BAM is produced by aligning the reads with bwa mem and sorting with samtools
    And BioSharp calls variants from that BAM using the HashMap seeder
    And samtools mpileup piped to bcftools calls variants from the same BAM
    Then the BioSharp SNP positions should overlap samtools-bcftools SNP positions by at least <MinOverlapPct> percent

    Examples:
      | ReferenceSize | SnpCount | ReadCount | ReadLength | MinOverlapPct |
      | 10000         | 5        | 500       | 150        | 50            |
      | 10000         | 10       | 1000      | 150        | 50            |
