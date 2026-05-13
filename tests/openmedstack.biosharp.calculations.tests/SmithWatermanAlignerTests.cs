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
        var reference = new Sequence("ref", "TTTTACGTACGTAAAA".AsMemory(), new string('I', 16).AsMemory());
        var read = new Sequence("read", "ACGTACGT".AsMemory(), new string('I', 8).AsMemory());

        var alignment = SmithWatermanAligner.Align(reference, read, minScore: 4);

        Assert.NotNull(alignment);
        Assert.Equal(4, alignment.ReferenceStartPosition);
        Assert.Equal("ACGTACGT", alignment.AlignedReference);
        Assert.Equal("ACGTACGT", alignment.AlignedRead);
    }

    [Fact]
    public void Align_WhenCellBudgetExceeded_Throws()
    {
        var reference = new Sequence("ref", new string('A', 64).AsMemory(), new string('I', 64).AsMemory());
        var read = new Sequence("read", new string('A', 64).AsMemory(), new string('I', 64).AsMemory());

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
        var reference = new Sequence("ref", referenceSeq.AsMemory(), new string('I', referenceSeq.Length).AsMemory());
        var read = new Sequence("read", target.AsMemory(), new string('I', target.Length).AsMemory());

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
        var reference = new Sequence("ref", "ACGTACGT".AsMemory(), new string('I', 8).AsMemory());
        var read = new Sequence("read", "ACGT".AsMemory(), new string('I', 4).AsMemory());

        var alignment = SmithWatermanAligner.Align(reference, read, matchScore: 2, minScore: 9);

        Assert.Null(alignment);
    }

    [Fact]
    public void Align_WithXDrop_PrunesHopelessAlignment()
    {
        var reference = new Sequence("ref", new string('A', 40).AsMemory(), new string('I', 40).AsMemory());
        var read = new Sequence("read", new string('T', 20).AsMemory(), new string('I', 20).AsMemory());

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
