using OpenMedStack.BioSharp.Model;
using OpenMedStack.BioSharp.Model.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Alignment;

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeBruijn;
using Io;

/// <summary>
/// Writes complete VCF files from variant results.
/// VCF 4.2 compliant with support for structural variant INFO fields
/// and diploid genotype FORMAT fields (GT:GQ:DP).
/// </summary>
public static class VcfWriter
{
    private const char Q = '"';

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
    /// Writes a complete VCF file to the specified path, optionally BGZF-compressed.
    /// When <paramref name="compress"/> is <c>true</c>, the output is written as BGZF-compressed
    /// VCF (`.vcf.gz`) compatible with tabix and bcftools.
    /// </summary>
    /// <param name="path">Destination file path. Should end in <c>.vcf.gz</c> when compressed.</param>
    /// <param name="variants">Variant records to write.</param>
    /// <param name="chromosome">Chromosome / contig name for the header.</param>
    /// <param name="chromLength">Optional chromosome length for the contig header line.</param>
    /// <param name="sampleNames">Optional sample names (adds FORMAT column).</param>
    /// <param name="genotypes">Optional per-variant genotype strings (parallel to <paramref name="variants"/>).</param>
    /// <param name="compress">
    /// When <c>true</c>, wraps the output in a <see cref="BgzfStream"/>.
    /// When <c>false</c> (default), writes plain text VCF.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task Write(
        string path,
        IReadOnlyList<LocalVariantResult> variants,
        string chromosome,
        long? chromLength = null,
        string[]? sampleNames = null,
        string?[]?[]? genotypes = null,
        bool compress = false,
        CancellationToken cancellationToken = default)
    {
        await using var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65536,
            useAsync: true);

        if (compress)
        {
            await using var bgzf = new BgzfStream(fileStream, CompressionLevel.Optimal, leaveOpen: false);
            await Write(bgzf, variants, chromosome, chromLength, sampleNames, genotypes, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await Write(fileStream, variants, chromosome, chromLength, sampleNames, genotypes, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes a complete VCF file from variant results.
    /// VCF 4.2 compliant with support for structural variant INFO fields.
    /// When sampleNames and genotypes are provided, FORMAT fields (GT:GQ:DP) are included.
    /// </summary>
    public static async Task Write(
        Stream stream,
        IReadOnlyList<LocalVariantResult> variants,
        string chromosome,
        long? chromLength = null,
        string[]? sampleNames = null,
        string?[]?[]? genotypes = null,
        CancellationToken cancellationToken = default)
    {
        var sampleList = sampleNames ?? [];
        var hasSamples = sampleList.Length > 0;

        var headerLines = new List<string>
        {
            "##fileformat=VCFv4.2",
            "##source=OpenMedStack.BioSharp VariantCallingPipeline"
        };
        headerLines.AddRange(StandardInfoDefinitions.Select(FormatInfoHeader));

        if (hasSamples)
        {
            headerLines.AddRange(StandardFormatDefinitions.Select(FormatFormatHeader));
        }

        headerLines.AddRange(StandardFilterDefinitions.Select(FormatFilterHeader));

        if (chromLength != null)
        {
            headerLines.Add(BuildContigHeader(chromosome, chromLength.Value));
        }

        headerLines.Add(hasSamples
            ? $"#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO\tFORMAT\t{string.Join("\t", sampleList)}"
            : "#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

        foreach (var line in headerLines)
        {
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"{line}\n"), cancellationToken);
        }

        for (var vi = 0; vi < variants.Count; vi++)
        {
            var variant = variants[vi];
            var geno = genotypes?.Length > vi ? genotypes[vi] : null;
            await WriteVariant(stream, variant, chromosome, sampleNames??[], geno, cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static string BuildContigHeader(string chrom, long chromLength)
    {
        var sb = new StringBuilder();
        sb.Append("##contig=<ID=");
        sb.Append(chrom);
        sb.Append(",length=");
        sb.Append(chromLength);
        sb.Append('>');
        return sb.ToString();
    }

    private static string FormatInfoHeader(VcfInfoDefinition infoDef)
    {
        var sb = new StringBuilder();
        sb.Append("##INFO=<ID=");
        sb.Append(infoDef.Id);
        sb.Append(",Number=");
        sb.Append(infoDef.Number);
        sb.Append(",Type=");
        sb.Append(infoDef.Type);
        sb.Append(",Description=");
        sb.Append(Q);
        sb.Append(infoDef.Description);
        sb.Append(Q);
        if (!string.IsNullOrWhiteSpace(infoDef.Source))
        {
            sb.Append(",Source=");
            sb.Append(Q);
            sb.Append(infoDef.Source);
            sb.Append(Q);
        }

        if (!string.IsNullOrWhiteSpace(infoDef.Version))
        {
            sb.Append(",Version=");
            sb.Append(Q);
            sb.Append(infoDef.Version);
            sb.Append(Q);
        }

        sb.Append('>');
        return sb.ToString();
    }

    private static string FormatFormatHeader(VcfFormatDefinition fmtDef)
    {
        var sb = new StringBuilder();
        sb.Append("##FORMAT=<ID=");
        sb.Append(fmtDef.Id);
        sb.Append(",Number=");
        sb.Append(fmtDef.Number);
        sb.Append(",Type=");
        sb.Append(fmtDef.Type);
        sb.Append(",Description=");
        sb.Append(Q);
        sb.Append(fmtDef.Description);
        sb.Append(Q);
        sb.Append('>');
        return sb.ToString();
    }

    private static string FormatFilterHeader(VcfFilterDefinition filterDef)
    {
        var sb = new StringBuilder();
        sb.Append("##FILTER=<ID=");
        sb.Append(filterDef.Id);
        sb.Append(",Description=");
        sb.Append(Q);
        sb.Append(filterDef.Description);
        sb.Append(Q);
        sb.Append('>');
        return sb.ToString();
    }

    private static async Task WriteVariant(
        Stream stream,
        LocalVariantResult variant,
        string chromosome,
        string[] samples,
        string?[]? genotype,
        CancellationToken cancellationToken)
    {
        var filter = variant.IsPass ? "PASS" : "LOW_QUAL";

        // Multi-alt allele support: comma-separated ALT per VCF spec
        var altAlleles = variant.AllAlternateAlleles;

        var infoParts = new List<string> { $"DP={variant.Depth}" };

        // Report multi-alt allele count when applicable
        if (altAlleles.Count > 1)
        {
            infoParts.Add($"AC={altAlleles.Count - 1}"); // alternate allele count
        }

        if (variant is { IsStructuralVariant: true, SvType: not null })
        {
            infoParts.Add($"SVTYPE={variant.SvType.Value.ToString().ToUpperInvariant()}");

            if (variant.EndPosition > 0)
            {
                infoParts.Add($"END={variant.EndPosition}");
            }

            infoParts.Add("CIPOS=0,2");
            if (variant.EndPosition > 0)
            {
                infoParts.Add("CIEND=0,2");
            }

            if (variant.AssemblyInfo != null)
            {
                infoParts.Add($"ALT_PATHS={variant.AssemblyInfo.AltPathCount}");
            }

            if (variant.SvType == SvType.Inversion)
            {
                infoParts.Add("HOMLEN=0");
                infoParts.Add("HOMSEQ=.");
            }
        }

        var columns = new List<string>(samples.Length > 0 ? 10 + samples.Length : 8)
        {
            chromosome,
            variant.Position.ToString(),
            ".",
            variant.Reference,
            string.Join(",", altAlleles),
            variant.QuantitativeQuality.ToString(),
            filter,
            string.Join(";", infoParts)
        };

        // Genotype FORMAT fields
        if (samples.Length > 0)
        {
            columns.Add("GT:GQ:DP");
            if (genotype != null)
            {
                foreach (var g in genotype)
                {
                    columns.Add(g ?? "./.:./.:.");
                }
            }
            else
            {
                var genoStr = "./.:./.:.";
                if (variant.Genotype != null)
                {
                    var g = variant.Genotype;
                    genoStr = $"{g.ToVcfGenotype()}:{g.GQ}:{g.AltCoverage + g.RefCoverage}";
                }

                columns.Add(genoStr);
            }
        }

        await stream.WriteAsync(Encoding.UTF8.GetBytes($"{string.Join('\t', columns)}\n"), cancellationToken);
    }

    /// <param name="result">The variant result to annotate.</param>
    extension(LocalVariantResult result)
    {
        /// <summary>
        /// Extends a LocalVariantResult with structural variant metadata for VCF writing.
        /// </summary>
        public LocalVariantResult WithStructuralVariant(
            SvType svType,
            int? endPosition = null,
            AssemblyInfo? assemblyInfo = null,
            int coverage = 1,
            int altPathCount = 1)
        {
            result.IsStructuralVariant = true;
            result.SvType = svType;
            result.EndPosition = endPosition ??
                (svType is SvType.Inversion or SvType.Translocation ? result.Position : 0);
            result.AssemblyInfo = assemblyInfo ?? new AssemblyInfo(coverage, result.Alternate.Length, altPathCount);

            return result;
        }

        /// <summary>
        /// Extends a LocalVariantResult with genotype calling from coverages.
        /// </summary>
        /// <param name="refCoverage">Coverage for reference allele.</param>
        /// <param name="altCoverage">Coverage for alternate allele.</param>
        /// <returns>The enriched variant result with Genotype populated.</returns>
        public LocalVariantResult WithGenotype(
            int refCoverage,
            int altCoverage)
        {
            result.Genotype = GenotypeCaller.Call(refCoverage, altCoverage);
            return result;
        }
    }
}
