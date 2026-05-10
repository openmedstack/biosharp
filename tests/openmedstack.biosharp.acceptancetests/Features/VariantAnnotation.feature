Feature: Variant Annotation
  As a bioinformatics developer
  I want to annotate variants with clinical and functional information
  So that clinical reports contain the required interpretation data

  # ANN-1: GTF-based transcript model loading
  Scenario: VariantAnnotationEngine loads transcripts from a GTF file
    Given I have a GTF file with exon and CDS records for a gene
    When I load transcripts from the GTF file into the annotation engine
    Then the engine should have at least one transcript loaded
    And annotation of a variant in a coding region should produce a consequence

  # ANN-2: ClinVar and dbSNP annotation
  Scenario: ClinVarAnnotator finds pathogenic classification for known variant
    Given I have a ClinVar VCF with a Pathogenic entry for a specific variant
    When I annotate that variant with the ClinVar annotator
    Then the clinical significance should be Pathogenic

  Scenario: ClinVarAnnotator returns empty classification for absent variant
    Given I have a ClinVar VCF without a particular variant
    When I annotate that variant with the ClinVar annotator
    Then the clinical significance should be empty or null

  Scenario: DbSnpAnnotator assigns rsID to a known variant
    Given I have a dbSNP VCF with an rsID entry for a specific variant
    When I annotate that variant with the DbSnp annotator
    Then the rsID annotation should be populated

  # ANN-3: Pathogenicity score integration
  Scenario: PathogenicityAnnotator annotates missense variant with SIFT and PolyPhen scores
    Given I have a dbNSFP-format lookup stream with SIFT and PolyPhen data for a missense variant
    When I annotate that missense variant with the pathogenicity annotator
    Then the SIFT score should be populated in the annotation

  Scenario: PathogenicityAnnotator returns missing indicator for absent entry
    Given I have a dbNSFP-format lookup stream without a particular variant
    When I annotate that variant with the pathogenicity annotator
    Then the SIFT score should indicate missing value

  # ANN-4: Splice site prediction
  Scenario: VariantAnnotationEngine classifies splice site disrupting variant
    Given I have a transcript with a canonical donor splice site
    When I annotate a variant at the donor site that disrupts the PWM score
    Then the consequence should be SpliceSiteDisruptive
