using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using OpenMedStack.BioSharp.Io.FastQ;
using Xunit;

namespace OpenMedStack.BioSharp.Io.Tests;

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
                .ReadPaired(r1, r2, cancellationToken: TestContext.Current.CancellationToken)
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
                .ReadPaired(r1, r2, cancellationToken: TestContext.Current.CancellationToken)
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
                .ReadPaired(r1, r2, cancellationToken: TestContext.Current.CancellationToken)
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
                await reader.ReadPaired(r1, r2, strict: true,
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
                .ReadInterleaved(path, cancellationToken: TestContext.Current.CancellationToken)
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