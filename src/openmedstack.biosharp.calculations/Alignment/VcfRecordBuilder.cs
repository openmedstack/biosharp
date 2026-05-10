namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System.Linq;
using DeBruijn;

/// <summary>
/// Builds VCF 4.2-formatted lines from variant results.
/// Supports standard variant records and structural variant records per VCF 4.2 spec.
/// </summary>
public static class VcfRecordBuilder
{
    /// <summary>
    /// Build a single VCF record line from a LocalVariantResult.
    /// Format: CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO
    /// SV fields (SVTYPE, END, CIPOS, CIEND) included when IsStructuralVariant is true.
    /// </summary>
    public static string Build(LocalVariantResult variant, string chrom)
    {
        var filter = variant.IsPass ? "PASS" : "LOW_QUAL";
        var info = $"DP={variant.Depth}";

        // SV fields as per VCF 4.2 spec
        if (variant is { IsStructuralVariant: true, SvType: not null })
        {
            info += $";SVTYPE={variant.SvType.Value}";

            if (variant.EndPosition > 0)
            {
                info += $";END={variant.EndPosition}";
            }

            info += ";CIPOS=0,2";
            info += ";CIEND=0,2";

            if (variant.AssemblyInfo != null)
            {
                info += $";ALT_PATHS={variant.AssemblyInfo.AltPathCount}";
            }

            if (variant.SvType == SvType.Inversion)
            {
                info += ";HOMLEN=0";
                info += ";HOMSEQ=.";
            }
        }

        return
            $"{chrom}\t{variant.Position}\t.\t{variant.Reference}\t{variant.Alternate}\t{variant.QuantitativeQuality}\t{filter}\t{info}";
    }

    private static string InfoDef(string id, string number, string type, string description)
    {
        var q = "\"";
        return $"##INFO=<ID name={id},Number={number},Type={type},Description={q}{description}{q}>";
    }

    private static string FilterDef(string id, string description)
    {
        var q = "\"";
        return $"##FILTER=<ID name={id},Description={q}{description}{q}>";
    }

    private static string FormatDef(string id, string number, string type, string description)
    {
        var q = "\"";
        return $"##FORMAT=<ID name={id},Number={number},Type={type},Description={q}{description}{q}>";
    }

    /// <summary>
    /// Build a VCF header with standard INFO/FILTER/FORMAT definitions.
    /// </summary>
    public static string BuildHeader()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("##fileformat=VCFv4.2");
        sb.AppendLine("##source=OpenMedStack.BioSharp VariantCallingPipeline");
        sb.AppendLine(InfoDef("DP", "1", "Integer", "Total read depth at the position"));
        sb.AppendLine(InfoDef("SVTYPE", "1", "String", "Type of structural variant"));
        sb.AppendLine(InfoDef("END", "1", "Integer", "End position of the structural variant"));
        sb.AppendLine(InfoDef("CIPOS", "2", "Integer", "95% confidence interval for POS"));
        sb.AppendLine(InfoDef("CIEND", "2", "Integer", "95% confidence interval for END"));
        sb.AppendLine(InfoDef("HOMLEN", "1", "Integer", "Length of homology at breakpoint"));
        sb.AppendLine(InfoDef("HOMSEQ", "1", "String", "Homologous sequence at breakpoint"));
        sb.AppendLine(InfoDef("ALT_PATHS", "1", "Integer", "Number of alt paths used in consensus"));
        sb.AppendLine(FilterDef("PASS", "All filters passed"));
        sb.AppendLine(FilterDef("LOW_QUAL", "Quality below threshold"));
        sb.AppendLine(FormatDef("GT", "1", "String", "Genotype"));
        sb.AppendLine(FormatDef("GQ", "1", "Integer", "Genotype quality"));
        sb.AppendLine(FormatDef("DP", "1", "Integer", "Read depth"));
        sb.AppendLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");
        return sb.ToString();
    }

    /// <summary>
    /// Build a batch of VCF records from multiple variant results.
    /// </summary>
    public static string BuildBatch(LocalVariantResult[] variants, string chrom, long? chromLength = null)
    {
        var sb = new System.Text.StringBuilder();

        if (chromLength.HasValue)
        {
            sb.AppendLine($"##contig=<ID name=\"{chrom}\">");
        }

        sb.AppendLine(BuildHeader());

        if (variants is { Length: > 0 })
        {
            foreach (var variant in variants.OrderBy(v => v.Position))
            {
                variant.Chromosome = chrom;
                sb.AppendLine(Build(variant, chrom));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Build a VCF line with GT (genotype) field in the FORMAT/INFO.
    /// </summary>
    public static string BuildWithGenotype(LocalVariantResult variant, string chrom, int genotype)
    {
        var filter = variant.IsPass ? "PASS" : "LOW_QUAL";
        var info = $"DP={variant.Depth};GT={genotype}";

        if (variant is { IsStructuralVariant: true, SvType: not null })
        {
            info += $";SVTYPE={variant.SvType.Value}";
            if (variant.EndPosition > 0)
            {
                info += $";END={variant.EndPosition}";
            }

            info += ";CIPOS=0,2;CIEND=0,2";
            if (variant.AssemblyInfo != null)
            {
                info += $";ALT_PATHS={variant.AssemblyInfo.AltPathCount}";
            }

            if (variant.SvType == SvType.Inversion)
            {
                info += ";HOMLEN=0;HOMSEQ=.";
            }
        }

        return
            $"{chrom}\t{variant.Position}\t.\t{variant.Reference}\t{variant.Alternate}\t{variant.QuantitativeQuality}\t{filter}\t{info}";
    }

    /// <summary>
    /// Build a VCF line with GT:GQ:DP FORMAT fields from a typed Genotype object.
    /// Prefer this overload over BuildWithGenotype(int) when a Genotype is available.
    /// </summary>
    /// <param name="variant">The variant to write.</param>
    /// <param name="chrom">Chromosome name.</param>
    /// <param name="genotype">Typed Genotype object with Type, GQ, and coverage data.</param>
    public static string BuildWithGenotype(LocalVariantResult variant, string chrom, Genotype genotype)
    {
        var filter = variant.IsPass ? "PASS" : "LOW_QUAL";
        var info = $"DP={variant.Depth};GT={genotype.ToVcfGenotype()};GQ={genotype.GQ}";

        if (variant is { IsStructuralVariant: true, SvType: not null })
        {
            info += $";SVTYPE={variant.SvType.Value}";
            if (variant.EndPosition > 0)
            {
                info += $";END={variant.EndPosition}";
            }

            info += ";CIPOS=0,2;CIEND=0,2";
            if (variant.AssemblyInfo != null)
            {
                info += $";ALT_PATHS={variant.AssemblyInfo.AltPathCount}";
            }

            if (variant.SvType == SvType.Inversion)
            {
                info += ";HOMLEN=0;HOMSEQ=.";
            }
        }

        return
            $"{chrom}\t{variant.Position}\t.\t{variant.Reference}\t{variant.Alternate}\t{variant.QuantitativeQuality}\t{filter}\t{info}";
    }
}
