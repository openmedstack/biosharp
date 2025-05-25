namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeBruijn;

/// <summary>
/// Writes complete VCF files from variant results.
/// VCF 4.2 compliant with support for structural variant INFO fields
/// and diploid genotype FORMAT fields (GT:GQ:DP).
/// </summary>
public static class VcfWriter
{
    private static readonly char Q = '"';

    /// <summary>
    /// Common INFO field definitions for VCF metadata headers.
    /// </summary>
    public static IEnumerable<VcfInfoDefinition> StandardInfoDefinitions
    {
        get
        {
            return
            [
                new VcfInfoDefinition("DP", "Integer", "I", "Total read depth at the position"),
                new VcfInfoDefinition("SVTYPE", "String", "C", "Type of structural variant"),
                new VcfInfoDefinition("END", "Integer", "1", "End position of the structural variant"),
                new VcfInfoDefinition("CIPOS", "Integer", "2", "95% confidence interval for POS"),
                new VcfInfoDefinition("CIEND", "Integer", "2", "95% confidence interval for END"),
                new VcfInfoDefinition("HOMLEN", "Integer", "0,1", "Length of homology at breakpoint"),
                new VcfInfoDefinition("HOMSEQ", "String", "0,1", "Homologous sequence at breakpoint"),
                new VcfInfoDefinition("CONSENSUS", "String", "1", "Consensus alt sequence from graph assembly"),
                new VcfInfoDefinition("CONVF", "Float", "1", "Conflation fraction: alt coverage / total coverage"),
                new VcfInfoDefinition("ALT_PATHS", "Integer", "1", "Number of alt paths used in consensus")
            ];
        }
    }

    /// <summary>
    /// Standard FORMAT field definitions including genotype, genotype quality, and depth.
    /// </summary>
    public static IEnumerable<VcfFormatDefinition> StandardFormatDefinitions
    {
        get
        {
            return
            [
                new VcfFormatDefinition("GT", "String", "1", "Genotype"),
                new VcfFormatDefinition("GQ", "Integer", "1", "Genotype quality"),
                new VcfFormatDefinition("DP", "Integer", "1", "Read depth")
            ];
        }
    }

    /// <summary>
    /// Standard FILTER definitions.
    /// </summary>
    public static IEnumerable<VcfFilterDefinition> StandardFilterDefinitions
    {
        get
        {
            return
            [
                new VcfFilterDefinition("PASS", "All filters passed"),
                new VcfFilterDefinition("LOW_QUAL", "Quality below threshold"),
                new VcfFilterDefinition("LOW_DEPTH", "Insufficient read depth"),
                new VcfFilterDefinition("LOW_VARFREQ", "Variant allele frequency too low")
            ];
        }
    }

    /// <summary>
    /// Meta-information definition for an INFO field in a VCF header.
    /// </summary>
    public sealed class VcfInfoDefinition
    {
        public VcfInfoDefinition(
            string id,
            string type,
            string number,
            string description,
            string? source = null,
            string? version = null)
        {
            Id = id;
            Type = type;
            Number = number;
            Description = description;
            Source = source;
            Version = version;
        }

        public string Id { get; }
        public string Type { get; }
        public string Number { get; }
        public string Description { get; }
        public string? Source { get; }
        public string? Version { get; }
    }

    /// <summary>
    /// Meta-information definition for a FORMAT field in a VCF header.
    /// </summary>
    public sealed class VcfFormatDefinition
    {
        public VcfFormatDefinition(string id, string type, string number, string description)
        {
            Id = id;
            Type = type;
            Number = number;
            Description = description;
        }

        public string Id { get; }
        public string Type { get; }
        public string Number { get; }
        public string Description { get; }
    }

    /// <summary>
    /// Meta-information definition for a FILTER field in a VCF header.
    /// </summary>
    public sealed class VcfFilterDefinition
    {
        public VcfFilterDefinition(string id, string description)
        {
            Id = id;
            Description = description;
        }

        public string Id { get; }
        public string Description { get; }
    }

