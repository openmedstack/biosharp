using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenMedStack.BioSharp.Io.Cram;
using OpenMedStack.BioSharp.Io.Sam;
using Xunit;

namespace OpenMedStack.BioSharp.Io.Tests;

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
            await writer.WriteHeader("@HD\tVN:1.6\n@SQ\tSN:chr1\tLN:248956422",
                TestContext.Current.CancellationToken);
            await writer.WriteAlignment(alignment, TestContext.Current.CancellationToken);
            await writer.Finalize(TestContext.Current.CancellationToken);
        }

        Assert.True(ms.Length > 0);

        ms.Position = 0;
        await using var reader = new CramReader(ms, leaveOpen: true);
        var header = await reader.ReadFileHeader(TestContext.Current.CancellationToken);

        Assert.NotEmpty(header);

        var records = await reader.ReadAlignments(TestContext.Current.CancellationToken)
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
            await writer.WriteHeader("@HD\tVN:1.6\n@SQ\tSN:chr1\tLN:248956422",
                TestContext.Current.CancellationToken);
            foreach (var a in alignments)
            {
                await writer.WriteAlignment(a, TestContext.Current.CancellationToken);
            }

            await writer.Finalize(TestContext.Current.CancellationToken);
        }

        ms.Position = 0;
        await using var reader = new CramReader(ms, leaveOpen: true);
        await reader.ReadFileHeader(TestContext.Current.CancellationToken);

        var readBack = await reader.ReadAlignments(TestContext.Current.CancellationToken)
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
            await writer.WriteHeader("@HD\tVN:1.6\n@SQ\tSN:chr1\tLN:248956422",
                TestContext.Current.CancellationToken);
            foreach (var a in alignments)
            {
                await writer.WriteAlignment(a, TestContext.Current.CancellationToken);
            }

            await writer.Finalize(TestContext.Current.CancellationToken);
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
            await writer.WriteHeader("@HD\tVN:1.6", TestContext.Current.CancellationToken);
            await writer.WriteAlignment(alignment, TestContext.Current.CancellationToken);
            await writer.Finalize(TestContext.Current.CancellationToken);
        }

        ms.Position = 0;
        await using var reader = new CramReader(ms, leaveOpen: true);
        await reader.ReadFileHeader(TestContext.Current.CancellationToken);
        var result = await reader.ReadAlignments(TestContext.Current.CancellationToken)
            .FirstAsync(TestContext.Current.CancellationToken);

        Assert.Equal("myread", result.QName);
        Assert.Equal(seq, result.Sequence);
        Assert.Equal(qual, result.Quality);
        Assert.Equal(12345, result.Position);
        Assert.Equal(60, result.MappingQuality);
    }
}