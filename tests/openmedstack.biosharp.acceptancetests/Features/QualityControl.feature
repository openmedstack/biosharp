Feature: Quality Control and Metrics
  As a bioinformatics developer
  I want to compute QC metrics from sequencing data
  So that clinical labs can validate their runs before reporting

  # QC-1: FastQC equivalent read-level quality metrics
  Scenario: FastQQualityReport computes per-base quality distribution
    Given I have a set of FASTQ reads with known base quality scores
    When I compute the FastQ quality report
    Then the report should contain per-base quality statistics for each cycle position

  Scenario: FastQQualityReport computes GC content distribution
    Given I have a set of FASTQ reads with known base composition
    When I compute the FastQ quality report
    Then the GC content histogram should contain an entry matching the expected GC percentage

  Scenario: FastQQualityReport detects high duplication level
    Given I have a set of 100 identical FASTQ reads
    When I compute the FastQ quality report
    Then the duplication level estimate should be greater than 0.5

  Scenario: FastQQualityReport computes per-base sequence composition
    Given I have a FASTQ read with known base sequence ACGT
    When I compute the FastQ quality report
    Then the composition at cycle 0 should be 100 percent A
    And the composition at cycle 1 should be 100 percent C

  Scenario: FastQQualityReport serialises to JSON
    Given I have a set of FASTQ reads
    When I compute the FastQ quality report
    Then the report should serialise to non-empty JSON

  # QC-2: Alignment summary statistics
  Scenario: AlignmentStatsCalculator computes total and mapped read counts
    Given I have a list of alignments where some are mapped and some are unmapped
    When I compute alignment statistics
    Then the total reads count should equal the number of alignments
    And the mapped reads count should equal the number of mapped alignments
    And the unmapped reads count should equal the number of unmapped alignments

  Scenario: AlignmentStatsCalculator computes properly paired read count
    Given I have a list of alignments with properly paired flag set on some
    When I compute alignment statistics
    Then the properly paired read count should match the expected count

  Scenario: AlignmentStatsCalculator computes duplicate read count
    Given I have a list of alignments where some are marked as duplicates
    When I compute alignment statistics
    Then the duplicate read count should equal the number of duplicate-flagged reads

  # QC-3: Coverage depth report
  Scenario: CoverageCalculator computes mean depth from alignments
    Given I have alignments that uniformly cover a reference of 1000 bases at 10x depth
    When I compute coverage statistics for the reference
    Then the mean depth should be approximately 10

  Scenario: CoverageCalculator respects target BED intervals
    Given I have alignments covering an entire reference
    And I have a BED interval covering only a subset of the reference
    When I compute coverage statistics restricted to the target BED
    Then the summary statistics should only consider positions within the target

  # QC-4: Panel QC report
  Scenario: PanelQcReport marks coverage as PASS when above threshold
    Given I have alignments that achieve a mean coverage above the threshold
    When I generate a panel QC report with default thresholds
    Then the mean coverage status should be Pass

  Scenario: PanelQcReport marks coverage as FAIL when below warn threshold
    Given I have alignments that achieve a mean coverage below the warn threshold
    When I generate a panel QC report with default thresholds
    Then the mean coverage status should be Fail

  Scenario: PanelQcReport serialises to JSON with all fields
    Given I have valid alignment and FASTQ data for a panel run
    When I generate a panel QC report and serialise it to JSON
    Then the JSON should contain MeanCoverage and MappingRate fields