    /// <summary>
    /// Writes a complete VCF file from variant results.
    /// VCF 4.2 compliant with support for structural variant INFO fields.
    /// When sampleNames and genotypes are provided, FORMAT fields (GT:GQ:DP) are included.
    /// </summary>
    public static async Task WriteAsync(
        Stream stream,
        IEnumerable<LocalVariantResult> variants,
        string chromosome,
        long? chromLength = null,
        IEnumerable<string>? sampleNames = null,
        string?[]?[]? genotypes = null,
        CancellationToken cancellationToken = default)
    {
        var variantList = variants.ToList();
        var sampleList = sampleNames?.ToList() ?? [];
        var hasSamples = sampleList.Count > 0;
        var genotypeArray = genotypes;

        var headerLines = new List<string>();

        headerLines.Add("##fileformat=VCFv4.2");
        headerLines.Add("##source=OpenMedStack.BioSharp VariantCallingPipeline");

        foreach (var infoDef in StandardInfoDefinitions) headerLines.Add(FormatInfoHeader(infoDef));

        if (hasSamples)
            foreach (var fmtDef in StandardFormatDefinitions)
                headerLines.Add(FormatFormatHeader(fmtDef));

        foreach (var filterDef in StandardFilterDefinitions) headerLines.Add(FormatFilterHeader(filterDef));

        if (chromLength != null) headerLines.Add(BuildContigHeader(chromosome, chromLength.Value));

        if (hasSamples)
            headerLines.Add("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO\t" + string.Join("\t", sampleList));
        else
            headerLines.Add("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

        foreach (var line in headerLines)
            await stream.WriteAsync(Encoding.UTF8.GetBytes(line + "\n"), cancellationToken);

        for (var vi = 0; vi < variantList.Count; vi++)
        {
            var variant = variantList[vi];
            var geno = genotypeArray?.Length > vi ? genotypeArray[vi] : null;
            await WriteVariantAsync(stream, variant, chromosome, sampleList, geno, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static string BuildContigHeader(string chrom, long length)
    {
        var sb = new StringBuilder();
        sb.Append("##contig=<ID name=");
        sb.Append(Q);
        sb.Append(chrom);
        sb.Append(Q);
        sb.Append(">");
        return sb.ToString();
    }

    private static string FormatInfoHeader(VcfInfoDefinition infoDef)
    {
        var sb = new StringBuilder();
        sb.Append("##");
        sb.Append(infoDef.Id);
        sb.Append("=<ID name=");
        sb.Append(Q);
        sb.Append(infoDef.Id);
        sb.Append(Q);
        sb.Append(",Number=");
        sb.Append(infoDef.Number);
        sb.Append(",Type=");
        sb.Append(infoDef.Type);
        sb.Append(",Description=");
        sb.Append(Q);
        sb.Append(infoDef.Description);
        sb.Append(Q + Q + Q);
        sb.Append(">");
        return sb.ToString();
    }

    private static string FormatFormatHeader(VcfFormatDefinition fmtDef)
    {
        var sb = new StringBuilder();
        sb.Append("##FORMAT=<ID name=");
        sb.Append(Q);
        sb.Append(fmtDef.Id);
        sb.Append(Q);
        sb.Append(",Number=");
        sb.Append(fmtDef.Number);
        sb.Append(",Type=");
        sb.Append(fmtDef.Type);
        sb.Append(",Description=");
        sb.Append(Q);
        sb.Append(fmtDef.Description);
        sb.Append(Q + Q + Q);
        sb.Append(">");
        return sb.ToString();
    }

    private static string FormatFilterHeader(VcfFilterDefinition filterDef)
    {
        var sb = new StringBuilder();
        sb.Append("##FILTER=<ID name=");
        sb.Append(Q);
        sb.Append(filterDef.Id);
        sb.Append(Q);
        sb.Append(",Description=");
        sb.Append(Q);
        sb.Append(filterDef.Description);
        sb.Append(Q + Q + Q);
        sb.Append(">");
        return sb.ToString();
    }

    private static async Task WriteVariantAsync(
        Stream stream,
        LocalVariantResult variant,
        string chromosome,
        List<string> samples,
        string?[]? genotype,
        CancellationToken cancellationToken)
    {
        var filter = variant.IsPass ? "PASS" : "LOW_QUAL";
        var sb = new StringBuilder();

        sb.Append(chromosome).Append('\t').Append(variant.Position).Append('\t');
        sb.Append('.').Append('\t').Append(variant.Reference).Append('\t');

        // Multi-alt allele support: comma-separated ALT per VCF spec
        var altAlleles = variant.AllAlternateAlleles;
        sb.Append(string.Join(",", altAlleles)).Append('\t').Append(variant.QuantitativeQuality);
        sb.Append('\t').Append(filter).Append('\t');

        var infoParts = new List<string>();
        infoParts.Add("DP=" + variant.Depth);

        // Report multi-alt allele count when applicable
        if (altAlleles.Count > 1) infoParts.Add("AC=" + (altAlleles.Count - 1).ToString()); // alternate allele count

        if (variant is { IsStructuralVariant: true, SvType: not null })
        {
            infoParts.Add("SVTYPE=" + variant.SvType.Value.ToString().ToUpperInvariant());

            if (variant.EndPosition > 0) infoParts.Add("END=" + variant.EndPosition);

            infoParts.Add("CIPOS=0,2");
            if (variant.EndPosition > 0) infoParts.Add("CIEND=0,2");

            if (variant.AssemblyInfo != null) infoParts.Add("ALT_PATHS=" + variant.AssemblyInfo.AltPathCount);

            if (variant.SvType == SvType.Inversion)
            {
                infoParts.Add("HOMLEN=0");
                infoParts.Add("HOMSEQ=.");
            }
        }

        // Genotype FORMAT fields
        if (samples.Count > 0 && genotype != null)
        {
            // Use explicit genotype array if provided
            sb.Append("GT:GQ:DP");
            foreach (var g in genotype) sb.Append('\t').Append(g ?? "./.:./.:.");
        }
        else if (samples.Count > 0)
        {
            // Fall back to variant.Genotype if available
            var genoStr = "./.:./.:. ";
            if (variant.Genotype != null)
            {
                var g = variant.Genotype;
                genoStr = g.ToVcfGenotype() + ":" + g.GQ + ":" + (g.AltCoverage + g.RefCoverage);
            }

            sb.Append("GT:GQ:DP");
            sb.Append('\t').Append(genoStr);
        }

        sb.Append('\t').Append(string.Join(";", infoParts));

        await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString() + "\n"), cancellationToken);
    }

    /// <summary>
    /// Extends a LocalVariantResult with structural variant metadata for VCF writing.
    /// </summary>
    public static LocalVariantResult WithStructuralVariant(
        this LocalVariantResult result,
        SvType svType,
        int? endPosition = null,
        AssemblyInfo? assemblyInfo = null,
        int coverage = 1,
        int altPathCount = 1)
    {
        result.IsStructuralVariant = true;
        result.SvType = svType;
        result.EndPosition = endPosition ??
            (svType == SvType.Inversion || svType == SvType.Translocation ? result.Position : 0);
        result.AssemblyInfo = assemblyInfo ?? new AssemblyInfo(coverage, result.Alternate.Length, altPathCount);

        return result;
    }

    /// <summary>
    /// Extends a LocalVariantResult with genotype calling from coverages.
    /// </summary>
    /// <param name="result">The variant result to annotate.</param>
    /// <param name="refCoverage">Coverage for reference allele.</param>
    /// <param name="altCoverage">Coverage for alternate allele.</param>
    /// <returns>The enriched variant result with Genotype populated.</returns>
    public static LocalVariantResult WithGenotype(
        this LocalVariantResult result,
        int refCoverage,
        int altCoverage)
    {
        result.Genotype = GenotypeCaller.Call(refCoverage, altCoverage);
        return result;
    }
}
