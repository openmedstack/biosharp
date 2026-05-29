@Equivalency
Feature: BCL Conversion Equivalency
  As a bioinformatics developer
  I want BioSharp BCL conversion to produce the same FASTQ output as established external tools
  So that converted sequencing data is independently validated and clinically trustworthy

  # ─────────────────────────────────────────────────────────────────────────────
  # BCL conversion: BioSharp vs bcl-convert
  #
  # bcl-convert is Illumina's current production BCL converter.  Equivalency here
  # proves that BioSharp's IlluminaDataReader produces the same number of FASTQ
  # reads and bases as the reference implementation on synthetic run-folder data.
  #
  # The comparison uses a deterministic synthetic Illumina run folder (same
  # structure as BclHeadToHeadBenchmarks):
  #   • 1 lane, 1 tile (1101)
  #   • N cycles of template read
  #   • All clusters pass filter (filter byte = 0x01)
  #   • Read count = ClusterCount × 1 lane × 1 tile
  #   • Total bases = ReadCount × CycleCount
  #
  # Tests skip automatically on any OS / architecture where bcl-convert is absent.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresBclConvert
  Scenario Outline: BioSharp BCL conversion read count and bases match bcl-convert
    Given a synthetic Illumina BCL run directory with <CycleCount> cycles and <ClusterCount> clusters per tile
    When BioSharp converts the BCL run directory to FASTQ
    And bcl-convert converts the same BCL run directory to FASTQ
    Then the BioSharp BCL read count should exactly match the external converter read count
    And the BioSharp BCL total bases should exactly match the external converter total bases

    Examples:
      | CycleCount | ClusterCount |
      | 75         | 1024         |
      | 150        | 2048         |

  # ─────────────────────────────────────────────────────────────────────────────
  # BCL conversion: BioSharp vs bcl2fastq
  #
  # bcl2fastq is Illumina's legacy BCL converter (v2.x) still in widespread use.
  # Equivalency with bcl2fastq validates backward-compatibility of BioSharp's
  # BCL reading and FASTQ serialization.
  #
  # Tests skip automatically on any OS / architecture where bcl2fastq is absent.
  # ─────────────────────────────────────────────────────────────────────────────

  @RequiresBcl2Fastq
  Scenario Outline: BioSharp BCL conversion read count and bases match bcl2fastq
    Given a synthetic Illumina BCL run directory with <CycleCount> cycles and <ClusterCount> clusters per tile
    When BioSharp converts the BCL run directory to FASTQ
    And bcl2fastq converts the same BCL run directory to FASTQ
    Then the BioSharp BCL read count should exactly match the external converter read count
    And the BioSharp BCL total bases should exactly match the external converter total bases

    Examples:
      | CycleCount | ClusterCount |
      | 75         | 1024         |
      | 150        | 2048         |
