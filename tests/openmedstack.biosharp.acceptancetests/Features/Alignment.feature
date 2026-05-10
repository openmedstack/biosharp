Feature: Alignment and Read Processing
  As a bioinformatics developer
  I want to process aligned reads according to clinical best practices
  So that variant calling produces accurate and reliable results

  # ALN-1: Duplicate marking
  Scenario: DuplicateMarker marks lower-quality read in a duplicate pair
    Given I have two alignments with identical position, orientation and CIGAR but different mapping quality
    When I run duplicate marking
    Then the lower-quality alignment should have the duplicate flag set
    And the higher-quality alignment should not have the duplicate flag set

  Scenario: DuplicateMarker reports duplication rate metrics
    Given I have a list of alignments where some are duplicates
    When I run duplicate marking
    Then the metrics should report the total read count
    And the metrics should report the number of duplicate reads
    And the duplicate rate should be greater than zero

  # ALN-2: MAPQ scoring
  Scenario: Reads with unique mapping receive high MAPQ
    Given I have a reference sequence and a read that maps uniquely
    When I compute the mapping quality using the pipeline
    Then the alignment should have a mapping quality greater than 0

  # ALN-3: BQSR
  Scenario: BaseQualityRecalibrator adjusts quality scores based on covariates
    Given I have aligned reads and a reference sequence with no known variant sites
    When I collect covariates and apply recalibration
    Then the recalibrated reads should have quality strings of the same length as the originals
    And the recalibration table should contain covariate data

  # ALN-4: Adapter trimming
  Scenario: AdapterTrimmer detects and removes full-length adapter
    Given I have a read with a full Nextera adapter sequence appended
    When I trim the read with the Nextera preset
    Then the trimmed read should not contain the adapter sequence
    And the trim statistics should show one read trimmed

  Scenario: AdapterTrimmer discards reads shorter than minimum length after trimming
    Given I have a read that is shorter than the minimum length after adapter removal
    When I trim the read with minimum length of 20
    Then the result should be null indicating the read was discarded
    And the trim statistics should show one read discarded

  Scenario: AdapterTrimmer returns read unchanged when no adapter is present
    Given I have a read with no adapter contamination
    When I trim the read with the TruSeq preset
    Then the read should be returned unchanged with no bases removed

  # ALN-5: Paired-end insert size estimation
  Scenario: InsertSizeEstimator computes statistics from properly paired reads
    Given I have a set of paired-end alignments with known template lengths
    When I compute the insert size distribution
    Then the report should contain a non-zero mean insert size
    And the report should contain median, standard deviation, and percentile values
    And the histogram should have entries for the expected insert size bins

  Scenario: InsertSizeEstimator flags discordant pairs
    Given I have paired-end alignments where some mates map to different references
    When I compute the insert size distribution
    Then the report should count the discordant pairs separately
