Feature: Convert Fastq to Vcf
  As a bioinformatics developer
  I want to process FASTQ reads through the BioSharp variant calling pipeline
  So that I can obtain variant calls in standard VCF format

  # FTV-1: Pipeline loading and metrics
  Scenario: VariantCallingPipeline loads reads from an async FASTQ stream
    Given I have a 500 bp synthetic reference generated with seed 42
    And I generate 20 reads of length 50 from that reference
    When I load the 20 reads into a fresh VariantCallingPipeline
    Then the pipeline should report 20 processed reads in the metrics

  # FTV-2: VCF file output
  Scenario: VariantCallingPipeline produces a valid VCF file after loading reads
    Given I have a 500 bp synthetic reference generated with seed 42
    And I generate 10 reads of length 50 from that reference
    When I load the reads into the pipeline and write the result to a VCF file
    Then the VCF file should exist
    And the VCF file should begin with the required fileformat header

  # FTV-3: Adapter trimming pre-processing
  Scenario: AdapterTrimmer removes NexTera adapters from all reads before pipeline loading
    Given I have 10 synthetic reads each with a 40 bp payload and a NexTera adapter suffix
    When I run the NexTera AdapterTrimmer on all 10 reads
    Then the trimmer should report 10 reads trimmed
    And each surviving read should have a sequence length of 40 bp

  # FTV-4: Mapped read metrics
  Scenario: Pipeline metrics accurately reflect the number of mapped reads
    Given I have a 500 bp synthetic reference generated with seed 42
    And I generate 15 reads of length 50 from that reference
    When I load the 15 reads into a fresh VariantCallingPipeline
    Then the pipeline should report 15 processed reads in the metrics
    And the pipeline mapped read count should not exceed 15

  # FTV-5: VcfNormalizer post-processing
  Scenario: VcfNormalizer left-aligns a pipeline indel variant in a homopolymer region
    Given I have a pipeline result with a deletion variant in a poly-A homopolymer
    When I normalize the pipeline variants against the homopolymer reference
    Then the normalized variants should be ordered by position
    And the indel variant should be shifted to the leftmost position

  # FTV-6: BQSR integration
  Scenario: BaseQualityRecalibrator recalibrates base qualities on aligned reads
    Given I have aligned reads with quality score 20 aligned to a synthetic reference
    When I run BQSR covariate collection and apply recalibration to the alignments
    Then the recalibrated quality strings should differ from the original quality strings

  # FTV-7: Graph SV analysis
  Scenario: RunFullGraphAnalysisAsync runs to completion after FASTQ reads are loaded
    Given I have a 500 bp synthetic reference generated with seed 42
    And I generate 10 reads of length 50 from that reference
    When I load the reads into the pipeline and run full de Bruijn graph analysis
    Then the graph analysis should return a StructuralVariantAnalysis result

  # FTV-8: Full integration test
  Scenario: Full pipeline trims adapters, aligns reads, and writes a VCF file
    Given I have a 500 bp synthetic reference generated with seed 42
    And I have 10 reads with a 40 bp reference payload and NexTera adapter suffix
    When I trim the adapter contamination and load the cleaned reads into a pipeline
    And I write the variant calls to a new VCF file
    Then the final VCF file should exist on disk
    And the final VCF file should contain the fileformat declaration line
