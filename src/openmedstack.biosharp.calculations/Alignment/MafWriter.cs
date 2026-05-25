using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeBruijn;

/// <summary>
/// Writes Mutation Annotation Format (MAF) files from variant results.
/// Conforms to GDC/TCGA MAF specification v2.4.
/// </summary>
public static class MafWriter
{
    private static readonly string[] RequiredColumns =
    [
        "Hugo_Symbol",
        "Entrez_Gene_Id",
        "Center",
        "NCBI_Build",
        "Chromosome",
        "Start_Position",
        "End_Position",
        "Strand",
        "Variant_Classification",
        "Variant_Type",
        "Reference_Allele",
        "Tumor_Seq_Allele1",
        "Tumor_Seq_Allele2",
        "dbSNP_RS",
        "dbSNP_Val_Status",
        "Tumor_Sample_Barcode",
        "Matched_Norm_Sample_Barcode",
        "Match_Norm_Seq_Allele1",
        "Match_Norm_Seq_Allele2",
        "Tumor_Validation_Allele1",
        "Tumor_Validation_Allele2",
        "Match_Norm_Validation_Allele1",
        "Match_Norm_Validation_Allele2",
        "Verification_Status",
        "Validation_Status",
        "Mutation_Status",
        "Sequencing_Phase",
        "Sequence_Source",
        "Validation_Method",
        "Score",
        "BAM_File",
        "Sequencer",
        "Tumor_Sample_UUID",
        "Matched_Norm_Sample_UUID",
        "HGVSc",
        "HGVSp",
        "HGVSp_Short",
        "Transcript_ID",
        "Exon_Number",
        "t_depth",
        "t_ref_count",
        "t_alt_count",
        "n_depth",
        "n_ref_count",
        "n_alt_count"
    ];

