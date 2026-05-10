namespace OpenMedStack.BioSharp.Io.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cram;
using Microsoft.Extensions.Logging.Abstractions;
using Model;
using Model.Vcf;
using OpenMedStack.BioSharp.Io.FastQ;
using Sam;
using Vcf;
using Xunit;

/// <summary>
/// Tests for IO-5: BED/BEDGraph interval file reader.
/// </summary>
public class BedReaderTests
{
    [Fact]
    public async Task Reads3ColumnBed()
    {
        const string content = "chr1\t100\t200\n" +
                               "chr2\t0\t500\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var reader = new BedReader();

        var intervals = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, intervals.Count);
        Assert.Equal("chr1", intervals[0].Chrom);
        Assert.Equal(100, intervals[0].Start);
        Assert.Equal(200, intervals[0].End);
        Assert.False(intervals[0].IsBedGraph);
    }

    [Fact]
    public async Task Reads6ColumnBed()
    {
        const string content = "chr1\t100\t200\tfeature1\t500\t+\n" +
                               "chrX\t300\t400\tfeature2\t0\t-\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var reader = new BedReader();

        var intervals = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, intervals.Count);
        Assert.Equal("feature1", intervals[0].Name);
        Assert.Equal(500f, intervals[0].Score);
        Assert.Equal('+', intervals[0].Strand);
        Assert.Equal('-', intervals[1].Strand);
    }

    [Fact]
    public async Task Reads12ColumnBed()
    {
        const string content = "chr1\t0\t1000\tblock_feat\t900\t+\t100\t900\t0\t3\t200,200,200,\t0,300,600,\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var reader = new BedReader();

        var intervals = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(intervals);
        Assert.Equal(3, intervals[0].BlockCount);
        Assert.Equal("200,200,200,", intervals[0].BlockSizes);
        Assert.Equal("0,300,600,", intervals[0].BlockStarts);
    }

    [Fact]
    public async Task ReadsBedGraph()
    {
        const string content = "chr1\t100\t200\t1.5\n" +
                               "chr1\t200\t300\t3.14\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var reader = new BedReader();

        var intervals = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, intervals.Count);
        Assert.True(intervals[0].IsBedGraph);
        Assert.Equal(1.5f, intervals[0].Value!.Value, precision: 5);
        Assert.Equal(3.14f, intervals[1].Value!.Value, precision: 4);
    }

    [Fact]
    public async Task SkipsCommentAndTrackLines()
    {
        const string content =
            "track name=\"test\"\n" +
            "# comment line\n" +
            "browser position chr1:1-1000\n" +
            "chr1\t0\t100\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var reader = new BedReader();

        var intervals = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(intervals);
        Assert.Equal("chr1", intervals[0].Chrom);
    }

    [Fact]
    public async Task ReturnsZeroBasedCoordinates()
    {
        const string content = "chr1\t0\t100\n";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var reader = new BedReader();

        var interval = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, interval.Start);
        Assert.Equal(100, interval.End);
    }

    [Fact]
    public async Task ReadsMultiChromosomeFile()
    {
        var lines = new StringBuilder();
        for (var i = 1; i <= 22; i++)
        {
            lines.AppendLine($"chr{i}\t{i * 100}\t{i * 200}");
        }

        var stream = new MemoryStream(Encoding.UTF8.GetBytes(lines.ToString()));
        var reader = new BedReader();

        var intervals = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(22, intervals.Count);
        Assert.Equal("chr1", intervals[0].Chrom);
        Assert.Equal("chr22", intervals[21].Chrom);
    }
}

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
        var reader = new GffReader();

        var records = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
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
        var reader = new GffReader();

        var records = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
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
        var reader = new GffReader();

        var r = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
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
        var reader = new GffReader();

        var records = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
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
        var reader = new GffReader();

        var r = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
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
        var reader = new GffReader();

        var records = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
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
        var reader = new GffReader();

        var records = await reader.ReadAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        var features = records.Select(r => r.Feature).ToArray();
        Assert.Contains("gene", features);
        Assert.Contains("transcript", features);
        Assert.Contains("exon", features);
        Assert.Contains("CDS", features);
        Assert.Contains("UTR", features);
    }
}

