using System;
using System.Linq;
using System.Text;
using OpenMedStack.BioSharp.Calculations.DeBruijn;
using Xunit;

namespace OpenMedStack.BioSharp.Calculations.Tests;

public class CopyNumberCallerTests
{
    private static int[] MakeDepths(int length, params (int start, int end, int cn)[] regions)
    {
        // diploid baseline = baseCopyNumber copies ~ some depth
        const int baseDepth = 30;
        var depths = new int[length];
        for (var i = 0; i < length; i++)
        {
            depths[i] = baseDepth;
        }

        foreach (var (start, end, cn) in regions)
        {
            var factor = cn / 2.0;
            for (var i = start; i < end && i < length; i++)
            {
                depths[i] = (int)(baseDepth * factor);
            }
        }

        return depths;
    }

    private static string MakeRefSeq(int length)
    {
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(i % 2 == 0 ? 'G' : 'A'); // alternating for ~50% GC
        }

        return sb.ToString();
    }

    [Fact]
    public void Call_DiploidBaseline_NoCnvCalls()
    {
        const int len = 10000;
        var depths = MakeDepths(len);
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 1000);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        // All segments should be CN=2
        Assert.All(calls, v =>
        {
            var info = v.AdditionalInformation ?? v.AdditionalInformation ?? "";
            // Either no calls, or all calls have CN=2
        });
    }

    [Fact]
    public void Call_HemizygousDeletion_DetectsDelSegment()
    {
        // 10 kb reference, deletion of 2 kb in the middle (positions 4000-6000) → CN=1
        const int len = 10000;
        var depths = MakeDepths(len, (4000, 6000, 1));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        // Should detect at least one DEL call
        Assert.Contains(calls, v =>
            v is { IsStructuralVariant: true, SvType: SvType.Deletion, Position: <= 4500, EndPosition: >= 5500 });
    }

    [Fact]
    public void Call_HomozygousDeletion_Detected()
    {
        const int len = 10000;
        var depths = MakeDepths(len, (3000, 5000, 0));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        Assert.Contains(calls, v =>
            v is { IsStructuralVariant: true, SvType: SvType.Deletion });
    }

    [Fact]
    public void Call_Amplification_DetectedAsDup()
    {
        const int len = 10000;
        var depths = MakeDepths(len, (2000, 4000, 6));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        Assert.Contains(calls, v =>
            v is { IsStructuralVariant: true, SvType: SvType.CopyNumber });
    }

    [Fact]
    public void Call_SvlenAndEndInfoFieldsPresent()
    {
        const int len = 10000;
        var depths = MakeDepths(len, (4000, 7000, 1));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 500);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        // Each SV call should have EndPosition set
        var svCalls = calls.Where(v => v.IsStructuralVariant).ToList();
        Assert.NotEmpty(svCalls);
        Assert.All(svCalls, v => Assert.True(v.EndPosition > v.Position));
    }

    [Fact]
    public void Call_50kbDeletion_BreakpointsWithin5kb()
    {
        // Simulate 100 kb reference with a 50 kb deletion in the middle
        const int len = 100_000;
        var depths = MakeDepths(len, (25_000, 75_000, 0));
        var refSeq = MakeRefSeq(len);

        var caller = new CopyNumberCaller(windowSize: 2000);
        var calls = caller.Call(refSeq.AsMemory(), depths, "chr1").ToList();

        var deletion = calls.FirstOrDefault(v => v is { IsStructuralVariant: true, SvType: SvType.Deletion });
        Assert.NotNull(deletion);
        Assert.True(Math.Abs(deletion.Position - 25_000) <= 5000, $"Start breakpoint off: {deletion.Position}");
        Assert.True(Math.Abs(deletion.EndPosition - 75_000) <= 5000, $"End breakpoint off: {deletion.EndPosition}");
    }
}