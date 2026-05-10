namespace OpenMedStack.BioSharp.Calculations.Tests;

using System;
using Alignment;
using Model;
using Xunit;

public class SmithWatermanAlignerTests
{
    [Fact]
    public void Align_PerfectMatch_ReturnsExpectedCoordinates()
    {
        var reference = new Sequence("ref", "TTTTACGTACGTAAAA".ToCharArray(), new string('I', 16).ToCharArray());
        var read = new Sequence("read", "ACGTACGT".ToCharArray(), new string('I', 8).ToCharArray());

        var alignment = SmithWatermanAligner.Align(reference, read, minScore: 4);

        Assert.NotNull(alignment);
        Assert.Equal(4, alignment.ReferenceStartPosition);
        Assert.Equal("ACGTACGT", alignment.AlignedReference);
        Assert.Equal("ACGTACGT", alignment.AlignedRead);
    }

    [Fact]
    public void Align_WhenCellBudgetExceeded_Throws()
    {
        var reference = new Sequence("ref", new string('A', 64).ToCharArray(), new string('I', 64).ToCharArray());
        var read = new Sequence("read", new string('A', 64).ToCharArray(), new string('I', 64).ToCharArray());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SmithWatermanAligner.Align(reference, read, maxCellCount: 100));

        Assert.Contains("exceeds the configured budget", exception.Message);
    }

    [Fact]
    public void Align_SeedInformedBanding_FindsOffsetAlignment()
    {
        var prefix = new string('T', 40);
        var target = "ACGTGATTACAGGTT";
        var suffix = new string('C', 40);
        var referenceSeq = prefix + target + suffix;
        var reference = new Sequence("ref", referenceSeq.ToCharArray(), new string('I', referenceSeq.Length).ToCharArray());
        var read = new Sequence("read", target.ToCharArray(), new string('I', target.Length).ToCharArray());

        var alignment = SmithWatermanAligner.Align(
            reference,
            read,
            minScore: 10,
            bandWidth: 4,
            expectedReferenceStart: prefix.Length);

        Assert.NotNull(alignment);
        Assert.Equal(prefix.Length, alignment.ReferenceStartPosition);
        Assert.True(alignment.WasPruned);
    }

    [Fact]
    public void Align_WhenMinScoreExceedsTheoreticalMaximum_ReturnsNull()
    {
        var reference = new Sequence("ref", "ACGTACGT".ToCharArray(), new string('I', 8).ToCharArray());
        var read = new Sequence("read", "ACGT".ToCharArray(), new string('I', 4).ToCharArray());

        var alignment = SmithWatermanAligner.Align(reference, read, matchScore: 2, minScore: 9);

        Assert.Null(alignment);
    }

    [Fact]
    public void Align_WithXDrop_PrunesHopelessAlignment()
    {
        var reference = new Sequence("ref", new string('A', 40).ToCharArray(), new string('I', 40).ToCharArray());
        var read = new Sequence("read", new string('T', 20).ToCharArray(), new string('I', 20).ToCharArray());

        var alignment = SmithWatermanAligner.Align(
            reference,
            read,
            matchScore: 2,
            mismatchPenalty: -3,
            minScore: 1,
            bandWidth: 4,
            xDrop: 3,
            expectedReferenceStart: 0);

        Assert.Null(alignment);
    }
}