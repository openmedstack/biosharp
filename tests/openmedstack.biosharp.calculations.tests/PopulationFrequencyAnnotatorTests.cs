using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Calculations.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class PopulationFrequencyAnnotatorTests
{
    /// <summary>
    /// Builds a minimal population VCF in memory with known AF values.
    /// </summary>
    private static string BuildPopulationVcf(params (string chrom, int pos, string refB, string alt, double af, double afPopmax, int an, int ac)[] entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("##fileformat=VCFv4.2");
        sb.AppendLine("##INFO=<ID=AF,Number=A,Type=Float,Description=\"Allele Frequency\">");
        sb.AppendLine("##INFO=<ID=AF_popmax,Number=A,Type=Float,Description=\"Maximum AF across populations\">");
        sb.AppendLine("##INFO=<ID=AN,Number=1,Type=Integer,Description=\"Total Allele Number\">");
        sb.AppendLine("##INFO=<ID=AC,Number=A,Type=Integer,Description=\"Allele Count\">");
        sb.AppendLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");
        foreach (var (chrom, pos, refB, alt, af, afPopmax, an, ac) in entries)
        {
            sb.AppendLine(
                $"{chrom}\t{pos}\t.\t{refB}\t{alt}\t.\tPASS\tAF={af:G};AF_popmax={afPopmax:G};AN={an};AC={ac}");
        }

        return sb.ToString();
    }

    private static LocalVariantResult Variant(string chrom, int pos, string refB, string alt)
    {
        return new LocalVariantResult
        {
            Chromosome = chrom,
            Position = pos,
            Reference = refB,
            Alternate = alt,
            QuantitativeQuality = 50,
            Depth = 20
        };
    }

    [Fact]
    public async Task Annotate_MatchingVariant_GetsFrequencyFields()
    {
        var vcf = BuildPopulationVcf(("chr1", 100, "A", "T", 0.01, 0.02, 10000, 100));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var variants = new[] { Variant("chr1", 100, "A", "T") };
        var results = await PopulationFrequencyAnnotator.Annotate(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(0.01, results[0].GnomadAf, precision: 6);
        Assert.Equal(0.02, results[0].GnomadAfPopmax, precision: 6);
        Assert.Equal(10000, results[0].GnomadAn);
        Assert.Equal(100, results[0].GnomadAc);
    }

    [Fact]
    public async Task Annotate_NonMatchingVariant_GetsZeroFrequency()
    {
        var vcf = BuildPopulationVcf(("chr1", 200, "G", "A", 0.05, 0.10, 5000, 250));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var variants = new[] { Variant("chr1", 100, "A", "T") }; // not in pop VCF
        var results = await PopulationFrequencyAnnotator.Annotate(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(0.0, results[0].GnomadAf);
        Assert.Equal(0.0, results[0].GnomadAfPopmax);
        Assert.Equal(0, results[0].GnomadAn);
        Assert.Equal(0, results[0].GnomadAc);
    }

    [Fact]
    public async Task Annotate_MultipleVariants_EachAnnotatedIndependently()
    {
        var vcf = BuildPopulationVcf(
            ("chr1", 100, "A", "T", 0.01, 0.02, 10000, 100),
            ("chr1", 200, "G", "C", 0.05, 0.08, 8000, 400)
        );
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var variants = new[]
        {
            Variant("chr1", 100, "A", "T"),
            Variant("chr1", 200, "G", "C"),
            Variant("chr1", 300, "T", "A") // absent
        };
        var results = await PopulationFrequencyAnnotator.Annotate(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Equal(0.01, results[0].GnomadAf, precision: 6);
        Assert.Equal(0.05, results[1].GnomadAf, precision: 6);
        Assert.Equal(0.0, results[2].GnomadAf);
    }

    [Fact]
    public async Task Annotate_ExacSchema_ParsesAfField()
    {
        // ExAC uses AF= like gnomAD v2 but without AF_popmax — annotator should handle gracefully
        var sb = new StringBuilder();
        sb.AppendLine("##fileformat=VCFv4.2");
        sb.AppendLine("##INFO=<ID=AF,Number=A,Type=Float,Description=\"Allele Frequency\">");
        sb.AppendLine("##INFO=<ID=AN,Number=1,Type=Integer,Description=\"Total Allele Number\">");
        sb.AppendLine("##INFO=<ID=AC,Number=A,Type=Integer,Description=\"Allele Count\">");
        sb.AppendLine("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");
        sb.AppendLine("chr1\t100\t.\tA\tT\t.\tPASS\tAF=0.003;AN=120000;AC=360");

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        var variants = new[] { Variant("chr1", 100, "A", "T") };
        var results = await PopulationFrequencyAnnotator.Annotate(variants, ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(0.003, results[0].GnomadAf, precision: 6);
        Assert.Equal(0, results[0].GnomadAn == 120000 ? 0 : 1); // AN should be 120000
    }

    [Fact]
    public async Task Annotate_ResultHasSourceVariantReference()
    {
        var vcf = BuildPopulationVcf(("chr1", 100, "A", "T", 0.01, 0.02, 10000, 100));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(vcf));

        var original = Variant("chr1", 100, "A", "T");
        var results = await PopulationFrequencyAnnotator.Annotate([original], ms, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Same(original, results[0].Variant);
    }
}