Feature: Variant Calling
  As a bioinformatics developer
  I want to call variants using standard methods
  So that clinical results are accurate and interoperable

  # VC-1: gVCF output
  Scenario: GvcfWriter emits reference block records for uncalled positions
    Given I have a reference region of known length and a set of variants
    When I write gVCF output covering the region
    Then the output should contain NON_REF symbolic allele records for reference blocks
    And the output should contain the END INFO field in reference block records
    And variant positions should be emitted as standard variant records

  # VC-2: VCF normalisation
  Scenario: VcfNormalizer left-aligns an indel
    Given I have a deletion variant that is right-aligned in a homopolymer region
    When I normalize the variant with the reference sequence
    Then the normalized variant should be at the leftmost position

  Scenario: VcfNormalizer decomposes multi-allelic record into biallelic records
    Given I have a multi-allelic variant with two alternate alleles
    When I normalize the variant
    Then I should receive two separate biallelic variant records

  Scenario: VcfNormalizer is idempotent
    Given I have an already-normalized variant
    When I normalize it twice
    Then both outputs should have the same position and alleles

  # VC-3: CNV calling
  Scenario: CopyNumberCaller detects a large deletion in depth profile
    Given I have a depth profile with a 50-window region at 50 percent of baseline depth
    When I run the copy number caller with a deletion threshold of 0.6
    Then a DEL structural variant should be reported spanning the low-depth region

  Scenario: CopyNumberCaller detects a duplication in depth profile
    Given I have a depth profile with a 20-window region at 200 percent of baseline depth
    When I run the copy number caller with a duplication threshold of 1.5
    Then a DUP structural variant should be reported spanning the high-depth region

  # VC-4: Haplotype phasing
  Scenario: HaplotypePhasingEngine phases two SNPs supported by the same read
    Given I have two variants at adjacent positions and a read spanning both positions supporting both alt alleles
    When I run haplotype phasing
    Then both variants should be phased with a shared phase set
    And the genotype strings should use the pipe separator

  Scenario: HaplotypePhasingEngine leaves variants unphased when no bridging read exists
    Given I have two variants at distant positions with no read spanning both
    When I run haplotype phasing
    Then both variants should be unphased with slash separator in genotype

  # VC-5: Population frequency annotation
  Scenario: PopulationFrequencyAnnotator annotates variant with AF from database
    Given I have a population VCF database with a known variant at frequency 0.05
    And I have a variant matching that entry
    When I annotate the variant with population frequencies
    Then the annotation should have an AF value of 0.05

  Scenario: PopulationFrequencyAnnotator returns zero frequency for absent variants
    Given I have a population VCF database without a particular variant
    And I have that variant
    When I annotate the variant with population frequencies
    Then the annotation should have an AF value of 0.0
