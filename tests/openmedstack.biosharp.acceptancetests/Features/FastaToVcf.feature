Feature: Tutorial FASTA to VCF
  As a bioinformatics developer
  I want BioSharp to reproduce a frozen tutorial-derived FASTA-to-VCF slice
  So that the end-to-end output stays stable against a golden rerun of the public tutorial inputs

  Scenario: Tutorial-derived E. coli slice matches the frozen BioSharp golden output
    Given I load the acceptance fixtures
    When I validate the reference excerpt
    And I run the BioSharp slice pipeline to a BGZF-compressed VCF
    Then the FASTQ slice should contain the expected concordant read pairs
    And the slice metrics and VCF should match the frozen golden summary
    And the tabix query for the golden window should return the expected variants

  Scenario: Tutorial-derived E. coli slice can use freebayes-like read acceptance
    Given I load the acceptance fixtures
    When I validate the reference excerpt
    And I run the BioSharp slice pipeline with freebayes-like read acceptance
    Then the FASTQ slice should contain the expected concordant read pairs
    And the freebayes-like acceptance should suppress the sparse slice calls
