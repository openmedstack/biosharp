Feature: I/O and Format Support
  As a bioinformatics developer
  I want to read and write genomic data in standard formats
  So that BioSharp can interoperate with existing clinical pipelines

  # IO-1: CRAM read/write support
  Scenario: CRAM writer produces output and reader streams alignments back
    Given I have a set of alignment records
    When I write those records to a CRAM file with a reference path
    And I read alignments back from the CRAM file
    Then the number of records read back should match the number written

  # IO-2: BCF read/write support
  Scenario: BCF round-trip preserves all variant fields
    Given I have a set of VCF variants with known fields
    When I write those variants to a BCF file
    And I read variants back from the BCF file
    Then all variant chromosomes, positions, references and alternates should be preserved

  # IO-3: Tabix index writing for VCF
  Scenario: TabixIndexWriter produces a .tbi index file
    Given I have a BGZF-compressed VCF file with sorted records
    When I write a tabix index for the VCF file
    Then a .tbi index file should be created alongside the VCF file

  # IO-4: GFF3/GTF annotation file reader
  Scenario: GffReader parses all nine standard GFF3 columns
    Given I have a GFF3 format annotation string with gene, transcript and exon features
    When I read GFF records from the annotation stream
    Then all records should have seqname, source, feature, start, end, strand and attributes populated

  Scenario: GffReader parses GTF format attributes
    Given I have a GTF format annotation string with gene_id and transcript_id attributes
    When I read GFF records from the GTF stream
    Then the attributes dictionary should contain gene_id and transcript_id

  # IO-5: BED interval file reader
  Scenario: BedReader reads 3-column BED format
    Given I have a 3-column BED format string with two intervals
    When I read BED intervals from the stream
    Then I should get 2 intervals with correct chromosome, start and end

  Scenario: BedReader reads 6-column BED format with strand
    Given I have a 6-column BED format string with strand information
    When I read BED intervals from the stream
    Then each interval should have a strand value

  Scenario: BedReader skips comment lines and track headers
    Given I have a BED format string with comment lines and a track header
    When I read BED intervals from the stream
    Then only the data records should be returned

  # IO-6: Paired-end FASTQ reading
  Scenario: FastQReader reads paired-end reads from two files
    Given I have two FASTQ files representing R1 and R2 of a paired-end library
    When I read paired reads using ReadPairedAsync
    Then I should receive matched read pairs with names that correspond

  Scenario: FastQReader reads interleaved paired FASTQ
    Given I have an interleaved FASTQ file with alternating R1 and R2 reads
    When I read paired reads using ReadInterleavedAsync
    Then I should receive the correct number of read pairs

  # IO-7: BGZF-compressed VCF writing
  Scenario: VcfWriter produces BGZF-compressed output when compress flag is true
    Given I have a set of local variant results
    When I write those variants to a VCF file with compression enabled
    Then the output file should be smaller than the uncompressed equivalent
    And the output file should be readable as a valid VCF

  # IO-8: MAF writer
  Scenario: MafWriter produces all required MAF columns
    Given I have a set of somatic variant results
    When I write those variants to a MAF file
    Then the MAF file should contain the Hugo_Symbol header column
    And the MAF file should contain the Chromosome header column
    And the MAF file should contain the Variant_Type header column
    And the MAF file should contain data rows for each variant