/// <summary>
/// Tests for IO-6: Paired-end FASTQ reading.
/// </summary>
public class PairedFastQTests
{
    private static byte[] BuildFastqBytes(IEnumerable<(string id, string seq, string qual)> reads)
    {
        var sb = new StringBuilder();
        foreach (var (id, seq, qual) in reads)
        {
            sb.AppendLine($"@{id}");
            sb.AppendLine(seq);
            sb.AppendLine("+");
            sb.AppendLine(qual);
        }

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string WriteTempFastq(IEnumerable<(string, string, string)> reads)
    {
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, BuildFastqBytes(reads));
        return path;
    }

    [Fact]
    public async Task ReadPairedAsyncYieldsMatchedPairs()
    {
        var r1 = WriteTempFastq([("read1/1", "ACGT", "IIII"), ("read2/1", "TTTT", "HHHH")]);
        var r2 = WriteTempFastq([("read1/2", "TGCA", "IIII"), ("read2/2", "AAAA", "HHHH")]);

        try
        {
            var reader = new FastQReader(NullLogger.Instance);
            var pairs = await reader
                .ReadPairedAsync(r1, r2, cancellationToken: TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, pairs.Count);
            Assert.Equal("ACGT", new string(pairs[0].R1.GetData().Span));
            Assert.Equal("TGCA", new string(pairs[0].R2.GetData().Span));
        }
        finally
        {
            File.Delete(r1);
            File.Delete(r2);
        }
    }

