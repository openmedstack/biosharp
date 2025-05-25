using System;
using OpenMedStack.BioSharp.Io.FastA;

namespace OpenMedStack.BioSharp.Calculations.Tests;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Io.FastQ;
using Microsoft.Extensions.Logging.Abstractions;
using NeedlemanWunsch;
using Xunit;

public class NeedlemanWunschAlgoTests
{
    [Theory]
    [InlineData("SEND", "SEND", "AND", "A-ND")]
    [InlineData("SEND", "SEND", "END", "-END")]
    [InlineData("SENDER", "SENDER", "END", "-END--")]
    public async Task CreatesExpectedAlignment(string top, string topAligned, string left, string leftAligned)
    {
        var top2 = await top.AsMemory().Align(left.AsMemory(), 4);

        // Verify the aligned 'left' string matches expected leftAligned
        Assert.NotNull(leftAligned);
        Assert.Equal(leftAligned.AsMemory(), top2.alignment);
        // Verify the alignment length matches the expected topAligned length
        Assert.Equal(topAligned.Length, top2.alignment.Length);
    }

    [Fact]
    public async Task AlignSequences()
    {
        const string fastq = "ERR164409.fastq.gz";
        var reader = new FastQReader(NullLogger.Instance);
        var sequence = await reader.Read(fastq, CancellationToken.None).FirstAsync();

        var oneTwoAligned = await sequence.Align(sequence, 4);

        Assert.Equal(sequence.Length, oneTwoAligned.alignment.Length);
    }

    [Fact]
    public async Task AlignChromosomes()
    {
        const string fastq = "chr1.fa.gz";
        var reader = new FastAReader();
        var sequence = await reader.ReadGz(fastq).FirstAsync();
        var sequence1 = sequence[500_000..500_100]; // Slice to a manageable size for testing
        var sequence2 = sequence[500_010..500_100]; // Slice to a manageable size for testing
        var aligned = await sequence1.Align(sequence2, 4);
        // Alignment of sequence2 against sequence1: at minimum the length of sequence1 (100) since sequence1 is longer
        Assert.Equal(100, aligned.alignment.Length);
        // With the traceback fix, index is 0 (full traceback to position 0)
        Assert.Equal(0, aligned.index);
    }
}
