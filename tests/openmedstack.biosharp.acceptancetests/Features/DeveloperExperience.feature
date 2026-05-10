Feature: Developer Experience and API Surface
  As a bioinformatics developer
  I want a well-structured API with DI support, progress reporting and synthetic test data
  So that BioSharp integrates easily into production pipelines

  # DEV-1: Microsoft.Extensions.DependencyInjection registration
  Scenario: AddBioSharpIo registers all IO services in the DI container
    Given I have a service collection
    When I call AddBioSharpIo on the service collection
    Then I should be able to resolve a FastQReader from the container
    And I should be able to resolve a GffReader from the container
    And I should be able to resolve a BedReader from the container

  Scenario: AddBioSharpCalculations registers calculation services
    Given I have a service collection
    When I call AddBioSharpCalculations on the service collection
    Then I should be able to resolve a VariantCallingPipeline from the container

  # DEV-4: Structured progress reporting and cancellation
  Scenario: VariantCallingPipeline reports progress during FASTQ loading
    Given I have a synthetic FASTQ file with multiple reads
    When I load reads with a progress callback
    Then the progress callback should have been called at least once
    And each progress report should contain a reads processed count

  Scenario: VariantCallingPipeline respects cancellation token
    Given I have a synthetic FASTQ file with reads
    When I start loading reads and cancel immediately
    Then the loading should stop without producing a complete result

  # DEV-5: Synthetic test data generator
  Scenario: TestDataGenerator generates a reference of the specified length
    Given I create a TestDataGenerator with a fixed seed
    When I generate a reference of length 1000
    Then the reference should have exactly 1000 characters
    And the reference should contain only valid DNA bases A, C, G and T

  Scenario: TestDataGenerator generates reads in valid FASTQ format
    Given I create a TestDataGenerator with a fixed seed
    And I have a synthetic reference of length 500
    When I simulate reads at depth 5 with read length 50
    Then each read should have sequence and quality strings of the same length
    And each quality character should be a printable ASCII character above 32

  Scenario: TestDataGenerator injects variants at known positions
    Given I create a TestDataGenerator with a fixed seed
    And I have a synthetic reference
    When I inject a variant at position 10 changing the base
    Then the mutated reference should differ from the original at position 10

  Scenario: TestDataGenerator generates a complete variant set
    Given I create a TestDataGenerator with a fixed seed
    When I generate a variant set with 5 variants in a 200-base reference
    Then the returned variant list should contain exactly 5 entries
    And each synthetic variant should have a reference allele different from the alternate allele