    [Fact]
    public async Task ReadPairedAsyncStripsSlashSuffix()
    {
        var r1 = WriteTempFastq([("SRR123/1", "ACGT", "IIII")]);
        var r2 = WriteTempFastq([("SRR123/2", "TGCA", "IIII")]);

        try
        {
            var reader = new FastQReader(NullLogger.Instance);
            var pairs = await reader
                .ReadPairedAsync(r1, r2, cancellationToken: TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(pairs);
        }
        finally
        {
            File.Delete(r1);
            File.Delete(r2);
        }
    }

    [Fact]
    public async Task ReadPairedAsyncStripsSpaceColonSuffix()
    {
        var r1 = WriteTempFastq([("SRR123 1:N:0:ATCG", "ACGT", "IIII")]);
        var r2 = WriteTempFastq([("SRR123 2:N:0:ATCG", "TGCA", "IIII")]);

        try
        {
            var reader = new FastQReader(NullLogger.Instance);
            var pairs = await reader
                .ReadPairedAsync(r1, r2, cancellationToken: TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Single(pairs);
        }
        finally
        {
            File.Delete(r1);
            File.Delete(r2);
        }
    }

    [Fact]
    public async Task ReadPairedAsyncThrowsOnNameMismatch()
    {
        var r1 = WriteTempFastq([("readA/1", "ACGT", "IIII")]);
        var r2 = WriteTempFastq([("readB/2", "TGCA", "IIII")]);

        try
        {
            var reader = new FastQReader(NullLogger.Instance);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await reader.ReadPairedAsync(r1, r2, strict: true,
                        cancellationToken: TestContext.Current.CancellationToken)
                    .ToListAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            File.Delete(r1);
            File.Delete(r2);
        }
    }

    [Fact]
    public async Task ReadInterleavedAsyncYieldsPairs()
    {
        var path = WriteTempFastq(
        [
            ("read1/1", "ACGT", "IIII"),
            ("read1/2", "TGCA", "IIII"),
            ("read2/1", "GGGG", "HHHH"),
            ("read2/2", "CCCC", "HHHH")
        ]);

        try
        {
            var reader = new FastQReader(NullLogger.Instance);
            var pairs = await reader
                .ReadInterleavedAsync(path, cancellationToken: TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.Equal(2, pairs.Count);
            Assert.Equal("ACGT", new string(pairs[0].R1.GetData().Span));
            Assert.Equal("TGCA", new string(pairs[0].R2.GetData().Span));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StripPairSuffixStripsSlash()
    {
        Assert.Equal("SRR123", FastQReader.StripPairSuffix("SRR123/1"));
        Assert.Equal("SRR123", FastQReader.StripPairSuffix("SRR123/2"));
    }

    [Fact]
    public void StripPairSuffixStripsSpace()
    {
        Assert.Equal("SRR123", FastQReader.StripPairSuffix("SRR123 1:N:0:ATCG"));
        Assert.Equal("SRR123", FastQReader.StripPairSuffix("SRR123 2:Y:0:GCTA"));
    }
}

/// <summary>
/// Tests for IO-7: BGZF-compressed VCF writing (VcfWriter in calculations).
/// </summary>
public class BcfReaderWriterTests
{
    private static VcfVariant MakeVariant(string chrom, int pos, string @ref, string alt, int qual = 30)
        => new()
        {
            Chromosome = chrom,
            Position = pos,
            MarkerIdentifiers = ".",
            Reference = @ref,
            Alternate = alt,
            ErrorProbabilities = [qual],
            FailedFilter = ["PASS"],
            AdditionalInformation = "."
        };

    [Fact]
    public async Task RoundTripSingleVariant()
    {
        var ms = new MemoryStream();
        await using var writer = new BcfWriter(ms, leaveOpen: true);

        var variant = MakeVariant("chr1", 1000, "A", "T");
        await writer.WriteHeaderAsync(["chr1"], cancellationToken: TestContext.Current.CancellationToken);
        await writer.WriteVariantAsync(variant, TestContext.Current.CancellationToken);
        await writer.DisposeAsync();

        ms.Position = 0;
        var bgzfRead = new BgzfStream(ms, CompressionMode.Decompress, leaveOpen: true);
        await using var reader = new BcfReader(bgzfRead, leaveOpen: false);
        var header = await reader.ReadHeaderAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(header);

        var variants = await reader.ReadVariantsAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(variants);
        Assert.Equal("chr1", variants[0].Chromosome);
        Assert.Equal(1000, variants[0].Position);
        Assert.Equal("A", variants[0].Reference);
        Assert.Equal("T", variants[0].Alternate);
    }

    [Fact]
    public async Task RoundTripMultipleVariants()
    {
        var variants = new[]
        {
            MakeVariant("chr1", 100, "A", "C"),
            MakeVariant("chr1", 200, "G", "T"),
            MakeVariant("chr1", 300, "ATCG", "A"),  // deletion
            MakeVariant("chr1", 400, "A", "ATCG"),  // insertion
        };

        var ms = new MemoryStream();
        await using var writer = new BcfWriter(ms, leaveOpen: true);
        await writer.WriteHeaderAsync(["chr1"], cancellationToken: TestContext.Current.CancellationToken);
        await writer.WriteVariantsAsync(variants, TestContext.Current.CancellationToken);
        await writer.DisposeAsync();

        ms.Position = 0;
        var bgzfRead = new BgzfStream(ms, CompressionMode.Decompress, leaveOpen: true);
        await using var reader = new BcfReader(bgzfRead, leaveOpen: false);
        await reader.ReadHeaderAsync(TestContext.Current.CancellationToken);
        var readBack = await reader.ReadVariantsAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(4, readBack.Count);
        Assert.Equal("C", readBack[0].Alternate);
        Assert.Equal("T", readBack[1].Alternate);
        Assert.Equal("A", readBack[2].Alternate);
        Assert.Equal("ATCG", readBack[3].Alternate);
    }

    [Fact]
    public async Task AllFieldsPreserved()
    {
        var variant = MakeVariant("chr7", 55_249_071, "ACT", "A", 50);

        var ms = new MemoryStream();
        await using var writer = new BcfWriter(ms, leaveOpen: true);
        await writer.WriteHeaderAsync(["chr7"], cancellationToken: TestContext.Current.CancellationToken);
        await writer.WriteVariantAsync(variant, TestContext.Current.CancellationToken);
        await writer.DisposeAsync();

        ms.Position = 0;
        var bgzfRead = new BgzfStream(ms, CompressionMode.Decompress, leaveOpen: true);
        await using var reader = new BcfReader(bgzfRead, leaveOpen: false);
        await reader.ReadHeaderAsync(TestContext.Current.CancellationToken);
        var readBack = await reader.ReadVariantsAsync(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal("chr7", readBack.Chromosome);
        Assert.Equal(55_249_071, readBack.Position);
        Assert.Equal("ACT", readBack.Reference);
        Assert.Equal("A", readBack.Alternate);
    }
}

/// <summary>
/// Tests for IO-3: Tabix index writing and region-based VCF queries.
/// </summary>
public class TabixTests
{
    private static async Task<string> CreateBgzfVcfAsync(IEnumerable<VcfVariant> variants)
    {
        var tempPath = Path.GetTempFileName() + ".vcf.gz";

        await using var file = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var bgzf = new BgzfStream(file, CompressionLevel.Optimal, leaveOpen: false);
        await using var writer = new StreamWriter(bgzf, Encoding.UTF8, leaveOpen: true);

        await writer.WriteLineAsync("##fileformat=VCFv4.2");
        await writer.WriteLineAsync("#CHROM\tPOS\tID\tREF\tALT\tQUAL\tFILTER\tINFO");

        foreach (var v in variants)
        {
            await writer.WriteLineAsync(
                $"{v.Chromosome}\t{v.Position}\t.\t{v.Reference}\t{v.Alternate}\t.\tPASS\t.");
        }

        await writer.FlushAsync();
        await bgzf.FlushAsync();

        return tempPath;
    }

    [Fact]
    public async Task CanWriteTabixIndex()
    {
        var variants = Enumerable.Range(1, 100)
            .Select(i => new VcfVariant
            {
                Chromosome = "chr1",
                Position = i * 1000,
                MarkerIdentifiers = ".",
                Reference = "A",
                Alternate = "T",
                ErrorProbabilities = [30],
                FailedFilter = ["PASS"],
                AdditionalInformation = "."
            });

        var vcfPath = await CreateBgzfVcfAsync(variants);
        var tbiPath = vcfPath + ".tbi";

        try
        {
            await TabixIndexWriter.WriteAsync(vcfPath, tbiPath, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(tbiPath));
            Assert.True(new FileInfo(tbiPath).Length > 0);
        }
        finally
        {
            File.Delete(vcfPath);
            File.Delete(tbiPath);
        }
    }

    [Fact]
    public async Task RegionQueryReturnsSubset()
    {
        var variants = Enumerable.Range(1, 200)
            .Select(i => new VcfVariant
            {
                Chromosome = "chr1",
                Position = i * 1000,
                MarkerIdentifiers = ".",
                Reference = "A",
                Alternate = "T",
                ErrorProbabilities = [30],
                FailedFilter = ["PASS"],
                AdditionalInformation = "."
            })
            .ToList();

        var vcfPath = await CreateBgzfVcfAsync(variants);
        var tbiPath = vcfPath + ".tbi";

        try
        {
            await TabixIndexWriter.WriteAsync(vcfPath, tbiPath, TestContext.Current.CancellationToken);

            var vcfReader = new VcfFileReader(new VcfMetaReader());
            var results = await vcfReader
                .ReadRegionAsync(vcfPath, "chr1", 50_000, 100_000, tbiPath,
                    TestContext.Current.CancellationToken)
                .ToListAsync(TestContext.Current.CancellationToken);

            // Positions 50000, 51000, ..., 100000 → 51 records
            Assert.All(results, v => Assert.InRange(v.Position, 50_000, 100_000));
            Assert.NotEmpty(results);
        }
        finally
        {
            File.Delete(vcfPath);
            File.Delete(tbiPath);
        }
    }

    [Fact]
    public void RegionToBinIsConsistent()
    {
        // 0-based region
        var bin = TabixIndexWriter.RegionToBin(0, 100_000);
        Assert.True(bin >= 0);

        var bin2 = TabixIndexWriter.RegionToBin(100_000, 200_000);
        Assert.True(bin2 >= 0);
    }
}

/// <summary>
/// Tests for IO-1: CRAM read/write support.
/// </summary>
public class CramRoundTripTests
{
    private static AlignmentSection MakeAlignment(string qname, string chrom, int pos, string seq, string qual)
        => new(
            qname: qname,
            flag: AlignmentSection.AlignmentFlag.None,
            rname: chrom,
            position: pos,
            mapq: 60,
            cigar: [],
            rnext: -1,
            pnext: 0,
            templatelength: 0,
            sequence: seq,
            quality: qual,
            tags: []);

    [Fact]
    public async Task RoundTripSingleAlignment()
    {
        const string seq = "ACGTACGTACGT";
        var quals = new string('I', seq.Length);
        var alignment = MakeAlignment("read1", "chr1", 1000, seq, quals);

        var ms = new MemoryStream();
        await using (var writer = new CramWriter(ms, "reference.fa", leaveOpen: true))
        {
            await writer.WriteHeaderAsync("@HD\tVN:1.6\n@SQ\tSN:chr1\tLN:248956422",
                TestContext.Current.CancellationToken);
            await writer.WriteAlignmentAsync(alignment, TestContext.Current.CancellationToken);
            await writer.FinalizeAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(ms.Length > 0);

        ms.Position = 0;
        await using var reader = new CramReader(ms, leaveOpen: true);
        var header = await reader.ReadFileHeaderAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(header);

        var records = await reader.ReadAlignmentsAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Single(records);
        Assert.Equal("read1", records[0].QName);
        Assert.Equal(seq, records[0].Sequence);
        Assert.Equal(quals, records[0].Quality);
    }

    [Fact]
    public async Task RoundTripMultipleAlignments()
    {
        var alignments = Enumerable.Range(1, 10)
            .Select(i => MakeAlignment(
                $"read{i}",
                "chr1",
                i * 100,
                "ACGT",
                "IIII"))
            .ToList();

        var ms = new MemoryStream();
        await using (var writer = new CramWriter(ms, "reference.fa", leaveOpen: true))
        {
            await writer.WriteHeaderAsync("@HD\tVN:1.6\n@SQ\tSN:chr1\tLN:248956422",
                TestContext.Current.CancellationToken);
            foreach (var a in alignments)
            {
                await writer.WriteAlignmentAsync(a, TestContext.Current.CancellationToken);
            }

            await writer.FinalizeAsync(TestContext.Current.CancellationToken);
        }

        ms.Position = 0;
        await using var reader = new CramReader(ms, leaveOpen: true);
        await reader.ReadFileHeaderAsync(TestContext.Current.CancellationToken);

        var readBack = await reader.ReadAlignmentsAsync(TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(10, readBack.Count);
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal($"read{i + 1}", readBack[i].QName);
            Assert.Equal("ACGT", readBack[i].Sequence);
        }
    }

    [Fact]
    public async Task CramFileSmallerThanEquivalentData()
    {
        // Generate 1000 reads with highly compressible data
        var alignments = Enumerable.Range(1, 1000)
            .Select(i => MakeAlignment($"read{i}", "chr1", i * 150, new string('A', 150),
                new string('I', 150)))
            .ToList();

        var cramMs = new MemoryStream();
        await using (var writer = new CramWriter(cramMs, "reference.fa", leaveOpen: true))
        {
            await writer.WriteHeaderAsync("@HD\tVN:1.6\n@SQ\tSN:chr1\tLN:248956422",
                TestContext.Current.CancellationToken);
            foreach (var a in alignments)
            {
                await writer.WriteAlignmentAsync(a, TestContext.Current.CancellationToken);
            }

            await writer.FinalizeAsync(TestContext.Current.CancellationToken);
        }

        // Raw (uncompressed) size estimate: 1000 reads × ~200 bytes each
        const int rawEstimate = 1000 * 200;
        Assert.True(cramMs.Length < rawEstimate,
            $"CRAM ({cramMs.Length}) should be < raw estimate ({rawEstimate})");
    }

    [Fact]
    public async Task AllAlignmentFieldsPreserved()
    {
        const string seq = "ACGTTTAAACGT";
        var qual = "IIHHIIHHIIII";
        var alignment = new AlignmentSection(
            qname: "myread",
            flag: AlignmentSection.AlignmentFlag.None,
            rname: "chr5",
            position: 12345,
            mapq: 60,
            cigar: [],
            rnext: -1,
            pnext: 0,
            templatelength: 0,
            sequence: seq,
            quality: qual,
            tags: []);

        var ms = new MemoryStream();
        await using (var writer = new CramWriter(ms, "reference.fa", leaveOpen: true))
        {
            await writer.WriteHeaderAsync("@HD\tVN:1.6", TestContext.Current.CancellationToken);
            await writer.WriteAlignmentAsync(alignment, TestContext.Current.CancellationToken);
            await writer.FinalizeAsync(TestContext.Current.CancellationToken);
        }

        ms.Position = 0;
        await using var reader = new CramReader(ms, leaveOpen: true);
        await reader.ReadFileHeaderAsync(TestContext.Current.CancellationToken);
        var result = await reader.ReadAlignmentsAsync(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal("myread", result.QName);
        Assert.Equal(seq, result.Sequence);
        Assert.Equal(qual, result.Quality);
        Assert.Equal(12345, result.Position);
        Assert.Equal(60, result.MappingQuality);
    }
}
