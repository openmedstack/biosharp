using System;
using OpenMedStack.BioSharp.Calculations.Alignment;

namespace OpenMedStack.BioSharp.Calculations.Tests;

internal class TestAlignments
{
    public static AlignmentResult Create(
        string refStr,
        string readStr,
        int score = 20,
        int refStart = 0,
        int leftClip = 0,
        int rightClip = 0,
        int visLen = -1)
    {
        var vis = CreateVisual(refStr, readStr, visLen > 0 ? visLen : Math.Max(refStr.Length, readStr.Length));
        return new AlignmentResult(refStr, readStr, vis, score, refStart, leftClip, rightClip);
    }

    private static string CreateVisual(string refStr, string readStr, int len)
    {
        var sb = new System.Text.StringBuilder(len);
        for (var i = 0; i < len; i++)
        {
            var rf = i < refStr.Length ? refStr[i] : '-';
            var rd = i < readStr.Length ? readStr[i] : '-';
            if (rf == '-' || rd == '-')
            {
                sb.Append(' ');
            }
            else if (char.ToUpper(rf) == char.ToUpper(rd))
            {
                sb.Append('|');
            }
            else
            {
                sb.Append('X');
            }
        }

        return sb.ToString();
    }
}
