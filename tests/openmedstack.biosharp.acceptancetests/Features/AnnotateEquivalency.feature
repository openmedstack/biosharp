@Equivalency
Feature: Variant annotation equivalency between BioSharp and SnpEff

  # ─────────────────────────────────────────────────────────────────────────────
  # Annotation equivalency: BioSharp library vs SnpEff
  #
  # These tests verify that BioSharp's variant annotation engine produces results
  # compatible with SnpEff — the reference-standard Java annotation tool.
  # Both tools are given an identical synthetic GTF + reference FASTA + VCF;
  # the results are compared within a tolerance to account for algorithm differences.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresSnpEff
  Scenario Outline: BioSharp annotated variant count is within tolerance of SnpEff
    Given a synthetic reference with <TranscriptCount> transcripts and <VariantCount> planted variants
    When BioSharp annotates the variants using the synthetic GTF transcripts
    And SnpEff annotates the same variants using a custom database built from the synthetic GTF
    Then the BioSharp annotated variant count should be within <TolerancePct> percent of the SnpEff annotated count

    Examples:
      | TranscriptCount | VariantCount | TolerancePct |
      | 3               | 20           | 30           |
      | 5               | 50           | 30           |

  @RequiresSnpEff
  Scenario Outline: BioSharp coding consequence fraction is within tolerance of SnpEff
    Given a synthetic reference with <TranscriptCount> transcripts and <VariantCount> planted variants
    When BioSharp annotates the variants using the synthetic GTF transcripts
    And SnpEff annotates the same variants using a custom database built from the synthetic GTF
    Then the BioSharp coding consequence fraction should be within <TolerancePct> percent of the SnpEff coding fraction

    Examples:
      | TranscriptCount | VariantCount | TolerancePct |
      | 3               | 30           | 30           |
      | 5               | 60           | 30           |
