using System;
using System.IO;
using OpenMedStack.BioSharp.Calculations.Alignment;
using OpenMedStack.BioSharp.Model;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class AlignmentDebugTests
{
    [Fact]
    public void Debug_All()
    {
        var lines = new System.Collections.Generic.List<string>();

        Run("PERFECT", "AGCTAGCT", "AGCTAGCT");
        Run("SNP", "AGCTAGCT", "AGCTTGCT");
        Run("DELETION", "AGCTAGCT", "AGTAGCT");
        Run("INSERTION", "AGCTAGCT", "AGCTTAGCT");
        Run("HOMOPOLY", "AAAAAAAAAAAAAAAAAAAA", "AAAAAAAAAAAA");
        Run("SHORT_READ", "AAAAGGGGCCCATTT", "GGGCCCA", 3);
        Run("SHORT2", "AAAAGGGGCCCATTT", "GGG", 3);
        Run("MULTI_SNPs", "ATCGATCGATCGATCGATCG", "ATCAATCGTTCGATCGATCG");

        File.WriteAllText("/tmp/align_debug.txt", string.Join("\n", lines));

        // Simple asserts
        var a1 = SmithWatermanAligner.Align(
            new Sequence("chr1", "AGCTAGCT".AsMemory(), new string('I', 8).AsMemory()),
            new Sequence("r1", "AGCTAGCT".AsMemory(), new string('I', 8).AsMemory()));
        var a2 = SmithWatermanAligner.Align(
            new Sequence("chr1", "AGCTAGCT".AsMemory(), new string('I', 8).AsMemory()),
            new Sequence("r1", "AGCTTGCT".AsMemory(), new string('I', 8).AsMemory()));
        var a3 = SmithWatermanAligner.Align(
            new Sequence("chr1", "AGCTAGCT".AsMemory(), new string('I', 8).AsMemory()),
            new Sequence("r1", "AGTAGCT".AsMemory(), new string('I', 7).AsMemory()));
        var a4 = SmithWatermanAligner.Align(
            new Sequence("chr1", "AGCTAGCT".AsMemory(), new string('I', 8).AsMemory()),
            new Sequence("r1", "AGCTTAGCT".AsMemory(), new string('I', 9).AsMemory()));
        var a5 = SmithWatermanAligner.Align(
            new Sequence("chr1", "AAAAGGGGCCCATTT".AsMemory(), new string('I', 15).AsMemory()),
            new Sequence("r1", "GGGCCCA".AsMemory(), new string('I', 7).AsMemory()), 2, -3, -5, -2, 3);
        var a6 = SmithWatermanAligner.Align(
            new Sequence("chr1", "AAAAGGGCCCATTT".AsMemory(), new string('I', 14).AsMemory()),
            new Sequence("r1", "GGG".AsMemory(), new string('I', 3).AsMemory()), 2, -3, -5, -2, 3);
        var a7 = SmithWatermanAligner.Align(
            new Sequence("chr1", "ATCGATCGATCGATCGATCG".AsMemory(), new string('I', 20).AsMemory()),
            new Sequence("r1", "ATCAATCGTTCGATCGATCG".AsMemory(), new string('I', 20).AsMemory()));
        var a8 = SmithWatermanAligner.Align(
            new Sequence("chr1", "AAAAAAAAAAAAAAAAAAAA".AsMemory(), new string('I', 20).AsMemory()),
            new Sequence("r1", "AAAAAAAAAAAA".AsMemory(), new string('I', 12).AsMemory()), 2, -3, -5, -2, 10);

        Assert.NotNull(a1);
        Assert.NotNull(a2);
        Assert.NotNull(a3);
        Assert.NotNull(a4);
        Assert.NotNull(a5);
        Assert.NotNull(a6);
        Assert.NotNull(a7);
        Assert.NotNull(a8);
        return;

        void Run(
            string name,
            string refSeq,
            string readSeq,
            int ms = 5,
            int mS = 2,
            int mm = -3,
            int go = -5,
            int ge = -2)
        {
            var refS = new Sequence("chr1", refSeq.AsMemory(), new string('I', refSeq.Length).AsMemory());
            var readS = new Sequence("read1", readSeq.AsMemory(), new string('I', readSeq.Length).AsMemory());
            var a = SmithWatermanAligner.Align(refS, readS, mS, mm, go, ge, ms);

            lines.Add($"=== {name} ===");
            lines.Add($"Score: {a?.Score ?? -1}");
            lines.Add($"RefStart: {a?.ReferenceStartPosition ?? -1}");
            lines.Add($"Left:{a?.LeftSoftClip} Right:{a?.RightSoftClip}");
            lines.Add($"Ref:{a?.AlignedReference ?? "null"}");
            lines.Add($"Read:{a?.AlignedRead ?? "null"}");
            if (a != null)
            {
                lines.Add($"Vis:|{SmithWatermanAligner.CreateAlignmentString(a)}|");
            }

            lines.Add("");
        }
    }
}
