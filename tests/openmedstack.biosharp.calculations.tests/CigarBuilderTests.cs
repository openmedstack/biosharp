using OpenMedStack.BioSharp.Calculations.Alignment;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class CigarBuilderTests
{
    [Fact]
    public void BuildCigar_FromAlignmentResult_ProducesCorrectString()
    {
        var alignedRef = "ACGTACGT";
        var alignedRead = "ACGTACGT";
        var cigar = CigarBuilder.BuildCigar(alignedRef, alignedRead);
        Assert.Equal("8M", cigar);
    }

    [Fact]
    public void BuildCigar_WithInsertion_ProducesCorrectString()
    {
        // Ref: A C G - T (5 with gap at pos 3)
        // Read: A C G A T (5 bases, A inserted at pos 3)
        var alignedRef = "ACG-T";
        var alignedRead = "ACGAT";
        var cigar = CigarBuilder.BuildCigar(alignedRef, alignedRead);
        // Character-by-character: M M M I M -> merged to "3M1I1M"
        Assert.Equal("3M1I1M", cigar);
    }

    [Fact]
    public void BuildCigar_WithDeletion_ProducesCorrectString()
    {
        // Ref: A C G T A (5 bases)
        // Read: A C G - - (3 bases, 2 deleted bases in ref)
        var alignedRef = "ACGTA";
        var alignedRead = "ACG--";
        var cigar = CigarBuilder.BuildCigar(alignedRef, alignedRead);
        // Character-by-character: M M M D D -> "3M2D" (no trailing M since alignment ends with deletions)
        Assert.Equal("3M2D", cigar);
    }

    [Fact]
    public void BuildCigar_WithSoftClips_ProducesCorrectString()
    {
        var alignedRef = "ACGTACGT";
        var alignedRead = "ACGTACGT";
        var cigar = CigarBuilder.BuildCigar(alignedRef, alignedRead, 5, 3);
        Assert.Equal("5S8M3S", cigar);
    }

    [Fact]
    public void BuildCigarOps_ReturnsMergedOperations()
    {
        var ops = CigarBuilder.BuildCigarOps("ACGTACGT", "ACGTACGT");
        Assert.Single(ops);
        Assert.Equal((8, 'M'), ops[0]);
    }

    [Fact]
    public void BuildCigarOps_MergesConsecutiveOperations()
    {
        var ops = CigarBuilder.BuildCigarOps("AA--GG", "AATTGG");
        // A match, A match, - vs T -> insertion, - vs T -> insertion, G match, G match
        // Should produce: (2, 'M'), (2, 'I'), (2, 'M')
        Assert.Equal(3, ops.Length);
        Assert.Equal((2, 'M'), ops[0]);
        Assert.Equal((2, 'I'), ops[1]);
        Assert.Equal((2, 'M'), ops[2]);
    }
}
