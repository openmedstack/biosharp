Feature: Clinical and Regulatory Compliance
  As a clinical bioinformatics developer
  I want audit logging, sample tracking, reference validation and clinical reports
  So that the pipeline meets ISO 15189 and regulatory requirements

  # CLIN-1: Audit logging and pipeline provenance
  Scenario: ProvenanceWriter creates provenance JSON alongside VCF output
    Given I have a pipeline provenance record with version, checksums and timestamps
    When I write the provenance record for a VCF output file
    Then a provenance JSON file should exist alongside the VCF file
    And the provenance JSON should contain the pipeline version field
    And the provenance JSON should contain start and end timestamp fields

  # CLIN-2: Sample tracking and run metadata
  Scenario: SampleSheetReader parses Illumina sample sheet sections
    Given I have an Illumina sample sheet CSV with Header, Reads, Settings and Data sections
    When I parse the sample sheet
    Then the Header section should contain the expected key-value pairs
    And the Reads section should contain the expected read lengths
    And the Data section should contain one sample row per data entry

  Scenario: SampleSheetReader parses multi-sample sheets
    Given I have an Illumina sample sheet with three sample rows in the Data section
    When I parse the sample sheet
    Then the samples list should contain three entries

  # CLIN-3: Reference genome checksum validation
  Scenario: ReferenceValidator computes checksums for a reference file
    Given I have a small FASTA reference file with known content
    When I compute checksums for the reference file
    Then the MD5 checksum should be a 32-character hex string
    And the SHA256 checksum should be a 64-character hex string

  Scenario: ReferenceValidator throws exception when checksum does not match
    Given I have a reference file
    And I have an incorrect expected MD5 checksum
    When I validate the reference against the wrong checksum
    Then a ReferenceValidationException should be thrown

  Scenario: ReferenceValidator passes validation when checksum matches
    Given I have a small FASTA reference file with known content
    When I compute the MD5 and then validate against it
    Then validation should succeed without throwing an exception

  # CLIN-4: Clinical variant report generation
  Scenario: ClinicalReportWriter produces valid JSON report
    Given I have a set of annotated variants and clinical report metadata
    When I generate the JSON clinical report
    Then the JSON should contain a SampleName field
    And the JSON should contain a Variants array
    And variants in the JSON should be sorted by chromosome then position

  Scenario: ClinicalReportWriter produces HTML report that contains variant table
    Given I have a set of annotated variants and clinical report metadata
    When I generate the HTML clinical report
    Then the HTML should contain a table element
    And the HTML should contain the sample name in the report header
