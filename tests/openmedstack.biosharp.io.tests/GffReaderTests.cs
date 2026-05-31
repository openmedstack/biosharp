using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Io.Gff;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Io.Tests;

/// <summary>
/// Tests for IO-4: GFF3/GTF annotation file reader.
/// </summary>
public class GffReaderTests
{
    [Fact]
    public async Task ParsesGff3Record()
    {
        const string content =
            "##gff-version 3\n" +
            "chr1\tEnsembl\tgene\t1000\t2000\t.\t+\t.\tID=gene1;Name=BRCA1\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var records = await GffReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(records);
        var r = records[0];
        Assert.Equal("chr1", r.Seqname);
        Assert.Equal("Ensembl", r.Source);
        Assert.Equal("gene", r.Feature);
        Assert.Equal(1000, r.Start);
        Assert.Equal(2000, r.End);
        Assert.Equal('+', r.Strand);
        Assert.Equal("gene1", r.Attributes["ID"]);
        Assert.Equal("BRCA1", r.Attributes["Name"]);
        Assert.Equal(GffDialect.Gff3, r.Dialect);
    }

    [Fact]
    public async Task ParsesGtfRecord()
    {
        const string content =
            "chr1\tEnsembl\tgene\t1000\t2000\t.\t+\t.\tgene_id \"ENSG00001\"; gene_name \"TP53\";\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var records = await GffReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(records);
        var r = records[0];
        Assert.Equal("ENSG00001", r.Attributes["gene_id"]);
        Assert.Equal("TP53", r.Attributes["gene_name"]);
        Assert.Equal(GffDialect.Gtf, r.Dialect);
    }

    [Fact]
    public async Task ParsesAllNineColumns()
    {
        const string content =
            "##gff-version 3\n" +
            "chr1\tsrc\tCDS\t100\t200\t1.5\t-\t2\tID=cds1\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var r = await GffReader.Read(stream, TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1.5f, r.Score!.Value, precision: 5);
        Assert.Equal('-', r.Strand);
        Assert.Equal(2, r.Phase);
    }

    [Fact]
    public async Task ParsesForwardAndReverseStrand()
    {
        const string content =
            "##gff-version 3\n" +
            "chr1\tsrc\texon\t100\t200\t.\t+\t.\tID=e1\n" +
            "chr1\tsrc\texon\t300\t400\t.\t-\t.\tID=e2\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var records = await GffReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, records.Count);
        Assert.Equal('+', records[0].Strand);
        Assert.Equal('-', records[1].Strand);
    }

    [Fact]
    public async Task ParsesMultiValueGff3Attributes()
    {
        const string content =
            "##gff-version 3\n" +
            "chr1\tsrc\texon\t100\t200\t.\t+\t.\tParent=mRNA1,mRNA2\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var r = await GffReader.Read(stream, TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.True(r.MultiValueAttributes.ContainsKey("Parent"));
        Assert.Equal(2, r.MultiValueAttributes["Parent"].Count);
        Assert.Contains("mRNA1", r.MultiValueAttributes["Parent"]);
        Assert.Contains("mRNA2", r.MultiValueAttributes["Parent"]);
    }

    [Fact]
    public async Task SkipsCommentLines()
    {
        const string content =
            "##gff-version 3\n" +
            "# This is a comment\n" +
            "chr1\tsrc\tgene\t1\t100\t.\t.\t.\tID=g1\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var records = await GffReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(records);
    }

    [Fact]
    public async Task ParsesAllFeatureTypes()
    {
        const string content =
            "##gff-version 3\n" +
            "chr1\tsrc\tgene\t1\t10000\t.\t+\t.\tID=g1\n" +
            "chr1\tsrc\ttranscript\t1\t10000\t.\t+\t.\tID=t1;Parent=g1\n" +
            "chr1\tsrc\texon\t100\t200\t.\t+\t.\tID=e1;Parent=t1\n" +
            "chr1\tsrc\tCDS\t150\t200\t.\t+\t0\tID=cds1;Parent=t1\n" +
            "chr1\tsrc\tUTR\t100\t149\t.\t+\t.\tID=utr1;Parent=t1\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));

        var records = await GffReader.Read(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        var features = records.Select(r => r.Feature).ToArray();
        Assert.Contains("gene", features);
        Assert.Contains("transcript", features);
        Assert.Contains("exon", features);
        Assert.Contains("CDS", features);
        Assert.Contains("UTR", features);
    }
}