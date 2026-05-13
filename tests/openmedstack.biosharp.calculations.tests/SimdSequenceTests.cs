using System;
using System.Linq;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class SimdSequenceTests
{
    private static Sequence MakeSeq(string bases)
        => new Sequence("s", bases.AsMemory(), new string('I', bases.Length).AsMemory());

    [Fact]
    public void SimdSequence_CountMismatches_MatchesScalar()
    {
        const string a = "ACGTACGTACGTACGTACGTACGTACGTACGT"; // 32 chars
        const string b = "ACGTACGTACGTACGTCCGTACGTACGTACGT"; // diff at index 16

        var seqA = MakeSeq(a);
        var seqB = MakeSeq(b);

        var scalarCount = SequenceSimd.CountMismatchesScalar(seqA.GetData().Span, seqB.GetData().Span);
        var simdCount = SequenceSimd.CountMismatches(seqA.GetData().Span, seqB.GetData().Span);

        Assert.Equal(scalarCount, simdCount);
        Assert.Equal(1, simdCount);
    }

    [Fact]
    public void SimdSequence_CountMismatches_ForRandomSequences()
    {
        var rng = new Random(42);
        for (var len = 1; len <= 256; len++)
        {
            var a = new char[len];
            var b = new char[len];
            var bases = "ACGT";
            for (var i = 0; i < len; i++)
            {
                a[i] = bases[rng.Next(4)];
                b[i] = bases[rng.Next(4)];
            }
            var scalar = SequenceSimd.CountMismatchesScalar(a, b);
            var simd = SequenceSimd.CountMismatches(a, b);
            Assert.Equal(scalar, simd);
        }
    }

    [Fact]
    public void SimdSequence_ReverseComplement_MatchesScalar()
    {
        const string seq = "ACGTACGTACGTACGT";
        var seqMem = seq.AsMemory();

        var scalar = SequenceSimd.ReverseComplementScalar(seqMem.Span);
        var simd = SequenceSimd.ReverseComplement(seqMem.Span);

        Assert.Equal(new string(scalar), new string(simd));
    }

    [Fact]
    public void SimdSequence_FindKmer_MatchesScalar()
    {
        const string seq = "ACGTACGTACGTTTTTACGT";
        const string kmer = "ACGT";

        var seqSpan = seq.AsSpan();
        var kmerSpan = kmer.AsSpan();

        var scalarPositions = SequenceSimd.FindKmerScalar(seqSpan, kmerSpan).ToList();
        var simdPositions = SequenceSimd.FindKmer(seqSpan, kmerSpan).ToList();

        Assert.Equal(scalarPositions, simdPositions);
    }
}