    /// <summary>
    /// Writes a MAF file from a collection of <see cref="LocalVariantResult"/> records.
    /// </summary>
    /// <param name="stream">Output stream.</param>
    /// <param name="variants">Variant records to write.</param>
    /// <param name="tumorSampleBarcode">Tumor sample identifier (maps to Tumor_Sample_Barcode).</param>
    /// <param name="normalSampleBarcode">Matched normal sample identifier. Defaults to "NORMAL".</param>
    /// <param name="ncbiBuild">Reference genome build, e.g. "GRCh38". Defaults to "GRCh38".</param>
    /// <param name="center">Sequencing center name. Defaults to ".".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task Write(
        Stream stream,
        IEnumerable<LocalVariantResult> variants,
        string tumorSampleBarcode,
        string normalSampleBarcode = "NORMAL",
        string ncbiBuild = "GRCh38",
        string center = ".",
        CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("# GDC MAF v2.4".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(string.Join("\t", RequiredColumns).AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = FormatVariant(
                variant,
                tumorSampleBarcode,
                normalSampleBarcode,
                ncbiBuild,
                center);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a MAF file from a collection of <see cref="SomaticVariant"/> records.
    /// </summary>
    /// <param name="stream">Output stream.</param>
    /// <param name="variants">Somatic variant records to write.</param>
    /// <param name="chromosome">Chromosome name (all variants are assumed to be on this chromosome).</param>
    /// <param name="tumorSampleBarcode">Tumor sample identifier.</param>
    /// <param name="normalSampleBarcode">Matched normal sample identifier. Defaults to "NORMAL".</param>
    /// <param name="ncbiBuild">Reference genome build. Defaults to "GRCh38".</param>
    /// <param name="center">Sequencing center name. Defaults to ".".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task Write(
        Stream stream,
        IEnumerable<SomaticVariant> variants,
        string chromosome,
        string tumorSampleBarcode,
        string normalSampleBarcode = "NORMAL",
        string ncbiBuild = "GRCh38",
        string center = ".",
        CancellationToken cancellationToken = default)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("# GDC MAF v2.4".AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteLineAsync(string.Join("\t", RequiredColumns).AsMemory(), cancellationToken)
            .ConfigureAwait(false);

        foreach (var variant in variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = FormatSomaticVariant(
                variant,
                chromosome,
                tumorSampleBarcode,
                normalSampleBarcode,
                ncbiBuild,
                center);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatVariant(
        LocalVariantResult v,
        string tumorBarcode,
        string normalBarcode,
        string ncbiBuild,
        string center)
    {
        var (variantClass, variantType) = ClassifyVariant(v.Reference, v.Alternate);
        var (startPos, endPos) = GetPositions(v.Position, v.Reference, v.Alternate);

        var sb = new StringBuilder();
        AppendCol(sb, ".");                         // Hugo_Symbol
        AppendCol(sb, "0");                         // Entrez_Gene_Id
        AppendCol(sb, center);                      // Center
        AppendCol(sb, ncbiBuild);                   // NCBI_Build
        AppendCol(sb, v.Chromosome);                // Chromosome
        AppendCol(sb, startPos.ToString());         // Start_Position
        AppendCol(sb, endPos.ToString());           // End_Position
        AppendCol(sb, "+");                         // Strand
        AppendCol(sb, variantClass);                // Variant_Classification
        AppendCol(sb, variantType);                 // Variant_Type
        AppendCol(sb, v.Reference);                 // Reference_Allele
        AppendCol(sb, v.Reference);                 // Tumor_Seq_Allele1 (ref allele copy)
        AppendCol(sb, v.Alternate);                 // Tumor_Seq_Allele2
        AppendCol(sb, ".");                         // dbSNP_RS
        AppendCol(sb, ".");                         // dbSNP_Val_Status
        AppendCol(sb, tumorBarcode);                // Tumor_Sample_Barcode
        AppendCol(sb, normalBarcode);               // Matched_Norm_Sample_Barcode
        AppendCol(sb, v.Reference);                 // Match_Norm_Seq_Allele1
        AppendCol(sb, v.Reference);                 // Match_Norm_Seq_Allele2
        AppendCol(sb, ".");                         // Tumor_Validation_Allele1
        AppendCol(sb, ".");                         // Tumor_Validation_Allele2
        AppendCol(sb, ".");                         // Match_Norm_Validation_Allele1
        AppendCol(sb, ".");                         // Match_Norm_Validation_Allele2
        AppendCol(sb, "Unknown");                   // Verification_Status
        AppendCol(sb, "Unknown");                   // Validation_Status
        AppendCol(sb, "Somatic");                   // Mutation_Status
        AppendCol(sb, ".");                         // Sequencing_Phase
        AppendCol(sb, "Capture");                   // Sequence_Source
        AppendCol(sb, ".");                         // Validation_Method
        AppendCol(sb, ".");                         // Score
        AppendCol(sb, ".");                         // BAM_File
        AppendCol(sb, "Illumina");                  // Sequencer
        AppendCol(sb, ".");                         // Tumor_Sample_UUID
        AppendCol(sb, ".");                         // Matched_Norm_Sample_UUID
        AppendCol(sb, ".");                         // HGVSc
        AppendCol(sb, ".");                         // HGVSp
        AppendCol(sb, ".");                         // HGVSp_Short
        AppendCol(sb, ".");                         // Transcript_ID
        AppendCol(sb, ".");                         // Exon_Number
        AppendCol(sb, v.Depth.ToString());          // t_depth
        AppendCol(sb, ".");                         // t_ref_count
        AppendCol(sb, ".");                         // t_alt_count
        AppendCol(sb, ".");                         // n_depth
        AppendCol(sb, ".");                         // n_ref_count
        AppendLastCol(sb, ".");                     // n_alt_count
        return sb.ToString();
    }

    private static string FormatSomaticVariant(
        SomaticVariant v,
        string chromosome,
        string tumorBarcode,
        string normalBarcode,
        string ncbiBuild,
        string center)
    {
        var (variantClass, variantType) = ClassifyVariant(v.Reference, v.Alternate);
        var (startPos, endPos) = GetPositions(v.Position, v.Reference, v.Alternate);

        var sb = new StringBuilder();
        AppendCol(sb, ".");                                              // Hugo_Symbol
        AppendCol(sb, "0");                                              // Entrez_Gene_Id
        AppendCol(sb, center);                                           // Center
        AppendCol(sb, ncbiBuild);                                        // NCBI_Build
        AppendCol(sb, chromosome);                                       // Chromosome
        AppendCol(sb, startPos.ToString());                              // Start_Position
        AppendCol(sb, endPos.ToString());                                // End_Position
        AppendCol(sb, "+");                                              // Strand
        AppendCol(sb, variantClass);                                     // Variant_Classification
        AppendCol(sb, variantType);                                      // Variant_Type
        AppendCol(sb, v.Reference);                                      // Reference_Allele
        AppendCol(sb, v.Reference);                                      // Tumor_Seq_Allele1
        AppendCol(sb, v.Alternate);                                      // Tumor_Seq_Allele2
        AppendCol(sb, ".");                                              // dbSNP_RS
        AppendCol(sb, ".");                                              // dbSNP_Val_Status
        AppendCol(sb, tumorBarcode);                                     // Tumor_Sample_Barcode
        AppendCol(sb, normalBarcode);                                    // Matched_Norm_Sample_Barcode
        AppendCol(sb, v.Reference);                                      // Match_Norm_Seq_Allele1
        AppendCol(sb, v.Reference);                                      // Match_Norm_Seq_Allele2
        AppendCol(sb, ".");                                              // Tumor_Validation_Allele1
        AppendCol(sb, ".");                                              // Tumor_Validation_Allele2
        AppendCol(sb, ".");                                              // Match_Norm_Validation_Allele1
        AppendCol(sb, ".");                                              // Match_Norm_Validation_Allele2
        AppendCol(sb, "Unknown");                                        // Verification_Status
        AppendCol(sb, "Unknown");                                        // Validation_Status
        AppendCol(sb, "Somatic");                                        // Mutation_Status
        AppendCol(sb, ".");                                              // Sequencing_Phase
        AppendCol(sb, "Capture");                                        // Sequence_Source
        AppendCol(sb, ".");                                              // Validation_Method
        AppendCol(sb, ".");                                              // Score
        AppendCol(sb, ".");                                              // BAM_File
        AppendCol(sb, "Illumina");                                       // Sequencer
        AppendCol(sb, ".");                                              // Tumor_Sample_UUID
        AppendCol(sb, ".");                                              // Matched_Norm_Sample_UUID
        AppendCol(sb, ".");                                              // HGVSc
        AppendCol(sb, ".");                                              // HGVSp
        AppendCol(sb, ".");                                              // HGVSp_Short
        AppendCol(sb, ".");                                              // Transcript_ID
        AppendCol(sb, ".");                                              // Exon_Number
        AppendCol(sb, v.TumorCoverage.ToString());                       // t_depth
        AppendCol(sb, ".");                                              // t_ref_count
        AppendCol(sb, v.TumorCoverage.ToString());                       // t_alt_count
        AppendCol(sb, v.NormalCoverage.ToString());                      // n_depth
        AppendCol(sb, v.NormalCoverage.ToString());                      // n_ref_count
        AppendLastCol(sb, "0");                                          // n_alt_count
        return sb.ToString();
    }

    private static void AppendCol(StringBuilder sb, string value)
    {
        sb.Append(value).Append('\t');
    }

    private static void AppendLastCol(StringBuilder sb, string value)
    {
        sb.Append(value);
    }

    public static (string variantClass, string variantType) ClassifyVariant(string reference, string alternate)
    {
        // Insertions
        if (reference == "-" || (alternate.Length > reference.Length && reference.Length == 1))
        {
            return ("In_Frame_Ins", "INS");
        }

        // Deletions
        if (alternate == "-" || (reference.Length > alternate.Length && alternate.Length == 1))
        {
            return ("In_Frame_Del", "DEL");
        }

        // SNP
        if (reference.Length == 1 && alternate.Length == 1)
        {
            return ("Missense_Mutation", "SNP");
        }

        // MNP (multi-nucleotide polymorphism)
        if (reference.Length == alternate.Length && reference.Length > 1)
        {
            return ("Missense_Mutation", "DNP");
        }

        // Complex indel
        return ("Frame_Shift_Del", "DEL");
    }

    public static (int start, int end) GetPositions(int vcfPosition, string reference, string alternate)
    {
        // VCF uses 1-based positions. MAF also uses 1-based positions.
        // For SNPs: start == end == vcfPosition
        // For insertions: start == end == vcfPosition (MAF convention for insertions after vcfPosition)
        // For deletions: start = vcfPosition + 1, end = vcfPosition + deletion_length
        if (alternate == "-" || reference.Length > alternate.Length)
        {
            var delLength = reference.Length - Math.Max(0, alternate.Length - 1);
            return (vcfPosition + 1, vcfPosition + delLength);
        }

        if (reference == "-" || alternate.Length > reference.Length)
        {
            return (vcfPosition, vcfPosition + 1);
        }

        return (vcfPosition, vcfPosition + reference.Length - 1);
    }
}
